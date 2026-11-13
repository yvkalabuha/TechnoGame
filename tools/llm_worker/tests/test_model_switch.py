import tempfile
import unittest
from pathlib import Path

from helpers import FakeProvider

from tools.llm_worker.loop import ESCALATED, PASSED, CandidateLoop
from test_loop import EXPLAIN, FakeVerifier, art, contract


class Paid(FakeProvider):
    paid = True

    def __init__(self, model, script):
        super().__init__(script)
        self.model = model


class ModelSwitchTest(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self._tmp.name) / "c1"

    def tearDown(self):
        self._tmp.cleanup()

    def loop(self, flash, pro=None):
        return CandidateLoop(contract(), 1, flash, FakeVerifier(), self.dir, "CTX", lambda: 0.0,
                             fallbacks=[pro] if pro else ())

    def test_two_flash_failures_switch_to_pro_which_passes(self):
        flash = Paid("flash", [art("x_only"), EXPLAIN, art("x_only")])
        pro = Paid("pro", [EXPLAIN, art("good")])
        st = self.loop(flash, pro).run()
        self.assertEqual(st["outcome"], PASSED)
        self.assertEqual(len(flash.calls), 3)
        self.assertEqual(len(pro.calls), 2)
        self.assertEqual([r["model"] for r in st["rounds"]], ["flash", "flash", "pro"])
        self.assertIn("flash -> pro", st["rounds"][1]["switch"])

    def test_two_pro_failures_escalate(self):
        flash = Paid("flash", [art("x_only"), EXPLAIN, art("x_only")])
        pro = Paid("pro", [EXPLAIN, art("x_only"), EXPLAIN, art("x_only")])
        st = self.loop(flash, pro).run()
        self.assertEqual(st["outcome"], ESCALATED)
        self.assertEqual(st["failures"], {"attempt|flash": 2, "attempt|pro": 2})
        self.assertEqual(len(pro.calls), 4)

    def test_switch_survives_restart(self):
        flash = Paid("flash", [art("x_only"), EXPLAIN, art("x_only")])
        pro = Paid("pro", [EXPLAIN, ProviderDown()])
        with self.assertRaises(Exception):
            self.loop(flash, pro).run()
        flash2, pro2 = Paid("flash", []), Paid("pro", [art("good")])
        st = self.loop(flash2, pro2).run()
        self.assertEqual(st["outcome"], PASSED)
        self.assertEqual(flash2.calls, [])

    def test_no_fallback_still_escalates_after_limit(self):
        st = self.loop(Paid("flash", [art("x_only"), EXPLAIN, art("x_only")])).run()
        self.assertEqual(st["outcome"], ESCALATED)


def ProviderDown():
    from tools.llm_worker.provider import ProviderUnavailable
    return ProviderUnavailable("down")


if __name__ == "__main__":
    unittest.main()
