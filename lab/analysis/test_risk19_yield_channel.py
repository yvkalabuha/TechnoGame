import math
import unittest

import risk19_yield_channel as m


SHARE_TOL = 0.005
FALL_TOL = 0.0005


def share_and_fall(n, **kw):
    full, yc, ac = m.channels(n, **kw)
    return yc / (yc + ac), 1 - math.exp(-full)


def share_and_fall_x0(n, k):
    d = lambda y, a: math.log(m.cost_x0(n, y, a, k) / m.cost_x0(2 * n, y, a, k))
    full, none = d(True, True), d(False, False)
    yc = .5 * ((full - d(False, True)) + (d(True, False) - none))
    ac = .5 * ((full - d(True, False)) + (d(False, True) - none))
    return yc / (yc + ac), 1 - math.exp(-full)


class TestScenarioAMature(unittest.TestCase):
    def test_mature_min(self):
        self.assertEqual(m.mature_min(1), 16384)

    def test_n_16384(self):
        share, fall = share_and_fall(16384)
        self.assertAlmostEqual(fall, 0.022, delta=FALL_TOL)
        self.assertAlmostEqual(share, 0.68, delta=SHARE_TOL)

    def test_n_32768(self):
        share, fall = share_and_fall(32768)
        self.assertAlmostEqual(fall, 0.013, delta=FALL_TOL)
        self.assertAlmostEqual(share, 0.64, delta=SHARE_TOL)


class TestScenarioG(unittest.TestCase):
    def test_k6_n1(self):
        share, fall = share_and_fall_x0(1, 6)
        self.assertAlmostEqual(share, 0.0, delta=SHARE_TOL)
        self.assertAlmostEqual(fall, 0.040, delta=FALL_TOL)

    def test_k6_n400(self):
        share, fall = share_and_fall_x0(400, 6)
        self.assertAlmostEqual(share, 0.17, delta=SHARE_TOL)
        self.assertAlmostEqual(fall, 0.020, delta=FALL_TOL)

    def test_k6_n1000(self):
        share, _ = share_and_fall_x0(1000, 6)
        self.assertAlmostEqual(share, 0.29, delta=SHARE_TOL)

    def test_k3_n400(self):
        share, _ = share_and_fall_x0(400, 3)
        self.assertAlmostEqual(share, 0.34, delta=SHARE_TOL)


class TestRamp(unittest.TestCase):
    def test_ramp_at_zero(self):
        self.assertAlmostEqual(m.ramp(0, m.XH[5]), m.LAMBDA0, delta=1e-12)

    def test_ramp_grows_and_below_one(self):
        xh = m.XH[5]
        xs = [0.0, 1.0, 10.0, 100.0, 1000.0, 10000.0]
        vals = [m.ramp(x, xh) for x in xs]
        for a, b in zip(vals, vals[1:]):
            self.assertLess(a, b)
        for v in vals:
            self.assertLess(v, 1.0)
            self.assertGreaterEqual(v, m.LAMBDA0)


class TestUnitCostDecreases(unittest.TestCase):
    def test_cost_decreases_with_n(self):
        ns = [1, 2, 4, 8, 16, 64, 256, 1024, 4096, 16384]
        costs = [m.cost(n, True, True) for n in ns]
        for a, b in zip(costs, costs[1:]):
            self.assertLess(b, a)


if __name__ == "__main__":
    unittest.main()
