import io
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import era_price as ep  # noqa: E402
from test_slot_core import write_series  # noqa: E402

NODES = """nodes:
  - id: a
    number: 1
    anchor:
      year: 1950
  - id: b
    number: 2
    anchor:
      year: 1995
  - id: c
    number: 3
"""


class EraPriceTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.yaml = os.path.join(self.tmp.name, "nodes.yaml")
        with open(self.yaml, "w", encoding="utf-8") as f:
            f.write(NODES)

    def tearDown(self):
        self.tmp.cleanup()

    def test_anchors(self):
        self.assertEqual(ep.anchors(self.yaml), {1: 1950, 2: 1995, 3: 0})

    def test_returned_after_1990_and_late_gap(self):
        d = write_series(os.path.join(self.tmp.name, "s"), seeds=3, returned_after=0.9, late_gap=60)
        rows = ep.actor_rows(d, ep.anchors(self.yaml))
        usa = rows["USA"]
        self.assertAlmostEqual(usa["ret2"][0], 0.9)
        self.assertGreater(usa["gap2"], 36)   # 1991-01 .. the last workable month 600, no discovery in it
        self.assertLessEqual(usa["gap1"], 12)
        # nodes 1..N are known; of them only number 2 is anchored >= 1990
        self.assertEqual(usa["kn"][1], 1)
        self.assertEqual(usa["n"], 3)

    def test_year_returns(self):
        d = write_series(os.path.join(self.tmp.name, "y"), seeds=3)
        for s in range(1, 4):
            p = os.path.join(d, "runs", f"p0-s{s}-t1", "run.json")
            with open(p, encoding="utf-8") as f:
                j = json.load(f)
            j["model_info"]["slot_years_USA"] = f"1990:{500_000 + s * 100_000}:84:21;1995:0:84:84"
            with open(p, "w", encoding="utf-8") as f:
                json.dump(j, f)
        yr = ep.year_returns(d, "USA")
        self.assertAlmostEqual(yr[1990][0], 0.7)
        self.assertAlmostEqual(yr[1990][1], 0.25)
        self.assertEqual(yr[1995], (0.0, 1.0))
        self.assertIn("1990:70%/25%", ep.render_years(d, ["USA"]))

    def test_usage(self):
        buf = io.StringIO()
        self.assertEqual(ep.main([], out=lambda s: buf.write(s)), 2)
        self.assertEqual(ep.main(["--bad", "x"], out=lambda s: buf.write(s)), 2)


if __name__ == "__main__":
    unittest.main()
