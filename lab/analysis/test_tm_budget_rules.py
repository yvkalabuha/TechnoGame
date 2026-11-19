"""Tests for tm_budget_rules.py (TASK-0123): the Fx6 replica of the budget rules and the §7/§8 criteria on synthetic runs.

Run: python3 -m unittest discover -s lab/analysis -p 'test_tm_budget_rules.py' -v
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tm_budget_rules as T  # noqa: E402

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
B0 = {"USA": 1_000_000, "GBR": 239_000, "SUN": 229_000, "FRA": 70_000, "SWE": 24_000}


class Replica(unittest.TestCase):
    def setUp(self):
        self.g = T.scenario_growth(REPO)

    def test_growth_is_read_from_the_scenario(self):
        self.assertEqual(self.g, {"USA": 29_900, "GBR": 22_000, "SUN": 54_800, "FRA": 66_200, "SWE": 41_700})

    def test_month_1_of_rule_A_and_B2_values_of_the_pilot_run(self):
        # supply_month of the pilot (seed 1): A at t = 1; B2 SWE at t = 297, 298 (first capped month 298) and USA, SWE at t = 1023.
        a = T.replica("A", B0, self.g, 2)
        self.assertEqual({k: v[1] for k, v in a.items()}, {"USA": 1_002_491, "GBR": 239_346, "SUN": 230_628, "FRA": 70_842, "SWE": 24_074})
        b2 = T.replica("B2", B0, self.g, 1024)
        self.assertEqual((b2["SWE"][297], b2["SWE"][298], b2["SWE"][1023], b2["USA"][1023]), (66_987, 67_156, 407_759, 12_742_494))
        now = T.replica("NOW", B0, self.g, 1024)
        self.assertEqual(now["USA"], b2["USA"])
        first = next(t for t in range(1024) if now["SWE"][t] != b2["SWE"][t])
        self.assertEqual(first, 298)

    def test_floor_arithmetic_is_floor_not_truncation(self):
        self.assertEqual(T.fmul(-1, 1), -1)  # FloorDiv(−1, 10^6) = −1
        self.assertEqual(T.monthly(-1), -1)

    def test_closed_forms_follow_the_replica_closely(self):
        for rule in ("NOW", "A", "B1", "B2"):
            rep = T.replica(rule, B0, self.g, 1024)
            for a in T.ACTORS:
                for t in (0, 100, 500, 1023):
                    cf = T.closed_form(rule, a, t, B0, self.g)
                    self.assertLess(abs(rep[a][t] / T.FX - cf) / cf, 0.02, (rule, a, t))

    def test_real_ceiling_month_of_B2_SUN(self):
        self.assertEqual(T.real_ceiling_month("B2", "SUN", B0, self.g), 305)
        self.assertIsNone(T.real_ceiling_month("B2", "GBR", B0, self.g))


def run(ranks, key_g=2, win=None, first_unit=None, starts=None):
    """A synthetic analysed run: ranks[(year, actor)] (default 1), the same key_G everywhere, catch-up wins by actor."""
    yearly = {(y, a): {"rank": ranks.get((y, a), 1), "key_g": key_g} for y in T.CHECK_YEARS for a in T.ACTORS}
    return {"yearly": yearly, "win": win or {a: False for a in T.ACTORS}, "first_unit": first_unit or {}, "t_req": {},
            "starts": starts or {a: [] for a in T.ACTORS}}


class Criteria(unittest.TestCase):
    def test_s8_rank_needs_8_of_10_and_never_more_than_1(self):
        base = {s: run({}) for s in range(1, 11)}
        two_off = {s: run({(1960, "SUN"): 2} if s <= 2 else {}) for s in range(1, 11)}
        self.assertEqual(T.tolerance_s8(two_off, base), [])
        three_off = {s: run({(1960, "SUN"): 2} if s <= 3 else {}) for s in range(1, 11)}
        self.assertTrue(any("equal in 7 of 10" in v for v in T.tolerance_s8(three_off, base)))
        far = {s: run({(1977, "FRA"): 3} if s == 1 else {}) for s in range(1, 11)}
        self.assertTrue(any("more than 1" in v for v in T.tolerance_s8(far, base)))

    def test_s8_first_unit_and_lines_shift(self):
        base = {1: run({}, first_unit={("FRA", 1): 100}, starts={**{a: [] for a in T.ACTORS}, "FRA": [90, 200]})}
        ok = {1: run({}, first_unit={("FRA", 1): 124}, starts={**{a: [] for a in T.ACTORS}, "FRA": [114, 200]})}
        self.assertEqual(T.tolerance_s8(ok, base), [])
        late = {1: run({}, first_unit={("FRA", 1): 125}, starts={**{a: [] for a in T.ACTORS}, "FRA": [115, 200, 300]})}
        v = T.tolerance_s8(late, base)
        self.assertTrue(any("first unit" in x for x in v))
        self.assertTrue(any("FRA first line" in x for x in v))
        self.assertTrue(any("lines before" in x for x in v))

    def test_money_limits_by_places_or_wins(self):
        base = {s: run({}) for s in range(1, 11)}
        self.assertEqual(T.money_limits(base, base), [])
        places = {s: run({(2010, "GBR"): 3}) for s in range(1, 11)}
        self.assertTrue(any("GBR 2010" in h for h in T.money_limits(places, base)))
        wins = {s: run({}, win={**{a: False for a in T.ACTORS}, "SWE": s <= 3}) for s in range(1, 11)}
        self.assertTrue(any("SWE: catch-up wins 3 vs 0" in h for h in T.money_limits(wins, base)))


if __name__ == "__main__":
    unittest.main()
