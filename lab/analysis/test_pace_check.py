import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pace_check as pc  # noqa: E402


class PaceCheckTest(unittest.TestCase):
    def setUp(self):
        self.cfg = pc.pace_config({})
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)

    def side(self, name, **kw):
        d, t = pc.synthetic(os.path.join(self.tmp.name, name), **kw)
        return pc.summarize(d, t, self.cfg)

    def test_summary_medians_censoring_and_windows(self):
        s = self.side("b")
        self.assertEqual(s["n"], 5)
        self.assertEqual(s["gens"][2]["app_usa"], 102)       # 100..104, lower median
        self.assertEqual(s["gens"][2]["app_any"], 97)        # GBR 5 months earlier, run 4 GBR never -> USA 104
        self.assertEqual(s["gens"][3]["in_window"], 4)       # run 4 never built
        self.assertEqual(s["gens"][4]["unit"], 309)
        self.assertEqual(s["gens"][4]["censored"], {"app_usa": 0, "app_any": 0, "unit": 1})
        self.assertEqual(pc.month_label(pc.INF, 61, 100), "inf (61/100 never)")

    def test_same_build_passes(self):
        s = self.side("b")
        self.assertEqual(pc.compare(s, s, self.cfg)["verdict"], "PASS")

    def test_median_shift_over_24_months_warns(self):
        b = self.side("b")
        self.assertEqual(pc.compare(b, self.side("c24", shift=24), self.cfg)["verdict"], "PASS")
        res = pc.compare(b, self.side("c25", shift=25), self.cfg)
        self.assertEqual(res["verdict"], "WARN")
        self.assertTrue(all(r["verdict"] == "WARN" for r in res["rows"]))
        res = pc.compare(b, self.side("cm", shift=-30), self.cfg)
        self.assertEqual(res["verdict"], "WARN")   # faster by > 24 months also warns

    def test_window_share_drop_warns(self):
        res = pc.compare(self.side("b"), self.side("u", unit_shift=100), self.cfg)
        self.assertEqual(res["verdict"], "WARN")
        self.assertTrue(any("in-window share" in x for r in res["rows"] for x in r["reasons"]))

    def _shares(self, b_in, c_in, n=100):
        b = self.side("b")
        c = self.side("c")
        b["n"] = c["n"] = n
        for g in (2, 3, 4):
            b["gens"][g]["in_window"] = b_in
            c["gens"][g]["in_window"] = c_in
        return pc.compare(b, c, self.cfg)

    def test_window_share_drop_threshold_5pp(self):   # O-085: warn only when the drop is > 5 percentage points
        self.assertEqual(self._shares(85, 80)["verdict"], "PASS")    # -5 pp
        self.assertEqual(self._shares(85, 79)["verdict"], "WARN")    # -6 pp
        self.assertEqual(self._shares(85, 99)["verdict"], "PASS")    # a rise never warns
        self.assertEqual(self.cfg["warn_window_share_drop_pp"], 5)

    def test_median_inf_when_more_than_half_censored(self):
        root = os.path.join(self.tmp.name, "inf")
        d, t = pc.synthetic(root)
        nt, gt = [], []
        for i in range(5):
            rid = "p0-s%d-t1" % i
            for g, node in ((2, 59), (3, 60), (4, 61)):
                nt.append([rid, "USA", node, "" if i < 3 else 100, "", "", "", 0])
                gt.append([rid, "USA", g, 0, "" if i < 3 else 300, "", ""])
        pc._write_dir(d, nt, gt, 5)
        s = pc.summarize(d, t, self.cfg)
        self.assertEqual(s["gens"][4]["app_usa"], pc.INF)
        self.assertEqual(s["gens"][4]["censored"]["unit"], 3)
        self.assertEqual(pc.month_label(s["gens"][4]["unit"], s["gens"][4]["censored"]["unit"], s["n"]), "inf (3/5 never)")

    def test_untracked_files_mark_dirty(self):
        self.assertTrue(pc.is_dirty("?? src/Singularity.Core/New.cs\n"))
        self.assertTrue(pc.is_dirty(" M mods/core/nodes.yaml\n"))
        self.assertFalse(pc.is_dirty(""))

    def test_censored_median_against_finite_warns(self):
        b = self.side("b")
        c = self.side("c")
        c["gens"][4]["unit"] = pc.INF
        res = pc.compare(b, c, self.cfg)
        self.assertEqual(res["rows"][2]["d_unit"], pc.INF)
        self.assertEqual(res["rows"][2]["verdict"], "WARN")
        self.assertEqual(pc._delta(pc.INF, pc.INF), 0)

    def test_failed_run_is_an_error(self):
        root = os.path.join(self.tmp.name, "f")
        d, t = pc.synthetic(root)
        pc._write_dir(d, [], [], 2, status="failed")
        with self.assertRaises(ValueError):
            pc.summarize(d, t, self.cfg)

    def test_month_labels(self):
        self.assertEqual(pc.month_label(0), "0 (1945-09)")
        self.assertEqual(pc.month_label(16), "16 (1947-01)")
        self.assertEqual(pc.month_label(363), "363 (1975-12)")

    def test_manifest_pace_block(self):
        man = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "manifests", "PACE-check.json")
        import json
        cfg = pc.pace_config(pc.read_json(man))
        self.assertEqual(cfg["windows"], {2: (16, 87), 3: (100, 195), 4: (256, 363)})
        self.assertEqual(cfg["warn_median_shift_months"], 24)
        self.assertEqual(cfg["warn_window_share_drop_pp"], 5)

    def test_selftest(self):
        pc.selftest()


if __name__ == "__main__":
    unittest.main()
