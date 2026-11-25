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


SEEN = {**GOOD, "findings": [{**GOOD["findings"][0], "frame": "frame01.png"}]}  # the model sees neutral names


def stream(answer=SEEN, model=agyvisual.MODEL, status="SUCCESS"):
    lines = [{"event": "init", "conversation_id": CID, "init": {"model": model}},
             {"event": "result", "result": {"conversation_id": CID, "status": status, "response": "x",
                                            "structured_output": answer,
                                            "usage": {"input_tokens": 10, "output_tokens": 2, "thinking_tokens": 3}}}]
    return "\n".join(json.dumps(x) for x in lines)


class Fake:
    """Runner with the signature of sketch.default_runner; records what agy would have been given. Like the real
    agy it leaves a transcript: a read of every file of the workspace, unless reads=False."""

    brain = None

    def __init__(self, out=None, rc=0, err="", reads=True, media=True, tool="read_file"):
        self.out, self.rc, self.err, self.reads, self.media, self.tool, self.calls = (
            out if out is not None else stream(), rc, err, reads, media, tool, [])

    def write_transcript(self, cwd):
        log = self.brain / CID / ".system_generated" / "logs"
        log.mkdir(parents=True, exist_ok=True)
        files = sorted(Path(cwd).iterdir()) if self.reads else []
        rows = [{"tool_calls": [{"name": self.tool, "args": {"Path": json.dumps(str(p))}} for p in files]}]
        rows += [{"type": "GENERIC", "media": [{"mime_type": "image/png", "uri": "file:///x"}]} for _ in files if self.media]
        (log / "transcript.jsonl").write_text("\n".join(json.dumps(r) for r in rows), encoding="utf-8")

    def __call__(self, cmd, cwd, timeout, stdin=None):
        self.calls.append((cmd, cwd, stdin, sorted(p.name for p in Path(cwd).iterdir())))
        if self.brain is not None:
            self.write_transcript(cwd)
        return self.rc, self.out, self.err


class AgyVisualTest(Base):
    def run_it(self, fake, transcript=None, scan_error=None, **kw):
        brain = self.tmp / "brain"
        if transcript is not None:  # an explicit transcript replaces the runner's own
            log = brain / CID / ".system_generated" / "logs"
            log.mkdir(parents=True, exist_ok=True)
            (log / "transcript.jsonl").write_text(transcript, encoding="utf-8")
        elif isinstance(fake, Fake):
            fake.brain = brain
        guard = kw.pop("guard", None) or visual.RateGuard(self.tmp / "agy.json", rpm=100, rpd=100)
        # The real scanner needs a gitleaks binary that CI does not install; the other visual tests stub it too.
        with mock.patch("tools.llm_worker.sketch.AGY_WS_DIR", self.tmp / "ws"), \
                mock.patch("tools.llm_worker.gitleaks.scan_text", side_effect=scan_error), \
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
        self.assertIn("frame02.png", msg)
        self.assertNotIn("a.png", msg)  # a caller's file name is never sent
        self.assertEqual(rec["result"]["findings"][0]["frame"], "a.png")  # mapped back from frame01.png
        self.assertNotIn("unverified", rec["result"]["findings"][0])
        self.assertEqual(rec["tokens"], {"prompt": 10, "completion": 2, "thinking": 3})
        self.assertTrue(path.name.endswith("-agy.json") and md.exists())

    def test_settings_deny_shell_and_writes(self):
        seen = {}

        class Spy(Fake):
            def __call__(self, cmd, cwd, timeout, stdin=None):
                seen["rules"] = json.loads(Path(cmd[cmd.index("--settings") + 1]).read_text())["permissions"]["deny"]
                return super().__call__(cmd, cwd, timeout, stdin)
        self.run_it(Spy())
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

    def test_answer_without_successful_reads_of_every_frame_is_not_published(self):
        def only_first(f):  # reads one frame of two
            def write(cwd):
                log = f.brain / CID / ".system_generated" / "logs"
                log.mkdir(parents=True, exist_ok=True)
                (log / "transcript.jsonl").write_text("\n".join(json.dumps(r) for r in [
                    {"tool_calls": [{"name": "read_file", "args": {"Path": json.dumps(str(Path(cwd) / "frame01.png"))}}]},
                    {"media": [{"mime_type": "image/png"}]}]), encoding="utf-8")
            f.write_transcript = write
            return f
        cases = (Fake(reads=False), only_first(Fake()), Fake(tool="find_by_name"), Fake(tool="list_dir"),
                 Fake(media=False))  # nothing read; one frame; a search; a listing; reads that attached nothing
        for fake in cases:
            with self.assertRaises(visual.VisualError) as c:
                self.run_it(fake)
            self.assertEqual(c.exception.code, "agy_failed")
            self.assertIn("no successful read of", str(c.exception))
        self.assertFalse(self.runs.exists() and list(self.runs.glob("*.json")))

    def test_capacity_error_in_result_response_is_not_counted(self):
        guard = visual.RateGuard(self.tmp / "agy.json", rpm=100, rpd=100)
        out = stream(status="ERROR").replace('"response": "x"', '"response": "503 No capacity available"')
        with self.assertRaises(visual.VisualError) as c:
            self.run_it(Fake(out, rc=1), guard=guard)
        self.assertEqual(c.exception.code, "agy_capacity")
        self.assertEqual(json.loads((self.tmp / "agy.json").read_text())["count"], 0)

    def test_refund_of_an_earlier_day_keeps_todays_count(self):
        day = 86400.0 * 20000
        now = [day - 10]
        guard = visual.RateGuard(self.tmp / "agy.json", rpm=100, rpd=5, clock=lambda: now[0])
        old = guard.admit()
        now[0] = day + 10
        guard.admit()  # first attempt of the new day
        guard.refund(old)  # the attempt of the previous day: today's count stays
        st = json.loads((self.tmp / "agy.json").read_text())
        self.assertEqual((st["count"], st["recent"]), (1, [day + 10]))

    def test_sketch_with_visual_provider_flag(self):
        with mock.patch.object(visual, "run_visual", side_effect=visual.VisualError("agy_quota", "x")) as rv:
            for argv, want in ((["--with-visual"], "agy"), (["--with-visual", "--visual-provider", "key"], "key")):
                from tools.llm_worker import __main__ as cli
                cli.main(["sketch", "--task", "TASK-0126", "--prompt-file", "x.md", "--ref", str(self.frames[0]), "--dry-run"] + argv)
                self.assertEqual(rv.call_args.kwargs["provider"], want)

    def test_overload_is_not_counted_but_other_failure_is(self):
        guard = visual.RateGuard(self.tmp / "agy.json", rpm=100, rpd=100)
        with self.assertRaises(visual.VisualError) as c:
            self.run_it(Fake(stream(status="ERROR"), rc=1, err="503 No capacity available"), guard=guard)
        self.assertEqual(c.exception.code, "agy_capacity")
        st = json.loads((self.tmp / "agy.json").read_text())
        self.assertEqual((st["count"], st["recent"]), (0, []))
        with self.assertRaises(visual.VisualError) as c:
            self.run_it(Fake(stream(status="ERROR"), rc=1, err="boom"), guard=guard)
        self.assertEqual(c.exception.code, "agy_failed")
        st = json.loads((self.tmp / "agy.json").read_text())
        self.assertEqual((st["count"], len(st["recent"])), (1, 1))
        # an ordinary failure text is not an overload
        with self.assertRaises(visual.VisualError) as c:
            self.run_it(Fake(stream(status="ERROR").replace('"x"', '"the frames look fine"'), rc=1, err="boom"), guard=guard)
        self.assertEqual(c.exception.code, "agy_failed")

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
        with self.assertRaises(visual.VisualError) as c:
            self.run_it(fake, scan_error=visual.gitleaks.SecretDetected([{"rule": "r", "line": 1}]))
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
        with mock.patch("tools.llm_worker.gitleaks.scan_text"):  # no gitleaks binary needed
            _, _, rec = visual.run_visual("TASK-0126", self.frames, shots_dir=self.shots, provider="key", dry_run=True)
        self.assertEqual(rec["provider"], "gemini-flash")


if __name__ == "__main__":
    unittest.main()
