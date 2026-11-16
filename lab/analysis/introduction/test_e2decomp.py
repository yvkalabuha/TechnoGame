"""Unit tests of the TASK-0059 E2 decomposition helpers (Kaplan-Meier and the keep-the-line counterfactual)."""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import e2decomp as E  # noqa: E402


class KaplanMeier(unittest.TestCase):
    def test_hand_example(self):
        # S(10) = 3/4, S(30) = 3/4 * 1/2 = 3/8 (the censored 20 leaves the risk set), S(40) = 0
        r = E.km([(10, True), (20, False), (30, True), (40, True)])
        self.assertEqual((r["p25"], r["median"], r["p75"]), (10, 30, 40))
        self.assertEqual(r["at_risk_at_median"], 2)

    def test_all_censored(self):
        r = E.km([(50, False), (60, False)])
        self.assertIsNone(r["median"])
        self.assertEqual(r["S120"], 1.0)


class Counterfactual(unittest.TestCase):
    def test_keep_line(self):
        # X grows 10 units/month before the close; with g = 1 the yield reaches 0.7 at X = 4/3 * 800 = 1066.7
        rows = {t: {"x": str(10.0 * t), "x_half": "800", "yield": str(0.3 + 0.7 * 10 * t / (10 * t + 800))} for t in range(0, 101)}
        rate, (hit_c, _hit_s) = E.counterfactual(rows, 100, lambda t: 1.0)
        self.assertAlmostEqual(rate, 10.0)
        self.assertEqual(hit_c, 108)       # X(100) = 1000; 1000 + 10 * 7 = 1070 >= 1066.7 at month 108


if __name__ == "__main__":
    unittest.main()
