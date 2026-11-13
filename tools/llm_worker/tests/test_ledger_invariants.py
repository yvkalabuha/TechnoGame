"""Money-cap invariants under random failure sequences, truncation and crash/resume (TASK-0035 test review).
Seeded stdlib random, no network. The former *_gap tests (fixed in the rework) are normal tests now."""

import json
import random
import socket
import unittest
import urllib.error
from decimal import Decimal

from helpers import FakeProvider
from test_ledger import MODEL, LedgerBase, entries, prices
from test_loop import FakeVerifier, art, contract
from test_provider_context import FakeOpener

from tools.llm_worker import config
from tools.llm_worker.ledger import Guard, SpendRefused
from tools.llm_worker.loop import CandidateLoop
from tools.llm_worker.provider import (AuthError, InsufficientFunds, OpenAICompatProvider, ProviderError,
                                       ProviderUnavailable)

BODY = {"choices": [{"message": {"content": "{}"}}], "usage": {"prompt_tokens": 5, "completion_tokens": 2}}
NOJSON = None  # marker: HTTP 200 with a body that is not JSON


def http(code):
    return urllib.error.HTTPError("u", code, "x", {}, None)


class Crash(BaseException):
    pass


class RandomOpener:
    """Picks an outcome per request from a seeded generator; 'crash' simulates a kill after the reservation."""

    def __init__(self, rng):
        self.rng = rng
        self.requests = 0

    def __call__(self, req, timeout):
        self.requests += 1
        kind = self.rng.choice(["ok", "ok", "5xx", "timeout", "402", "429", "400", "crash", "nonjson", "401"])
        if kind == "ok":
            return FakeOpener([BODY])(req, timeout)
        if kind == "nonjson":
            import io
            r = io.BytesIO(b"<html>")
            r.__enter__ = lambda s=r: s
            r.__exit__ = lambda *a: False
            return r
        if kind == "5xx":
            raise http(503)
        if kind == "timeout":
            raise socket.timeout("t")
        if kind == "crash":
            raise Crash()
        raise http(int(kind) if kind[0].isdigit() and kind != "5xx" else 400)


class CapPropertyTest(LedgerBase):
    def test_random_sequences_never_exceed_cap(self):
        for seed in range(12):
            rng = random.Random(seed)
            cap = Decimal(str(rng.choice(["0.0005", "0.002", "0.01"])))
            self.tearDown()
            self.setUp()
            g = Guard(self.ledger, prices(cap=str(cap), task_cap=str(cap)), MODEL, task="T1")
            op = RandomOpener(rng)
            p = OpenAICompatProvider(profile=config.PROFILES["deepseek-flash"], api_key="k", opener=op, guard=g,
                                     sleep=lambda s: None, retries=rng.randint(0, 3))
            for _ in range(60):
                try:
                    p.complete([{"role": "user", "content": "x" * rng.randint(0, 400)}], {}, "r",
                               rng.randint(1, 300))
                except (ProviderUnavailable, ProviderError, AuthError, InsufficientFunds, SpendRefused, Crash):
                    pass
                after = self.total()
                self.assertLessEqual(after, cap, f"seed {seed}")  # reserves + spent never pass the cap
            # every reservation is either settled or kept; nothing is deleted from the file
            es = entries(self.ledger)
            ids = [e["id"] for e in es if e["t"] == "reserve"]
            self.assertEqual(len(ids), len(set(ids)))

    def test_total_never_decreases_except_by_release_of_unbilled(self):
        # a 5xx / timeout / crash / non-JSON never lowers the total
        for kind in ("5xx", "timeout", "crash"):
            self.tearDown()
            self.setUp()
            before = Decimal(0)
            for i in range(3):
                exc = {"5xx": http(503), "timeout": socket.timeout("t"), "crash": Crash()}[kind]
                p, _ = self.provider([exc], cap="1.00")
                p.retries = 0
                try:
                    p.complete([], {}, "r", 100)
                except (ProviderUnavailable, Crash):
                    pass
                self.assertGreater(self.total(), before)
                before = self.total()

    def test_5xx_drain_stops_at_cap_and_then_no_request_is_made(self):
        p, op = self.provider([http(503)] * 50, cap="0.001", task_cap="0.001")
        p.retries = 40
        with self.assertRaises(SpendRefused):
            p.complete([], {}, "r", 100)
        n = len(op.requests)
        self.assertLessEqual(self.total(), Decimal("0.001"))
        with self.assertRaises(SpendRefused):
            p.complete([], {}, "r", 100)
        self.assertEqual(len(op.requests), n)  # refused before any request

    def test_overrun_is_bounded_by_one_calls_estimate_error(self):
        big = dict(BODY, usage={"prompt_tokens": 10 ** 6, "completion_tokens": 10 ** 6})
        p, _ = self.provider([big], cap="0.05", task_cap="0.05")
        p.complete([], {}, "r", 10)
        self.assertGreater(self.total(), Decimal("0.05"))
        with self.assertRaises(SpendRefused):  # the next call is refused: no second overrun
            p.complete([], {}, "r", 10)
        self.assertEqual(len(entries(self.ledger)), 2)


class CorruptionTest(LedgerBase):
    def setUp(self):
        super().setUp()
        self.guard().before_attempt(b"x" * 100, 100)
        self.guard().before_attempt(b"x" * 100, 100)

    def refuses(self):
        with self.assertRaises(SpendRefused):
            self.guard().before_attempt(b"x", 1)

    def test_truncated_mid_line_refuses(self):
        data = self.ledger.path.read_bytes()
        self.ledger.path.write_bytes(data[:-15])
        self.refuses()

    def test_blank_line_and_garbage_refuse(self):
        for tail in ("\n", "{}\n", '{"t": "settle", "id": "nope", "usd": "0"}\n', '{"t": "reserve"}\n',
                     '{"t":"reserve","id":"a","task":"T","model":"m","usd":"abc"}\n', "\x00\x00\n"):
            self.ledger.path.write_text(self.ledger.path.read_text().rstrip("\n") + "\n" + tail)
            self.refuses()
            self.tearDown()
            self.setUp()

    def test_binary_garbage_refuses(self):
        self.ledger.path.write_bytes(b"\xff\xfe\x00bad")
        self.refuses()

    def test_negative_amount_in_file_refuses_gap(self):
        # GAP: a hand-edited negative amount lowers the total; nothing validates sign
        self.ledger.path.write_text(self.ledger.path.read_text() +
                                    '{"t":"reserve","id":"z","task":"T9","model":"m","usd":"-100"}\n')
        self.refuses()

    def test_truncation_to_empty_refuses_gap(self):
        # GAP: an empty (or line-aligned truncated) ledger with the marker present reads as "spent 0"
        self.ledger.path.write_text("")
        self.refuses_when_history_was_there()

    def refuses_when_history_was_there(self):
        with self.assertRaises(SpendRefused):
            self.guard().before_attempt(b"x", 1)


class ZeroUsageTest(LedgerBase):
    def test_zero_usage_is_not_booked_as_free_gap(self):
        # GAP: usage {0, 0} (or a buggy provider) settles to 0 USD; a non-empty request cannot cost nothing
        z = dict(BODY, usage={"prompt_tokens": 0, "completion_tokens": 0})
        p, _ = self.provider([z])
        p.complete([{"role": "user", "content": "x" * 1000}], {}, "r", 1000)
        self.assertGreater(self.total(), 0)

    def test_bool_usage_is_not_trusted_gap(self):
        # GAP: True/False pass isinstance(int), so usage {true,false} settles to ~0
        z = dict(BODY, usage={"prompt_tokens": True, "completion_tokens": False})
        p, _ = self.provider([z])
        p.complete([], {}, "r", 100)
        self.assertEqual(self.ledger.summary()["unsettled"], 1)  # worst case stays


class CrashResumeTest(LedgerBase):
    def loop(self, provider, d):
        return CandidateLoop(contract(max_rounds=3), 1, provider, FakeVerifier(), d, "CTX", clock=lambda: 0.0)

    def paid(self, script):
        op = FakeOpener(script)
        p = OpenAICompatProvider(profile=config.PROFILES["deepseek-flash"], api_key="k", opener=op,
                                 guard=self.guard(), sleep=lambda s: None, retries=0)
        return p, op

    def good(self):
        return {"choices": [{"message": {"content": json.dumps(art("good"))}}],
                "usage": {"prompt_tokens": 5, "completion_tokens": 2}}

    def test_crash_before_request_keeps_reservation_and_resume_pays_once(self):
        d = self.tmp / "c1"
        p, _ = self.paid([Crash()])
        with self.assertRaises(Crash):
            self.loop(p, d).run()
        reserved = self.total()
        self.assertGreater(reserved, 0)
        p2, op2 = self.paid([self.good()])
        st = self.loop(p2, d).run()
        self.assertEqual(st["outcome"], "passed")
        self.assertEqual(len(op2.requests), 1)
        es = entries(self.ledger)
        self.assertEqual([e["t"] for e in es], ["reserve", "reserve", "settle"])  # the dead attempt stays booked
        self.assertGreaterEqual(self.total(), reserved)

    def test_crash_in_settle_resume_makes_no_second_request_or_reservation(self):
        d = self.tmp / "c1"
        p, op = self.paid([self.good()])
        real = p.guard.settle

        def boom(rid, usage):
            raise Crash()
        p.guard.settle = boom
        with self.assertRaises(Crash):
            self.loop(p, d).run()
        n = len(entries(self.ledger))
        self.assertEqual(n, 1)
        p.guard.settle = real
        p2, op2 = self.paid([])  # empty script: any request would fail
        st = self.loop(p2, d).run()
        self.assertEqual(st["outcome"], "passed")
        self.assertEqual(op2.requests, [])
        self.assertEqual(len(entries(self.ledger)), n)  # not recharged, worst case stays booked

    def test_crash_after_response_json_saved_resume_is_free(self):
        d = self.tmp / "c1"
        p, _ = self.paid([self.good()])
        orig = CandidateLoop._save

        calls = {"n": 0}

        def save(s):
            calls["n"] += 1
            if calls["n"] == 3:  # after the response file, before the round state is recorded
                raise Crash()
            orig(s)
        CandidateLoop._save = save
        try:
            with self.assertRaises(Crash):
                self.loop(p, d).run()
        finally:
            CandidateLoop._save = orig
        n = len(entries(self.ledger))
        p2, op2 = self.paid([])
        self.loop(p2, d).run()
        self.assertEqual(op2.requests, [])
        self.assertEqual(len(entries(self.ledger)), n)


if __name__ == "__main__":
    unittest.main()
