"""Sketches through agy (TASK-0141): stream parsing, copying, refusals. Fake runner, no agy, no network."""

import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from tools.llm_worker import config, sketch
from tools.llm_worker.visual import RateGuard

FIXTURE = Path(config.PACKAGE_DIR) / "fixtures" / "agy_sketch_stream.ndjson"
MAIN = "79326e30-603b-445c-a531-fdd176a874ba"
SUB = "41d22a1b-5ee2-4191-9a5d-926bf2ff1bb5"
JPEG = b"\xff\xd8\xff\xe0" + b"x" * 40


class SketchTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        t = Path(self.tmp.name).resolve()
        self.root, self.shots, self.brain, self.runs = t / "repo", t / "shots-out", t / "brain", t / "runs"
        for d in (self.root, self.shots, self.brain):
            d.mkdir()
        self.prompt = self.root / "brief.md"
        self.prompt.write_text("Draw a paper desk sketch.", encoding="utf-8")
        self.calls = []
        self.guard = RateGuard(t / "usage.json", rpm=5, rpd=20)
        self.addCleanup(self.tmp.cleanup)
        m = mock.patch("tools.llm_worker.gitleaks.scan_text")
        m.start()
        self.addCleanup(m.stop)

    def runner(self, stream=None, rc=0, image=True, err=""):
        def run(cmd, cwd, timeout):
            self.calls.append((cmd, cwd, timeout))
            if cmd[1] == "--version":
                return 0, "agy 1.5.15\n", ""
            if image:
                (self.brain / MAIN).mkdir(exist_ok=True)
                (self.brain / MAIN / "sketch_1.jpg").write_bytes(JPEG)
                (self.brain / MAIN / "notes.md").write_text("not an image")
            return rc, FIXTURE.read_text() if stream is None else stream, err
        return run

    def go(self, **kw):
        args = dict(runs_dir=self.runs, shots_dir=self.shots, root=self.root, brain_dir=self.brain,
                    which=lambda n: "/bin/true", guard=self.guard)
        args.update(kw)
        return sketch.run_sketch("TASK-0141", self.prompt, **args)

    def test_parse_fixture(self):
        ids, result, tools = sketch.parse_stream(FIXTURE.read_text())
        self.assertEqual(ids, [MAIN, SUB])
        self.assertEqual(result["status"], "SUCCESS")
        self.assertIn("invoke_subagent", tools)

    def test_parse_ignores_garbage_and_bad_ids(self):
        ids, result, _ = sketch.parse_stream('not json\n[1]\n{"event":"init","conversation_id":"../../etc"}\n')
        self.assertEqual((ids, result), ([], None))

    def test_copy_and_record(self):
        path, md, rec = self.go(runner=self.runner(), refs=[], count=2)
        self.assertEqual([f["name"] for f in rec["files"]], ["sketch_1.jpg"])
        out = self.shots / "sketches" / "TASK-0141" / "sketch_1.jpg"
        self.assertEqual(out.read_bytes(), JPEG)
        saved = json.loads(path.read_text())
        self.assertEqual(saved["conversation_id"], MAIN)
        self.assertEqual(saved["agy_version"], "agy 1.5.15")
        self.assertIn("approved by", saved["attribution"][0])
        self.assertTrue(md.read_text().count("sketch_1.jpg") >= 1)
        cmd = self.calls[-1][0]
        self.assertNotIn("--dangerously-skip-permissions", cmd)
        self.assertEqual(cmd[cmd.index("--mode") + 1], "accept-edits")
        self.assertIn("exactly 2", cmd[2])

    def test_old_images_are_not_copied(self):
        (self.brain / MAIN).mkdir()
        old = self.brain / MAIN / "old.jpg"
        old.write_bytes(JPEG)
        import os
        os.utime(old, (1, 1))
        _, _, rec = self.go(runner=self.runner())
        self.assertEqual([f["name"] for f in rec["files"]], ["sketch_1.jpg"])

    def test_second_run_does_not_overwrite(self):
        self.go(runner=self.runner())
        _, _, rec = self.go(runner=self.runner())
        self.assertEqual(rec["files"][0]["name"], "sketch_1-1.jpg")

    def test_partial_result_is_marked(self):
        _, md, rec = self.go(runner=self.runner(), count=3)
        self.assertTrue(rec["partial"])
        self.assertEqual((rec["requested"], len(rec["files"])), (3, 1))
        self.assertIn("PARTIAL", md.read_text())

    def test_complete_result_not_partial(self):
        self.assertFalse(self.go(runner=self.runner(), count=1)[2]["partial"])

    def test_same_basename_refs_do_not_collapse(self):
        a, b = self.shots / "a", self.shots / "b"
        a.mkdir(), b.mkdir()
        for d in (a, b):
            (d / "f.png").write_bytes(b"\x89PNG\r\n\x1a\n" + b"0" * 20)
        self.go(runner=self.runner(), refs=[a / "f.png", b / "f.png"])
        cmd = self.calls[-1][0]
        self.assertIn("1-f.png", cmd[2])
        self.assertIn("2-f.png", cmd[2])

    def test_attribution_has_model_and_prompt(self):
        _, _, rec = self.go(runner=self.runner())
        line = rec["attribution"][0]
        self.assertIn("image-generator", rec["model"])
        self.assertIn(rec["prompt_sha256"], line)
        self.assertIn("brief.md", line)
        self.assertEqual(rec["prompt"], "Draw a paper desk sketch.")

    def test_model_from_events(self):
        ev = '{"event":"step_update","step_update":{"model":"gemini-x"}}\n'
        self.assertEqual(sketch.find_model(ev), "gemini-x")

    def test_no_images(self):
        with self.assertRaises(sketch.SketchError) as c:
            self.go(runner=self.runner(image=False))
        self.assertEqual(c.exception.code, "no_images")

    def test_agy_missing_when_nothing_executable(self):
        with self.assertRaises(sketch.SketchError) as c:
            sketch.find_agy(lambda n: str(self.root / "nope"))
        self.assertEqual(c.exception.code, "agy_missing")

    def test_not_logged_in(self):
        with self.assertRaises(sketch.SketchError) as c:
            self.go(runner=self.runner(stream="", rc=1, err="Please log in with agy auth login", image=False))
        self.assertEqual(c.exception.code, "agy_not_logged_in")

    def test_failed_status(self):
        bad = '{"event":"result","result":{"conversation_id":"%s","status":"ERROR"}}\n' % MAIN
        with self.assertRaises(sketch.SketchError) as c:
            self.go(runner=self.runner(stream=bad, image=False))
        self.assertEqual(c.exception.code, "agy_failed")

    def test_refs_only_from_shots_out(self):
        outside = self.root / "a.png"
        outside.write_bytes(b"\x89PNG\r\n\x1a\n" + b"0" * 20)
        with self.assertRaises(sketch.SketchError) as c:
            self.go(runner=self.runner(), refs=[outside])
        self.assertEqual(c.exception.code, "bad_ref")

    def test_ref_copied_to_workspace_and_named_in_prompt(self):
        ref = self.shots / "frame.png"
        ref.write_bytes(b"\x89PNG\r\n\x1a\n" + b"0" * 20)
        _, _, rec = self.go(runner=self.runner(), refs=[ref])
        cmd, cwd, _ = self.calls[-1]
        self.assertIn(cwd, cmd[2])
        self.assertNotIn(self.root, Path(cwd).parents)
        self.assertNotIn(str(config.REPO_ROOT), cwd)
        self.assertIn("frame.png", cmd[2])
        self.assertEqual(rec["refs"], ["frame.png"])

    def test_prompt_must_be_in_repo_and_count_bounded(self):
        with self.assertRaises(sketch.SketchError):
            sketch.run_sketch("TASK-0141", self.shots / "x.md", root=self.root, shots_dir=self.shots)
        with self.assertRaises(sketch.SketchError) as c:
            self.go(runner=self.runner(), count=9)
        self.assertEqual(c.exception.code, "bad_count")
        with self.assertRaises(sketch.SketchError) as c:
            sketch.run_sketch("bad", self.prompt, root=self.root)
        self.assertEqual(c.exception.code, "bad_task")

    def test_rate_guard_refuses(self):
        guard = RateGuard(Path(self.tmp.name) / "u2.json", rpm=1, rpd=20)
        self.go(runner=self.runner(), guard=guard)
        with self.assertRaises(sketch.VisualError) as c:
            self.go(runner=self.runner(), guard=guard)
        self.assertEqual(c.exception.code, "rate_limit")

    def test_dry_run_runs_nothing(self):
        path, md, rec = self.go(runner=self.runner(), dry_run=True, which=lambda n: "/bin/true")
        self.assertIsNone(path)
        self.assertEqual(self.calls, [])


if __name__ == "__main__":
    unittest.main()
