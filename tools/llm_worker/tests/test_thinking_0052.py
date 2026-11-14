"""DeepSeek thinking mode (TASK-0052): request body, reasoning_content, empty answers. No network."""

import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from helpers import TempRepo
from test_provider_context import FakeOpener
from test_review import GOOD, make_repo

from tools.llm_worker import config, review
from tools.llm_worker.provider import Completion, OpenAICompatProvider, parse_response
from tools.llm_worker.review import ReviewError

GATE_OK = {"STATUS": "changes_requested", "SUMMARY": "s",
           "FINDINGS": [{"severity": "low", "item": "i", "reason": "r", "proposal": "p"}], "OPEN": []}


def ds(**kw):
    return OpenAICompatProvider(profile=config.PROFILES["deepseek-flash"], api_key="k", **kw)


class BodyTest(unittest.TestCase):
    def test_thinking_enabled_body(self):
        b = ds(thinking=True, effort="max").body([], {"type": "object"}, "r", 32768)
        self.assertEqual(b["thinking"], {"type": "enabled"})
        self.assertEqual(b["reasoning_effort"], "max")
        self.assertNotIn("temperature", b)
        self.assertEqual(b["max_tokens"], 32768)

    def test_default_is_off_and_keeps_temperature(self):
        b = ds().body([], {"type": "object"}, "r", 10)
        self.assertEqual(b["thinking"], {"type": "disabled"})
        self.assertNotIn("reasoning_effort", b)
        self.assertIn("temperature", b)

    def test_bad_effort(self):
        with self.assertRaises(ValueError):
            ds(thinking=True, effort="huge")

    def test_llamacpp_ignores_thinking_flag(self):
        b = OpenAICompatProvider(api_key="k", thinking=True).body([], {"type": "object"}, "r", 10)
        self.assertNotIn("thinking", b)
        self.assertIn("temperature", b)


class ParseTest(unittest.TestCase):
    def test_reasoning_content_is_not_the_answer(self):
        raw = json.dumps({"choices": [{"finish_reason": "stop", "message": {
            "content": "{\"a\": 1}", "reasoning_content": "{\"STATUS\": \"approve\"} thinking"}}],
            "usage": {"prompt_tokens": 5, "completion_tokens": 9,
                      "completion_tokens_details": {"reasoning_tokens": 7}}}).encode()
        c = parse_response(raw)
        self.assertEqual(c.content, "{\"a\": 1}")
        self.assertEqual((c.reasoning_tokens, c.finish_reason), (7, "stop"))
        self.assertEqual(c.reasoning_chars, len("{\"STATUS\": \"approve\"} thinking"))

    def test_no_reasoning_fields(self):
        raw = json.dumps({"choices": [{"message": {"content": "x"}}], "usage": {}}).encode()
        c = parse_response(raw)
        self.assertEqual((c.reasoning_tokens, c.reasoning_chars, c.finish_reason), (None, 0, ""))


class Seq:
    """Provider that returns scripted Completions and records requests."""
    thinking = True
    effort = "high"

    def __init__(self, items):
        self.items, self.calls = list(items), 0

    def complete(self, messages, schema, name, max_tokens, seed=None, on_raw=None):
        self.calls += 1
        return self.items.pop(0)


def comp(content, finish="stop", rt=5):
    return Completion(content=content, usage={"prompt_tokens": 1, "completion_tokens": 2}, finish_reason=finish,
                      reasoning_chars=10, reasoning_tokens=rt)


class EmptyAnswerTest(unittest.TestCase):
    def call(self, seq):
        return review._deepseek_call(seq, "S", "T", review.GATE_SCHEMA, "gate", 100)

    def test_whitespace_then_good_is_retried_once(self):
        seq = Seq([comp("  \n "), comp(json.dumps(GATE_OK))])
        content, _, info = self.call(seq)
        self.assertEqual((seq.calls, info["attempts"], info["reasoning_tokens"]), (2, 2, 5))
        self.assertEqual(review.parse_answer(content, review.GATE_SCHEMA, "gate")["STATUS"], "changes_requested")

    def test_second_empty_is_schema_invalid(self):
        seq = Seq([comp(""), comp("   ")])
        content, _, info = self.call(seq)
        self.assertEqual(seq.calls, 2)
        with self.assertRaises(ReviewError) as e:
            review.parse_answer(content, review.GATE_SCHEMA, "gate")
        self.assertEqual(e.exception.code, "schema_invalid")

    def test_length_cut_is_not_retried(self):
        seq = Seq([comp("", finish="length")])
        self.call(seq)
        self.assertEqual(seq.calls, 1)


class GateRunTest(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.addCleanup(self._tmp.cleanup)
        m = mock.patch("tools.llm_worker.gitleaks.scan_text")
        m.start()
        self.addCleanup(m.stop)
        self.repo = TempRepo({"docs/x.md": "# doc\n"})

    def gate(self, seq, **kw):
        return review.run_gate("docs/x.md", "HEAD", "third", root=self.repo.root, runs_dir=self.tmp / "runs",
                               make_provider=lambda: seq, **kw)

    def test_record_has_thinking_info_and_max_tokens(self):
        seen = []
        seq = Seq([comp(json.dumps(GATE_OK))])
        orig = seq.complete
        seq.complete = lambda m, s, n, mt, **k: (seen.append(mt), orig(m, s, n, mt))[1]
        path, rec = self.gate(seq)
        self.assertEqual(seen, [config.THINKING_MAX_TOKENS])
        self.assertGreaterEqual(config.THINKING_MAX_TOKENS, 32768)
        saved = json.loads(path.read_text())
        self.assertEqual(saved["thinking"]["reasoning_tokens"], 5)
        self.assertNotIn("reasoning_content", json.dumps(saved))

    def test_thinking_off_keeps_small_limit(self):
        seen = []
        seq = Seq([comp(json.dumps(GATE_OK))])
        orig = seq.complete
        seq.complete = lambda m, s, n, mt, **k: (seen.append(mt), orig(m, s, n, mt))[1]
        self.gate(seq, thinking=False)
        self.assertEqual(seen, [6000])

    def test_dry_run_reports_thinking(self):
        _, rec = self.gate(Seq([]), dry_run=True)
        self.assertEqual(rec["thinking"], {"enabled": True, "effort": "high", "max_tokens": config.THINKING_MAX_TOKENS})

    def test_review_uses_thinking_limit(self):
        repo, base, head = make_repo()
        seen = []
        seq = Seq([comp(json.dumps(GOOD))])
        orig = seq.complete
        seq.complete = lambda m, s, n, mt, **k: (seen.append(mt), orig(m, s, n, mt))[1]
        with mock.patch("tools.llm_worker.gitleaks.scan_dir"):
            _, _, rec = review.run_review("TASK-0001", base, head, "deepseek", root=repo.root,
                                          runs_dir=self.tmp / "r2", make_provider=lambda: seq, log=lambda m: None)
        self.assertEqual(seen, [config.THINKING_MAX_TOKENS])
        self.assertEqual(rec["thinking"]["effort"], "high")


if __name__ == "__main__":
    unittest.main()
