import json
import tempfile
import unittest
from pathlib import Path

from helpers import FakeProvider

from tools.llm_worker.loop import ESCALATED, PASSED, CandidateLoop
from tools.llm_worker.provider import ProviderUnavailable
from tools.llm_worker.verifiers.common import make_report

ACCEPTANCE = [{"check": "x", "hard": True}, {"check": "y", "hard": True}, {"check": "s", "hard": False}]

# Artifact name -> measured values of checks x, y, s.
OUTCOMES = {
    "good": (1, 1, 1),
    "x_only": (1, 0, 0),
    "y_only": (0, 1, 1),
    "none": (0, 0, 0),
    "x_soft": (1, 0, 1),
    "x_half_y": (1, 0.5, 0),
}


class FakeVerifier:
    def __init__(self):
        self.seen = []

    def verify(self, artifact):
        self.seen.append(artifact["v"])
        x, y, s = OUTCOMES[artifact["v"]]
        detail = lambda v: "ok" if v >= 1 else f"fail {artifact['v']}"
        return make_report({"x": (x, detail(x)), "y": (y, detail(y)), "s": (s, detail(s))}, ACCEPTANCE)

    def finalize(self, artifact):
        return []


def contract(max_rounds=2, **budget):
    return {
        "task_id": "T1", "title": "t", "instructions": "do it", "max_rounds": max_rounds,
        "acceptance": ACCEPTANCE,
        "output_schema": {"type": "object", "required": ["v"], "properties": {"v": {"type": "string"}}},
        "budget": {"max_total_tokens": 10 ** 6, "max_minutes": 1000, "max_output_tokens": 100,
                   "max_context_tokens": 1000, **budget},
    }


EXPLAIN = {"failure_cause": "missed y", "fix_plan": "add y"}


def art(name):
    return {"v": name}


class LoopTest(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self._tmp.name) / "c1"

    def tearDown(self):
        self._tmp.cleanup()

    def loop(self, script, c=None):
        self.provider = FakeProvider(script) if not isinstance(script, FakeProvider) else script
        self.verifier = FakeVerifier()
        return CandidateLoop(c or contract(), 1, self.provider, self.verifier, self.dir, "CTX", clock=lambda: 0.0)

    def test_pass_first_attempt(self):
        st = self.loop([art("good")]).run()
        self.assertEqual(st["outcome"], PASSED)
        self.assertEqual(len(self.provider.calls), 1)

    def test_explain_before_fix_then_all_checks_rerun(self):
        lp = self.loop([art("x_only"), EXPLAIN, art("good")])
        st = lp.run()
        self.assertEqual(st["outcome"], PASSED)
        self.assertEqual([c["schema_name"] for c in self.provider.calls], ["result", "explain", "result"])
        explain_prompt = self.provider.calls[1]["messages"][-1]["content"]
        self.assertIn("fail x_only", explain_prompt)  # the concrete check report is sent
        self.assertIn("Do not fix yet", explain_prompt)
        fix_msgs = self.provider.calls[2]["messages"]
        self.assertEqual(fix_msgs[-2]["content"], json.dumps(EXPLAIN))
        self.assertEqual(self.verifier.seen, ["x_only", "good"])
        self.assertEqual(len(st["rounds"][1]["report"]["checks"]), 4)  # schema + all three, again
        for name in ("attempt_request.json", "attempt_response.json", "explain_response.json", "report.json"):
            self.assertTrue((self.dir / "round_1" / name).is_file(), name)
        self.assertEqual(lp.best_artifact(), art("good"))

    def test_at_most_two_retries(self):
        st = self.loop([art("none"), EXPLAIN, art("x_only"), EXPLAIN, art("x_soft"), EXPLAIN, art("good")]).run()
        self.assertEqual(st["outcome"], ESCALATED)
        self.assertIn("retries exhausted", st["stop"])
        self.assertEqual(len(st["rounds"]), 3)
        self.assertEqual(len(self.provider.calls), 5)

    def test_regression_rolls_back(self):
        # y_only fails x (passed in best) but has higher quality than x_only: rollback, loop goes on.
        st = self.loop([art("x_only"), EXPLAIN, art("y_only"), EXPLAIN, art("x_half_y")]).run()
        self.assertEqual(st["rounds"][1]["decision"], "rollback")
        self.assertIn("x", st["rounds"][1]["reason"])
        # Retry after a rollback continues from the best round and says what was rolled back.
        prompt = self.provider.calls[3]["messages"]
        self.assertEqual(prompt[2]["content"], json.dumps(art("x_only")))
        self.assertIn("rolled back", prompt[3]["content"])
        self.assertEqual(st["best"], 2)

    def test_rollback_without_quality_gain_stops(self):
        st = self.loop([art("x_soft"), EXPLAIN, art("y_only")]).run()
        self.assertEqual(st["rounds"][1]["decision"], "rollback")
        self.assertEqual(st["stop"], "rolled back and quality did not improve")
        self.assertEqual(st["outcome"], ESCALATED)
        self.assertEqual(st["best"], 0)
        self.assertEqual(len(self.provider.calls), 3)

    def test_same_failure_twice_stops(self):
        st = self.loop([art("x_only"), EXPLAIN, art("x_only")]).run()
        self.assertEqual(st["stop"], "same failure twice")
        self.assertEqual(st["outcome"], ESCALATED)

    def test_no_quality_gain_stops(self):
        st = self.loop([art("x_soft"), EXPLAIN, art("x_only")]).run()
        self.assertEqual(st["stop"], "quality did not improve")

    def test_schema_failure_is_a_failed_hard_check(self):
        st = self.loop(["not json", EXPLAIN, {"w": 1}], contract(max_rounds=1)).run()
        r0 = st["rounds"][0]["report"]
        self.assertFalse(r0["checks"][0]["passed"])
        self.assertEqual(r0["checks"][0]["check"], "schema")
        self.assertEqual(self.verifier.seen, [])
        self.assertEqual(st["outcome"], ESCALATED)

    def test_outage_resumes_without_repeating_steps(self):
        lp = self.loop([art("x_only"), ProviderUnavailable("down")])
        with self.assertRaises(ProviderUnavailable):
            lp.run()
        saved = json.loads((self.dir / "loop.json").read_text())
        self.assertIn("report", saved["rounds"][0])
        lp2 = self.loop([EXPLAIN, art("good")])  # fresh process, same run dir
        st = lp2.run()
        self.assertEqual(st["outcome"], PASSED)
        self.assertEqual([c["schema_name"] for c in self.provider.calls], ["explain", "result"])
        self.assertEqual(self.verifier.seen, ["good"])

    def test_budget_stops(self):
        st = self.loop([art("x_only"), EXPLAIN], contract(max_total_tokens=150)).run()
        self.assertTrue(st["stop"].startswith("budget"))
        self.assertEqual(st["outcome"], ESCALATED)
        self.assertEqual(st["rounds"][-1]["decision"], "not_run")


if __name__ == "__main__":
    unittest.main()
