import io
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import slot_core as sc  # noqa: E402

ACTORS = ["USA", "GBR", "SUN", "FRA", "SWE"]


def month(year):
    return int(round((year - 1945) * 12 - 8))


def write_series(d, seeds=10, g=(1953.7, 1960.0, 1970.0), idle_free=0, gap_months=12, returned=0.05, upkeep=100_000, leader=0,
                 series="SLOTS", elapsed=1000, returned_after=0.05, late_gap=0):
    os.makedirs(os.path.join(d, "runs"), exist_ok=True)
    runs, gens, nodes, summ = [], [], [], []
    for s in range(1, seeds + 1):
        run = f"p0-s{s}-t1"
        runs.append(f"{run},{series},0,{s},1,ok")
        info = {"slot_ai_idle_with_frontier": str(idle_free), "slot_ai_idle_with_frontier_first": "-1", "slot_upkeep_ceiling_ppm": "250000", "slot_player": ""}
        for a in ACTORS:
            for p in (1, 2):
                for k, v in {"slot_months": 3000, "idle": 0, "idle_free": idle_free if a == "USA" else 0, "focus1": 0, "focus2": 0, "frontier": 9000,
                             "short_months": 100, "months": 500}.items():
                    info[f"slot_p{p}_{a}_{k}"] = str(v)
            info[f"slot_{a}_returned_raw"] = str(int(returned * 1_000_000))
            info[f"slot_{a}_science_raw"] = "1000000"
            info[f"slot_w47_{a}_returned_raw"] = str(int(returned * 1_000_000))
            info[f"slot_w47_{a}_science_raw"] = "1000000"
            info[f"slot_p2_{a}_returned_raw"] = str(int(returned_after * 1_000_000))
            info[f"slot_p2_{a}_science_raw"] = "1000000"
            info[f"slot_{a}_upkeep_max_ppm"] = str(upkeep)
            info[f"slot_{a}_workable_last"] = "600"
            months = [m for m in range(0, 601, gap_months) if not (late_gap and 544 <= m < 544 + late_gap)]
            for i, t in enumerate(months):
                nodes.append(f"{run},{a},{i + 1},{t},,,,0")
            for gen, y in zip((2, 3, 4), g):
                gens.append(f"{run},{a},{gen},-1,{month(y) if a == 'USA' else -1},-1,")
        summ.append(f"{run},1,1,1,1,1,{'0' * 384},{str(leader) * 384},{'=' * 384},h,h")
        os.makedirs(os.path.join(d, "runs", run), exist_ok=True)
        with open(os.path.join(d, "runs", run, "run.json"), "w", encoding="utf-8") as f:
            json.dump({"model_info": info, "elapsed_ms": elapsed}, f)
    files = {
        "runs.csv": ["run,series,point,seed,threads,status"] + runs,
        "gen_times.csv": ["run,actor,gen,t_req,t_first_unit,t_fielded,cost_u100"] + gens,
        "node_times.csv": ["run,actor,node,t_known,t_first,t_07,t_mastered,lic"] + nodes,
        "summary.csv": ["run," + ",".join("prof_" + a for a in ACTORS) + ",lead,lead_all,lead_field,hash_final,hash_years"] + summ,
    }
    for name, lines in files.items():
        with open(os.path.join(d, name), "w", encoding="utf-8") as f:
            f.write("\n".join(lines) + "\n")
    return d


class SlotCoreTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)

    def run_main(self, *argv):
        buf = io.StringIO()
        code = sc.main(list(argv), out=lambda s: buf.write(s + "\n"))
        return code, buf.getvalue()

    def series(self, name, **kw):
        return write_series(os.path.join(self.tmp.name, name), **kw)

    def failing(self, text):
        return sorted({line.split()[1] for line in text.splitlines() if line.strip().startswith("FAIL")})

    def test_zero_runs_fail(self):
        d = self.series("empty", seeds=0)
        code, text = self.run_main(d)
        self.assertEqual(code, 1, text)
        self.assertIn("нет ни одного прогона", text)

    def test_all_gates_pass(self):
        code, text = self.run_main(self.series("ok"))
        self.assertEqual(code, 0, text)
        for gate in ("LIVE0", "LIVE5", "IDLE90", "GAP36", "RET20", "CEIL", "BANDS", "LEAD77", "SPREAD", "G4BY2030"):
            self.assertIn(f"PASS {gate}", text)
        self.assertIn("ИТОГ: PASS", text)

    def test_each_criterion_fails_on_its_own(self):
        cases = {
            "LIVE0": dict(idle_free=1),
            "GAP36": dict(gap_months=37),
            "RET20": dict(returned=0.21),
            "CEIL": dict(upkeep=250_001),
            "LEAD77": dict(leader=1),            # GBR leads 1977
        }
        for gate, kw in cases.items():
            code, text = self.run_main(self.series(gate, **kw))
            self.assertEqual(code, 1, gate)
            self.assertEqual(self.failing(text), [gate], text)

    def test_bands_have_both_bounds(self):
        # G2 in 1946 lies inside the old C4 band with no lower bound; the gate of SCIENCE_SLOTS §10 starts in 1948.
        code, text = self.run_main(self.series("early", g=(1946.5, 1960.0, 1970.0)))
        self.assertEqual(code, 1)
        self.assertIn("FAIL BANDS", text)
        code, _ = self.run_main(self.series("late4", g=(1953.7, 1960.0, 1986.5)))
        self.assertEqual(code, 1)

    def test_determinism_and_time(self):
        d = self.series("det")
        with open(os.path.join(d, "matrix.json"), "w", encoding="utf-8") as f:
            json.dump({"thread_mismatch": 1, "valid": False}, f)
        code, text = self.run_main(self.series("ok2"), "--l2", d)
        self.assertEqual(code, 1)
        self.assertIn("FAIL DET", text)
        with open(os.path.join(d, "matrix.json"), "w", encoding="utf-8") as f:
            json.dump({"thread_mismatch": [], "valid": True}, f)   # the matrix writes the list of mismatching runs
        code, text = self.run_main(self.series("ok3"), "--l2", d)
        self.assertEqual(code, 0, text)
        both = self.series("both")
        write_series(both, series="SHARES", elapsed=400)
        self.assertEqual(sc.time_gate({"SLOTS": ["a"], "SHARES": ["b"]}, {"a": 1000, "b": 400})[0][3], False)

    def test_gap_counts_the_tail_until_the_frontier_runs_out(self):
        self.assertEqual(sc.max_gap([10, 20], 70), 50)
        self.assertEqual(sc.max_gap([10, 20, 90], 30), 10)
        self.assertEqual(sc.max_gap([], -1), 0)
        # Orchestrator 2026-11-26: the window 1947-01..1990-12; a long first discovery in 1945-1946 and gaps after 1990 do not count.
        self.assertEqual(sc.max_gap([40, 50, 700], 900, sc.GATE_FIRST, sc.PERIOD1_LAST), 543 - 50)
        self.assertEqual(sc.max_gap([5, 30, 60, 540], 900, sc.GATE_FIRST, sc.PERIOD1_LAST), 480)
        self.assertEqual(sc.max_gap([20, 50], 40, sc.GATE_FIRST, sc.PERIOD1_LAST), 20)

    def test_after_1990_is_information_only(self):
        # Orchestrator 2026-11-26: money returned and long gaps after 1990 are printed, not gated.
        code, text = self.run_main(self.series("late", returned_after=0.9, late_gap=120))
        self.assertEqual(code, 0, text)
        self.assertIn("после 1990 (печать) 90.0%", text)
        self.assertIn("после 1990 (печать) max 56", text)   # no discovery from 1991-01 (month 544) to the end of the frontier (600)


if __name__ == "__main__":
    unittest.main()
