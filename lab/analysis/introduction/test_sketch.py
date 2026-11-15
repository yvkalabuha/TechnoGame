"""Unit tests of the HYP-0004 sketch analysis (pool, E1-E3, H1, H2 on a hand-made matrix directory)."""
import csv
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sketch as S  # noqa: E402


def write(d, name, header, rows):
    with open(os.path.join(d, name), "w", newline="") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(header)
        w.writerows(rows)


class Sketch(unittest.TestCase):
    def test_criteria(self):
        with tempfile.TemporaryDirectory() as d:
            runs, nt, pm = [], [], []
            for s in range(1, 5):
                rid = "r%d" % s
                runs.append([rid, "test/X", 0, 30000 + s, 1, "ok", "g%d" % s])
                nt.append([rid, "USA", 40, 10, 20, 60, "", 0])        # t_07 - t_first = 40 months
                nt.append([rid, "FRA", 40, 10, 20, "", "", 0])        # censored
                nt.append([rid, "GBR", 40, 340, "", "", "", 0])       # late: out of the pool
                nt.append([rid, "SWE", 49, -1, -1, -1, -1, 0])        # mastered at the start: out
                pm.append([rid, 68, "USA", 40, 1, 1, 800000, 0, 1])   # t_first + 48
                pm.append([rid, 68, "FRA", 40, 1, 1, 100000, 0, 1])
            write(d, "runs.csv", ["run", "series", "point", "seed", "threads", "status", "golden_hash"], runs)
            write(d, "node_times.csv", ["run", "actor", "node", "t_known", "t_first", "t_07", "t_mastered", "lic"], nt)
            write(d, "prc_month.csv", ["run", "t", "actor", "node", "known", "admitted", "yield", "ucm", "v"], pm)
            r = S.series_stats("test/X", ["r1", "r2", "r3", "r4"], *S.load(d)[1:3])
        self.assertEqual((r["pool"], r["late_known"], r["not_mastered_pairs"]), (8, 4, 12))
        self.assertEqual(r["E1_no_t_first_share"], 0.0)
        self.assertEqual(r["E2_censored_share"], 0.5)          # not < 0.5: not escaped
        self.assertEqual(r["E3_yield_p50_at_48"], 0.1)         # lower median of 4 x 0.1 and 4 x 0.8
        self.assertFalse(r["escaped"])
        self.assertEqual(r["H2_p10_t07_minus_t_first_months"], 40)
        self.assertIsNone(r["H1_t07_share_spread"])            # no actor with >= 20 pool pairs


if __name__ == "__main__":
    unittest.main()
