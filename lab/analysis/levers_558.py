#!/usr/bin/env python3
"""levers-558 (docs/simulation/levers-on-baseline-558.md §0): pre-registered criteria of the lever rules on the #558 pace baseline.

Per matrix directory, every series against LEGACY of the same directory on paired seeds:
 1. dates: median over seeds of the first unit of any actor for G2, G3, G4 (never = +inf) and its distance to the C4 window
    (G2 months 16-87, G3 100-195, G4 256-363 from 1945-09; lab/analysis/ai_goals_history.py); passes when d(series) <= d(LEGACY) for each gen;
 2. structure: the rank (1960, 1977) and key_G (1977) items of tm_budget_rules.tolerance_s8; passes when there is none.
Printed for information only: the date items of §8-d (institutes_recal.tolerance_s8d) and the licence deals before 1977-09 (market.csv, LIC_PROBE).
Usage: python3 lab/analysis/levers_558.py DIR[=LABEL] [DIR[=LABEL] ...] [--detail]. Exit 1 = a run not ok or LEGACY missing in a directory.
"""
import csv
import math
import os
import statistics
import sys
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import institutes_recal as ir  # noqa: E402
import tm_budget_rules as tm  # noqa: E402

WINDOWS = {2: (16, 87), 3: (100, 195), 4: (256, 363)}
INF = math.inf


def first_any(run):
    out = {}
    for g in WINDOWS:
        ts = [t for (a, gg), t in run["first_unit"].items() if gg == g and t is not None and t >= 0]
        out[g] = min(ts) if ts else INF
    return out


def dist(m, g):
    lo, hi = WINDOWS[g]
    return 0 if lo <= m <= hi else (lo - m if m < lo else m - hi)


def deals(run_dir):
    p = os.path.join(run_dir, "market.csv")
    if not os.path.exists(p):
        return None
    with open(p, newline="") as f:
        return [r for r in csv.DictReader(f) if r["kind"] == "DEAL"]


def structure(var, base):
    return [x for x in tm.tolerance_s8(var, base) if x.startswith("rank ") or x.startswith("key_G ")]


def fmt(m):
    return "never" if m == INF else f"{m:g}"


def load(d):
    rows = ir.read(d, "runs")
    bad = [r["run"] for r in rows if r["status"] != "ok"]
    runs, dl = defaultdict(dict), defaultdict(dict)
    for r in rows:
        if r["threads"] == "1" and r["status"] == "ok":
            rd = os.path.join(d, "runs", r["run"])
            a = ir.analyse(rd)
            a["any"] = first_any(a)
            runs[r["series"]][int(r["seed"])] = a
            dl[r["series"]][int(r["seed"])] = deals(rd)
    return rows, bad, runs, dl


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    detail = "--detail" in sys.argv
    failed = False
    print("| matrix:series | seeds | G2 med (d) | G3 med (d) | G4 med (d) | dates | structure items | structure | §8-d date items (info) | deals < 1977-09 (info) | verdict |")
    print("|---|---|---|---|---|---|---|---|---|---|---|")
    details = []
    for arg in args:
        d, _, label = arg.partition("=")
        label = label or os.path.basename(os.path.normpath(d))
        rows, bad, runs, dl = load(d)
        if bad or "LEGACY" not in runs:
            print(f"| {label} | INVALID: runs not ok {bad}, LEGACY present {'LEGACY' in runs} |")
            failed = True
            continue
        base = runs["LEGACY"]
        med = {s: {g: statistics.median([r["any"][g] for r in runs[s].values()]) for g in WINDOWS} for s in runs}
        dbase = {g: dist(med["LEGACY"][g], g) for g in WINDOWS}
        for s in dict.fromkeys(r["series"] for r in rows):
            seeds = sorted(runs[s])
            cells = [f"{fmt(med[s][g])} ({fmt(dist(med[s][g], g))})" for g in WINDOWS]
            if s == "LEGACY":
                print(f"| {label}:LEGACY | {seeds[0]}-{seeds[-1]} | " + " | ".join(cells) + " | base | — | — | — | — | — |")
                continue
            dates_ok = all(dist(med[s][g], g) <= dbase[g] for g in WINDOWS)
            st = structure(runs[s], base)
            dd = [x for x in ir.tolerance_s8d(runs[s], base) if not (x.startswith("rank ") or x.startswith("key_G "))]
            dls = [v for v in dl[s].values() if v is not None]
            nd = sum(1 for v in dls for r in v if int(r["t"]) < tm.H_CALIB) if dls else None
            verdict = "PASS" if dates_ok and not st else "FAIL"
            print(f"| {label}:{s} | {seeds[0]}-{seeds[-1]} | " + " | ".join(cells)
                  + f" | {'ok' if dates_ok else 'FAIL'} | {len(st)} | {'ok' if not st else 'FAIL'} | {len(dd)} | {'-' if nd is None else nd} | {verdict} |")
            if detail:
                per = {g: sorted(fmt(r["any"][g]) for r in runs[s].values()) for g in WINDOWS}
                details.append(f"\n## {label}:{s}\n- first unit any actor per seed: " + "; ".join(f"G{g} {per[g]}" for g in WINDOWS)
                               + "".join("\n- " + x for x in st) + "".join("\n- (8-d date) " + x for x in dd))
    for x in details:
        print(x)
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
