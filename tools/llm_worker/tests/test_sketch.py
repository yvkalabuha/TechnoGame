"""Sketches through agy (TASK-0141): stream parsing, copying, refusals. Fake runner, no agy, no network."""

import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from tools.llm_worker import config, gitleaks, sketch
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
        subprocess.run(["git", "init", "-q", str(self.root)], check=True)
        self.prompt = self.root / "brief.md"
        self.prompt.write_text("Draw a paper desk sketch.", encoding="utf-8")
        self.calls = []
        self.guard = RateGuard(t / "usage.json", rpm=50, rpd=50)
        self.addCleanup(self.tmp.cleanup)
        m = mock.patch("tools.llm_worker.gitleaks.scan_text")
        self.scan = m.start()
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
                    which=lambda n: "/bin/true", guard=self.guard, sleep=lambda s: None)
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

    def stream_with(self, step, result_response="Done"):
        lines = [json.dumps({"event": "init", "conversation_id": MAIN, "init": {"tools": ["run_command", "browser_click_element"]}}),
                 json.dumps({"event": "step_update", "step_update": dict(conversation_id=MAIN, **step)}),
                 json.dumps({"event": "result", "result": {"conversation_id": MAIN, "status": "SUCCESS", "response": result_response}})]
        return "\n".join(lines) + "\n"

    def test_settings_allowlist_passed(self):
        self.go(runner=self.runner())
        cmd = self.calls[-1][0]
        s = json.loads(cmd[cmd.index("--settings") + 1])
        self.assertEqual(s["permissions"]["allow"], [])
        self.assertNotIn("--dangerously-skip-permissions", cmd)

    def test_tool_violation_refused_and_recorded(self):
        for step in ({"tool_name": "browser_click_element"}, {"tool_name": "read_url_content"}, {"tool_name": "call_mcp_tool"},
                     {"tool_name": "search_web"}, {"tool_name": "write_to_file"},
                     {"tool_name": "view_file", "args": {"AbsolutePath": "/home/u/.config/gh/hosts.yml"}},
                     {"tool_name": "view_file", "args": {"AbsolutePath": "~/.ssh/id_rsa"}},
                     {"tool_name": "list_dir", "args": {"DirectoryPath": "../.."}}, {"tool_name": "view_file"},
                     {"tool_name": "run_command", "args": {"CommandLine": "curl http://x"}},
                     {"tool_name": "run_command", "args": {"CommandLine": "ls -la"}},
                     {"tool_name": "run_command", "args": {"CommandLine": "file a.png"}},
                     {"tool_name": "run_command", "args": {"CommandLine": "ls; cat .env"}}, {"tool_name": "run_command"}):
            with self.subTest(step=step), self.assertRaises(sketch.SketchError) as c:
                self.go(runner=self.runner(stream=self.stream_with(step)))
            self.assertEqual(c.exception.code, "agent_tool_violation")
        self.assertTrue(list(self.runs.glob("sketch-*.json")))
        self.assertFalse((self.shots / "sketches").exists())

    def test_allowed_tools_pass(self):
        ws = self.root
        for step in ({"tool_name": "generate_image"}, {"tool_name": "finish"}, {"tool_name": "wait"},
                     {"tool_name": "view_file", "args": {"AbsolutePath": str(ws / "1-frame.png")}},
                     {"tool_name": "list_dir", "args": {"DirectoryPath": str(ws)}},
                     ):
            with self.subTest(step=step):
                self.assertEqual(sketch.tool_violations(self.stream_with(step), str(ws)), [])

    def test_view_file_in_workspace_passes_through_run(self):
        step = {"tool_name": "view_file", "args": {"AbsolutePath": "1-frame.png"}}
        _, _, rec = self.go(runner=self.runner(stream=self.stream_with(step)))
        self.assertEqual(len(rec["files"]), 1)

    def test_quota_words_in_successful_run_are_ignored(self):
        stream = self.stream_with({"step_type": "agent_response", "text_delta": "quota 429"}, "Done, no quota problems (429 tokens)")
        stream = stream.replace('"status": "SUCCESS"', '"status": "SUCCESS", "usage": {"input_tokens": 429}')
        _, _, rec = self.go(runner=self.runner(stream=stream))
        self.assertEqual(len(rec["files"]), 1)

    def test_quota_with_success_and_no_image_is_quota(self):
        with self.assertRaises(sketch.SketchError) as c:
            self.go(runner=self.runner(stream=self.stream_with({"step_type": "agent_response"}, "ResourceExhausted"), image=False))
        self.assertEqual(c.exception.code, "agy_quota")

    RF_ID = "0af7a474-ce07-4bc4-9735-46858b750c5e"

    def rf_runner(self, path_template, image=True):
        """Stream of a live read_file refusal (arguments absent from the stream); the transcript is written into the brain dir."""
        stream = (Path(config.PACKAGE_DIR) / "fixtures" / "agy_read_file_stream.ndjson").read_text()
        tr = (Path(config.PACKAGE_DIR) / "fixtures" / "agy_read_file_transcript.jsonl").read_text()
        inner = self.runner(stream=stream, image=image)

        def run(cmd, cwd, timeout):
            if cmd[1] != "--version":
                d = self.brain / self.RF_ID / ".system_generated" / "logs"
                d.mkdir(parents=True, exist_ok=True)
                (d / "transcript.jsonl").write_text(tr.replace("{WS}", path_template.replace("{WS}", cwd)))
                if image:
                    (self.brain / self.RF_ID / "sketch_1.jpg").write_bytes(JPEG)
            return inner(cmd, cwd, timeout)
        return run

    def test_read_file_inside_workspace_is_allowed(self):
        _, _, rec = self.go(runner=self.rf_runner("{WS}"))
        self.assertIn("read_file", rec["tools"])

    def test_read_file_outside_workspace_is_refused(self):
        with self.assertRaises(sketch.SketchError) as c:
            self.go(runner=self.rf_runner("/home/u/.config"))
        self.assertEqual(c.exception.code, "agent_tool_violation")
        self.assertIn("read_file", str(c.exception))

    def test_read_file_without_any_transcript_is_refused(self):
        stream = (Path(config.PACKAGE_DIR) / "fixtures" / "agy_read_file_stream.ndjson").read_text()
        with self.assertRaises(sketch.SketchError) as c:
            self.go(runner=self.runner(stream=stream))
        self.assertEqual(c.exception.code, "agent_tool_violation")

    def test_transcript_run_command_is_refused(self):
        calls = [("run_command", ["x"]), ("read_file", ['"/ws/a.png"']), ("send_message", [])]
        self.assertEqual(sketch.call_violations(calls, "/ws"), ["run_command"])

    def test_event_read_file_with_quoted_path(self):
        ev = self.stream_with({"tool_name": "read_file", "args": {"Path": '"/ws/1-a.png"'}})
        self.assertEqual(sketch.tool_violations(ev, "/ws"), [])

    def test_env_allowlist(self):
        env = {"HOME": "/h", "PATH": "/b", "LANG": "C", "LC_ALL": "C", "XDG_CONFIG_HOME": "/c", "TERM": "x", "USER": "u",
               "GEMINI_API_KEY": "k", "GITHUB_TOKEN": "t", "AWS_SECRET_ACCESS_KEY": "s", "OPENAI_API_KEY": "o"}
        self.assertEqual(sorted(sketch.safe_env(env)), ["HOME", "LANG", "LC_ALL", "PATH", "TERM", "USER", "XDG_CONFIG_HOME"])

    def test_default_runner_gets_clean_env_and_new_session(self):
        with mock.patch.dict(os.environ, {"GEMINI_API_KEY": "k", "SOME_TOKEN": "t"}), \
                mock.patch("subprocess.Popen") as popen:
            popen.return_value.communicate.return_value = ("o", "e")
            popen.return_value.returncode = 0
            sketch.default_runner(["agy"], None, 5)
        kw = popen.call_args.kwargs
        self.assertNotIn("GEMINI_API_KEY", kw["env"])
        self.assertNotIn("SOME_TOKEN", kw["env"])
        self.assertTrue(kw["start_new_session"])

    def test_timeout_kills_process_group(self):
        with mock.patch("subprocess.Popen") as popen, mock.patch("os.killpg") as killpg:
            popen.return_value.pid = 4242
            popen.return_value.communicate.side_effect = [subprocess.TimeoutExpired("agy", 1), ("", "")]
            with self.assertRaises(sketch.SketchError) as c:
                sketch.default_runner(["agy"], None, 1)
        self.assertEqual(c.exception.code, "agy_timeout")
        killpg.assert_called_once()
        self.assertEqual(killpg.call_args.args[0], 4242)

    def test_prompt_file_rules(self):
        (self.root / ".gitignore").write_text("secret.md\n")
        cases = {".env": "KEY=1", "secret.md": "x", "notes.py": "x", ".hid/brief.md": "x"}
        for name, body in cases.items():
            p = self.root / name
            p.parent.mkdir(exist_ok=True)
            p.write_text(body)
            with self.subTest(name=name), self.assertRaises(sketch.SketchError) as c:
                sketch.run_sketch("TASK-0141", p, root=self.root, shots_dir=self.shots, dry_run=True,
                                  which=lambda n: "/bin/true")
            self.assertEqual(c.exception.code, "bad_prompt")
        sketch.run_sketch("TASK-0141", self.prompt, root=self.root, shots_dir=self.shots, dry_run=True,
                                  which=lambda n: "/bin/true")

    def test_quota_in_final_response(self):
        resp = "The image generation tool returned a quota/capacity error (`ResourceExhausted`, HTTP 429)."
        with self.assertRaises(sketch.SketchError) as c:
            self.go(runner=self.runner(stream=self.stream_with({"step_type": "agent_response"}, resp), image=False))
        self.assertEqual(c.exception.code, "agy_quota")
        self.assertIn("ResourceExhausted", str(c.exception))

    def test_quota_in_stderr(self):
        with self.assertRaises(sketch.SketchError) as c:
            self.go(runner=self.runner(stream="", rc=1, err="rpc error: RESOURCE_EXHAUSTED", image=False))
        self.assertEqual(c.exception.code, "agy_quota")

    def test_quota_word_in_init_event_is_ignored(self):
        ev = json.dumps({"event": "init", "conversation_id": MAIN, "init": {"note": "quota 429"}}) + "\n"
        self.assertFalse(sketch.quota_hit("", {"status": "SUCCESS", "response": "Done"}, ev))

    def test_images_polled_after_result(self):
        sleeps = []

        def sleep(s):
            sleeps.append(s)
            if len(sleeps) == 2:
                (self.brain / MAIN / "late.jpg").write_bytes(JPEG)
        (self.brain / MAIN).mkdir()
        _, _, rec = self.go(runner=self.runner(image=False), sleep=sleep)
        self.assertEqual([f["name"] for f in rec["files"]], ["late.jpg"])
        self.assertEqual(len(sleeps), 2)

    def test_no_images_after_poll_gives_up(self):
        sleeps = []
        with self.assertRaises(sketch.SketchError) as c:
            self.go(runner=self.runner(image=False), sleep=sleeps.append)
        self.assertEqual(c.exception.code, "no_images")
        self.assertEqual(len(sleeps), sketch.IMAGE_POLL_S)

    def test_login_only_from_stderr_and_result(self):
        stream = self.stream_with({"step_type": "agent_response", "text_delta": "please log in"})
        with self.assertRaises(sketch.SketchError) as c:
            self.go(runner=self.runner(stream=stream, rc=1, image=False))
        self.assertEqual(c.exception.code, "agy_failed")

    def test_secret_in_prompt_does_not_start_runner(self):
        self.scan.side_effect = gitleaks.SecretDetected([{"rule": "generic", "line": 1}])
        with self.assertRaises(sketch.SketchError) as c:
            self.go(runner=self.runner())
        self.assertEqual(c.exception.code, "secret_detected")
        self.assertEqual(self.calls, [])

    def test_retry_does_not_overwrite_record(self):
        p1, m1, _ = self.go(runner=self.runner(), clock=lambda: 1000.0)
        p2, m2, _ = self.go(runner=self.runner(), clock=lambda: 1000.0)
        self.assertNotEqual(p1, p2)
        self.assertTrue(all(p.exists() for p in (p1, m1, p2, m2)))
        self.assertEqual(len(list(self.runs.glob("sketch-*.json"))), 2)

    def test_dry_run_runs_nothing(self):
        path, md, rec = self.go(runner=self.runner(), dry_run=True, which=lambda n: "/bin/true")
        self.assertIsNone(path)
        self.assertEqual(self.calls, [])


if __name__ == "__main__":
    unittest.main()
