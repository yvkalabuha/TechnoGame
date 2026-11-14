"""Tests for exp0002.py (TASK-0053, T11) on synthetic CSV with known answers.

Run: python3 -m unittest discover -s lab/analysis -p 'test_exp0002.py' -v
The decision-rule scenarios (P1-P5, S1-S3, diagnostics, control tables) are the script's own selftest; each of its
checks is one test here. The v0.5 r7 prints (A40, A39, F17b, licences, fleet, C1, T-SK, C2) have their own cases.
"""
import copy
import csv
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
                rows.append(["DEV", "FULL", k, 1, 2, 0.2, 0.12, 0.08, 0.1, 0.2])
            # MATURE/FULL: 2 doublings, cost 0.03, yield 0.003, asm 0.027 -> 0.1; Wright 0.2 and 0.35 (P0 fails)
            rows.append(["MATURE", "FULL", 0, 400, 800, 0.03, 0.003, 0.027, 0.15, 0.2])
            rows.append(["MATURE", "FULL", 1, 800, 1600, 0.03, 0.003, 0.027, 0.15, 0.35])
            rows.append(["DEV", "NO_YIELD", 0, 1, 2, 0.05, 0.0, 0.05, 0.1, 0.2])
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
        self.assertFalse(r["wright_P0"]["ok"])

    def test_c1_empty_setup_is_d1(self):
        with tempfile.TemporaryDirectory() as d:
            write(d, "c1_stand.csv", X.C1_COLS, [["DEV", "FULL", 0, 1, 2, 0.2, 0.12, 0.08, 0.1, 0.2]])
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
                    nt.append([rid, a, 49, -1, 0, -1, "", 0])            # tier-4 PRC known and mastered at start (USA/GBR...)
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
        self.assertTrue(r["T_SK"]["ok"])
        c2 = r["C2"]
        self.assertEqual(c2["tier4"]["n"], 20)     # node 49 mastered at the start is excluded
        self.assertEqual(c2["tier4"]["right_censored"], 10)
        self.assertEqual(c2["tier4"]["p50_years"], 4.0)    # lower median of 10 x 4.0 and 10 x inf
        self.assertFalse(c2["tier4"]["d_is_1"])
        self.assertTrue(c2["tier5"]["d_is_1"])
        self.assertFalse(c2["tier5_longer"])       # fully censored tier 5: not met


class P5Labels(unittest.TestCase):
    def test_kdiff_axis_labels(self):
        kd = CFG["p5_kdiff"]
        self.assertEqual((kd["lo"], kd["mid"], kd["hi"]), (0.75, 0.5, 0.0))
        self.assertEqual([1 - kd[x] for x in ("lo", "mid", "hi")], [0.25, 0.5, 1.0])


if __name__ == "__main__":
    unittest.main()
