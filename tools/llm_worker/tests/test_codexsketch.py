"""Codex sketches (TASK-0145): JSONL parsing, limit, cap, engine fallback. Fake runners, no codex, no network.
The JSONL image item shape is an assumption until a live run after the limit reset (see README)."""

import base64
import json
import subprocess
import tempfile
import unittest
import os
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest import mock

from tools.llm_worker import codex, codexsketch, config, limitstate, sketch
from tools.llm_worker.tests.test_sketch import png, JPEG

THREAD = "019e0000-aaaa-bbbb-cccc-000000000001"


def ev(**kw):
    return json.dumps(kw)


def stream(*lines):
    return "\n".join(lines) + "\n"


LIMIT = stream(ev(type="thread.started", thread_id=THREAD),
               ev(type="turn.failed", error={"message": "You've hit your usage limit. Try again at 3:44 PM."}))


class CodexSketchTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        t = Path(self.tmp.name).resolve()
        self.root, self.shots, self.runs, self.state, self.ledger, self.home = (t / n for n in ("repo", "shots-out", "runs", "state", "ledger", "home"))
        for d in (self.root, self.shots, self.home / ".codex"):
            d.mkdir(parents=True)
        subprocess.run(["git", "init", "-q", str(self.root)], check=True)
        self.prompt = self.root / "brief.md"
        self.prompt.write_text("Draw a desk.", encoding="utf-8")
        self.calls = []
        type(self)._real_bwrap = staticmethod(codex.bwrap_argv)  # before the mock below hides it
        for p in (mock.patch("tools.llm_worker.gitleaks.scan_text"),
                  mock.patch.object(codex, "bwrap_argv", lambda *a, **k: ["bwrap"]),
                  mock.patch.object(codex, "codex_mounts", lambda exe, home=None: (Path(exe), [], Path("/usr/bin")))):
            p.start()
            self.addCleanup(p.stop)

    _real_bwrap = staticmethod(codex.bwrap_argv)

    def runner(self, out, rc=0, err="", save=None):
        def run(argv, prompt, timeout, env):
            self.calls.append((argv, prompt, env))
            if save:
                d = self.home / ".codex" / "generated_images" / THREAD
                d.mkdir(parents=True, exist_ok=True)
                (d / "ig_1.png").write_bytes(save)
            return rc, out, err
        return run

    def go(self, runner, **kw):
        args = dict(runs_dir=self.runs, shots_dir=self.shots, root=self.root, runner=runner, state_dir=self.state,
                    ledger_dir=self.ledger, home=self.home, find=lambda: "/usr/bin/codex", environ={"PATH": "/usr/bin", "SECRET_KEY": "x"})
        args.update(kw)
        return codexsketch.run_codex_sketch("TASK-0145", self.prompt, **args)

    def test_parse_image_item_and_thread(self):
        out = stream(ev(type="thread.started", thread_id=THREAD),
                     ev(type="item.completed", item={"type": "image_generation", "path": "/x/a.png"}),
                     ev(type="item.completed", item={"type": "agent_message", "text": "done"}))
        thread, paths, blobs, model, shell = codexsketch.parse_run(out)
        self.assertEqual((thread, paths, blobs, shell), (THREAD, ["/x/a.png"], [], []))

    def test_parse_flags_shell(self):
        out = stream(ev(type="item.completed", item={"type": "command_execution", "command": "ls"}))
        self.assertEqual(codexsketch.parse_run(out)[4], ["command_execution"])

    def test_image_from_session_dir(self):
        out = stream(ev(type="thread.started", thread_id=THREAD), ev(type="turn.completed", usage={}))
        _, _, rec = self.go(self.runner(out, save=png()))
        self.assertEqual(rec["engine"], "codex")
        self.assertEqual(rec["model"], "codex image_generation")
        self.assertEqual(len(rec["files"]), 1)
        self.assertIn("Codex CLI", rec["attribution"][0])
        self.assertIn("approved by", rec["attribution"][0])
        self.assertTrue((self.shots / "sketches" / "TASK-0145" / "ig_1.png").exists())

    def test_image_from_base64_event(self):
        b = base64.b64encode(png(idat=b"y" * 200)).decode()
        out = stream(ev(type="thread.started", thread_id=THREAD), ev(type="item.completed", item={"type": "image_generation_call", "result": b}))
        _, _, rec = self.go(self.runner(out))
        self.assertEqual(rec["files"][0]["bytes"], len(png(idat=b"y" * 200)))

    def test_event_path_outside_codex_images_is_ignored(self):
        evil = self.home / "secret.png"
        evil.write_bytes(png())
        out = stream(ev(type="thread.started", thread_id=THREAD), ev(type="item.completed", item={"type": "image_generation", "path": str(evil)}))
        with self.assertRaises(sketch.SketchError) as c:
            self.go(self.runner(out))
        self.assertEqual(c.exception.code, "no_images")

    def test_truncated_image_refused(self):
        out = stream(ev(type="thread.started", thread_id=THREAD))
        with self.assertRaises(sketch.SketchError) as c:
            self.go(self.runner(out, save=png(iend=False)))
        self.assertEqual(c.exception.code, "no_images")

    def test_limit_records_reset_and_code(self):
        with self.assertRaises(sketch.SketchError) as c:
            self.go(self.runner(LIMIT, rc=1))
        self.assertEqual(c.exception.code, "codex_quota")
        self.assertIsNotNone(limitstate.limited_until(datetime.now(timezone.utc) - timedelta(days=2), self.state))

    def test_remembered_limit_blocks_without_call(self):
        now = datetime.now(timezone.utc)
        limitstate.record_limit(now + timedelta(hours=1), now, self.state)
        with self.assertRaises(sketch.SketchError) as c:
            self.go(self.runner(""))
        self.assertEqual((c.exception.code, self.calls), ("codex_quota", []))

    def test_other_failure(self):
        with self.assertRaises(sketch.SketchError) as c:
            self.go(self.runner(stream(ev(type="turn.failed", error={"message": "boom"})), rc=1, err="bad"))
        self.assertEqual(c.exception.code, "codex_failed")

    def test_shell_use_refused_and_recorded(self):
        out = stream(ev(type="thread.started", thread_id=THREAD), ev(type="item.completed", item={"type": "command_execution"}))
        with self.assertRaises(sketch.SketchError) as c:
            self.go(self.runner(out, save=png()))
        self.assertEqual(c.exception.code, "agent_tool_violation")
        self.assertEqual(len(list(self.runs.glob("sketch-*.json"))), 1)
        self.assertFalse((self.shots / "sketches").exists())

    def test_daily_cap(self):
        out = stream(ev(type="thread.started", thread_id=THREAD))
        for _ in range(2):
            with self.assertRaises(sketch.SketchError):
                self.go(self.runner(out), daily_cap=2)
        n = len(self.calls)
        with self.assertRaises(sketch.SketchError) as c:
            self.go(self.runner(out), daily_cap=2)
        self.assertEqual((c.exception.code, len(self.calls)), ("codex_daily_cap", n))

    def test_command_is_sandboxed_and_clean(self):
        out = stream(ev(type="thread.started", thread_id=THREAD))
        self.go(self.runner(out, save=png()))
        argv, prompt, env = self.calls[0]
        self.assertIn("read-only", argv)
        self.assertNotIn("--dangerously-bypass-approvals-and-sandbox", argv)
        self.assertNotIn("SECRET_KEY", env)
        self.assertIn("ONE image", prompt)

    def test_one_image_per_call(self):
        with self.assertRaises(sketch.SketchError) as c:
            codexsketch.run_codex_sketch("TASK-0145", self.prompt, count=2, root=self.root)
        self.assertEqual(c.exception.code, "bad_count")

    def test_land_lock_blocks_live_pid_only(self):
        lock = self.home / "land.lock"
        lock.write_text(str(os.getpid()))
        with self.assertRaises(sketch.SketchError) as c:
            self.go(self.runner(""), land_lock=lock)
        self.assertEqual((c.exception.code, self.calls), ("land_in_progress", []))
        lock.write_text("999999999")  # no such pid: a stale lock
        out = stream(ev(type="thread.started", thread_id=THREAD))
        self.go(self.runner(out, save=png()), land_lock=lock)
        self.assertEqual(len(self.calls), 1)

    def test_bad_thread_id_is_ignored(self):
        out = stream(ev(type="thread.started", thread_id="../../.."))
        self.assertIsNone(codexsketch.parse_run(out)[0])

    def test_item_started_and_web_search_are_violations(self):
        for kind in ("command_execution", "web_search"):
            out = stream(ev(type="item.started", item={"type": kind}))
            self.assertEqual(codexsketch.parse_run(out)[4], [kind])

    def test_real_bwrap_argv_without_schema(self):
        with mock.patch.object(codex, "bwrap_argv", self._real_bwrap), \
             mock.patch.object(codex, "codex_mounts", lambda exe, home=None: (Path(exe), [], Path("/usr/bin"))):
            a = codex.bwrap_argv("/ws", None, "/usr/bin/codex", home=self.home, bwrap="bwrap")
        self.assertNotIn("/schema.json", a)
        self.assertEqual(a[-2:], ["--chdir", "/work"])

    def test_dry_run_calls_nothing(self):
        _, _, rec = self.go(self.runner(""), dry_run=True)
        self.assertEqual((rec["engine"], self.calls), ("codex", []))


class EngineTest(unittest.TestCase):
    def call(self, engine, gemini, count=1):
        codex_calls = []

        def fake_codex(*a, **k):
            codex_calls.append(k.get("fallback"))
            return None, None, {"engine": "codex"}
        with mock.patch.object(codexsketch, "run_gemini", gemini), mock.patch.object(codexsketch, "run_codex_sketch", fake_codex):
            try:
                r = codexsketch.run_engine(engine, "TASK-0145", "p.md", (), count)
            except sketch.SketchError as e:
                r = e
        return r, codex_calls

    def test_auto_uses_gemini_when_it_works(self):
        r, c = self.call("auto", lambda *a, **k: (None, None, {"engine": "gemini"}))
        self.assertEqual((r[2]["engine"], c), ("gemini", []))

    def test_auto_falls_back_on_gemini_quota(self):
        def quota(*a, **k):
            raise sketch.SketchError("agy_quota", "reset after 2h")
        r, c = self.call("auto", quota)
        self.assertEqual(r[2]["engine"], "codex")
        self.assertEqual(c[0]["code"], "agy_quota")

    def test_auto_does_not_fall_back_on_other_errors(self):
        def bad(*a, **k):
            raise sketch.SketchError("agy_failed", "x")
        r, c = self.call("auto", bad)
        self.assertEqual((r.code, c), ("agy_failed", []))

    def test_gemini_engine_never_uses_codex(self):
        def quota(*a, **k):
            raise sketch.SketchError("agy_quota", "x")
        r, c = self.call("gemini", quota)
        self.assertEqual((r.code, c), ("agy_quota", []))

    def test_auto_multi_image_does_not_fall_back(self):
        def quota(*a, **k):
            raise sketch.SketchError("agy_quota", "x")
        r, c = self.call("auto", quota, count=2)
        self.assertEqual((r.code, c), ("agy_quota", []))

    def test_codex_engine_skips_gemini(self):
        def boom(*a, **k):
            raise AssertionError("gemini called")
        r, c = self.call("codex", boom)
        self.assertEqual(len(c), 1)


if __name__ == "__main__":
    unittest.main()
