#!/usr/bin/env python3
"""Tests of the provenance checks of ahead_probe.py (Codex review of train 2): VALIDITY must fail when compared matrices differ in build or data."""
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ahead_probe import cross_check, provenance  # noqa: E402


def matrix(root, name, runs):
    d = os.path.join(root, name)
    os.makedirs(os.path.join(d, "runs"))
    with open(os.path.join(d, "runs.csv"), "w") as f:
        f.write("run,status\n")
        for i, r in enumerate(runs):
            rid = f"r{i}"
            f.write(f"{rid},ok\n")
            os.makedirs(os.path.join(d, "runs", rid))
            info = {"model": "JetSlice", "core_commit": r.get("core", "c1"), "build_commit": r.get("core", "c1"), "dirty": r.get("dirty", False),
                    "model_info": {"test_api_version": 20, "scenario_hash": "s", "data_hash": r.get("data", "d1")}}
            json.dump(info, open(os.path.join(d, "runs", rid, "run.json"), "w"))
    return d


class ProvenanceTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()

    def test_consistent_axes_pass(self):
        p = {n: provenance(matrix(self.tmp, n, [{"data": dh}, {"data": dh}]))[0] for n, dh in (("A", "a"), ("B", "b"), ("S", "b"))}
        self.assertEqual(cross_check(p, [["B", "S"]], [("A", "B")]), [])

    def test_mixed_data_within_matrix_fails(self):
        _, problems = provenance(matrix(self.tmp, "B", [{"data": "b"}, {"data": "x"}]))
        self.assertTrue(any("data_hash differs within" in x for x in problems))

    def test_dirty_run_is_counted_not_fatal(self):
        prov, problems = provenance(matrix(self.tmp, "B", [{"dirty": True}, {}]))
        self.assertEqual((prov["dirty_runs"], problems), (1, []))

    def test_other_build_or_data_between_matrices_fails(self):
        p = {"A": provenance(matrix(self.tmp, "A", [{"data": "a"}]))[0], "B": provenance(matrix(self.tmp, "B", [{"data": "b", "core": "c2"}]))[0],
             "S": provenance(matrix(self.tmp, "S", [{"data": "z"}]))[0]}
        problems = cross_check(p, [["B", "S"]], [("A", "B")])
        self.assertTrue(any("core_commit" in x for x in problems))
        self.assertTrue(any("data_hash differs between" in x for x in problems))

    def test_same_data_on_chains_axis_fails(self):
        p = {n: provenance(matrix(self.tmp, n, [{"data": "b"}]))[0] for n in ("A", "B")}
        self.assertTrue(cross_check(p, [], [("A", "B")]))


if __name__ == "__main__":
    unittest.main()
