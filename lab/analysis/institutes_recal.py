#!/usr/bin/env python3
"""TASK-0176: 1945-1977 tolerances (slice-budget-convergence.md §8, as lab/analysis/tm_budget_rules.py) of every series against a baseline series.

Reads a matrix directory (any JetSlice horizon >= 1977-09) and prints, per series: the §8 violations against BASE (default LEGACY) on paired seeds,
first-unit months per actor and generation (median over seeds, both sides), and the institute build months when inst_build.csv is present.
Usage: python3 lab/analysis/institutes_recal.py DIR [--base LEGACY] [--detail]. Exit 1 = a run not ok or the baseline missing.
"""
import csv
import os
import statistics
import sys
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tm_budget_rules as tm  # noqa: E402  (the §8 rule itself: tolerance_s8, timing, line_starts, comp_rank)

ACTORS = tm.ACTORS
YEARS = (1960, 1977)


def read(run_dir, name):
    p = os.path.join(run_dir, name + ".csv")
    if not os.path.exists(p):
        return []
    with open(p, newline="") as f:
        return list(csv.DictReader(f))


def analyse(run_dir):
    """The fields tolerance_s8 reads: yearly rank and key_G (Sept 1960, 1977), first unit and t_req per (actor, gen), line starts."""
    key = {}
    for r in read(run_dir, "actor_year"):
        key[(int(r["t"]), r["actor"])] = (int(r["key_G"]), int(r["key_T"]), int(r["key_N"]), int(r["key_P"]))
    yearly = {}
    for y in YEARS:
        t0 = tm.t_sept(y)
        rank = tm.comp_rank({a: key[(t0, a)] for a in ACTORS})
        for a in ACTORS:
            yearly[(y, a)] = dict(rank=rank[a], key_g=key[(t0, a)][0])
    gen = read(run_dir, "gen_times")
    first_unit = {(r["actor"], int(r["gen"])): tm.num(r["t_first_unit"]) for r in gen}
    t_req = {(r["actor"], int(r["gen"])): tm.num(r["t_req"]) for r in gen}
    starts = tm.line_starts(read(run_dir, "lines"))
    return dict(yearly=yearly, first_unit=first_unit, t_req=t_req, starts=starts)


def tolerance_s8d(var, base, max_seeds=2, max_median=12):
    """[Предл., TASK-0176, written after the placebo of the note §4] §8 with a seed allowance: the same items as tm.tolerance_s8; a date item
    (first unit, project t_req, k-th line start of an actor) fails when it violates timing() in more than max_seeds seeds or the median of its
    paired shifts over the seeds where it is in scope exceeds max_median months; line counts before H_CALIB may differ in at most max_seeds seeds;
    rank and key_G rules unchanged (they already allow 2 of 10)."""
    seeds = sorted(set(var) & set(base))
    out = [x for x in tm.tolerance_s8(var, base) if not x.startswith("seed ")]
    items = defaultdict(lambda: {"bad": [], "shift": []})
    count_bad = defaultdict(list)
    for s in seeds:
        v, b = var[s], base[s]
        for name, col in (("first unit", "first_unit"), ("project t_req", "t_req")):
            for a, g in sorted(set(b[col]) | set(v[col])):
                tv, tb = v[col].get((a, g)), b[col].get((a, g))
                key = f"{a} G{g} {name}"
                if tm.timing(tv, tb):
                    items[key]["bad"].append(s)
                if tv is not None and tb is not None and min(tv, tb) < tm.H_CALIB:
                    items[key]["shift"].append(tv - tb)
        for a in ACTORS:
            lv, lb = v["starts"][a], b["starts"][a]
            for k in range(max(len(lv), len(lb))):
                x = lv[k] if k < len(lv) else None
                z = lb[k] if k < len(lb) else None
                key = f"{a} line {k + 1}"
                if tm.timing(x, z):
                    items[key]["bad"].append(s)
                if x is not None and z is not None and min(x, z) < tm.H_CALIB:
                    items[key]["shift"].append(x - z)
            if sum(1 for t in lv if t < tm.H_CALIB) != sum(1 for t in lb if t < tm.H_CALIB):
                count_bad[a].append(s)
    for key, it in sorted(items.items()):
        m = statistics.median(it["shift"]) if it["shift"] else 0
        if len(it["bad"]) > max_seeds or abs(m) > max_median:
            out.append(f"{key}: violates in seeds {it['bad']}, median shift {m:g}")
    for a, ss in sorted(count_bad.items()):
        if len(ss) > max_seeds:
            out.append(f"{a} lines before {tm.H_CALIB}: count differs in seeds {ss}")
    return out


def med(xs):
    xs = [x for x in xs if x is not None]
    return statistics.median(xs) if xs else None


def main():
    root = sys.argv[1]
    base_name = sys.argv[sys.argv.index("--base") + 1] if "--base" in sys.argv else "LEGACY"
    detail = "--detail" in sys.argv
    runs_csv = read(root, "runs")
    bad = [r["run"] for r in runs_csv if r["status"] != "ok"]
    runs = defaultdict(dict)
    for r in runs_csv:
        if r["threads"] == "1" and r["status"] == "ok":
            runs[r["series"]][int(r["seed"])] = analyse(os.path.join(root, "runs", r["run"]))
    if bad or base_name not in runs:
        print(f"INVALID: runs not ok {bad}; baseline {base_name} present: {base_name in runs}")
        sys.exit(1)
    base = runs[base_name]
    series = [s for s in dict.fromkeys(r["series"] for r in runs_csv) if s != base_name]
    print(f"# §8 against {base_name}, seeds {sorted(base)}\n")
    print("| series | §8 violations | seeds with a violation | §8 | §8-d [Предл.] violations | §8-d |\n|---|---|---|---|---|---|")
    report, report_d = {}, {}
    for s in series:
        v = tm.tolerance_s8(runs[s], base)
        vd = tolerance_s8d(runs[s], base)
        report[s], report_d[s] = v, vd
        bad_seeds = sorted({int(x.split()[1]) for x in v if x.startswith("seed ")})
        print(f"| {s} | {len(v)} | {bad_seeds} | {'PASS' if not v else 'FAIL'} | {len(vd)} | {'PASS' if not vd else 'FAIL'} |")
    print("\n# First unit, median month over seeds (G1-G3; base / series)\n")
    gens = (1, 2, 3)
    print("| series | " + " | ".join(f"{a} G{g}" for a in ACTORS for g in gens) + " |")
    print("|---|" + "---|" * (len(ACTORS) * len(gens)))
    for s in [base_name] + series:
        cells = []
        for a in ACTORS:
            for g in gens:
                m = med([runs[s][k]["first_unit"].get((a, g)) for k in runs[s]])
                cells.append("-" if m is None else f"{m:g}")
        print(f"| {s} | " + " | ".join(cells) + " |")
    if detail:
        for s in series:
            if report[s]:
                print(f"\n## {s}\n" + "\n".join("- " + x for x in report[s]))
            if report_d[s]:
                print(f"\n## {s} §8-d\n" + "\n".join("- " + x for x in report_d[s]))


if __name__ == "__main__":
    main()
