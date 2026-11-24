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

def png(w=1920, h=1080):
    """A PNG signature and IHDR with the given size, then filler: enough for the size check."""
    return visual.PNG + b"\x00\x00\x00\rIHDR" + w.to_bytes(4, "big") + h.to_bytes(4, "big") + b"fake-image-bytes"


PNG = png()
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

    def guard(self):
        return visual.RateGuard(self.tmp / "g.json", rpm=100, rpd=100)

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
        p = OpenAICompatProvider(profile=self.profile(self.key_file()), guard=self.guard())
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

    def test_client_shots_dir_accepted_other_path_refused(self):
        client = self.tmp / "client-shots"
        client.mkdir()
        (client / "c.png").write_bytes(PNG)
        other = self.tmp / "elsewhere"
        other.mkdir()
        (other / "d.png").write_bytes(PNG)
        with mock.patch.object(config, "SHOTS_DIR", self.shots), mock.patch.object(config, "CLIENT_SHOTS_DIR", client):
            self.assertEqual(visual.load_frames([client / "c.png", self.shots / "a.png"])[0][0], "c.png")
            with self.assertRaises(visual.VisualError):
                visual.load_frames([other / "d.png"])

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

        p = OpenAICompatProvider(profile=self.profile(self.key_file()), guard=self.guard(), opener=opener, sleep=lambda s: None)
        with self.assertRaises(ProviderUnavailable):
            p.complete([{"role": "user", "content": "hi"}], {"type": "object"}, "visual", 10)
        self.assertEqual(len(n), config.PROVIDER_RETRIES + 1)

    def test_provider_scans_text_parts_of_image_messages(self):
        seen = []
        with mock.patch("tools.llm_worker.gitleaks.scan_text", side_effect=seen.append):
            p = OpenAICompatProvider(profile=self.profile(self.key_file()), guard=self.guard(),
                                     opener=lambda r, timeout: Resp(ok_body("{}")), sleep=lambda s: None)
            msgs = visual.build_messages("TASK-0126", "crit", visual.load_frames(self.frames, self.shots))
            p.complete(msgs, {"type": "object"}, "visual", 10)
        self.assertIn("crit", seen[0])
        self.assertNotIn("base64", seen[0])

    def test_other_free_profile_still_local_only(self):
        with self.assertRaises(ValueError):
            OpenAICompatProvider(profile=replace(config.PROFILES["llamacpp"]), url="https://example.com/v1", api_key="k")


class Round3(Base):
    def test_day_change_keeps_minute_window(self):
        now = [86400 * 10 - 5.0]
        g = visual.RateGuard(self.tmp / "u.json", rpm=2, rpd=100, clock=lambda: now[0])
        g.before_attempt(b"", 1)
        g.before_attempt(b"", 1)
        now[0] += 10  # next UTC day, 10 s later
        with self.assertRaises(visual.RateLimited):
            g.before_attempt(b"", 1)

    def test_settle_adds_daily_token_totals_and_new_day_resets(self):
        now = [86400 * 10.0]
        g = visual.RateGuard(self.tmp / "u.json", clock=lambda: now[0])
        usage = {"prompt_tokens": 100, "completion_tokens": 20, "completion_tokens_details": {"reasoning_tokens": 7}}
        for _ in range(2):
            g.settle(g.before_attempt(b"", 1), usage)
        st = json.loads((self.tmp / "u.json").read_text())
        self.assertEqual(st["tokens"], {"prompt": 200, "completion": 40, "thinking": 14})
        self.assertNotIn("usd", st)
        now[0] += 86400
        g.before_attempt(b"", 1)
        self.assertEqual(json.loads((self.tmp / "u.json").read_text())["tokens"]["prompt"], 0)

    def test_small_frame_refused_unless_allowed(self):
        (self.shots / "s.png").write_bytes(png(1280, 720))
        with self.assertRaises(visual.VisualError):
            visual.load_frames([self.shots / "s.png"], self.shots)
        self.assertEqual(len(visual.load_frames([self.shots / "s.png"], self.shots, allow_small=True)), 1)

    def test_size_without_header_refused(self):
        (self.shots / "n.png").write_bytes(visual.PNG + b"x")
        with self.assertRaises(visual.VisualError):
            visual.load_frames([self.shots / "n.png"], self.shots)

    def test_jpeg_size_read(self):
        jpg = b"\xff\xd8\xff\xe0\x00\x04ab\xff\xc0\x00\x0b\x08" + (1080).to_bytes(2, "big") + (1920).to_bytes(2, "big") + b"\x03\x00"
        self.assertEqual(visual.image_size(jpg), (1920, 1080))

    @mock.patch("tools.llm_worker.gitleaks.scan_text")
    def test_unverified_findings_flagged_and_pair_written_with_hash(self, _scan):
        bad = {**GOOD, "findings": [GOOD["findings"][0], {**GOOD["findings"][0], "criterion": "Z99", "frame": "zzz.png"}]}
        fake = FakeProvider([bad])
        path, md, rec = visual.run_visual("TASK-0126", self.frames, runs_dir=self.runs, shots_dir=self.shots,
                                          make_provider=lambda: fake)
        f = json.loads(path.read_text())["result"]["findings"]
        self.assertNotIn("unverified", f[0])
        self.assertEqual(f[1]["unverified"], ["criterion", "frame"])
        text = md.read_text()
        self.assertIn("unverified: criterion, frame", text)
        digest = json.loads(path.read_text())["response_sha256"]
        self.assertIn(digest, text)
        self.assertEqual(rec["tokens"], {"prompt": 100, "completion": 10, "thinking": 0})
        self.assertEqual(sorted(p.suffix for p in self.runs.iterdir() if p.name != ".visual.lock"), [".json", ".md"])  # no temp files left


class Round4(Base):
    def test_limit_applies_to_encoded_request(self):
        raw = sum(len(PNG) for _ in self.frames)
        with mock.patch.object(config, "MAX_REQUEST_BYTES", raw + 10), mock.patch("tools.llm_worker.gitleaks.scan_text"):
            with self.assertRaises(visual.VisualError) as cm:  # raw bytes fit, base64 does not
                visual.run_visual("TASK-0126", self.frames, shots_dir=self.shots, dry_run=True)
        self.assertIn("encoded request", str(cm.exception))

    def test_frame_names_change_input_hash(self):
        with mock.patch("tools.llm_worker.gitleaks.scan_text"):
            a = visual.run_visual("TASK-0126", [self.shots / "a.png"], shots_dir=self.shots, dry_run=True)[2]
            (self.shots / "c.png").write_bytes(PNG)
            b = visual.run_visual("TASK-0126", [self.shots / "c.png"], shots_dir=self.shots, dry_run=True)[2]
        self.assertNotEqual(a["input_hash"], b["input_hash"])

    def test_concurrent_writes_leave_a_consistent_pair(self):
        import threading
        recs = []
        for i in range(8):
            r = {"task": "TASK-0126", "model": "m", "frames": [{"name": "a.png"}], "response_sha256": f"{i:064d}",
                 "tokens": {"prompt": 1, "completion": 1, "thinking": 0},
                 "result": {"opinion": "pass", "summary": "s", "findings": []}}
            recs.append(r)
        ths = [threading.Thread(target=visual.write_pair, args=(self.runs, "visual-x", r)) for r in recs]
        for th in ths:
            th.start()
        for th in ths:
            th.join()
        digest = json.loads((self.runs / "visual-x.json").read_text())["response_sha256"]
        self.assertIn(digest, (self.runs / "visual-x.md").read_text())
        self.assertEqual(sorted(p.name for p in self.runs.iterdir() if p.suffix == ".tmp"), [])

    def test_gemini_key_not_sent_to_a_redirect_target(self):
        import http.server
        import threading
        seen = []

        class Target(http.server.BaseHTTPRequestHandler):
            def do_GET(self):  # urllib turns a 302 of a POST into a GET and keeps the headers
                seen.append(self.headers.get("Authorization"))
                self.send_response(200)
                self.end_headers()

            def log_message(self, *a):
                pass

        target = http.server.HTTPServer(("127.0.0.1", 0), Target)

        class Redirector(Target):
            def do_POST(self):
                self.send_response(302)
                self.send_header("Location", f"http://127.0.0.1:{target.server_port}/x")
                self.end_headers()

        front = http.server.HTTPServer(("127.0.0.1", 0), Redirector)
        for s in (target, front):
            threading.Thread(target=s.serve_forever, daemon=True).start()
            self.addCleanup(s.server_close)
            self.addCleanup(s.shutdown)
        url = f"http://127.0.0.1:{front.server_port}/chat"
        prof = replace(self.profile(self.key_file()), url=url)
        p = OpenAICompatProvider(profile=prof, guard=self.guard(), retries=0, sleep=lambda s: None)
        with mock.patch("tools.llm_worker.gitleaks.scan_text"), self.assertRaises(prov.ProviderError):
            p.complete([{"role": "user", "content": "hi"}], {"type": "object"}, "visual", 10)
        self.assertEqual(seen, [])


class Review2(Base):
    def test_free_public_needs_guard_and_own_url(self):
        prof = self.profile(self.key_file())
        with self.assertRaises(ValueError):
            OpenAICompatProvider(profile=prof)  # no guard
        with self.assertRaises(ValueError):
            OpenAICompatProvider(profile=prof, guard=self.guard(), url="https://example.com/v1/chat")
        OpenAICompatProvider(profile=prof, guard=self.guard())

    def test_run_does_not_offer_free_public_profile(self):
        err = io.StringIO()
        with mock.patch("sys.stderr", err), self.assertRaises(SystemExit):
            cli.main(["run", "--profile", "gemini-flash"])
        self.assertIn("invalid choice", err.getvalue())

    def test_corrupted_usage_file_refuses(self):
        path = self.tmp / "u.json"
        path.write_text("{not json", encoding="utf-8")
        with self.assertRaises(visual.RateLimited):
            visual.RateGuard(path).before_attempt(b"", 1)

    def test_oversized_frame_refused_before_read(self):
        (self.shots / "big.png").write_bytes(PNG + b"0" * 100)
        with mock.patch.object(config, "MAX_FRAME_BYTES", 50), \
                mock.patch.object(Path, "read_bytes", side_effect=AssertionError("read")):
            with self.assertRaises(visual.VisualError):
                visual.load_frames([self.shots / "big.png"], self.shots)

    def test_secret_found_by_provider_scan_is_refused(self):
        class Leaky:
            def complete(self, *a, **k):
                raise prov.gitleaks.SecretDetected([{"rule": "r", "line": 1}])

        with mock.patch("tools.llm_worker.gitleaks.scan_text"):
            with self.assertRaises(visual.VisualError) as cm:
                visual.run_visual("TASK-0126", self.frames, runs_dir=self.runs, shots_dir=self.shots,
                                  make_provider=Leaky)
        self.assertEqual(cm.exception.code, "secret_detected")

    def test_cli_maps_gitleaks_unavailable_to_refused(self):
        with mock.patch.object(visual, "run_visual", side_effect=prov.gitleaks.GitleaksUnavailable("x")), \
                redirect_stdout(io.StringIO()) as out:
            rc = cli.main(["visual", "--task", "TASK-0126", "--frames", "a.png"])
        self.assertEqual(rc, 4)
        self.assertIn("refused", out.getvalue())


DESIGN = {**GOOD, "proposals": [{"title": "Bigger top bar", "change": "font 16", "why": "C3", "frames": ["a.png"],
                                 "effort": "S", "clarity_risk": "none"}]}


class DesignMode(Base):
    def run_with(self, content, mode):
        fake = FakeProvider([content])
        res = visual.run_visual("TASK-0126", self.frames, runs_dir=self.runs, shots_dir=self.shots,
                                make_provider=lambda: fake, mode=mode)
        return res, fake

    @mock.patch("tools.llm_worker.gitleaks.scan_text")
    def test_design_prompt_has_direction_and_proposals_are_written(self, _scan):
        (path, md, rec), fake = self.run_with(DESIGN, "design")
        user = fake.calls[0]["messages"][1]["content"][0]["text"]
        self.assertIn("Visual direction:", user)
        self.assertIn(visual.load_criteria(visual.DIRECTION)[:40], user)
        self.assertIn("design reviewer", fake.calls[0]["messages"][0]["content"])
        self.assertEqual(rec["mode"], "design")
        self.assertIn("Bigger top bar", md.read_text())

    @mock.patch("tools.llm_worker.gitleaks.scan_text")
    def test_criteria_mode_is_default_without_direction(self, _scan):
        (path, md, rec), fake = self.run_with(GOOD, "criteria")
        self.assertNotIn("Visual direction:", fake.calls[0]["messages"][1]["content"][0]["text"])
        self.assertEqual(rec["mode"], "criteria")

    @mock.patch("tools.llm_worker.gitleaks.scan_text")
    def test_design_answer_without_proposals_is_invalid(self, _scan):
        with self.assertRaises(visual.VisualError):
            self.run_with(GOOD, "design")

    @mock.patch("tools.llm_worker.gitleaks.scan_text")
    def test_modes_have_different_hashes(self, _scan):
        a = visual.run_visual("TASK-0126", self.frames, shots_dir=self.shots, dry_run=True)[2]
        b = visual.run_visual("TASK-0126", self.frames, shots_dir=self.shots, dry_run=True, mode="design")[2]
        self.assertNotEqual(a["input_hash"], b["input_hash"])


if __name__ == "__main__":
    unittest.main()
