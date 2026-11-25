"""Gemini visual review through agy (TASK-0190). A fake runner stands in for agy: no network, no subscription."""

import json
import tempfile
import time
import unittest
from pathlib import Path
from unittest import mock

from test_visual import GOOD, Base

from tools.llm_worker import agyreview, agyvisual, config, visual

CID = "11111111-2222-3333-4444-555555555555"


def stream(answer=GOOD, model=agyvisual.MODEL, status="SUCCESS"):
    lines = [{"event": "init", "conversation_id": CID, "init": {"model": model}},
             {"event": "result", "result": {"conversation_id": CID, "status": status, "response": "x",
                                            "structured_output": answer,
                                            "usage": {"input_tokens": 10, "output_tokens": 2, "thinking_tokens": 3}}}]
    return "\n".join(json.dumps(x) for x in lines)


class Fake:
    """Runner with the signature of sketch.default_runner; records what agy would have been given."""

    def __init__(self, out=None, rc=0, err=""):
        self.out, self.rc, self.err, self.calls = out if out is not None else stream(), rc, err, []

    def __call__(self, cmd, cwd, timeout, stdin=None):
        self.calls.append((cmd, cwd, stdin, sorted(p.name for p in Path(cwd).iterdir())))
        return self.rc, self.out, self.err


class AgyVisualTest(Base):
    def run_it(self, fake, transcript="", **kw):
        brain = self.tmp / "brain"
        log = brain / CID / ".system_generated" / "logs"
        log.mkdir(parents=True, exist_ok=True)
        (log / "transcript.jsonl").write_text(transcript, encoding="utf-8")
        guard = kw.pop("guard", None) or visual.RateGuard(self.tmp / "agy.json", rpm=100, rpd=100)
        with mock.patch("tools.llm_worker.sketch.AGY_WS_DIR", self.tmp / "ws"), \
                mock.patch("tools.llm_worker.sketch.scoped_deny_rules", return_value=[]), \
                mock.patch("tools.llm_worker.sketch.find_agy", return_value="/usr/bin/agy"):
            return visual.run_visual("TASK-0126", self.frames, runs_dir=self.runs, shots_dir=self.shots, agy_runner=fake,
                                     agy_guard=guard, agy_brain_dir=brain, **kw)

    def test_default_provider_is_agy_and_writes_record(self):
        fake = Fake()
        path, md, rec = self.run_it(fake)
        cmd, cwd, stdin, ws_files = fake.calls[0]
        self.assertEqual(rec["provider"], "agy")
        self.assertEqual(rec["model"], agyvisual.MODEL)
        self.assertEqual(ws_files, ["frame01.png", "frame02.png"])  # frames copied under neutral names
        self.assertNotIn("--dangerously-skip-permissions", cmd)
        self.assertEqual(cmd[cmd.index("--mode") + 1], "plan")
        self.assertEqual(cmd[cmd.index("--model") + 1], agyvisual.MODEL)
        msg = json.loads(stdin)["message"]["content"]
        self.assertIn("a.png", msg)  # the frame name the findings must use
        self.assertIn("frame02.png", msg)
        self.assertEqual(rec["tokens"], {"prompt": 10, "completion": 2, "thinking": 3})
        self.assertTrue(path.name.endswith("-agy.json") and md.exists())

    def test_settings_deny_shell_and_writes(self):
        seen = {}

        def runner(cmd, cwd, timeout, stdin=None):
            seen["rules"] = json.loads(Path(cmd[cmd.index("--settings") + 1]).read_text())["permissions"]["deny"]
            return 0, stream(), ""
        self.run_it(runner)
        for rule in ("command(*)", "write_file(*)", "mcp(*)"):
            self.assertIn(rule, seen["rules"])

    def test_wrong_model_answers_is_refused(self):
        with self.assertRaises(visual.VisualError) as c:
            self.run_it(Fake(stream(model="other-model")))
        self.assertEqual(c.exception.code, "agy_failed")

    def test_unknown_tool_in_stream_is_a_violation(self):
        out = stream().split("\n")
        bad = {"event": "step_update", "step_update": {"conversation_id": CID, "tool_name": "run_command",
                                                       "tool_info": {"name": "run_command", "parameters": {}}}}
        with self.assertRaises(visual.VisualError) as c:
            self.run_it(Fake("\n".join([out[0], json.dumps(bad), out[1]])))
        self.assertEqual(c.exception.code, "agent_tool_violation")

    def test_read_outside_workspace_is_a_violation(self):
        step = {"event": "step_update", "step_update": {"conversation_id": CID, "tool_name": "read_file",
                                                        "tool_info": {"name": "read_file", "parameters": {}}}}
        out = stream().split("\n")
        tr = json.dumps({"tool_calls": [{"name": "read_file", "args": {"path": "/etc/passwd"}}]})
        with self.assertRaises(visual.VisualError) as c:
            self.run_it(Fake("\n".join([out[0], json.dumps(step), out[1]])), transcript=tr)
        self.assertEqual(c.exception.code, "agent_tool_violation")

    def test_overload_is_not_counted_but_other_failure_is(self):
        guard = visual.RateGuard(self.tmp / "agy.json", rpm=100, rpd=100)
        with self.assertRaises(visual.VisualError) as c:
            self.run_it(Fake(stream(status="ERROR"), rc=1, err="503 No capacity available"), guard=guard)
        self.assertEqual(c.exception.code, "agy_capacity")
        self.assertEqual(json.loads((self.tmp / "agy.json").read_text())["count"], 0)
        with self.assertRaises(visual.VisualError) as c:
            self.run_it(Fake(stream(status="ERROR"), rc=1, err="boom"), guard=guard)
        self.assertEqual(c.exception.code, "agy_failed")
        self.assertEqual(json.loads((self.tmp / "agy.json").read_text())["count"], 1)

    def test_quota_text_is_agy_quota(self):
        with self.assertRaises(visual.VisualError) as c:
            self.run_it(Fake(stream(status="ERROR"), rc=1, err="RESOURCE_EXHAUSTED"))
        self.assertEqual(c.exception.code, "agy_quota")

    def test_daily_limit_refuses_before_agy_runs(self):
        guard = visual.RateGuard(self.tmp / "agy.json", rpm=100, rpd=1)
        self.run_it(Fake(), guard=guard)
        fake = Fake()
        with self.assertRaises(visual.VisualError) as c:
            self.run_it(fake, guard=guard)
        self.assertEqual(c.exception.code, "rate_limit")
        self.assertEqual(fake.calls, [])

    def test_secret_in_criteria_refuses_before_agy(self):
        fake = Fake()
        with mock.patch("tools.llm_worker.gitleaks.scan_text", side_effect=visual.gitleaks.SecretDetected([{"rule": "r", "line": 1}])):
            with self.assertRaises(visual.VisualError) as c:
                self.run_it(fake)
        self.assertEqual(c.exception.code, "secret_detected")
        self.assertEqual(fake.calls, [])

    def test_frames_outside_shots_refused(self):
        outside = self.tmp / "x.png"
        outside.write_bytes(self.frames[0].read_bytes())
        with self.assertRaises(visual.VisualError) as c:
            visual.run_visual("TASK-0126", [outside], runs_dir=self.runs, shots_dir=self.shots, agy_runner=Fake())
        self.assertEqual(c.exception.code, "bad_frame")

    def test_key_provider_still_selectable_and_bad_provider_refused(self):
        with self.assertRaises(visual.VisualError) as c:
            visual.run_visual("TASK-0126", self.frames, shots_dir=self.shots, provider="nope")
        self.assertEqual(c.exception.code, "bad_provider")
        _, _, rec = visual.run_visual("TASK-0126", self.frames, shots_dir=self.shots, provider="key", dry_run=True)
        self.assertEqual(rec["provider"], "gemini-flash")


if __name__ == "__main__":
    unittest.main()
