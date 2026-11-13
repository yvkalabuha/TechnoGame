"""Spend ledger and paid-provider rules (ADR-0015 §6, §9). No network: fake opener, temporary ledger."""

import json
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import urllib.error
from decimal import Decimal
from pathlib import Path

from helpers import NODES_CSV, FakeProvider, TempRepo, a2_contract
from test_loop import EXPLAIN, FakeVerifier, art, contract
from test_provider_context import OK_BODY, FakeOpener

from tools.llm_worker import config, report
from tools.llm_worker.ledger import Guard, Ledger, SpendRefused, load_prices
from tools.llm_worker.loop import ESCALATED, CandidateLoop
from tools.llm_worker.provider import (InsufficientFunds, ProviderError, OpenAICompatProvider, ProviderUnavailable,
                                       parse_response)
from tools.llm_worker.runner import Env, process_task, run_queue
from tools.llm_worker.taskqueue import TaskQueue

REPO_ROOT = Path(__file__).resolve().parents[3]
MODEL = "deepseek-flash"


def prices(ready=True, cap="1.00", task_cap="1.00"):
    return {"ready": ready, "cap": Decimal(cap), "task_cap": Decimal(task_cap),
            "models": {MODEL: {"input_per_mtok": Decimal("0.30"), "output_per_mtok": Decimal("1.20")}}}


def entries(ledger):
    return [json.loads(line) for line in ledger.path.read_text().splitlines()]


_scan = mock.patch("tools.llm_worker.gitleaks.scan_text")  # these tests are about spend; scanning is tested elsewhere


def setUpModule():
    _scan.start()


def tearDownModule():
    _scan.stop()


class LedgerBase(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.ledger = Ledger(self.tmp / "ledger")
        self.ledger.init()

    def tearDown(self):
        self._tmp.cleanup()

    def guard(self, **kw):
        return Guard(self.ledger, prices(**kw), MODEL, task="T1")

    def provider(self, script, **kw):
        op = FakeOpener(script)
        p = OpenAICompatProvider(profile=config.PROFILES["deepseek-flash"], api_key="SECRET-KEY", opener=op,
                                 guard=self.guard(**kw), sleep=lambda s: None, retries=2)
        return p, op

    def total(self):
        return self.ledger.summary()["total"]


class LedgerTest(LedgerBase):
    def test_missing_marker_refuses(self):
        (self.ledger.dir / "ledger.init").unlink()
        with self.assertRaises(SpendRefused) as cm:
            self.ledger.reserve("T1", MODEL, Decimal("0.01"), Decimal(1), Decimal(1))
        self.assertEqual(cm.exception.code, "ledger_missing")
        self.assertIsNone(self.ledger.summary())

    def test_init_never_recreates_lost_history(self):
        self.guard().before_attempt(b"x" * 100, 100)
        self.ledger.path.unlink()
        with self.assertRaises(SpendRefused) as cm:
            self.ledger.init()
        self.assertEqual(cm.exception.code, "ledger_missing")
        self.assertFalse(self.ledger.path.exists())

    def test_init_on_complete_ledger_is_a_noop(self):
        self.guard().before_attempt(b"x" * 100, 100)
        before = self.ledger.path.read_bytes()
        self.assertFalse(self.ledger.init())
        self.assertEqual(self.ledger.path.read_bytes(), before)

    def test_missing_head_or_edited_line_refuses(self):
        self.guard().before_attempt(b"x" * 100, 100)
        text = self.ledger.path.read_text()
        self.ledger.path.write_text(text.replace('"usd": "0.000', '"usd": "0.0000000'))  # lower amount, same count
        with self.assertRaises(SpendRefused) as cm:
            self.guard().before_attempt(b"x", 1)
        self.assertEqual(cm.exception.code, "ledger_corrupt")
        self.ledger.path.write_text(text)
        self.ledger.head.unlink()
        with self.assertRaises(SpendRefused):
            self.guard().before_attempt(b"x", 1)

    def test_reconcile_settles_only_unsettled(self):
        g = self.guard()
        rid = g.before_attempt(b"x" * 100, 100)
        self.ledger.reconcile(rid, "0.00001", "billing 2026-11-14")
        self.assertEqual(self.total(), Decimal("0.00001000"))
        self.assertEqual(entries(self.ledger)[-1]["source"], "billing 2026-11-14")
        for bad in ((rid, "0.1", "s"), ("nope", "0.1", "s")):
            with self.assertRaises(SpendRefused) as cm:
                self.ledger.reconcile(*bad)
            self.assertEqual(cm.exception.code, "bad_reconcile")
        rid2 = g.before_attempt(b"x", 1)
        for bad in (("-1", "s"), ("abc", "s"), ("0.1", " ")):
            with self.assertRaises(SpendRefused):
                self.ledger.reconcile(rid2, *bad)
        self.assertEqual(self.ledger.summary()["unsettled_ids"], [rid2])

    def test_reconcile_cli(self):
        rid = self.guard().before_attempt(b"x", 1)
        import tools.llm_worker.__main__ as M
        orig = M.Ledger
        M.Ledger = lambda: Ledger(self.ledger.dir)
        try:
            self.assertEqual(M.main(["ledger", "reconcile", rid, "0.002", "--source", "bill"]), 0)
            self.assertEqual(M.main(["ledger", "reconcile", rid, "0.002", "--source", "bill"]), 4)
        finally:
            M.Ledger = orig
        self.assertEqual(self.total(), Decimal("0.002"))

    def test_missing_ledger_file_refuses(self):
        self.ledger.path.unlink()
        with self.assertRaises(SpendRefused):
            self.guard().before_attempt(b"x", 10)

    def test_corrupt_ledger_refuses(self):
        self.ledger.path.write_text("not json\n")
        with self.assertRaises(SpendRefused) as cm:
            self.guard().before_attempt(b"x", 10)
        self.assertEqual(cm.exception.code, "ledger_corrupt")

    def test_two_processes_never_pass_the_cap(self):
        code = ("import sys; sys.path.insert(0, sys.argv[1]);"
                "from pathlib import Path; from decimal import Decimal;"
                "from tools.llm_worker.ledger import Ledger, SpendRefused;"
                "l = Ledger(Path(sys.argv[2])); ok = 0\n"
                "for i in range(10):\n"
                "    try: l.reserve('T', 'm', Decimal('0.30'), Decimal('1.00'), Decimal('9')); ok += 1\n"
                "    except SpendRefused: pass\n"
                "print(ok)")
        procs = [subprocess.Popen([sys.executable, "-c", code, str(REPO_ROOT), str(self.ledger.dir)],
                                  stdout=subprocess.PIPE, text=True) for _ in range(2)]
        granted = sum(int(p.communicate()[0]) for p in procs)
        self.assertEqual(granted, 3)  # 3 x 0.30 <= 1.00 < 4 x 0.30
        self.assertEqual(len(entries(self.ledger)), 3)
        self.assertEqual(self.total(), Decimal("0.90"))

    def test_worst_case_formula(self):
        g = self.guard()
        g.before_attempt(b"x" * 1000, 500)  # 500 input tokens * 0.30 + 500 * 1.20 per 1M
        self.assertEqual(entries(self.ledger)[0]["usd"], "0.00075000")

    def test_total_cap_refuses_without_writing(self):
        g = self.guard(cap="0.0001")
        with self.assertRaises(SpendRefused) as cm:
            g.before_attempt(b"x" * 1000, 500)  # worst case 0.00075 > cap
        self.assertEqual(cm.exception.code, "cap_exceeded")
        self.assertEqual(entries(self.ledger), [])

    def test_not_ready_flag_refuses_before_any_write(self):
        with self.assertRaises(SpendRefused) as cm:
            self.guard(ready=False).before_attempt(b"x", 10)
        self.assertEqual(cm.exception.code, "provider_not_ready")
        self.assertEqual(entries(self.ledger), [])

    def test_unknown_model_and_missing_task_refuse(self):
        with self.assertRaises(SpendRefused) as cm:
            Guard(self.ledger, prices(), "other", task="T1").before_attempt(b"x", 1)
        self.assertEqual(cm.exception.code, "unknown_model_price")
        with self.assertRaises(SpendRefused) as cm:
            Guard(self.ledger, prices(), MODEL).before_attempt(b"x", 1)
        self.assertEqual(cm.exception.code, "no_task")

    def test_real_prices_file_is_valid(self):
        # The readiness flag is the Orchestrator's switch (ADR-0015 §9); only its type is checked here.
        p = load_prices()
        self.assertIsInstance(p["ready"], bool)
        self.assertEqual(p["cap"], Decimal("5.00"))
        for prof in config.PROFILES.values():
            if prof.paid:
                self.assertIn(prof.model, p["models"])

    def test_overrun_is_at_most_one_call(self):
        g = self.guard(cap="0.01")
        rid = g.before_attempt(b"x" * 100, 100)  # worst case 0.000135
        g.settle(rid, {"prompt_tokens": 5000, "completion_tokens": 5000})  # real cost 0.0075: above reserve
        self.assertEqual(self.total(), Decimal("0.00750000"))
        with self.assertRaises(SpendRefused) as cm:
            for _ in range(100):
                g.before_attempt(b"x" * 100, 100)
        self.assertEqual(cm.exception.code, "cap_exceeded")
        self.assertLessEqual(self.total(), Decimal("0.01"))

    def test_task_cap_refuses(self):
        g = self.guard(task_cap="0.0002")
        g.before_attempt(b"x" * 100, 100)
        with self.assertRaises(SpendRefused) as cm:
            g.before_attempt(b"x" * 100, 100)
        self.assertEqual(cm.exception.code, "task_cap_exceeded")

    def test_negative_amount_refuses_even_with_valid_chain(self):
        self.guard().before_attempt(b"x" * 100, 100)
        self.ledger._append({"t": "reserve", "id": "z", "task": "T9", "model": "m", "usd": "-5"})
        with self.assertRaises(SpendRefused) as cm:
            self.guard().before_attempt(b"x", 1)
        self.assertEqual(cm.exception.code, "ledger_corrupt")

    def test_missing_usage_keeps_reservation(self):
        g = self.guard()
        rid = g.before_attempt(b"x" * 100, 100)
        before = self.total()
        g.settle(rid, {})
        g.settle(rid, {"prompt_tokens": "7"})
        g.settle(rid, {"prompt_tokens": 0, "completion_tokens": 0})
        g.settle(rid, {"prompt_tokens": True, "completion_tokens": False})
        g.settle(rid, {"prompt_tokens": -1, "completion_tokens": 5})
        self.assertEqual(self.total(), before)
        self.assertEqual(self.ledger.summary()["unsettled"], 1)


class PaidProviderTest(LedgerBase):
    def test_request_is_json_object_with_schema_in_prompt(self):
        p, op = self.provider([OK_BODY])
        p.complete([{"role": "system", "content": "S"}, {"role": "user", "content": "u"}],
                   {"type": "object", "title": "SCHEMA-MARK"}, "result", 100, seed=3)
        body = json.loads(op.requests[0][0].data)
        self.assertEqual(body["response_format"], {"type": "json_object"})
        self.assertEqual(body["thinking"], {"type": "disabled"})
        self.assertNotIn("seed", body)
        self.assertIn("SCHEMA-MARK", body["messages"][0]["content"])
        self.assertTrue(body["messages"][0]["content"].startswith("S"))
        self.assertIn("JSON", body["messages"][0]["content"])
        self.assertEqual(body["model"], "deepseek-flash")

    def test_llamacpp_profile_unchanged(self):
        p = OpenAICompatProvider(api_key="k")
        body = p.body([], {"type": "object"}, "result", 10, seed=2)
        self.assertEqual(body["response_format"]["type"], "json_schema")
        self.assertEqual(body["chat_template_kwargs"], {"enable_thinking": False})
        self.assertEqual(body["seed"], 2)
        self.assertFalse(p.paid)

    def test_every_attempt_including_retries_is_reserved_before_the_request(self):
        seen = []
        p, op = self.provider([urllib.error.HTTPError("u", 503, "busy", {}, None),
                               urllib.error.URLError("down"), OK_BODY])
        orig = op.__call__

        def spy(req, timeout):
            seen.append(len(entries(self.ledger)))
            return orig(req, timeout)
        p._opener = spy
        p.complete([], {}, "r", 10)
        self.assertEqual(seen, [1, 2, 3])  # a reservation exists before attempt k

    def test_retry_is_refused_when_cap_does_not_allow_it(self):
        p, op = self.provider([urllib.error.URLError("down"), OK_BODY], cap="0.0002")
        with self.assertRaises(SpendRefused):
            p.complete([], {}, "r", 50)  # worst case ~0.00006: the first fits, the retry does not
        self.assertEqual(len(op.requests), 1)

    def test_settle_uses_actual_usage_and_4xx_is_released(self):
        p, _ = self.provider([urllib.error.HTTPError("u", 429, "slow", {}, None), OK_BODY])
        p.complete([], {}, "r", 1000)
        es = entries(self.ledger)
        self.assertEqual([e["t"] for e in es], ["reserve", "settle", "reserve", "settle"])
        self.assertEqual(es[1]["usd"], "0")
        self.assertEqual(es[3]["usd"], "0.00000390")  # 5 in, 2 out

    def test_5xx_and_timeout_keep_worst_case(self):
        p, _ = self.provider([urllib.error.HTTPError("u", 503, "x", {}, None), TimeoutError("t"),
                              urllib.error.URLError("d")])
        with self.assertRaises(ProviderUnavailable):
            p.complete([], {}, "r", 100)
        self.assertEqual(self.ledger.summary()["unsettled"], 3)

    def test_connection_refused_and_dns_failure_release_the_reserve(self):
        import socket
        p, _ = self.provider([urllib.error.URLError(ConnectionRefusedError()),
                              urllib.error.URLError(socket.gaierror(-2, "no name")), OK_BODY])
        p.complete([], {}, "r", 100)
        self.assertEqual([e["usd"] for e in entries(self.ledger) if e["t"] == "settle"][:2], ["0", "0"])

    def test_reset_and_timeout_keep_the_reserve(self):
        p, _ = self.provider([ConnectionResetError("r"), TimeoutError("t"), urllib.error.URLError(TimeoutError())])
        with self.assertRaises(ProviderUnavailable):
            p.complete([], {}, "r", 100)
        self.assertEqual(self.ledger.summary()["unsettled"], 3)

    def test_answer_without_choices_books_its_usage(self):
        p, _ = self.provider([{"usage": {"prompt_tokens": 1000, "completion_tokens": 1000}}])
        with self.assertRaises(ProviderError):
            p.complete([], {}, "r", 100)
        self.assertEqual(self.ledger.summary()["unsettled"], 0)
        self.assertEqual(self.total(), Decimal("0.00150000"))

    def test_free_profile_refuses_public_host(self):
        for url in ("https://api.deepseek.com/chat/completions", "http://example.com/v1"):
            with self.assertRaises(ValueError):
                OpenAICompatProvider(url=url, api_key="k")
        for url in ("http://127.0.0.1:8090/v1", "http://10.8.0.1:8090/v1", "http://localhost/v1"):
            OpenAICompatProvider(url=url, api_key="k")

    def test_prompt_has_lowercase_json_and_example(self):
        p, _ = self.provider([OK_BODY])
        schema = {"type": "object", "properties": {"a": {"type": "string"}, "b": {"type": "array", "items": {"type": "integer"}}}}
        body = p.body([{"role": "user", "content": "u"}], schema, "result", 10)
        text = body["messages"][0]["content"]
        self.assertIn("json", text)
        self.assertIn('{"a": "text", "b": [0]}', text)

    def test_model_ids_are_consistent(self):
        raw = json.loads(config.PRICES_FILE.read_text())
        for key, prof in config.PROFILES.items():
            self.assertEqual(key, prof.name)
            if prof.paid:
                self.assertEqual(key, prof.model)
                self.assertIn(prof.model, raw["models"])
        self.assertEqual(set(raw["models"]), {"deepseek-flash", "deepseek-v4-pro"})

    def test_http_402_stops_without_retry(self):
        p, op = self.provider([urllib.error.HTTPError("u", 402, "pay", {}, None)])
        with self.assertRaises(InsufficientFunds):
            p.complete([], {}, "r", 10)
        self.assertEqual(len(op.requests), 1)
        self.assertEqual(self.total(), 0)

    def test_paid_provider_without_guard_refuses(self):
        p = OpenAICompatProvider(profile=config.PROFILES["deepseek-flash"], api_key="k", opener=FakeOpener([]))
        with self.assertRaises(RuntimeError):
            p.complete([], {}, "r", 10)

    def test_raw_body_is_handed_over_before_parsing(self):
        raws = []
        p, _ = self.provider([OK_BODY])
        p.complete([], {}, "r", 10, on_raw=raws.append)
        self.assertEqual(json.loads(raws[0]), OK_BODY)
        self.assertEqual(parse_response(raws[0]).content, '{"x": 1}')


class PaidLoopTest(LedgerBase):
    """Resume without a second payment, and the per-(step, model) failure limit."""

    def loop(self, provider, run_dir=None):
        return CandidateLoop(contract(max_rounds=5), 1, provider, FakeVerifier(), run_dir or self.tmp / "c1",
                             "CTX", clock=lambda: 0.0)

    def test_resume_uses_saved_raw_body_without_a_request(self):
        class Dies(FakeProvider):
            def complete(self, messages, schema, schema_name, max_tokens, seed=None, on_raw=None):
                on_raw(json.dumps({"choices": [{"message": {"content": json.dumps(art("good"))}}],
                                   "usage": {"prompt_tokens": 7, "completion_tokens": 3}}).encode())
                raise KeyboardInterrupt("killed after the raw body was saved")
        with self.assertRaises(KeyboardInterrupt):
            self.loop(Dies([])).run()
        p = FakeProvider([])
        st = self.loop(p).run()
        self.assertEqual(p.calls, [])
        self.assertEqual(st["outcome"], "passed")
        self.assertEqual(st["tokens"], {"prompt": 7, "completion": 3})

    def test_non_json_stale_raw_does_not_block_a_new_request(self):
        class Junk(FakeProvider):
            def complete(self, messages, schema, schema_name, max_tokens, seed=None, on_raw=None):
                on_raw(b"<html>")
                raise ProviderUnavailable("down")
        with self.assertRaises(ProviderUnavailable):
            self.loop(Junk([])).run()
        self.assertEqual(self.loop(FakeProvider([art("good")])).run()["outcome"], "passed")

    def test_two_failures_of_a_model_stop_a_paid_loop(self):
        p = FakeProvider([art("x_only"), EXPLAIN, art("y_only"), EXPLAIN, art("good")])
        p.paid, p.model = True, MODEL
        st = self.loop(p).run()
        self.assertEqual(st["outcome"], ESCALATED)
        self.assertIn("failures of deepseek-flash", st["stop"])
        self.assertEqual(st["failures"], {f"attempt|{MODEL}": 2})
        self.assertEqual(st["provider"], "unknown")

    def test_free_provider_keeps_all_rounds(self):
        st = self.loop(FakeProvider([art("x_only"), EXPLAIN, art("good")])).run()
        self.assertEqual(st["outcome"], "passed")


class PaidRunnerTest(LedgerBase):
    def setUp(self):
        super().setUp()
        self.repo = TempRepo({"data/nodes.csv": NODES_CSV})
        self.qdir = self.repo.tmp / "queue"
        self.env = Env(repo_root=self.repo.root, runs_dir=self.repo.tmp / "runs",
                       worktrees_dir=self.repo.tmp / "wt", lessons_dir=self.repo.tmp / "lessons")
        self.q = TaskQueue(self.qdir)

    def tearDown(self):
        self.repo.cleanup()
        super().tearDown()

    def submit(self, task_id):
        self.q.submit(a2_contract(repo=self.repo, task_id=task_id), repo_root=self.repo.root)

    def paid_provider(self, script):
        p = FakeProvider(script)
        p.paid, p.model, p.guard = True, MODEL, self.guard()
        return p

    def test_402_stops_the_run_and_requeues(self):
        self.submit("T-a")
        self.submit("T-b")
        p = self.paid_provider([InsufficientFunds("HTTP 402")])
        logs = []
        self.assertFalse(run_queue(self.qdir, p, self.env, log=logs.append))
        self.assertEqual(len(p.calls), 1)
        for t in ("T-a", "T-b"):
            self.assertEqual(self.q.state(t)["state"], "QUEUED")
        self.assertIn("402", self.q.state("T-a")["history"][-1]["reason"])
        self.assertIn("402", logs[0])

    def test_spend_refusal_stops_the_run_and_requeues(self):
        self.submit("T-a")
        p = self.paid_provider([SpendRefused("cap_exceeded")])
        self.assertFalse(run_queue(self.qdir, p, self.env, log=lambda m: None))
        self.assertEqual(self.q.state("T-a")["state"], "QUEUED")
        self.assertIn("cap_exceeded", self.q.state("T-a")["history"][-1]["reason"])

    def test_guard_is_bound_to_the_task(self):
        self.submit("T-a")
        p = self.paid_provider([ProviderUnavailable("down")])
        p.guard.task = None
        with self.assertRaises(ProviderUnavailable):
            process_task(self.q, "T-a", p, self.env)
        self.assertIsNone(p.guard.task)  # reset in finally

    def test_paid_provider_uses_one_candidate(self):
        self.submit("T-a")
        orig = self.q.contract
        self.q.contract = lambda t: {**orig(t), "max_candidates": 2}  # as a B1 contract would ask
        bad = {"status": "done", "assumptions": [], "open_questions": [], "findings": []}
        p = self.paid_provider([bad, EXPLAIN, bad])
        process_task(self.q, "T-a", p, self.env)
        self.assertEqual(len(list((self.env.runs_dir / "T-a").glob("c[0-9]*"))), 1)

    def test_infra_failures_are_limited_per_task(self):
        self.submit("T-a")
        p = self.paid_provider([ProviderUnavailable("down")] * 3)
        for _ in range(2):
            with self.assertRaises(ProviderUnavailable):
                process_task(self.q, "T-a", p, self.env)
        self.assertEqual(process_task(self.q, "T-a", p, self.env), "ESCALATED")
        self.assertIn("infrastructure", self.q.state("T-a")["history"][-1]["reason"])

    def test_free_provider_outages_are_unlimited(self):
        self.submit("T-a")
        p = FakeProvider([ProviderUnavailable("down")] * 4)
        for _ in range(4):
            with self.assertRaises(ProviderUnavailable):
                process_task(self.q, "T-a", p, self.env)
        self.assertEqual(self.q.state("T-a")["state"], "QUEUED")

    def test_api_key_never_reaches_runs_or_log(self):
        self.submit("T-a")
        good = {"choices": [{"message": {"content": json.dumps({"status": "done", "assumptions": [],
                "open_questions": [], "findings": [{"check": "dangling_requires", "node_id": "c", "ref": "zz",
                "explanation": "zz is not an id in the table at all"}]})}}],
                "usage": {"prompt_tokens": 50, "completion_tokens": 20}}
        p, _ = self.provider([good])
        logs = []
        self.assertTrue(run_queue(self.qdir, p, self.env, log=logs.append))
        self.assertEqual(self.q.state("T-a")["state"], "ACCEPTED")
        files = [f for f in self.env.runs_dir.rglob("*") if f.is_file()]
        self.assertTrue(any(f.name.endswith("_raw.bin") for f in files))
        for f in files:
            self.assertNotIn(b"SECRET-KEY", f.read_bytes(), f.name)
        self.assertNotIn("SECRET-KEY", "\n".join(logs))
        self.assertNotIn("SECRET-KEY", report.build(self.qdir, self.env.runs_dir, "2026-11-13", self.ledger))

    def test_second_run_is_refused_while_the_first_holds_the_lock(self):
        import fcntl
        self.submit("T-a")
        self.qdir.mkdir(exist_ok=True)
        with open(self.qdir / ".run.lock", "w") as held:
            fcntl.flock(held, fcntl.LOCK_EX)
            p = self.paid_provider([])
            logs = []
            self.assertFalse(run_queue(self.qdir, p, self.env, log=logs.append))
        self.assertEqual(p.calls, [])
        self.assertEqual(self.q.state("T-a")["state"], "QUEUED")
        self.assertIn("another run", logs[0])

    def test_report_names_provider_and_shows_ledger(self):
        self.submit("T-a")
        good = {"status": "done", "assumptions": [], "open_questions": [], "findings": [
            {"check": "dangling_requires", "node_id": "c", "ref": "zz",
             "explanation": "zz is not an id in the table at all"}]}
        p = self.paid_provider([good])
        p.name = MODEL
        self.guard().before_attempt(b"x" * 100, 100)
        process_task(self.q, "T-a", p, self.env)
        text = report.build(self.qdir, self.env.runs_dir, "2026-11-13", self.ledger, Decimal("5.00"))
        self.assertIn("Токены (deepseek-flash)", text)
        self.assertNotIn("Qwen", text)
        self.assertIn("## Расход (ledger)", text)
        self.assertIn("из лимита $5.00", text)
        self.assertIn("`T1`", text)
        absent = report.build(self.qdir, self.env.runs_dir, "2026-11-13", Ledger(self.tmp / "none"))
        self.assertIn("Ledger не создан", absent)


if __name__ == "__main__":
    unittest.main()
