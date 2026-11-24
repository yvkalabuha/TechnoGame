"""Gemini visual second opinion (TASK-0135). No network: fake openers and providers."""

import io
import json
import tempfile
import unittest
import urllib.error
from contextlib import redirect_stdout
from dataclasses import replace
from pathlib import Path
from unittest import mock

from helpers import FakeProvider

from tools.llm_worker import __main__ as cli, config, provider as prov, visual
from tools.llm_worker.provider import OpenAICompatProvider, ProviderUnavailable

PNG = visual.PNG + b"fake-image-bytes"
GOOD = {"opinion": "mixed", "summary": "Readable, but small text.",
        "findings": [{"criterion": "C3", "frame": "a.png", "location": "top bar", "severity": "medium",
                      "fix": "raise the font size | to 16"}]}


class Resp:
    def __init__(self, body):
        self.body = body

    def read(self):
        return self.body

    def __enter__(self):
        return self

    def __exit__(self, *a):
        return False


def ok_body(content):
    return json.dumps({"choices": [{"message": {"content": content}, "finish_reason": "stop"}],
                       "usage": {"prompt_tokens": 5, "completion_tokens": 5}}).encode()


class Base(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.shots = self.tmp / "shots-out"
        self.shots.mkdir()
        self.runs = self.tmp / "runs"
        for n in ("a.png", "b.png"):
            (self.shots / n).write_bytes(PNG)
        self.frames = [self.shots / "a.png", self.shots / "b.png"]
        self.addCleanup(self._tmp.cleanup)

    def key_file(self, mode=0o600, text="test-key-not-real"):
        p = self.tmp / "key"
        p.write_text(text, encoding="utf-8")
        p.chmod(mode)
        return str(p)

    def profile(self, key_file):
        return replace(config.PROFILES["gemini-flash"], key_file=key_file)


class FramesAndRequest(Base):
    def test_messages_carry_every_frame_as_data_uri(self):
        frames = visual.load_frames(self.frames, self.shots)
        msgs = visual.build_messages("TASK-0126", "criteria text", frames)
        imgs = [p for p in msgs[1]["content"] if p["type"] == "image_url"]
        self.assertEqual(len(imgs), 2)
        self.assertTrue(imgs[0]["image_url"]["url"].startswith("data:image/png;base64,"))
        self.assertIn("criteria text", msgs[1]["content"][0]["text"])

    def test_frame_outside_shots_out_refused(self):
        outside = self.tmp / "x.png"
        outside.write_bytes(PNG)
        with self.assertRaises(visual.VisualError):
            visual.load_frames([outside], self.shots)

    def test_symlink_escape_refused(self):
        outside = self.tmp / "x.png"
        outside.write_bytes(PNG)
        (self.shots / "link.png").symlink_to(outside)
        with self.assertRaises(visual.VisualError):
            visual.load_frames([self.shots / "link.png"], self.shots)

    def test_not_an_image_and_too_large_refused(self):
        (self.shots / "t.png").write_bytes(b"text")
        with self.assertRaises(visual.VisualError):
            visual.load_frames([self.shots / "t.png"], self.shots)
        (self.shots / "big.png").write_bytes(PNG + b"0" * 100)
        with mock.patch.object(config, "MAX_FRAME_BYTES", 50), self.assertRaises(visual.VisualError):
            visual.load_frames([self.shots / "big.png"], self.shots)

    def test_criteria_section(self):
        text = visual.load_criteria(visual.DEFAULT_CRITERIA)
        self.assertTrue(text.startswith("## 12."))
        self.assertNotIn("## 13.", text)
        with self.assertRaises(visual.VisualError):
            visual.load_criteria("docs/ui/UI_DESIGN_SYSTEM.md#999")
        with self.assertRaises(visual.VisualError):
            visual.load_criteria("../../etc/passwd")

    def test_image_message_survives_json_object_body(self):
        p = OpenAICompatProvider(profile=self.profile(self.key_file()))
        msgs = visual.build_messages("TASK-0126", "c", visual.load_frames(self.frames, self.shots))
        body = p.body(msgs, {"type": "object", "properties": {}}, "visual", 100)
        self.assertEqual(body["messages"][1]["content"], msgs[1]["content"])
        self.assertIn("json", body["messages"][0]["content"])


class RunVisual(Base):
    def run_with(self, content, **kw):
        fake = FakeProvider([content])
        res = visual.run_visual("TASK-0126", self.frames, runs_dir=self.runs, shots_dir=self.shots,
                                make_provider=lambda: fake, **kw)
        return res, fake

    @mock.patch("tools.llm_worker.gitleaks.scan_text")
    def test_writes_json_and_markdown(self, _scan):
        (path, md, rec), fake = self.run_with(GOOD)
        self.assertRegex(path.name, r"^visual-TASK-0126-[0-9a-f]{8}-gemini\.json$")
        self.assertEqual(md.suffix, ".md")
        self.assertEqual(json.loads(path.read_text())["result"]["opinion"], "mixed")
        self.assertIn("raise the font size / to 16", md.read_text())  # table pipes are escaped
        self.assertEqual(len(fake.calls), 1)

    @mock.patch("tools.llm_worker.gitleaks.scan_text")
    def test_invalid_answer_kept_as_invalid(self, _scan):
        bad = {**GOOD, "opinion": "great"}
        with self.assertRaises(visual.VisualError) as cm:
            self.run_with(bad)
        self.assertEqual(cm.exception.code, "schema_invalid")
        self.assertEqual(len(list(self.runs.glob("*-invalid.json"))), 1)

    @mock.patch("tools.llm_worker.gitleaks.scan_text")
    def test_pass_with_high_finding_rejected(self, _scan):
        bad = {**GOOD, "opinion": "pass", "findings": [{**GOOD["findings"][0], "severity": "high"}]}
        with self.assertRaises(visual.VisualError):
            self.run_with(bad)

    def test_secret_in_text_stops_before_provider(self):
        with mock.patch("tools.llm_worker.gitleaks.scan_text", side_effect=prov.gitleaks.SecretDetected([{"rule": "r", "line": 1}])):
            with self.assertRaises(visual.VisualError) as cm:
                self.run_with(GOOD)
        self.assertEqual(cm.exception.code, "secret_detected")

    @mock.patch("tools.llm_worker.gitleaks.scan_text")
    def test_dry_run_needs_no_key_and_sends_nothing(self, _scan):
        called = []
        res = visual.run_visual("TASK-0126", self.frames, runs_dir=self.runs, shots_dir=self.shots,
                                make_provider=lambda: called.append(1), dry_run=True)
        self.assertEqual(res[2]["frames"][0]["name"], "a.png")
        self.assertFalse(called)

    def test_bad_task_id(self):
        with self.assertRaises(visual.VisualError):
            visual.run_visual("../x", self.frames, shots_dir=self.shots)


class KeyRefusal(Base):
    def cli(self, key_file):
        out = io.StringIO()
        with mock.patch.dict(config.PROFILES, {"gemini-flash": self.profile(key_file)}), \
                mock.patch.object(config, "SHOTS_DIR", self.shots), \
                mock.patch("tools.llm_worker.gitleaks.scan_text"), \
                redirect_stdout(out):
            rc = cli.main(["--runs", str(self.runs), "visual", "--task", "TASK-0126", "--frames",
                           *map(str, self.frames)])
        return rc, out.getvalue(), None

    def test_missing_key_file_refuses_with_hint(self):
        rc, out, _ = self.cli(str(self.tmp / "absent"))
        self.assertEqual(rc, 4)
        self.assertIn("~/.config/singularity/gemini-api-key, mode 0600", out)

    def test_wrong_mode_refuses(self):
        rc, out, _ = self.cli(self.key_file(mode=0o644))
        self.assertEqual(rc, 4)
        self.assertNotIn("test-key-not-real", out)


class RateAndRetry(Base):
    def test_per_minute_limit_then_window_passes(self):
        now = [1000.0]
        g = visual.RateGuard(self.tmp / "u.json", rpm=2, rpd=100, clock=lambda: now[0])
        g.before_attempt(b"", 1)
        g.before_attempt(b"", 1)
        with self.assertRaises(visual.RateLimited):
            g.before_attempt(b"", 1)
        now[0] += 61
        g.before_attempt(b"", 1)

    def test_per_day_limit_and_new_day(self):
        now = [86400 * 10.0]
        g = visual.RateGuard(self.tmp / "u.json", rpm=100, rpd=3, clock=lambda: now[0])
        for _ in range(3):
            now[0] += 61
            g.before_attempt(b"", 1)
        now[0] += 61
        with self.assertRaises(visual.RateLimited):
            g.before_attempt(b"", 1)
        now[0] += 86400
        g.before_attempt(b"", 1)

    @mock.patch("tools.llm_worker.gitleaks.scan_text")
    def test_429_pauses_and_retries_then_succeeds(self, _scan):
        answers = [urllib.error.HTTPError("u", 429, "x", {}, None), urllib.error.HTTPError("u", 429, "x", {}, None),
                   Resp(ok_body("{}"))]
        pauses = []

        def opener(req, timeout):
            a = answers.pop(0)
            if isinstance(a, Exception):
                raise a
            return a

        guard = visual.RateGuard(self.tmp / "u.json", rpm=10, rpd=10)
        p = OpenAICompatProvider(profile=self.profile(self.key_file()), guard=guard, opener=opener, sleep=pauses.append)
        comp = p.complete([{"role": "user", "content": "hi"}], {"type": "object"}, "visual", 10)
        self.assertEqual(comp.content, "{}")
        self.assertEqual(len(pauses), 2)
        self.assertEqual(json.loads((self.tmp / "u.json").read_text())["count"], 3)  # every attempt counted

    @mock.patch("tools.llm_worker.gitleaks.scan_text")
    def test_429_forever_gives_up_with_a_ceiling(self, _scan):
        n = []

        def opener(req, timeout):
            n.append(1)
            raise urllib.error.HTTPError("u", 429, "x", {}, None)

        p = OpenAICompatProvider(profile=self.profile(self.key_file()), opener=opener, sleep=lambda s: None)
        with self.assertRaises(ProviderUnavailable):
            p.complete([{"role": "user", "content": "hi"}], {"type": "object"}, "visual", 10)
        self.assertEqual(len(n), config.PROVIDER_RETRIES + 1)

    def test_provider_scans_text_parts_of_image_messages(self):
        seen = []
        with mock.patch("tools.llm_worker.gitleaks.scan_text", side_effect=seen.append):
            p = OpenAICompatProvider(profile=self.profile(self.key_file()),
                                     opener=lambda r, timeout: Resp(ok_body("{}")), sleep=lambda s: None)
            msgs = visual.build_messages("TASK-0126", "crit", visual.load_frames(self.frames, self.shots))
            p.complete(msgs, {"type": "object"}, "visual", 10)
        self.assertIn("crit", seen[0])
        self.assertNotIn("base64", seen[0])

    def test_other_free_profile_still_local_only(self):
        with self.assertRaises(ValueError):
            OpenAICompatProvider(profile=replace(config.PROFILES["llamacpp"]), url="https://example.com/v1", api_key="k")


if __name__ == "__main__":
    unittest.main()
