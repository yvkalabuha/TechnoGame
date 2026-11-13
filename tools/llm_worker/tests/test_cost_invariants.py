"""Invariants a paid provider needs: no lost task, no double charge, bounded attempts.
Tests marked expectedFailure document a known gap in production code (they flip to
'unexpected success' once the gap is fixed: remove the decorator then)."""

import json
import tempfile
import unittest
from pathlib import Path

from helpers import FakeProvider

from tools.llm_worker.loop import PASSED, CandidateLoop
from tools.llm_worker.provider import ProviderUnavailable
from tools.llm_worker.taskqueue import TaskQueue
from test_loop import EXPLAIN, FakeVerifier, art, contract


class CostBase(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self._tmp.name) / "c1"

    def tearDown(self):
        self._tmp.cleanup()

    def loop(self, provider, c=None):
        self.verifier = FakeVerifier()
        return CandidateLoop(c or contract(), 1, provider, self.verifier, self.dir, "CTX", clock=lambda: 0.0)

    def saved(self):
        return json.loads((self.dir / "loop.json").read_text())


class CostTest(CostBase):
    def test_outage_then_resume_counts_tokens_once(self):
        ref_dir = self.dir
        self.dir = Path(self._tmp.name) / "ref"
        ref = self.loop(FakeProvider([art("x_only"), EXPLAIN, art("good")])).run()
        self.dir = ref_dir
        p = FakeProvider([art("x_only"), ProviderUnavailable("down")])
        with self.assertRaises(ProviderUnavailable):
            self.loop(p).run()
        st = self.loop(FakeProvider([EXPLAIN, art("good")])).run()
        self.assertEqual(st["tokens"], ref["tokens"])
        self.assertEqual(st["outcome"], PASSED)

    def test_repeated_outages_do_not_consume_retries(self):
        for _ in range(4):
            with self.assertRaises(ProviderUnavailable):
                self.loop(FakeProvider([ProviderUnavailable("down")])).run()
        self.assertEqual(self.saved()["tokens"], {"prompt": 0, "completion": 0})
        st = self.loop(FakeProvider([art("good")])).run()
        self.assertEqual(len(st["rounds"]), 1)
        self.assertEqual(st["outcome"], PASSED)

    def test_finished_loop_makes_no_further_calls(self):
        st = self.loop(FakeProvider([art("good")])).run()
        p = FakeProvider([])  # any call would raise AssertionError
        st2 = self.loop(p).run()
        self.assertEqual(p.calls, [])
        self.assertEqual(st2["tokens"], st["tokens"])

    def test_budget_stop_is_persistent_and_overshoot_is_one_call(self):
        c = contract(max_total_tokens=150)  # each fake call = 110 tokens
        st = self.loop(FakeProvider([art("x_only"), EXPLAIN]), c).run()
        used = st["tokens"]["prompt"] + st["tokens"]["completion"]
        self.assertLessEqual(used, 150 + 110)
        p = FakeProvider([])
        self.loop(p, c).run()
        self.assertEqual(p.calls, [])

    def test_time_budget_stops(self):
        st = self.loop(FakeProvider([art("x_only"), EXPLAIN]), contract(max_minutes=0.01)).run()
        self.assertTrue(st["stop"].startswith("budget"))
        self.assertEqual(len(st["rounds"]), 2)  # round 0 ran (provider_s=1.0 >= 0.6 s), round 1 not_run

    @unittest.expectedFailure
    def test_crash_after_response_before_save_does_not_recharge(self):
        """GAP (high): the paid response is lost if the process dies between provider.complete()
        and the save of rnd['raw']; the resumed run asks (and pays) again."""
        p = FakeProvider([art("good"), art("good")])
        lp = self.loop(p)
        orig = lp._save

        def crash_after_raw():
            r = lp.st["rounds"][-1] if lp.st["rounds"] else {}
            if "raw" in r and "report" not in r:
                raise KeyboardInterrupt("killed")
            orig()
        lp._save = crash_after_raw
        with self.assertRaises(KeyboardInterrupt):
            lp.run()
        self.loop(p).run()
        self.assertEqual(len(p.calls), 1, "the same request was sent (and billed) twice")


class QueueCostTest(unittest.TestCase):
    def test_interrupt_leaves_running_but_recover_requeues(self):
        with tempfile.TemporaryDirectory() as d:
            q = TaskQueue(Path(d))
            c = {"task_id": "T-x"}
            (Path(d) / "T-x").mkdir()
            from tools.llm_worker.contract import contract_hash
            (Path(d) / "T-x" / "contract.json").write_text(json.dumps(c))
            (Path(d) / "T-x" / "state.json").write_text(json.dumps(
                {"task_id": "T-x", "contract_sha256": contract_hash(c), "state": "RUNNING", "history": []}))
            q.recover()
            self.assertEqual(q.state("T-x")["state"], "QUEUED")
            self.assertEqual(q.contract("T-x"), c)


if __name__ == "__main__":
    unittest.main()
