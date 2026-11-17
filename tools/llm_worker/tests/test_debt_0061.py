"""TASK-0061 part A: DEBT-0030 (round lock, mtime order) and DEBT-0035 (retry usage, raw body, run thinking, gate cap)."""

import json
import os
import tempfile
import threading
import unittest
from decimal import Decimal
from pathlib import Path
from unittest import mock

from helpers import FakeProvider
from test_ledger import MODEL, prices
from test_loop import FakeVerifier, art, contract
from test_review import GOOD, make_repo
from test_thinking_0052 import GATE_OK, Seq

from tools.llm_worker import report, review
from tools.llm_worker.ledger import Guard, Ledger, SpendRefused
from tools.llm_worker.loop import CandidateLoop
from tools.llm_worker.provider import Completion


class RoundTest(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.addCleanup(self._tmp.cleanup)
        m = mock.patch("tools.llm_worker.gitleaks.scan_text")
        m.start()
        self.addCleanup(m.stop)
        self.repo, self.base, self.head = make_repo()

    def one(self, out):
        out.append(review.run_review("TASK-0001", self.base, self.head, "deepseek", root=self.repo.root,
                                     runs_dir=self.tmp / "runs", attempts_dir=self.tmp / "att", log=lambda m: None,
                                     make_provider=lambda: FakeProvider([GOOD]))[2]["round"])

    def test_parallel_reviews_get_distinct_rounds(self):
        out = []
        ts = [threading.Thread(target=self.one, args=(out,)) for _ in range(5)]
        for t in ts:
            t.start()
        for t in ts:
            t.join()
        self.assertEqual(sorted(out), [1, 2, 3, 4, 5])

    def test_lock_file_is_not_a_round(self):
        self.one([])
        self.assertTrue((self.tmp / "runs" / ".review-TASK-0001.lock").is_file())
        self.assertEqual(review.next_round(self.tmp / "runs", "TASK-0001"), 2)

    def test_legacy_records_order_by_mtime_not_name(self):
        runs = self.tmp / "runs"
        runs.mkdir()
        rec = {"kind": "review", "task": "TASK-0001", "result": {"verdict": "approve", "summary": "", "findings": []}}
        for i, name in enumerate(["review-TASK-0001-zzzz-codex.json", "review-TASK-0001-aaaa-codex.json"]):
            p = runs / name
            p.write_text(json.dumps({**rec, "head": name}))
            os.utime(p, ns=(10**9 * (i + 1), 10**9 * (i + 1)))
        got = [r["head"] for r in report._review_rounds(runs)["TASK-0001"]]
        self.assertEqual(got, ["review-TASK-0001-zzzz-codex.json", "review-TASK-0001-aaaa-codex.json"])


def comp(content, raw_note="", rt=5, finish="stop"):
    return Completion(content=content, usage={"prompt_tokens": 10, "completion_tokens": 3,
                                              "completion_tokens_details": {"reasoning_tokens": rt}},
                      finish_reason=finish, reasoning_chars=7, reasoning_tokens=rt)


class RawSeq(Seq):
    def complete(self, messages, schema, name, max_tokens, seed=None, on_raw=None):
        c = super().complete(messages, schema, name, max_tokens)
        if on_raw:
            on_raw(f"RAW{self.calls}".encode())
        return c


class RetryStatsTest(unittest.TestCase):
    def test_usage_and_reasoning_cover_all_attempts_and_raw_is_kept(self):
        seq = RawSeq([comp("  "), comp(json.dumps(GATE_OK), rt=6)])
        _, usage, info = review._deepseek_call(seq, "S", "T", review.GATE_SCHEMA, "gate", 100)
        self.assertEqual((usage["prompt_tokens"], usage["completion_tokens"]), (20, 6))
        self.assertEqual(usage["completion_tokens_details"]["reasoning_tokens"], 11)
        self.assertEqual((info["reasoning_chars"], info["reasoning_tokens"], info["attempts"]), (14, 11, 2))
        self.assertEqual(info["first_attempt_raw"], "RAW1")

    def test_raw_of_first_attempt_survives_a_failing_retry(self):
        class Boom(RawSeq):
            def complete(self, *a, **k):
                if self.calls >= 1:
                    raise RuntimeError("network")
                return super().complete(*a, **k)

        with self.assertRaises(RuntimeError) as cm:
            review._deepseek_call(Boom([comp("  ")]), "S", "T", review.GATE_SCHEMA, "gate", 100)
        self.assertEqual(cm.exception.first_attempt_raw, "RAW1")

    def test_single_attempt_has_no_raw(self):
        seq = RawSeq([comp(json.dumps(GATE_OK))])
        _, usage, info = review._deepseek_call(seq, "S", "T", review.GATE_SCHEMA, "gate", 100)
        self.assertEqual(usage["prompt_tokens"], 10)
        self.assertNotIn("first_attempt_raw", info)


class ThinkingProvider(FakeProvider):
    thinking = True

    def complete(self, messages, schema, schema_name, max_tokens, seed=None, on_raw=None):
        c = super().complete(messages, schema, schema_name, max_tokens, seed, on_raw)
        c.reasoning_chars, c.reasoning_tokens, c.finish_reason = 42, 9, "stop"
        return c


class RunThinkingTest(unittest.TestCase):
    def run_loop(self, provider):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        d = Path(tmp.name) / "c1"
        CandidateLoop(contract(), 1, provider, FakeVerifier(), d, "CTX", clock=lambda: 0.0).run()
        return json.loads(next(d.glob("round_*/attempt_response.json")).read_text())

    def test_thinking_stats_in_response_record(self):
        r = self.run_loop(ThinkingProvider([art("good")]))
        self.assertEqual(r["thinking"], {"reasoning_chars": 42, "reasoning_tokens": 9, "finish_reason": "stop"})

    def test_no_thinking_key_when_off(self):
        self.assertNotIn("thinking", self.run_loop(FakeProvider([art("good")])))


class GateCapTest(unittest.TestCase):
    """#226: `gate:<ID>` -> max_usd_task_gate, other task keys -> max_usd_task, no key -> no_task."""

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.led = Ledger(Path(self._tmp.name) / "l")
        self.led.init()
        self.prices = {**prices(cap="5.00", task_cap="0.50"), "gate_task_cap": Decimal("1.00")}
        self.body = b"x" * 2_000_000  # 1M input tokens = $0.30
        self.max_tokens = 400_000     # $0.48 -> worst $0.78: over 0.50, under 1.00

    def attempt(self, task):
        return Guard(self.led, self.prices, MODEL, task=task).before_attempt(self.body, self.max_tokens)

    def test_gate_key_uses_gate_cap(self):
        self.assertTrue(self.attempt("gate:TASK-0061"))

    def test_other_key_uses_task_cap(self):
        with self.assertRaises(SpendRefused) as e:
            self.attempt("TASK-0061")
        self.assertEqual(e.exception.code, "task_cap_exceeded")

    def test_no_key_is_refused(self):
        with self.assertRaises(SpendRefused) as e:
            self.attempt(None)
        self.assertEqual(e.exception.code, "no_task")


if __name__ == "__main__":
    unittest.main()
