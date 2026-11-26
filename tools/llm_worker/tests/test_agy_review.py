"""Second review fallback to Sonnet 5.5 through agy (#456, TASK-0146). No network, no real agy."""

import json
import os
import subprocess
import time
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest import mock

from helpers import FakeProvider
from test_review import GOOD, make_repo

from tools.llm_worker import agyreview, codex, config, limitstate, review, sketch
from tools.llm_worker.review import ReviewError

SCHEMA = config.PACKAGE_DIR / "schemas" / "review.schema.json"
CID = "11111111-2222-3333-4444-555555555555"
NOW = datetime(2026, 11, 24, 13, 50, tzinfo=timezone.utc)


def stream(tool_events=(), result=None, model="claude-sonnet-5-5-medium"):
    lines = [{"event": "init", "conversation_id": CID, "init": {"model": model}}]
    for name, params in tool_events:
        lines.append({"event": "step_update", "step_update": {"conversation_id": CID, "tool_name": name,
                                                              "tool_info": {"name": name, "parameters": params}}})
    lines.append({"event": "result", "result": result or {
        "conversation_id": CID, "status": "SUCCESS", "response": "x", "structured_output": GOOD,
        "usage": {"input_tokens": 10, "output_tokens": 2, "thinking_tokens": 0}}})
    return "\n".join(json.dumps(x) for x in lines)


class LimitStateTest(unittest.TestCase):
    def test_codex_limit_message_is_recognised_and_time_parsed(self):
        msg = "You've hit your usage limit. Upgrade or try again at 3:44 PM."
        self.assertTrue(codex.LIMIT_RE.search(msg))
        now = datetime(2026, 11, 24, 13, 44, tzinfo=timezone.utc)
        self.assertEqual(limitstate.parse_reset(msg, now), now.replace(hour=15, minute=44))
        self.assertEqual(limitstate.parse_reset("try again at 1:10 AM", now), now.replace(hour=1, minute=10) + timedelta(days=1))
        self.assertEqual(limitstate.parse_reset("try again in 2 hours 5 minutes", now), now + timedelta(hours=2, minutes=5))
        self.assertIsNone(limitstate.parse_reset("rate limited", now))
        self.assertEqual(limitstate.parse_reset("try again in 100 hours", now), now + timedelta(hours=24))  # capped

    def test_state_until_reset_then_expired(self):
        with tempfile.TemporaryDirectory() as d:
            self.assertIsNone(limitstate.limited_until(NOW, d))
            at = NOW + timedelta(hours=2)
            limitstate.record_limit(at, NOW, d)
            self.assertEqual(limitstate.limited_until(NOW, d), at)
            limitstate.record_limit(NOW + timedelta(days=30), NOW, d)  # a stored reset is capped too
            self.assertEqual(limitstate.limited_until(NOW, d), NOW + limitstate.MAX_COOLDOWN)
            limitstate.record_limit(at, NOW, d)
            self.assertIsNone(limitstate.limited_until(at + timedelta(seconds=1), d))
            limitstate.record_limit(None, NOW, d)  # no time in the message: default cooldown
            self.assertEqual(limitstate.limited_until(NOW, d), NOW + limitstate.DEFAULT_COOLDOWN)
            (Path(d) / limitstate.STATE_FILE).write_text("{broken")
            self.assertIsNone(limitstate.limited_until(NOW, d))


class BoxTest(unittest.TestCase):
    def setUp(self):
        self.home = Path(tempfile.mkdtemp())
        for rel in ("oauth_creds.json", "google_accounts.json", "settings.json", "config/mcp_config.json", "tmp/user/logs.json"):
            p = self.home / ".gemini" / rel
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text("secret")

    def wrap(self, writable, cwd="/work/x", bus=None):
        box = sketch.Box(Path("/h/brain"), Path("/h/conv"), writable, bus=bus, home=self.home, bwrap="/usr/bin/bwrap")
        return box.wrap(["/home/u/.local/bin/agy", "--settings", "/tmp/s/settings.json"], cwd)

    def pairs(self, argv, flag):
        return [tuple(argv[i + 1:i + 3]) for i, a in enumerate(argv) if a == flag]

    def test_mounts_only_what_agy_needs(self):
        argv = self.wrap(False)
        gem, cli = f"{self.home}/.gemini", f"{self.home}/.gemini/antigravity-cli"
        self.assertEqual(argv[0], "/usr/bin/bwrap")
        self.assertIn("--clearenv", argv)
        self.assertEqual(argv[argv.index("--tmpfs", argv.index("/tmp")) + 1], str(self.home))  # $HOME starts empty
        self.assertIn(("/work/x", "/work/x"), self.pairs(argv, "--ro-bind"))  # the review copy is read-only
        self.assertNotIn(("/work/x", "/work/x"), self.pairs(argv, "--bind"))
        self.assertIn(("/h/brain", f"{cli}/brain"), self.pairs(argv, "--bind"))  # private brain
        self.assertIn((cli, cli), self.pairs(argv, "--ro-bind"))  # agy's own folder is never written
        self.assertNotIn((cli, cli), self.pairs(argv, "--bind"))
        tmpfs = {argv[i + 1] for i, a in enumerate(argv) if a == "--tmpfs"}
        for name in ("log", "presence", "implicit", "cache"):
            self.assertIn(f"{cli}/{name}", tmpfs)
        masked = {d for s, d in self.pairs(argv, "--ro-bind") if s == "/dev/null"}
        for name in ("oauth_creds.json", "google_accounts.json", "settings.json", "config/mcp_config.json", "tmp/user/logs.json"):
            self.assertIn(f"{gem}/{name}", masked)
        everything = " ".join(argv)
        for secret in (".ssh", ".config/singularity", ".codex", ".gnupg", "gemini-api-key", "deepseek-api-key"):
            self.assertNotIn(secret, everything)
        self.assertNotIn("--unshare-net", argv)  # the model API needs the network
        self.assertIn(("/tmp/s/settings.json", "/tmp/s/settings.json"), self.pairs(argv, "--ro-bind"))  # rules file visible
        self.assertEqual(argv[-3:], ["/home/u/.local/bin/agy", "--settings", "/tmp/s/settings.json"])

    def test_filtered_bus_replaces_the_real_one(self):
        argv = self.wrap(False, bus=Path("/p/bus"))
        self.assertIn(("/p/bus", f"/run/user/{os.getuid()}/bus"), self.pairs(argv, "--bind"))
        self.assertNotIn(f"/run/user/{os.getuid()}/bus", [p[0] for p in self.pairs(argv, "--bind-try")])

    def test_dbus_filter_starts_proxy_with_secrets_only_and_stops_it(self):
        with tempfile.TemporaryDirectory() as d:
            fake = Path(d) / "xdg-dbus-proxy"
            fake.write_text("#!/usr/bin/env python3\nimport sys, time, pathlib\n"
                            "pathlib.Path(sys.argv[2]).touch()\npathlib.Path(sys.argv[2] + '.args').write_text(' '.join(sys.argv[1:]))\n"
                            "time.sleep(60)\n")
            fake.chmod(0o755)
            host = Path(d) / "hostbus"
            host.touch()
            with sketch.dbus_filter(Path(d) / "bus", which=lambda n: str(fake), host=str(host)) as bus:
                self.assertEqual(bus, Path(d) / "bus")
                for _ in range(50):
                    if (Path(d) / "bus.args").exists():
                        break
                    time.sleep(0.1)
                args = (Path(d) / "bus.args").read_text()
            self.assertIn("--filter --talk=org.freedesktop.secrets", args)
            self.assertNotIn("--own", args)

    def test_no_proxy_means_no_run_when_a_host_bus_exists(self):
        with tempfile.TemporaryDirectory() as d:
            host = Path(d) / "hostbus"
            host.touch()
            with self.assertRaises(sketch.SketchError) as cm:
                with sketch.dbus_filter(Path(d) / "bus", which=lambda n: None, host=str(host)):
                    pass
            self.assertEqual(cm.exception.code, "sandbox_unavailable")
            with sketch.dbus_filter(Path(d) / "bus", which=lambda n: None, host=str(Path(d) / "nobus")) as bus:
                self.assertIsNone(bus)  # no host bus at all: nothing to expose
        self.assertNotIn(f"/run/user/{os.getuid()}/bus", " ".join(self.wrap(False)))  # and none is mounted

    def test_sketch_workdir_is_writable_and_missing_bwrap_refuses(self):
        self.assertIn(("/work/x", "/work/x"), self.pairs(self.wrap(True), "--bind"))
        with mock.patch.object(sketch, "find_bwrap", return_value=None):
            with self.assertRaises(sketch.SketchError) as cm:
                sketch.Box(Path("/b"), Path("/c")).wrap(["/x/agy"], None)
        self.assertEqual(cm.exception.code, "sandbox_unavailable")

    def test_missing_sandbox_is_not_a_paid_fallback(self):
        with mock.patch.object(sketch, "find_bwrap", return_value=None):
            with self.assertRaises(codex.SandboxUnavailable):
                agyreview.run_agy("p", "/tmp", SCHEMA, which=lambda n: "/bin/true")


class ScopedRulesTest(unittest.TestCase):
    def test_everything_outside_the_allowed_folders_is_denied(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d).resolve()
            (root / "home" / "u" / ".config").mkdir(parents=True)
            (root / "home" / "u" / ".config" / "key").write_text("k")
            (root / "home" / "u" / ".cache" / "wt" / "run1").mkdir(parents=True)
            (root / "home" / "u" / ".cache" / "wt" / "other").mkdir()
            (root / "home" / "u" / ".cache" / "brain" / "id1").mkdir(parents=True)
            (root / "etc").mkdir()
            (root / "home" / "u" / "note.txt").write_text("n")
            rules = sketch.scoped_deny_rules([root / "home" / "u" / ".cache" / "wt" / "run1"], top=root)
        denied = {r[len("read_file("):-1] for r in rules if r.startswith("read_file(")}
        for sibling in (".config", ".config/**", "note.txt", ".cache/brain", ".cache/brain/**", ".cache/wt/other/**"):
            self.assertIn(f"{root}/home/u/{sibling}", denied, sibling)
        self.assertIn(f"{root}/etc/**", denied)
        self.assertFalse([x for x in denied if x.startswith(f"{root}/home/u/.cache/wt/run1")])  # the allowed folder
        self.assertNotIn(f"{root}/home/u/.cache/wt", denied)  # its ancestors stay listable, only siblings are denied
        self.assertEqual(rules, [r for r in rules if r.startswith(tuple(t + "(" for t in sketch.SCOPED_TOOLS))])

    def test_other_conversations_in_brain_are_denied_own_stays_open(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d).resolve()
            (root / "wt" / "run").mkdir(parents=True)
            (root / "brain" / "old-conv").mkdir(parents=True)
            rules = sketch.scoped_deny_rules([root / "wt" / "run", root / "brain"], top=root, closed_children=[root / "brain"])
        denied = {r[len("read_file("):-1] for r in rules if r.startswith("read_file(")}
        self.assertIn(f"{root}/brain/old-conv/**", denied)
        self.assertNotIn(f"{root}/brain", denied)  # a conversation created later is not named by any rule
        for tool in ("list_dir", "grep_search", "find_by_name"):
            self.assertIn(f"{tool}({root}/brain/old-conv/**)", rules)

    def test_unlistable_folder_refuses(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d).resolve()
            (root / "run").mkdir()
            with mock.patch("os.listdir", side_effect=PermissionError(13, "Permission denied")):
                with self.assertRaises(sketch.SketchError) as cm:
                    sketch.scoped_deny_rules([root / "run"], top=root)
        self.assertEqual(cm.exception.code, "sandbox_unavailable")

    def test_unwritable_name_and_huge_folder_refuse(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d).resolve()
            (root / "run").mkdir()
            (root / "bad(name)").mkdir()
            with self.assertRaises(sketch.SketchError):
                sketch.scoped_deny_rules([root / "run"], top=root)
            (root / "bad(name)").rmdir()
            with mock.patch.object(sketch, "MAX_DIR_ENTRIES", 0):
                with self.assertRaises(sketch.SketchError):
                    sketch.scoped_deny_rules([root / "run"], top=root)


class LimitCacheTest(unittest.TestCase):
    def test_clear_and_record_exclude_each_other(self):
        import threading
        with tempfile.TemporaryDirectory() as d:
            limitstate.record_limit(NOW + timedelta(hours=1), NOW - timedelta(minutes=5), d)
            done = threading.Event()
            t = threading.Thread(target=lambda: (limitstate.clear(NOW, d), done.set()))
            with limitstate._locked(d):  # a concurrent writer holds the state
                t.start()
                self.assertFalse(done.wait(0.3))  # clear waits instead of reading a stale record
                limitstate._write(NOW + timedelta(hours=2), NOW + timedelta(seconds=1), Path(d))  # the writer's fresh record
            t.join(5)
            self.assertTrue(done.is_set())
            self.assertIsNotNone(limitstate.limited_until(NOW, d))  # the fresh limit survived the clear

    def test_parallel_writers_leave_a_valid_file_and_no_temp_files(self):
        import threading
        with tempfile.TemporaryDirectory() as d:
            ts = [threading.Thread(target=limitstate.record_limit, args=(NOW + timedelta(hours=1 + i % 3), NOW, d)) for i in range(40)]
            for t in ts:
                t.start()
            for t in ts:
                t.join()
            self.assertIsNotNone(limitstate.limited_until(NOW, d))
            self.assertEqual(sorted(p.name for p in Path(d).iterdir()), sorted([limitstate.LOCK_FILE, limitstate.STATE_FILE]))
            self.assertEqual((Path(d) / limitstate.STATE_FILE).stat().st_mode & 0o777, 0o600)


class RunAgyTest(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.wt = self.tmp / "wt"
        self.wt.mkdir()
        self.brain = self.tmp / "brain"
        p = mock.patch.object(sketch, "scoped_deny_rules", lambda allowed, **kw: [])  # the real walk is tested below
        p.start()
        self.addCleanup(p.stop)
        (self.brain / CID / ".system_generated" / "logs").mkdir(parents=True)

    def run_agy(self, out, rc=0, err="", transcript=()):
        (self.brain / CID / ".system_generated" / "logs" / "transcript.jsonl").write_text(
            "\n".join(json.dumps({"tool_calls": [{"name": n, "args": a}]}) for n, a in transcript))
        seen = []

        def runner(cmd, cwd, timeout, stdin=None):
            seen.append((cmd, cwd, json.loads(Path(cmd[cmd.index("--settings") + 1]).read_text()), stdin))
            return rc, out, err

        res = agyreview.run_agy("p", self.wt, config.PACKAGE_DIR / "schemas" / "review.schema.json",
                                brain_dir=self.brain, runner=runner, which=lambda n: "/bin/true")
        return res, seen[0]

    def test_answer_parsed_and_command_is_locked_down(self):
        (answer, usage, model), (cmd, cwd, settings, stdin) = self.run_agy(
            stream([("read_file", {"Path": str(self.wt / "a.py")})]),
            transcript=[("read_file", {"Path": str(self.wt / "a.py")})])
        self.assertEqual(json.loads(answer), GOOD)
        self.assertEqual((usage["input_tokens"], model, cwd), (10, "claude-sonnet-5-5-medium", str(self.wt)))
        self.assertNotIn("--dangerously-skip-permissions", cmd)
        self.assertNotIn("-p", cmd)
        self.assertEqual(json.loads(stdin)["message"]["content"], "p")  # the prompt travels on stdin, not in argv
        deny = settings["permissions"]["deny"]
        self.assertTrue({"command(*)", "write_file(*)", "read_url(*)", "execute_url(*)", "mcp(*)"} <= set(deny))
        self.assertEqual(settings["permissions"]["allow"], [])
        self.assertIn("claude-sonnet-5-5-medium", cmd)

    def test_forbidden_tool_or_path_is_refused(self):
        for tools in ([("run_command", {"CommandLine": "ls"})], [("generate_image", {})],
                      [("read_file", {"Path": "/etc/passwd"})],
                      [("read_file", {"Path": "/home/user/.config/singularity/gemini-api-key"})]):
            with self.subTest(tools=tools):
                with self.assertRaises(agyreview.AgyViolation):
                    self.run_agy(stream(tools), transcript=tools)

    def test_transcript_call_outside_worktree_is_refused(self):
        with self.assertRaises(agyreview.AgyViolation):  # the stream hides the path, the transcript shows it
            self.run_agy(stream([("view_file", {})]), transcript=[("view_file", {"AbsolutePath": "/etc/shadow"})])

    def test_missing_transcript_fails_closed(self):
        runner = lambda cmd, cwd, t, stdin=None: (0, stream(), "")
        with self.assertRaises(agyreview.AgyViolation):
            agyreview.run_agy("p", self.wt, SCHEMA, brain_dir=self.tmp / "nobrain", runner=runner, which=lambda n: "/bin/true")

    def test_large_prompt_does_not_go_into_argv(self):
        big = "x" * 900_000
        seen = []
        (self.brain / CID / ".system_generated" / "logs" / "transcript.jsonl").write_text("")

        def runner(cmd, cwd, timeout, stdin=None):
            seen.append((sum(len(a) for a in cmd), stdin))
            return 0, stream(), ""

        agyreview.run_agy(big, self.wt, SCHEMA, brain_dir=self.brain, runner=runner, which=lambda n: "/bin/true")
        self.assertLess(seen[0][0], 100_000)
        self.assertGreater(len(seen[0][1]), 900_000)

    def test_timeout_audits_the_stream_and_is_not_a_failure_to_fall_back_from(self):
        (self.brain / CID / ".system_generated" / "logs" / "transcript.jsonl").write_text("")

        def runner(timed_tools):
            def run(cmd, cwd, timeout, stdin=None):
                e = sketch.SketchError("agy_timeout", "no answer")
                e.out, e.err = stream(timed_tools).rsplit("\n", 1)[0], ""  # no result event: it timed out
                raise e
            return run

        with self.assertRaises(agyreview.AgyTimeout):  # clean stream: a timeout, never AgyFailed
            agyreview.run_agy("p", self.wt, SCHEMA, brain_dir=self.brain, runner=runner([]), which=lambda n: "/bin/true")
        with self.assertRaises(agyreview.AgyViolation):  # a forbidden tool before the timeout wins
            agyreview.run_agy("p", self.wt, SCHEMA, brain_dir=self.brain, runner=runner([("run_command", {"CommandLine": "ls"})]),
                              which=lambda n: "/bin/true")

    def test_other_model_than_asked_is_refused(self):
        for bad in ("claude-sonnet-5-5-low", "gemini-3.8-flash-medium", None):
            with self.subTest(reported=bad):
                out = stream(model=bad) if bad else stream().replace('"init": {"model": "claude-sonnet-5-5-medium"}', '"init": {}')
                with self.assertRaises(agyreview.AgyFailed):
                    self.run_agy(out)

    def test_success_without_conversation_id_fails_closed(self):
        out = json.dumps({"event": "result", "result": {"status": "SUCCESS", "response": "x", "structured_output": GOOD}})
        runner = lambda cmd, cwd, t, stdin=None: (0, out, "")
        with self.assertRaises(agyreview.AgyViolation):
            agyreview.run_agy("p", self.wt, SCHEMA, brain_dir=self.brain, runner=runner, which=lambda n: "/bin/true")

    def test_quota_and_failure(self):
        bad = stream(result={"conversation_id": CID, "status": "ERROR", "error": "RESOURCE_EXHAUSTED"})
        with self.assertRaises(agyreview.AgyLimit):
            self.run_agy(bad, rc=1, err="RESOURCE_EXHAUSTED")
        with self.assertRaises(agyreview.AgyFailed):
            self.run_agy(bad.replace("RESOURCE_EXHAUSTED", "boom"), rc=1)


class ChainTest(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        for target in ("scan_text", "scan_dir"):
            m = mock.patch(f"tools.llm_worker.gitleaks.{target}")
            m.start()
            self.addCleanup(m.stop)
        m = mock.patch.object(config, "WORKTREES_DIR", self.tmp / "wt")
        m.start()
        self.addCleanup(m.stop)
        self.repo, self.base, self.head = make_repo()
        self.state = self.tmp / "state"

    def run_review(self, provider, **kw):
        return review.run_review("TASK-0001", self.base, self.head, provider, root=self.repo.root,
                                 runs_dir=self.tmp / "runs", attempts_dir=self.tmp / "attempts", log=lambda m: None,
                                 state_dir=self.state, now=NOW, **kw)

    @staticmethod
    def agy_ok(prompts=None):
        def run(prompt, wt):
            if prompts is not None:
                prompts.append(prompt)
            return json.dumps(GOOD), {"input_tokens": 5}, "claude-sonnet-5-5-medium"
        return run

    def test_auto_codex_limit_goes_to_agy_and_remembers_reset(self):
        def limited(prompt, wt):
            raise codex.CodexLimit("limit", NOW + timedelta(hours=2))

        prompts = []
        path, line, rec = self.run_review("auto", codex_runner=limited, agy_runner=self.agy_ok(prompts))
        self.assertEqual((rec["provider"], rec["model"], rec["fallback"]), ("agy-sonnet", "claude-sonnet-5-5-medium", "codex_limit"))
        self.assertEqual(rec["codex_reset_at"], (NOW + timedelta(hours=2)).isoformat(timespec="seconds"))
        self.assertIn("(agy-sonnet, head", line)
        self.assertNotIn("/work", prompts[0])
        self.assertEqual(limitstate.limited_until(NOW, self.state), NOW + timedelta(hours=2))
        att = [json.loads(x) for x in (self.tmp / "attempts" / codex.ATTEMPTS).read_text().splitlines()]
        self.assertEqual([(a["outcome"], a.get("provider")) for a in att], [("codex_limit", None), ("ok", "agy-sonnet")])

    def test_auto_service_says_codex_out_skips_codex_without_calling_it(self):
        def never(prompt, wt):
            raise AssertionError("codex must not be called")

        until = "2026-11-28T08:04:03+00:00"
        limits = lambda c: {"state": "out" if c == "codex" else "ok", "resets_at": until if c == "codex" else None}
        _, _, rec = self.run_review("auto", codex_runner=never, agy_runner=self.agy_ok(), limits=limits)
        self.assertEqual((rec["provider"], rec["fallback"], rec["codex_reset_at"]), ("agy-sonnet", "codex_limit_service", until))
        self.assertEqual(rec["fallbacks"][0]["codex_reset_at"], until)
        self.assertIsNotNone(limitstate.limited_until(NOW, self.state))

    def test_agy_prompt_names_worktree_without_touching_data(self):
        prompts = []
        _, _, rec = self.run_review("agy-sonnet", agy_runner=self.agy_ok(prompts))
        p = prompts[0]
        self.assertIn("read them in the current directory", p)
        self.assertNotIn("/work", p)
        self.assertIn("x = 2", p)  # the diff body is the same bytes the markers were hashed over

    def test_agy_data_containing_adapter_phrase_is_unchanged(self):
        repo, base, head = make_repo(changed={"tools/a.py": "# read them in /work\nx = 2\n"})
        prompts = []
        review.run_review("TASK-0001", base, head, "agy-sonnet", root=repo.root, runs_dir=self.tmp / "r", attempts_dir=self.tmp / "a",
                          log=lambda m: None, state_dir=self.state, now=NOW, agy_runner=self.agy_ok(prompts))
        self.assertIn("+# read them in /work", prompts[0])  # data is never rewritten after its hash was taken

    def test_success_does_not_erase_a_newer_limit(self):
        limitstate.record_limit(NOW + timedelta(hours=1), NOW + timedelta(seconds=5), self.state)  # recorded after this run began
        limitstate.clear(NOW, self.state)
        self.assertIsNotNone(limitstate.limited_until(NOW, self.state))
        limitstate.clear(NOW + timedelta(minutes=1), self.state)  # an older limit is cleared
        self.assertIsNone(limitstate.limited_until(NOW, self.state))

    def test_cache_write_failure_does_not_stop_the_review(self):
        def limited(prompt, wt):
            raise codex.CodexLimit("limit", NOW + timedelta(hours=2))

        with mock.patch.object(limitstate, "record_limit", side_effect=OSError("disk full")):
            _, _, rec = self.run_review("auto", codex_runner=limited, agy_runner=self.agy_ok())
        self.assertEqual(rec["provider"], "agy-sonnet")

    def test_auto_skips_codex_until_reset_then_returns(self):
        limitstate.record_limit(NOW + timedelta(hours=1), NOW, self.state)
        boom = lambda p, w: self.fail("codex must not run before the reset")
        _, _, rec = self.run_review("auto", codex_runner=boom, agy_runner=self.agy_ok())
        self.assertEqual((rec["provider"], rec["fallbacks"][0]["reason"]), ("agy-sonnet", "codex_limit_cached"))
        later = NOW + timedelta(hours=1, minutes=1)
        _, _, rec = review.run_review("TASK-0001", self.base, self.head, "auto", root=self.repo.root, runs_dir=self.tmp / "runs2",
                                      attempts_dir=self.tmp / "attempts", log=lambda m: None, state_dir=self.state, now=later,
                                      codex_runner=lambda p, w: (json.dumps(GOOD), {}),
                                      agy_runner=lambda p, w: self.fail("agy must not run after the reset"))
        self.assertEqual(rec["provider"], "codex")
        self.assertIsNone(limitstate.limited_until(later, self.state))

    def test_auto_other_codex_failure_does_not_reach_sonnet(self):
        def failing(prompt, wt):
            raise codex.CodexFailed("timeout")

        fake = FakeProvider([GOOD])
        _, _, rec = self.run_review("auto", codex_runner=failing, agy_runner=lambda p, w: self.fail("owner #456: usage limit only"),
                                    make_provider=lambda: fake)
        self.assertEqual((rec["provider"], rec["fallback"]), ("deepseek", "codex_failed"))

    def test_agy_timeout_is_an_error_not_a_paid_fallback(self):
        def timing_out(prompt, wt):
            raise agyreview.AgyTimeout("slow")

        def limited(prompt, wt):
            raise codex.CodexLimit("limit")

        fake = FakeProvider([GOOD])
        with self.assertRaises(ReviewError) as cm:
            self.run_review("auto", codex_runner=limited, agy_runner=timing_out, make_provider=lambda: fake)
        self.assertEqual((cm.exception.code, fake.calls), ("agy_timeout", []))

    def test_moving_ref_is_resolved_once_for_the_whole_chain(self):
        subprocess.run(["git", "branch", "mine", self.head], cwd=self.repo.root, check=True)

        def limited(prompt, wt):
            subprocess.run(["git", "branch", "-f", "mine", self.base], cwd=self.repo.root, check=True)  # the ref moves
            raise codex.CodexLimit("limit")

        prompts = []
        _, _, rec = review.run_review("TASK-0001", self.base, "mine", "auto", root=self.repo.root, runs_dir=self.tmp / "r",
                                      attempts_dir=self.tmp / "a", log=lambda m: None, state_dir=self.state, now=NOW,
                                      codex_runner=limited, agy_runner=self.agy_ok(prompts))
        self.assertEqual(rec["head"], self.head)
        self.assertIn("x = 2", prompts[0])  # the diff of the original head, not of the moved ref

    def test_agy_failure_falls_to_deepseek_and_violation_does_not(self):
        def limited(prompt, wt):
            raise codex.CodexLimit("limit")

        def failing(prompt, wt):
            raise agyreview.AgyLimit("quota")

        fake = FakeProvider([GOOD])
        _, _, rec = self.run_review("auto", codex_runner=limited, agy_runner=failing, make_provider=lambda: fake)
        self.assertEqual((rec["provider"], rec["fallback"], len(rec["fallbacks"])), ("deepseek", "agy_limit", 2))

        def violating(prompt, wt):
            raise agyreview.AgyViolation("run_command")

        for exc, code in ((agyreview.AgyFailed("boom"), "agy_failed"), (agyreview.AgyLimit("quota"), "agy_limit")):
            fake3 = FakeProvider([GOOD])  # explicit agy-sonnet fails closed: DeepSeek (paid) is never called

            def raising(prompt, wt, exc=exc):
                raise exc

            with self.assertRaises(ReviewError) as cm:
                self.run_review("agy-sonnet", agy_runner=raising, make_provider=lambda: fake3)
            self.assertEqual((cm.exception.code, fake3.calls), (code, []))

        fake2 = FakeProvider([GOOD])
        with self.assertRaises(ReviewError) as cm:
            self.run_review("agy-sonnet", agy_runner=violating, make_provider=lambda: fake2)
        self.assertEqual(cm.exception.code, "agent_tool_violation")
        self.assertEqual(fake2.calls, [])

    def test_explicit_codex_does_not_touch_state(self):
        def limited(prompt, wt):
            raise codex.CodexLimit("limit", NOW + timedelta(hours=2))

        self.run_review("codex", codex_runner=limited, make_provider=lambda: FakeProvider([GOOD]))
        self.assertFalse(self.state.exists())


if __name__ == "__main__":
    unittest.main()
