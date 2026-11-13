#!/usr/bin/env python3
"""TASK-0044 (T10): informational print of A40 firings (spec slice-jet-aviation.md v0.24 §3.5 A40) on seeds 1..20.

Input: output of lab/runners/t6e/run_t6e.sh (T6eHarness, read-only observer of the slice) for one arm/config:
    events.csv  — branch О (`cancel_design`) with class Processes / Defects (PolicySystem.ClassifyGiveUp on the snapshot);
    lines2.csv  — every line in С per month: y_min over prc_set, `cultural` flag, idle_c of the snapshot, AiClose at t+1.
Branch С firing = AiClose at t+1 of a line with snapshot idle_c >= T_ab. Class CULTURE (A40 L-C0): in each of the T_ab months
before the close the line's `cultural` flag is 1 (min yield over prc_set = 0, or no alloy supply with all smelts at yield 0);
otherwise OTHER (print the months). Expectations: spec A40 (б) v0.16 table.
Usage: python3 lab/analysis/t10_a40_print.py lab/results/<name>/out/<config>/<arm>
"""
import csv
import json
import os
import sys

T_AB = 60
ACTORS = ["USA", "GBR", "SUN", "FRA", "SWE"]
# spec v0.24 §3.5 A40 (б) v0.16: branch С share of seeds with >= 1 firing; branch О class Processes mean per seed (band)
EXPECT = {"USA": ("<=0.16", (0.5, 2.0), 1.2), "GBR": ("<=0.16", (0.0, 0.15), None), "SUN": ("<=0.16", (0.2, 1.0), 0.55),
          "FRA": ("<=0.16", (0.05, 0.6), 0.3), "SWE": ("<=0.16", (0.0, 0.15), None)}


def rows(path):
    with open(path, newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def main(root):
    seeds = sorted(os.listdir(root), key=lambda s: int(s.replace("seed", "")))
    o = {a: {"Processes": 0, "Defects": 0, "?": 0} for a in ACTORS}
    c = {a: {"CULTURE": 0, "OTHER": 0} for a in ACTORS}
    seeds_c = {a: 0 for a in ACTORS}
    o_by_prof = {}
    detail = []
    n_o_seed, n_c_seed = [], []
    for s in seeds:
        d = os.path.join(root, s)
        run = json.load(open(os.path.join(d, "run.json")))
        prof = dict(p.split("=") for p in run["profiles"])
        no = nc = 0
        for e in rows(os.path.join(d, "events.csv")):
            if e["kind"] == "O":
                o[e["actor"]][e["cls"]] = o[e["actor"]].get(e["cls"], 0) + 1
                key = (e["actor"], prof[e["actor"]], e["cls"])
                o_by_prof[key] = o_by_prof.get(key, 0) + 1
                no += 1
                detail.append((s, "O", e["actor"], prof[e["actor"]], e["design"], e["t"], e["cls"]))
        hist = {}
        fired = set()
        for r in rows(os.path.join(d, "lines2.csv")):
            if r["ymin"] == "":
                continue  # a start row (design not a line in the snapshot)
            k = (r["actor"], r["design"])
            hist.setdefault(k, []).append((int(r["t"]), r["cultural"] == "1", int(r["ymin"])))
            if r["close"] == "1" and int(r["idle"]) >= T_AB:
                h = hist[k][-T_AB:]
                cls = "CULTURE" if len(h) == T_AB and all(x[1] for x in h) else "OTHER"
                c[r["actor"]][cls] += 1
                fired.add(r["actor"])
                nc += 1
                detail.append((s, "C", r["actor"], prof[r["actor"]], r["design"], r["t"], cls + " gp=" + r["gp"]))
        for a in fired:
            seeds_c[a] += 1
        n_o_seed.append(no)
        n_c_seed.append(nc)
    n = len(seeds)
    print(f"seeds: {n} ({seeds[0]}..{seeds[-1]})")
    print("actor | С: seeds>=1 (share) | С classes | О Processes (mean/seed) | О Defects | expectation С; О band")
    for a in ACTORS:
        exp = EXPECT[a]
        mean_p = o[a]["Processes"] / n
        band = exp[1]
        flag = "in" if band[0] <= mean_p <= band[1] else "OUT"
        print(f"{a} | {seeds_c[a]} ({seeds_c[a] / n:.2f}) | {c[a]} | {o[a]['Processes']} ({mean_p:.2f}, {flag}) | {o[a]['Defects']} | "
              f"{exp[0]}; {band[0]}-{band[1]}")
    print(f"n_O per seed: mean {sum(n_o_seed) / n:.2f} (expect ~2.0, band 1.0-3.0); per seed {n_o_seed}")
    print(f"n_C per seed: mean {sum(n_c_seed) / n:.2f} (expect ~0, <=0.2); per seed {n_c_seed}")
    print("О by (actor, profile, class):", {"/".join(k): v for k, v in sorted(o_by_prof.items())})
    print("firings (seed, branch, actor, profile, design, t, class):")
    for x in detail:
        print("  ", *x)


if __name__ == "__main__":
    main(sys.argv[1])
