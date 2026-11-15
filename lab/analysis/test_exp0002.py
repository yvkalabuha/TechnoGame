"""Tests for exp0002.py (TASK-0053, T11) on synthetic CSV with known answers.

Run: python3 -m unittest discover -s lab/analysis -p 'test_exp0002.py' -v
The decision-rule scenarios (P1-P5, S1-S3, diagnostics, control tables) are the script's own selftest; each of its
checks is one test here. The v0.5 r7 prints (A40, A39, F17b, licences, fleet, C1, T-SK, C2) have their own cases.
"""
import copy
import csv
import json
import math
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import exp0002 as X  # noqa: E402

CFG = copy.deepcopy(X.DEFAULTS)
FX = X.FX
N = CFG["n_months"]


def write(d, name, header, rows):
    with open(os.path.join(d, name), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(header)
        w.writerows(rows)


def summary_row(rid):
    return [rid, 1, 2, 3, 1, 2, "0" * N, "0" * N, "=" * N]


class SelfTest(unittest.TestCase):
    """Every check of `exp0002.py --selftest --quick` (synthetic data with exact truth for P1-P5 etc.)."""

    @classmethod
    def setUpClass(cls):
        cls.results, _ = X.selftest(quick=True)

    def test_all_selftest_checks(self):
        self.assertGreaterEqual(len(self.results), 40)
        for name, ok, info in self.results:
            with self.subTest(check=name):
                self.assertTrue(ok, "%s %s" % (name, info))


class V05Prints(unittest.TestCase):
    """a40, a40_c_months, tooling_ledger, lines, fleet_cmd, licences (format v0.5 r7 §4.9-4.14, §7)."""

    def build(self, d):
        runs = [["r1", "main", 0, 1, 1, "ok", "h1"], ["r2", "main", 0, 2, 1, "ok", "h2"],
                ["r3", "sale/X", 1, 1, 1, "ok", "h1"], ["r4", "sale/X", 1, 2, 1, "ok", "h4"],
                ["r5", "det", 2, 1, 4, "ok", "h1"]]
        write(d, "runs.csv", ["run", "series", "point", "seed", "threads", "status", "golden_hash"], runs)
        write(d, "summary.csv", X.SUMMARY_COLS, [summary_row(r[0]) for r in runs])
        # a40: r1 one O PROCESSES, one C COEXIST_MONEY (months fine), one C OTHER (L-C0 violation);
        # r2 one O DEFECTS (L-O0 violation) and one COEXIST_MONEY whose month has left > 0 (class violation)
        write(d, "a40.csv", X.A40_COLS, [
            ["r1", 10, "USA", 1, 2, 1, "SUPERIOR", "O", "PROCESSES", 4, "", 0, 0, 0],
            ["r1", 100, "GBR", 2, 2, 2, "PRODUCIBLE", "C", "COEXIST_MONEY", 20, 30, 0, 60, 0],
            ["r1", 200, "SUN", 3, 2, 3, "ADAPTIVE", "C", "OTHER", 20, 30, 0, 60, 0],
            ["r2", 50, "USA", 1, 2, 1, "SUPERIOR", "O", "DEFECTS", 4, "", 0, 0, 0],
            ["r2", 120, "GBR", 2, 2, 2, "PRODUCIBLE", "C", "COEXIST_MONEY", 20, 30, 0, 60, 0]])
        cm = []
        for (rid, tf, a, dsg, left, wn) in (("r1", 100, "GBR", 2, 0, 0), ("r1", 200, "SUN", 3, 5, 1), ("r2", 120, "GBR", 2, 0, 0)):
            for k in range(3):
                bad_left = left if k == 2 else 0
                if rid == "r2" and k == 1:
                    bad_left = 7
                cm.append([rid, tf, a, dsg, tf - 2 + k, 0, 39, FX, 0, 0, bad_left, wn if k == 0 else 0, 0])
        write(d, "a40_c_months.csv", X.A40_C_COLS, cm)
        # ledger: r1 clean; r2 one row invest > rest + b_cap and pool_exh at USA in 1 of 2 months
        lg = []
        for rid in ("r1", "r2", "r3", "r4"):
            for t in (0, 1):
                inv = 300 if (rid == "r2" and t == 1) else 10
                lg.append([rid, t, "USA", 100, 100, inv, 5, 5, 95, 95, 0, 1, 0, 0, 0, 0, 1 if t == 1 else 0])
        write(d, "tooling_ledger.csv", X.LEDGER_COLS, lg)
        # lines: r1 USA pair (0 W, 1 N) months 10..19, closed by YPROD in month 19; GBR pair from 370 to horizon;
        # a launch of design 2 (g_p 1) at month 25 while design 1 (g_p 2) runs; left_share sum 1.2e6 at GBR month 370 (violation)
        ln = []
        for t in range(10, 20):
            ln.append(["r1", t, "USA", 0, 1, "W", 0, "", 0, 0, 0, 1 if t == 19 else 0, 0, "YPROD" if t == 19 else "",
                       "YPROD" if t == 19 else "", 100000, 0])
            ln.append(["r1", t, "USA", 1, 2, "N", 0, "", 0, 0, 0, 0, 0, "", "", 500000, 0])
        for t in range(20, 25):
            ln.append(["r1", t, "USA", 1, 2, "SOLE", 0, "", 0, 0, 0, 0, 0, "", "", 900000, 0])
        ln.append(["r1", 25, "USA", 1, 2, "W", 0, "", 0, 0, 0, 0, 0, "", "", 0, 0])
        ln.append(["r1", 25, "USA", 2, 1, "N", 0, "", 0, 0, 0, 0, 0, "", "", 0, 1])
        for t in range(370, N):
            ln.append(["r1", t, "GBR", 0, 2, "W", 0, "", 0, 0, 0, 0, 0, "", "", 700000 if t == 370 else 0, 0])
            ln.append(["r1", t, "GBR", 1, 3, "N", 0, "", 0, 0, 0, 0, 0, "", "", 500000 if t == 370 else 0, 0])
        write(d, "lines.csv", X.LINES_COLS, ln)
        # fleet: r1 one RETIRE (2,1); one SELL (3,1) with g_front 4 -> not laggard (gen_d < G); r2 SELL (2,2): R-gen
        # violation and a laggard sale (g_front 3 - G 2 >= 1, gen_d 2 >= G 2)
        write(d, "fleet_cmd.csv", X.FLEET_COLS, [
            ["r1", 100, "USA", "RETIRE", 0, 1, 2, FX, 2, "MONEY"],
            ["r1", 200, "USA", "SELL", 0, 1, 3, FX, 4, "ALLOY"],
            ["r2", 150, "GBR", "SELL", 1, 2, 2, FX, 3, "MONEY"]])
        # licences: r1 matches its events; r2 has a licence without LIC_BUY (mismatch)
        write(d, "licences.csv", X.LICENCES_COLS, [
            ["r1", 25, "GBR", "FRA", 1, 2, 20000, 145], ["r1", 73, "GBR", "SUN", 1, 3, 20000, ""],
            ["r2", 30, "GBR", "FRA", 1, 3, 20000, ""]])
        write(d, "events.csv", X.EVENTS_COLS, [
            ["r1", 25, "LIC_BUY", "FRA", "GBR", 20000], ["r1", 73, "LIC_BUY", "SUN", "GBR", 20000],
            ["r1", 145, "LIC_END", "FRA", "GBR", 120], ["r1", 160, "LIC_RESUME", "FRA", "GBR", 135],
            ["r1", 40, "LIC_REFUSE_MONEY", "SWE", "SUN:G1", 20000]])

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.build(self.tmp.name)
        self.D = X.load([self.tmp.name], CFG)
        self.p0 = X.p0_prints(self.D, CFG)

    def tearDown(self):
        self.tmp.cleanup()

    def test_a40_lo0_lc0_and_coexist_class(self):
        ch = self.p0["a40"]["checks"]
        self.assertEqual(ch["L_O0"], {"defects_main": 1, "ok": False})
        self.assertEqual(ch["L_C0"], {"other_main": 1, "ok": False})
        self.assertEqual(ch["coexist_money_main"], 2)
        self.assertEqual(ch["coexist_class_violations_n"], 1)        # r2: left = 7 in one month
        self.assertEqual(ch["c_fires_with_w_after_n"], 1)
        self.assertEqual(ch["t_ab_months_per_fire"], [3])
        m = self.p0["a40"]["series"]["main"]
        self.assertEqual(m["cancel_design"], 2)
        self.assertEqual(m["seeds_with_c"]["k"], 2)
        self.assertAlmostEqual(m["per_seed"]["GBR:C:COEXIST_MONEY"], 1.0)

    def test_ledger_invariants_and_pool_exh(self):
        g = self.p0["ledger"]
        self.assertEqual(g["violations"], 1)
        self.assertFalse(g["ok"])
        self.assertAlmostEqual(g["series"]["main"]["USA"], 0.5)

    def test_f17b_coexistences(self):
        f = self.p0["f17b"]
        co = X.coexistences(self.D.lines["0:r1"], N - 1)
        usa = [c for c in co if c["actor"] == "USA"]
        gbr = [c for c in co if c["actor"] == "GBR"]
        self.assertEqual(usa[0], {"actor": "USA", "start": 10, "months": 10, "end": "YPROD"})
        self.assertEqual(usa[1]["start"], 25)
        self.assertEqual(gbr, [{"actor": "GBR", "start": 370, "months": N - 370, "end": "HORIZON"}])
        self.assertEqual(f["left_share_violations"], 1)
        s = f["series"]["main"]
        self.assertEqual(s["end"], {"HORIZON": 1, "NONE": 1, "YPROD": 1})   # USA pair at 25 ends with the rows: NONE
        self.assertEqual(s["launches_gp_le_W"], 1)
        self.assertEqual(s["w_after_n_actor_months"], 1)

    def test_licences(self):
        li = self.p0["licences"]
        self.assertEqual(li["mismatch_runs"], ["0:r2"])
        m = li["series"]["main"]
        self.assertEqual(m["licences"], 3)
        self.assertEqual(m["term"]["open"], 2)
        self.assertEqual(m["resumes"], 1)
        self.assertEqual(m["refuse_money"], {"SWE": 1})

    def test_fleet(self):
        fl = self.p0["fleet"]
        self.assertEqual(fl["r_gen_violations"], 1)
        m = fl["series"]["main"]
        self.assertEqual(m["commands"], 3)
        self.assertAlmostEqual(m["egen_gen_line_ge3"], 1 / 3)
        self.assertAlmostEqual(m["alloy_share"], 1 / 3)
        self.assertEqual(m["laggard_sales"]["n"], 1)

    def test_series_identity(self):
        si = X.series_identity(self.D, CFG)
        self.assertEqual(si, {"same_as_main": [], "differ": 1})     # sale/X equals main on seed 1 only

    def test_render_has_all_sections(self):
        res = X.analyze(self.D, CFG)
        md = X.render(res, CFG, {})
        for title in ("## A40", "## A39", "## F17b", "## Лицензии", "## Парк", "НАРУШЕНИЕ (P0)"):
            self.assertIn(title, md)


class C1Stand(unittest.TestCase):
    def test_c1_statistics(self):
        with tempfile.TemporaryDirectory() as d:
            rows = []
            # DEV/FULL: 3 doublings, dlog_cost 0.2 each -> drop 1 - e^-0.2 = 0.181; yield 0.12, asm 0.08 -> 0.6
            for k in range(3):
                rows.append(["DEV", "FULL", k, 1, 2, 0.5, 1, 0.2, 0.12, 0.08, 0.1, 0.2])
            # a doubling below the serial admission (in_stat = 0) does not count
            rows.append(["DEV", "FULL", 3, 1, 2, 0.1, 0, 0.9, 0.9, 0.0, 0.1, 0.2])
            # MATURE/FULL: 2 doublings, cost 0.03, yield 0.003, asm 0.027 -> 0.1; Wright 0.2 and 0.45: window mean
            # 1 - sqrt(0.8 * 0.55) = 0.337 > 0.30 (P0 fails on the mean)
            rows.append(["MATURE", "FULL", 0, 200, 400, 0.9, 1, 0.03, 0.003, 0.027, 0.15, 0.2])
            rows.append(["MATURE", "FULL", 1, 400, 800, 0.9, 1, 0.03, 0.003, 0.027, 0.15, 0.45])
            rows.append(["MATURE", "FULL", 2, 800, 1600, 0.9, 0, 0.5, 0.0, 0.5, 0.15, 0.9])
            rows.append(["DEV", "NO_YIELD", 0, 1, 2, 0.5, 1, 0.05, "", "", 0.1, 0.2])
            p = os.path.join(d, "c1_stand.csv")
            write(d, "c1_stand.csv", X.C1_COLS, rows)
            r = X.c1_stand(p, CFG)
        self.assertAlmostEqual(r["C1a"]["value"], 1 - math.exp(-0.2))
        self.assertTrue(r["C1a"]["ok"])
        self.assertAlmostEqual(r["C1b"]["value"], 0.6)
        self.assertTrue(r["C1b"]["ok"])
        self.assertAlmostEqual(r["C1d"]["value"], 0.1)
        self.assertAlmostEqual(r["C1e"]["value"], 1 - math.exp(-0.03))
        self.assertEqual(r["C1e"]["d"], 0.0)
        self.assertAlmostEqual(r["wright_P0"]["window_mean"], 1 - math.sqrt(0.8 * 0.55))
        self.assertFalse(r["wright_P0"]["ok"])
        self.assertEqual(r["loss_C1"], 0.0)

    def test_c1_wright_on_window_mean_and_dev_below_admission_is_d1(self):
        # Wright P0 (#187): the first doubling 12.2% is below 15% by construction; the corridor holds for the mean only.
        # DEV with min yield < 0.25 at every doubling: the set is empty, d_C1a = d_C1b = 1 (spec §8.2 п.1).
        with tempfile.TemporaryDirectory() as d:
            rows = [["DEV", "FULL", k, 25, 50, 0.0, 0, 0.05, 0.04, 0.01, 0.1, 0.2] for k in range(3)]
            rows += [["MATURE", "FULL", k, 1, 2, 0.9, 1, 0.04, 0.0, 0.04, 0.1, x] for k, x in enumerate([0.122, 0.17, 0.2])]
            write(d, "c1_stand.csv", X.C1_COLS, rows)
            r = X.c1_stand(os.path.join(d, "c1_stand.csv"), CFG)
        self.assertTrue(r["wright_P0"]["ok"])
        self.assertEqual(r["DEV"]["n"], 0)
        self.assertEqual(r["C1a"]["d"], 1.0)
        self.assertEqual(r["C1b"]["d"], 1.0)
        self.assertGreaterEqual(r["loss_C1"], 2.0)

    def test_c1_empty_setup_is_d1(self):
        with tempfile.TemporaryDirectory() as d:
            write(d, "c1_stand.csv", X.C1_COLS, [["DEV", "FULL", 0, 1, 2, 0.5, 1, 0.2, 0.12, 0.08, 0.1, 0.2]])
            r = X.c1_stand(os.path.join(d, "c1_stand.csv"), CFG)
        self.assertIsNone(r["C1e"]["value"])
        self.assertEqual(r["C1e"]["d"], 1.0)
        self.assertFalse(r["C1d"]["ok"])


class CalibStartKnowledge(unittest.TestCase):
    """Spec §6.1 v0.24 r2: start knowledge t_known = -384 in m, s, delta, Y_k (part B); T-SK; C2 censoring."""

    def test_start_knowledge_and_c2(self):
        with tempfile.TemporaryDirectory() as d:
            runs, nt, gt, pm, am = [], [], [], [], []
            for s in range(1, 5):
                rid = "c%d" % s
                runs.append([rid, "calib", 10000 + s, 1, "ok", "h"])
                for a in CFG["actors"]:
                    nt.append([rid, a, 58, -1, "", "", "", 0])           # APP G1 known at start
                    nt.append([rid, a, 17, 10 * s, "", "", "", 0])       # ENG G1 known at 10*s
                    nt.append([rid, a, 49, -1, 0, -1, -1, 0])            # tier-4 PRC known and mastered at start: out of C2
                    nt.append([rid, a, 40, 5, 12, "" if s <= 2 else 60, "", 0])   # tier-4: 2 censored, 2 of 4 years
                    nt.append([rid, a, 41, 5, 12, "", "", 0])            # tier-5: all censored
                    for t in range(3):
                        pm.append([rid, t, a, 49, 1, 1, 800000 + t, FX, FX])
                        pm.append([rid, t + 5, a, 40, 1, 1, 1000 * t, FX, FX])
                    gt.append([rid, a, 1, -1, -1, -1, ""])
                    am.append([rid, 0, a, FX, FX, 1, 0, 0, 0, 1, 0, 0, 0])
            write(d, "runs.csv", X.RUNS_COLS, runs)
            write(d, "calib_summary.csv", X.CALIB_SUMMARY_COLS, [[r[0], 1, 1, 1, 1, 1] for r in runs])
            write(d, "node_times.csv", X.NODE_TIMES_COLS, nt)
            write(d, "gen_times.csv", X.GEN_TIMES_COLS, gt)
            write(d, "prc_month.csv", X.PRC_MONTH_COLS, pm)
            write(d, "actor_month.csv", X.ACTOR_MONTH_COLS, am)
            r = X.calib_tables(d, CFG)
        lk = r["control_tables"]["lk"]["USA:1"]
        self.assertEqual((lk["m"], lk["s"], lk["half"], lk["p_cens_fx"]), (-N, 0.0, 0, 0))
        self.assertEqual(r["control_tables"]["delta"]["USA:17"]["delta"], 384 + 20)    # median_low of 10*s + 384
        y = r["control_tables"]["Y"]["4"]
        self.assertEqual(len(y), 2 * N)
        self.assertEqual(y[N], 800000)            # part B, tau = 384 = month 0 of the start PRC 49
        self.assertEqual(y[N + 2], 800002)
        self.assertEqual(y[0], 0)                  # part A: PRC 40 known at 5, yield 0 at tau 0
        self.assertEqual(y[2], 2000)
        self.assertEqual(r["T_SK"]["failed_tiers"], [])
        self.assertEqual(r["T_SK"]["tiers_without_obs"], ["3", "5"])    # no prc_month rows for tiers 3 and 5
        self.assertFalse(r["T_SK"]["ok"])
        self.assertEqual(r["C4"]["2"]["d"], 1.0)                        # share 0 in [0.5; 1]: d = 0.5 / 0.5
        c2 = r["C2"]
        self.assertEqual(c2["tier4"]["n"], 20)     # node 49 mastered at the start is excluded
        self.assertEqual(c2["tier4"]["right_censored"], 10)
        self.assertEqual(c2["tier4"]["p50_years"], 4.0)    # lower median of 10 x 4.0 and 10 x inf
        self.assertFalse(c2["tier4"]["d_is_1"])
        self.assertFalse(c2["tier5"]["d_is_1"])    # fully censored, but every pair has t_first: d = 1 is not by censoring
        self.assertEqual(c2["tier5"]["no_t_first_share"], 0.0)
        self.assertFalse(c2["tier5_longer"])       # fully censored tier 5: not met


class CalibSegmentB232(unittest.TestCase):
    """#232 (spec §6.1 v0.27): part B of Y_k only over pairs with v > 0; T-SK needs Y_k >= 0.25 on part B."""

    def run_calib(self, idle_yield, used_yield):
        with tempfile.TemporaryDirectory() as d:
            runs = [["c%d" % s, "calib", 10000 + s, 1, "ok", "h"] for s in range(1, 3)]
            nt, pm = [], []
            for r in runs:
                for a in CFG["actors"]:
                    nt.append([r[0], a, 49, -1, -1, -1, -1, 0])               # tier-4 PRC known at the start
                    used = a in ("USA", "GBR")
                    for t in range(2):
                        # three idle actors (v = 0) with a decaying yield, two working ones (v > 0)
                        pm.append([r[0], t, a, 49, 1, 1, used_yield if used else idle_yield, FX, FX if used else 0])
            write(d, "runs.csv", X.RUNS_COLS, runs)
            write(d, "calib_summary.csv", X.CALIB_SUMMARY_COLS, [[r[0], 1, 1, 1, 1, 1] for r in runs])
            write(d, "node_times.csv", X.NODE_TIMES_COLS, nt)
            write(d, "gen_times.csv", X.GEN_TIMES_COLS, [])
            write(d, "prc_month.csv", X.PRC_MONTH_COLS, pm)
            write(d, "actor_month.csv", X.ACTOR_MONTH_COLS, [])
            return X.calib_tables(d, CFG)

    def test_idle_pairs_do_not_enter_part_b(self):
        r = self.run_calib(idle_yield=3000, used_yield=950000)
        y = r["control_tables"]["Y"]["4"]
        self.assertEqual(y[N], 950000)          # without the filter: the lower median of 6 idle and 4 used = 3000
        self.assertEqual(y[N + 1], 950000)
        self.assertNotIn("4", r["T_SK"]["failed_tiers"])

    def test_tsk_fails_below_serial_admission(self):
        r = self.run_calib(idle_yield=0, used_yield=200000)    # working pairs at 0.2 < 0.25: positive, still a failure
        self.assertEqual(r["control_tables"]["Y"]["4"][N], 200000)
        self.assertIn("4", r["T_SK"]["failed_tiers"])
        self.assertEqual(r["T_SK"]["tiers"]["4"]["B_min"], 200000)


class CalibPoint(unittest.TestCase):
    """--calib --point P (TASK-0055 Codex r2): only the runs of matrix point P, other points' rows are skipped, not errors."""

    def test_point_filter(self):
        with tempfile.TemporaryDirectory() as d:
            runs, nt, a40 = [], [], []
            for pt, yv in ((0, 100000), (1, 900000)):
                for s in range(1, 3):
                    rid = "p%d-s%d-t1" % (pt, s)
                    runs.append([rid, "calib", 10000 + s, 1, "ok", "h", pt])
                    nt.append([rid, "USA", 40, 5, "", "", "", 0])
                    a40.append([rid, 300, "USA", 1, 4, 1, "SUPERIOR", "O", "PROCESSES", 240, "", 0, 0, 0])
            write(d, "runs.csv", X.RUNS_COLS + ["point"], runs)
            write(d, "calib_summary.csv", X.CALIB_SUMMARY_COLS, [[r[0], 1, 1, 1, 1, 1] for r in runs])
            write(d, "node_times.csv", X.NODE_TIMES_COLS, nt)
            write(d, "gen_times.csv", X.GEN_TIMES_COLS, [])
            write(d, "prc_month.csv", X.PRC_MONTH_COLS,
                  [[r[0], 6, "USA", 40, 1, 1, 100000 if r[6] == 0 else 900000, X.FX, 0] for r in runs])
            write(d, "actor_month.csv", X.ACTOR_MONTH_COLS, [])
            write(d, "a40.csv", X.A40_COLS, a40)
            r0 = X.calib_tables(d, CFG, point=0)
            r1 = X.calib_tables(d, CFG, point=1)
            with self.assertRaises(X.InputError):
                X.calib_tables(d, CFG, point=7)
        self.assertEqual((r0["seeds"], r1["seeds"]), (2, 2))
        self.assertEqual(r0["control_tables"]["Y"]["4"][1], 100000)
        self.assertEqual(r1["control_tables"]["Y"]["4"][1], 900000)


class CalibCodexR1(unittest.TestCase):
    """Codex round 1 (TASK-0053): tau = 0 kept whatever `known` says; C2 keeps pairs in use but not mastered at the
    start; d = 1 by the share of pairs WITHOUT t_first."""

    def run_calib(self, nt_rows, pm_rows):
        with tempfile.TemporaryDirectory() as d:
            runs = [["c%d" % s, "calib", 10000 + s, 1, "ok", "h"] for s in range(1, 5)]
            write(d, "runs.csv", X.RUNS_COLS, runs)
            write(d, "calib_summary.csv", X.CALIB_SUMMARY_COLS, [[r[0], 1, 1, 1, 1, 1] for r in runs])
            write(d, "node_times.csv", X.NODE_TIMES_COLS, nt_rows)
            write(d, "gen_times.csv", X.GEN_TIMES_COLS, [])
            write(d, "prc_month.csv", X.PRC_MONTH_COLS, pm_rows)
            write(d, "actor_month.csv", X.ACTOR_MONTH_COLS, [])
            return X.calib_tables(d, CFG)

    def test_tau0_row_with_known_0_is_kept(self):
        nt = [["c%d" % s, "USA", 40, 10, "", "", "", 0] for s in range(1, 5)]
        pm = [["c%d" % s, 10, "USA", 40, 0, 0, 123000, 5 * FX, 0] for s in range(1, 5)]   # month of discovery, known = 0
        pm += [["c%d" % s, 11, "USA", 40, 1, 1, 456000, 2 * FX, 0] for s in range(1, 5)]
        r = self.run_calib(nt, pm)
        self.assertEqual(r["control_tables"]["Y"]["4"][0], 123000)
        self.assertEqual(r["control_tables"]["U"]["4"][0], 5 * FX)
        self.assertEqual(r["control_tables"]["Y"]["4"][1], 456000)

    def test_c2_counts_pairs_in_use_but_not_mastered_at_start(self):
        # t_first = -1 (in use at the start), t_mastered empty: 47 - (-1) = 48 months = 4 years; mastered ones excluded
        nt = [["c%d" % s, "USA", 40, -1, -1, 47, "", 0] for s in range(1, 5)]
        nt += [["c%d" % s, "GBR", 40, -1, -1, -1, -1, 0] for s in range(1, 5)]
        r = self.run_calib(nt, [])
        t4 = r["C2"]["tier4"]
        self.assertEqual((t4["pool"], t4["n"], t4["p50_years"]), (4, 4, 4.0))
        self.assertTrue(r["C2"]["tier4_in_corridor"])

    def test_t_design_mixes_start_and_new_projects(self):
        # USA: start project of G2 not yet in service (t_req -1, first unit 30) -> 30 from month 0;
        # GBR: new project, Requirement 12 -> first unit 52 -> 40; SUN: start G2 already in service (-1) -> out
        with tempfile.TemporaryDirectory() as d:
            runs = [["c%d" % s, "calib", 10000 + s, 1, "ok", "h"] for s in range(1, 3)]
            write(d, "runs.csv", X.RUNS_COLS, runs)
            write(d, "calib_summary.csv", X.CALIB_SUMMARY_COLS, [[r[0], 1, 1, 1, 1, 1] for r in runs])
            write(d, "node_times.csv", X.NODE_TIMES_COLS, [])
            gt = []
            for r in runs:
                gt += [[r[0], "USA", 2, -1, 30, "", ""], [r[0], "GBR", 2, 12, 52, "", ""], [r[0], "SUN", 2, -1, -1, -1, ""]]
            write(d, "gen_times.csv", X.GEN_TIMES_COLS, gt)
            write(d, "prc_month.csv", X.PRC_MONTH_COLS, [])
            write(d, "actor_month.csv", X.ACTOR_MONTH_COLS, [])
            r = X.calib_tables(d, CFG)
        self.assertEqual(r["control_tables"]["T_design"]["2"], 30)     # median_low of 30, 30, 40, 40
        self.assertEqual(r["T_design_obs"]["2"], {"n": 4, "start_from_month0": 2, "start_in_service": 2})
        self.assertIsNone(r["control_tables"]["T_design"]["3"])

    def test_c2_d1_by_share_without_t_first(self):
        # 3 of 4 pairs without t_first, the 4th observed: d = 1 although nothing is censored
        nt = [["c%d" % s, "USA", 40, 10, "" if s <= 3 else 12, "" if s <= 3 else 60, "", 0] for s in range(1, 5)]
        r = self.run_calib(nt, [])
        t4 = r["C2"]["tier4"]
        self.assertEqual((t4["no_t_first"], t4["right_censored"]), (3, 0))
        self.assertTrue(t4["d_is_1"])
        self.assertEqual(t4["p50_years"], 4.0)                     # the one observed pair sits inside [2; 8] ...
        self.assertFalse(r["C2"]["tier4_in_corridor"])            # ... but d = 1 rules: not in the corridor
        self.assertEqual(r["C2"]["d_tier4"], 1.0)
        self.assertEqual(r["C2"]["d_order"], 1.0)
        self.assertTrue(r["T_SK"]["ok"] is False and "4" in r["T_SK"]["tiers_without_obs"])   # no prc_month rows at all
        # 4 of 4 censored with t_first: no d = 1 from this rule
        nt = [["c%d" % s, "USA", 40, 10, 12, "", "", 0] for s in range(1, 5)]
        t4 = self.run_calib(nt, [])["C2"]["tier4"]
        self.assertEqual((t4["no_t_first_share"], t4["censored_share"]), (0.0, 1.0))
        self.assertFalse(t4["d_is_1"])


class P5Labels(unittest.TestCase):
    def test_kdiff_axis_labels(self):
        kd = CFG["p5_kdiff"]
        self.assertEqual((kd["lo"], kd["mid"], kd["hi"]), (0.75, 0.5, 0.0))
        self.assertEqual([1 - kd[x] for x in ("lo", "mid", "hi")], [0.25, 0.5, 1.0])


class ControlTablesFile(unittest.TestCase):
    """Format §3.1 (TASK-0054): the CONTROL_TABLES file is canonical integer JSON; APP delta 0, never -> null."""

    def tables(self):
        nm = CFG["n_months"]
        t = {"lk": {}, "delta": {}, "Y": {}, "U": {}, "T_design": {}, "rho": {}}
        for a in CFG["actors"]:
            for g in range(1, 5):
                t["lk"]["%s:%d" % (a, g)] = ({"p_cens_fx": X.FX, "m_int": None, "half": None} if g == 4 else
                                             {"p_cens_fx": 10000, "m_int": 50 * g, "half": 7})
            for nd, (typ, ng, _) in X.NODES.items():
                if 1 <= ng <= 4:
                    t["delta"]["%s:%d" % (a, nd)] = {"never": nd % 2 == 0, "delta": 3, "n": 5}
        for k in ("3", "4", "5"):
            t["Y"][k], t["U"][k] = [500000] * 2 * nm, [1000000] * 2 * nm
        for g in ("1", "2", "3", "4"):
            t["T_design"][g], t["rho"][g] = 24, 1000
        return t

    def test_file_is_canonical_and_integer(self):
        text = X.control_tables_file(self.tables(), CFG)
        doc = json.loads(text)
        self.assertEqual(text, X.canonical_json(doc))
        self.assertFalse(text.endswith("\n"))
        self.assertEqual(doc["format"], "control_tables/1")
        self.assertEqual(doc["actors"], CFG["actors"])
        self.assertEqual(doc["lk"]["USA"]["4"], {"p_cens": X.FX, "mean": 0, "half": 0})
        self.assertEqual(doc["lk"]["GBR"]["2"], {"p_cens": 10000, "mean": 100, "half": 7})
        for g, app in CFG["calib"]["app_nodes"].items():
            self.assertEqual(doc["delta"]["SUN"][str(app)], 0)
        odd = [nd for nd, (_, ng, _) in X.NODES.items() if 1 <= ng <= 4 and nd % 2 and nd not in (58, 59, 60, 61)][0]
        even = [nd for nd, (_, ng, _) in X.NODES.items() if 1 <= ng <= 4 and nd % 2 == 0 and nd not in (58, 59, 60, 61)][0]
        self.assertEqual(doc["delta"]["FRA"][str(odd)], 3)
        self.assertIsNone(doc["delta"]["FRA"][str(even)])
        self.assertEqual(len(doc["lc"]["Y"]["5"]), 2 * CFG["n_months"])

    def test_incomplete_tables_raise(self):
        t = self.tables()
        del t["Y"]["5"]
        with self.assertRaises(X.InputError):
            X.control_tables_file(t, CFG)


if __name__ == "__main__":
    unittest.main()
