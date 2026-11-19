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

    def test_rule_A_clamp_keeps_the_share_from_crossing_s(self):
        # 810e12a: share exactly s = 0.027 of B_F = 1; the plain floor step gives 27067 < s·Next(B_F) = 27067.257, the clamp rounds up.
        self.assertEqual(T.next_a(27_000, 1_000_000, 29_900, 27_000, 60_000), 27_068)
        # Below s the result stays at or below floor(s·Next(B_F)).
        self.assertLessEqual(T.next_a(26_999, 1_000_000, 29_900, 27_000, 12_000_000) * T.FX, 27_000 * T.next_now(1_000_000, 29_900))

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


def run(ranks, key_g=2, win=None, first_unit=None, starts=None, t_req=None):
    """A synthetic analysed run: ranks[(year, actor)] (default 1), the same key_G everywhere, catch-up wins by actor."""
    yearly = {(y, a): {"rank": ranks.get((y, a), 1), "key_g": key_g} for y in T.CHECK_YEARS for a in T.ACTORS}
    return {"yearly": yearly, "win": win or {a: False for a in T.ACTORS}, "first_unit": first_unit or {}, "t_req": t_req or {},
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
        self.assertTrue(any("FRA lines before 384 (count change): 3 vs 2" in x for x in v))

    def test_s8_project_moved_into_the_window_by_the_variant_is_compared(self):
        # Codex (TASK-0123): NOW t_req 410 is past the cutoff, the variant brings it to 350: a 60-month shift inside the window.
        base = {1: run({}, first_unit={("SWE", 2): None}, t_req={("SWE", 2): 410})}
        var = {1: run({}, first_unit={("SWE", 2): None}, t_req={("SWE", 2): 350})}
        self.assertTrue(any("SWE G2 project t_req" in x and "shift -60" in x for x in T.tolerance_s8(var, base)))
        # Present before the cutoff on one side only.
        only = {1: run({}, first_unit={("SWE", 2): 370})}
        self.assertTrue(any("one side only" in x for x in T.tolerance_s8(only, {1: run({})})))

    def test_s8_line_crossing_384_within_24_months_is_a_count_change_with_a_note(self):
        # Codex r2 (TASK-0123): §8 "the number of lines does not change" is its own condition. Base 370, variant 390: the shift (20) passes,
        # the count before 384 changes 1 -> 0, so it is a violation, annotated as a boundary crossing.
        base = {1: run({}, starts={**{a: [] for a in T.ACTORS}, "GBR": [370]})}
        var = {1: run({}, starts={**{a: [] for a in T.ACTORS}, "GBR": [390]})}
        v = T.tolerance_s8(var, base)
        self.assertEqual(len(v), 1, v)
        self.assertIn("GBR lines before 384 (count change): 0 vs 1; boundary crossing within 24 months: line 1 390 vs 370", v[0])
        # Mirror: the variant brings the line into the window.
        v = T.tolerance_s8(base, var)
        self.assertEqual(len(v), 1, v)
        self.assertIn("GBR lines before 384 (count change): 1 vs 0; boundary crossing within 24 months: line 1 370 vs 390", v[0])
        # Far: a shift violation and a count violation without the note.
        far = {1: run({}, starts={**{a: [] for a in T.ACTORS}, "GBR": [420]})}
        v = T.tolerance_s8(far, base)
        self.assertTrue(any("GBR line 1 (shift)" in x for x in v))
        self.assertTrue(any("(count change): 0 vs 1" in x and "boundary" not in x for x in v))

    def test_money_limits_by_places_or_wins(self):
        base = {s: run({}) for s in range(1, 11)}
        self.assertEqual(T.places_or_wins(base, base), [])
        places = {s: run({(2010, "GBR"): 3}) for s in range(1, 11)}
        self.assertTrue(any("GBR 2010" in h for h in T.places_or_wins(places, base)))
        wins = {s: run({}, win={**{a: False for a in T.ACTORS}, "SWE": s <= 3}) for s in range(1, 11)}
        self.assertTrue(any("SWE: catch-up wins 3 vs 0" in h for h in T.places_or_wins(wins, base)))


class Validity(unittest.TestCase):
    def test_series_with_fewer_than_10_seeds_fails_unless_pilot(self):
        # Codex (TASK-0123): a non-pilot directory with 1 seed printed VALIDITY: OK.
        self.assertTrue(T.seed_problems([1], pilot=False))
        self.assertEqual(T.seed_problems([1], pilot=True), [])
        self.assertEqual(T.seed_problems(list(range(1, 11)), pilot=False), [])

    def test_b1_may_differ_one_month_before_the_first_budget_difference(self):
        # Reviewer risk: the products step of month tc - 1 reads budget(tc), so a difference at tc - 1 is not a failure; at tc - 2 it is.
        self.assertIsNone(T.b1_problem(1, 396, 395))
        self.assertIsNotNone(T.b1_problem(1, 396, 394))
        self.assertIsNone(T.b1_problem(1, 396, None))


if __name__ == "__main__":
    unittest.main()
