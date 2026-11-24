#!/usr/bin/env python3
"""TASK-0134 step 4: K2b' of the mastery programme (docs/simulation/g5-g6-probe.md §9.3), from the harness lab/analysis/mastery_k2b.

Pair = (seed, actor, tier >= 5 process). Norm (point B0): from the first month >= 388 with proc_E > 0 or proc_v > 0 while CultureYield < 0.5,
to the first month with CultureYield >= 0.5; Cost_norm = sum proc_E. Programme (first episode of the pair): from its start month to the first
month with CultureYield >= 0.5; Cost_prog = sum upkeep_mult * proc_E. Fail: both finite and T_prog < m*T_norm or Cost_prog < k*Cost_norm.
m = 2, k = 1.5 (#101/#106, culture-round2.md §1.5), fixed before the run.

Usage: python3 lab/analysis/mastery_k2b.py NORM_DIR:POINT PROG_DIR:POINT --seeds 1-10 [--upkeep 1.5]
Each DIR holds p{POINT}-s{SEED}.csv files of the harness.
"""
import argparse
import csv
import os
import statistics

M, K = 2, 1.5
Y_START = 500_000
T_1978 = 388


def load(path):
    rows = {}
    with open(path, newline="") as f:
        for r in csv.DictReader(f):
            rows.setdefault((r["actor"], int(r["prc"])), []).append((int(r["month"]), int(r["e"]), int(r["v"]), int(r["cy"]), int(r["stamp"])))
    return rows


def norm(series):
    start = next((m for m, e, v, cy, _ in series if m >= T_1978 and (e > 0 or v > 0) and cy < Y_START), None)
    if start is None:
        return None
    end = next((m for m, _, _, cy, _ in series if m >= start and cy >= Y_START), None)
    if end is None:
        return None
    return end - start, sum(e for m, e, _, _, _ in series if start <= m <= end)


def programme(series, upkeep):
    first = next(((m, st) for m, _, _, _, st in series if st > 0), None)
    if first is None:
        return None
    start = first[1] - 1
    end = next((m for m, _, _, cy, _ in series if m >= start and cy >= Y_START), None)
    cost = sum(upkeep * e for m, e, _, _, _ in series if m >= start and (end is None or m <= end))
    return (None if end is None else end - start), cost, start


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("norm")
    ap.add_argument("prog")
    ap.add_argument("--seeds", default="1-10")
    ap.add_argument("--upkeep", type=float, default=1.5)
    a = ap.parse_args()
    lo, hi = (int(x) for x in a.seeds.split("-"))
    nd, npnt = a.norm.rsplit(":", 1)
    pd, ppnt = a.prog.rsplit(":", 1)
    pairs, comparable, fails, censored, no_norm = 0, 0, [], 0, 0
    ratios_t, ratios_c, prog_t = [], [], []
    for s in range(lo, hi + 1):
        N = load(os.path.join(nd, f"p{npnt}-s{s}.csv"))
        P = load(os.path.join(pd, f"p{ppnt}-s{s}.csv"))
        for key, series in sorted(P.items()):
            pr = programme(series, a.upkeep)
            if pr is None:
                continue
            pairs += 1
            t_p, c_p, start = pr
            if t_p is None:
                censored += 1
                continue
            prog_t.append(t_p)
            nm = norm(N.get(key, []))
            if nm is None:
                no_norm += 1
                continue
            comparable += 1
            t_n, c_n = nm
            ratios_t.append(t_p / t_n if t_n else float("inf"))
            ratios_c.append(c_p / c_n if c_n else float("inf"))
            if t_p < M * t_n or c_p < K * c_n:
                fails.append(f"s{s} {key[0]} {key[1]}: T_prog {t_p} vs T_norm {t_n}; cost {c_p:.0f} vs {c_n:.0f} raw (start {start})")
    med = lambda v: statistics.median(v) if v else None
    print(f"K2b' {a.prog} vs norm {a.norm}: programme pairs {pairs}; ready by 2030 {pairs - censored} (censored {censored}); "
          f"norm never ready {no_norm}; comparable {comparable}; fails {len(fails)} (m = {M}, k = {K})")
    print(f"T_prog months median {med(prog_t)}; T_prog/T_norm median {med(ratios_t)}; Cost ratio median {med(ratios_c)}")
    print("measurable" if comparable >= 5 else "NOT MEASURABLE (comparable pairs < 5)")
    for f in fails:
        print("  FAIL", f)


if __name__ == "__main__":
    main()
