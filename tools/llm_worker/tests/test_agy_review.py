"""Second review fallback to Sonnet 5.5 through agy (#456, TASK-0146). No network, no real agy."""

import json
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest import mock

from helpers import FakeProvider
from test_review import GOOD, make_repo

from tools.llm_worker import agyreview, codex, config, limitstate, review, sketch
from tools.llm_worker.review import ReviewError

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
        self.assertEqual(rules, [r for r in rules if r.startswith(("read_file(", "view_file("))])

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


class RunAgyTest(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.wt = self.tmp / "wt"
        self.wt.mkdir()
        self.brain = self.tmp / "brain"
        p = mock.patch.object(sketch, "scoped_deny_rules", lambda allowed: [])  # the real walk is tested below
        p.start()
        self.addCleanup(p.stop)
        (self.brain / CID / ".system_generated" / "logs").mkdir(parents=True)

    def run_agy(self, out, rc=0, err="", transcript=()):
        (self.brain / CID / ".system_generated" / "logs" / "transcript.jsonl").write_text(
            "\n".join(json.dumps({"tool_calls": [{"name": n, "args": a}]}) for n, a in transcript))
        seen = []

        def runner(cmd, cwd, timeout):
            seen.append((cmd, cwd, json.loads(Path(cmd[cmd.index("--settings") + 1]).read_text())))
            return rc, out, err

        res = agyreview.run_agy("p", self.wt, config.PACKAGE_DIR / "schemas" / "review.schema.json",
                                brain_dir=self.brain, runner=runner, which=lambda n: "/bin/true")
        return res, seen[0]

    def test_answer_parsed_and_command_is_locked_down(self):
        (answer, usage, model), (cmd, cwd, settings) = self.run_agy(
            stream([("read_file", {"Path": str(self.wt / "a.py")})]),
            transcript=[("read_file", {"Path": str(self.wt / "a.py")})])
        self.assertEqual(json.loads(answer), GOOD)
        self.assertEqual((usage["input_tokens"], model, cwd), (10, "claude-sonnet-5-5-medium", str(self.wt)))
        self.assertNotIn("--dangerously-skip-permissions", cmd)
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
        runner = lambda cmd, cwd, t: (0, stream(), "")
        with self.assertRaises(agyreview.AgyViolation):
            agyreview.run_agy("p", self.wt, "s.json", brain_dir=self.tmp / "nobrain", runner=runner, which=lambda n: "/bin/true")

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
