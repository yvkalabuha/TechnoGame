"""Tests for exp0008.py (TASK-0106) on synthetic series directories with known answers.

Run: python3 -m unittest discover -s lab/analysis -p 'test_exp0008.py' -v
"""
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import exp0008 as X  # noqa: E402

GATE = {"outcome_block": "A", "noise_max": 0.10}
COND = {"configuration": "Release", "gc_server": False, "power": "ac", "runtime_version": "10.0.0"}


def process_doc(t_ms, m_ms, months=10, warmup=2, gc_pause=0.0, gen2=0, v2=True, commit="abc", foreign=0.01):
    """A process JSON whose daily months are all t_ms and monthly months all m_ms (after warm-up)."""
    def run(mode, v):
        all_ = [v] * (warmup + months)
        return {"mode": mode, "month_ms": {"p50": v}, "month_ms_all": all_, "window_months": months, "window_wall_ms": 1000.0,
                "gc_pause_ms": gc_pause, "gc_gen2": gen2, "allocated_bytes": 1000, "allocated_bytes_net": 900}
    return {"params": {"warmup_months": warmup}, "runs": [run("monthly", m_ms), run("daily", t_ms)], "v2_monthly_daily_hash_equal": v2,
            "conditions": COND, "provenance": {"build_commit": commit, "dirty": False},
            "frequency": {"possible_thermal_throttle": False},
            "machine": {"foreign_cpu_window": foreign, "threads_k": 8, "self_cpu_s_window": 8.0, "wall_s_window": 1.0, "run_delay_share_window": 0.0}}


class Series:
    def __init__(self, d):
        self.d = d
        self.procs = []
        os.makedirs(os.path.join(d, "attempts"), exist_ok=True)

    def add(self, block, docs, status="Accepted", accepted=0, by="clean", seed=1):
        idx = len(self.procs)
        atts = []
        for n, doc in enumerate(docs):
            f = f"attempts/p{idx:03d}-a{n}.json"
            with open(os.path.join(self.d, f), "w", encoding="utf-8") as fh:
                json.dump(doc, fh)
            atts.append({"attempt": n, "file": f})
        self.procs.append({"index": idx, "block": block, "group": f"{block}|pt|t8|bg0", "point": "pt", "seed": seed, "replica": 1,
                           "status": status, "accepted_attempt": accepted if status == "Accepted" else None,
                           "accepted_by": by if status == "Accepted" else None, "attempts": atts})

    def write(self, inconclusive=False, reference=None, validity_exit=0):
        s = {"experiment": "T", "manifest_hash": "0" * 64, "exit_code": 4 if inconclusive else 0, "smoke": False, "gate": GATE,
             "main": {"processes": self.procs}, "exclusion_verdict": {"inconclusive": inconclusive},
             "validity": {"exit": validity_exit}, "reference": reference or {"modes": []}}
        with open(os.path.join(self.d, "series.json"), "w", encoding="utf-8") as fh:
            json.dump(s, fh)
        return X.analyse(self.d)


class Stats(unittest.TestCase):
    def test_percentile_type7(self):
        x = list(range(1, 11))
        self.assertAlmostEqual(X.percentile(x, 0.5), 5.5)
        self.assertAlmostEqual(X.percentile(x, 0.95), 9.55)
        self.assertAlmostEqual(X.percentile(x, 0.1), 1.9)

    def test_noise_and_mad(self):
        meds = [100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114]
        self.assertAlmostEqual(X.noise(meds), (X.percentile(meds, 0.9) - X.percentile(meds, 0.1)) / 107)
        self.assertAlmostEqual(X.mad_noise(meds), 4 / 107)

    def test_median_ci_n100_known_ranks(self):
        ci = X.median_ci(list(range(1, 101)))
        self.assertEqual((ci["rank_lower"], ci["rank_upper"]), (40, 61))
        self.assertAlmostEqual(ci["coverage"], 0.9648, places=4)

    def test_median_ci_n360_symmetric_and_covers(self):
        ci = X.median_ci([i / 360 for i in range(360)])
        self.assertEqual(ci["rank_lower"] + ci["rank_upper"], 361)
        self.assertGreaterEqual(ci["coverage"], 0.95)
        self.assertLess(ci["coverage"], 0.965)

    def test_median_ci_too_small(self):
        self.assertIsNone(X.median_ci([1, 2, 3, 4, 5]))


class Outcome(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.s = Series(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def fill_a(self, t_values, m=290):
        for t in t_values:
            self.s.add("A", [process_doc(t, m)])

    def test_pass(self):
        self.fill_a([110] * 15, m=90)
        r = self.s.write()
        self.assertEqual(r["outcome"], "PASS")
        a = r["points"][r["outcome_point"]]
        # k = 20/29, S = 90 - k, K_d = 600/29.
        self.assertAlmostEqual(a["K_d_p95_ms"], 600 / 29)
        self.assertAlmostEqual(a["S_p95_ms"], 90 - 20 / 29)
        self.assertEqual(a["noise"], 0)

    def test_noise_inconclusive_and_frequency_is_no_condition(self):
        self.fill_a([250 + 10 * i for i in range(15)])
        r = self.s.write()
        self.assertTrue(r["noise_inconclusive"])
        self.assertEqual(r["outcome"], "INCONCLUSIVE")

    def test_exclusion_verdict_inconclusive(self):
        self.fill_a([300] * 15)
        self.assertEqual(self.s.write(inconclusive=True)["outcome"], "INCONCLUSIVE")

    def test_fail_and_warn(self):
        self.fill_a([450] * 15, m=440)
        self.assertEqual(self.s.write()["outcome"], "FAIL")
        t = Series(os.path.join(self.tmp.name, "w"))
        for _ in range(15):
            t.add("A", [process_doc(300, 150)])   # K_d = 30 * 150/29 > 50
        self.assertEqual(t.write()["outcome"], "WARN")

    def test_invalid_builds_and_v2(self):
        self.fill_a([300] * 14)
        self.s.add("A", [process_doc(300, 290, commit="other")])
        r = self.s.write()
        self.assertEqual(r["outcome"], "INVALID")
        self.assertTrue(any("different builds" in x for x in r["invalid_reasons"]))

    def test_r1_r2_differ_gives_inconclusive(self):
        self.fill_a([110] * 13, m=90)
        self.s.add("A", [process_doc(800, 790)] * 3, status="Excluded")
        self.s.add("A", [process_doc(800, 790)] * 3, status="Excluded")
        r = self.s.write()
        self.assertEqual((r["r1_row"], r["r2_row"]), ("PASS", "FAIL"))
        self.assertTrue(r["r1_r2_differ"])
        self.assertEqual(r["outcome"], "INCONCLUSIVE")
        self.assertEqual(r["blocks"]["A"]["excluded"], 2)
        self.assertEqual(r["blocks"]["A"]["repeats"], 4)

    def test_r2_uses_first_attempts_of_accepted_too(self):
        # Every process accepted on its repeat; its slow first attempt is in Р2 only.
        for _ in range(15):
            self.s.add("A", [process_doc(900, 890, foreign=0.3), process_doc(110, 90)], accepted=1)
        r = self.s.write()
        self.assertEqual(r["r1_row"], "PASS")
        self.assertEqual(r["r2_row"], "FAIL")
        self.assertEqual(r["outcome"], "INCONCLUSIVE")

    def test_throttle_mark_on_pass(self):
        for i in range(15):
            self.s.add("A", [process_doc(110, 90)])
        for p in self.s.procs[:4]:
            p["attempts"][0]["thermal"] = {"core_throttle_delta": 3, "package_throttle_delta": 0}
        r = self.s.write()
        self.assertEqual(r["outcome"], "PASS")
        self.assertEqual(len(r["outcome_marks"]), 1)   # 4/15 = 27 % > 20 %

    def test_calibration_rounds_p90_up(self):
        for i in range(10):
            self.s.add("A", [process_doc(110, 90)])
            self.s.procs[-1]["attempts"][0].update({"foreign_cpus": 0.1 * (i + 1), "foreign_over_share": 0.01 * i, "run_delay_share": 0.001 * i})
        self.s.write()
        c = X.calibrate(self.tmp.name)
        self.assertAlmostEqual(c["foreign_cpus_max"]["q"], 0.91)
        self.assertAlmostEqual(c["foreign_cpus_max"]["threshold"], 1.0)
        self.assertAlmostEqual(c["run_delay_max"]["threshold"], 0.01)
        self.assertEqual(c["windows"], 10)

    def test_accepted_repeat_is_used(self):
        for _ in range(15):
            self.s.add("A", [process_doc(900, 890, foreign=0.3), process_doc(300, 290)], accepted=1)
        r = self.s.write()
        self.assertAlmostEqual(r["points"][r["outcome_point"]]["median_T_ms"], 300)
        self.assertAlmostEqual(r["points_r2"][r["outcome_point"]]["median_T_ms"], 900)

    def test_gc_pause_share(self):
        for _ in range(15):
            self.s.add("A", [process_doc(300, 290, gc_pause=30.0)])   # 60 ms over 2000 ms
        self.s.add("A", [process_doc(300, 290, gc_pause=0.0)] * 3, status="Excluded")
        a = self.s.write()["points"]["A|pt|t8|bg0"]
        self.assertAlmostEqual(a["gc_pause_share_pooled"], 0.03)
        # Median over all 16 processes, the excluded one with its first attempt.
        self.assertAlmostEqual(a["gc_pause_share_median_all"], 0.03)
        self.assertFalse(a["gc_pause_median_le_2pct"])

    def r2d_mode(self, mid, ratios_t1, ratios_t8, hashes=True, status="Accepted"):
        f = f"attempts/{mid}.json"
        doc = {"r2": {"d_month_cow_vs_full": {"by_threads": [
            {"threads": 1, "ratio_cow_over_full_all": ratios_t1, "hashes_equal": hashes},
            {"threads": 8, "ratio_cow_over_full_all": ratios_t8, "hashes_equal": hashes}]}}}
        with open(os.path.join(self.tmp.name, f), "w", encoding="utf-8") as fh:
            json.dump(doc, fh)
        return {"id": mid, "gate": {"processes": [{"status": status, "accepted_attempt": 0 if status == "Accepted" else None,
                                                   "attempts": [{"file": f}]}]}}

    def test_r2d_closes_debt_only_with_both_threads_below_2pct(self):
        self.fill_a([300] * 15)
        ok = [1.0 + (i % 20 - 10) / 1000 for i in range(120)]       # g ~ 0, CI within ±1 %
        bad = [1.02 + (i % 20 - 10) / 1000 for i in range(120)]     # g ~ +2 %, upper bound > 2 %
        ref = {"modes": [self.r2d_mode(f"r2d-s{s}", ok, ok) for s in (1, 2, 3)]}
        r = self.s.write(reference=ref)["r2d"]
        self.assertEqual(r["by_threads"]["1"]["pairs"], 360)
        self.assertTrue(r["debt_0045_closes"])
        ref = {"modes": [self.r2d_mode(f"r2d-s{s}", ok, bad) for s in (1, 2, 3)]}
        self.assertFalse(self.s.write(reference=ref)["r2d"]["debt_0045_closes"])
        ref = {"modes": [self.r2d_mode(f"r2d-s{s}", ok, ok, hashes=(s != 2)) for s in (1, 2, 3)]}
        self.assertFalse(self.s.write(reference=ref)["r2d"]["debt_0045_closes"])
        ref = {"modes": [self.r2d_mode("r2d-s1", ok, ok), self.r2d_mode("r2d-s2", ok, ok, status="Excluded")]}
        self.assertFalse(self.s.write(reference=ref)["r2d"]["debt_0045_closes"])


if __name__ == "__main__":
    unittest.main()
