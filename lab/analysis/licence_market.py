#!/usr/bin/env python3
"""TASK-0179: the licence market against LEGACY (docs/simulation/licence-market-calibration.md).

Usage: python3 lab/analysis/licence_market.py BASE_DIR VAR_DIR [VAR_DIR ...] [--base LEGACY] [--detail] [--deals]
BASE_DIR holds the baseline series (default LEGACY); each VAR_DIR is one matrix of TASK-0179-licm.json (its first series other than the baseline, with
the market probe table market.csv). Prints per VAR_DIR: §8 and §8-d (institutes_recal.tolerance_s8d) against the baseline on paired seeds, the purchases
of the AI before H_CALIB and in the whole run, licensed projects that built a unit before H_CALIB, first-unit medians. --deals lists every purchase.
Exit 1 = a run not ok or the baseline missing.
"""
import csv
import os
import statistics
import sys
from collections import Counter, defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import institutes_recal as ir  # noqa: E402
import tm_budget_rules as tm  # noqa: E402


def load(root, want=None, skip=None):
    """{series: {seed: analyse()}} and {series: {seed: run dir}} of the ok runs (threads 1)."""
    runs, dirs = defaultdict(dict), defaultdict(dict)
    rows = ir.read(root, "runs")
    bad = [r["run"] for r in rows if r["status"] != "ok"]
    if bad:
        print(f"INVALID {root}: runs not ok {bad}")
        sys.exit(1)
    for r in rows:
        if r["threads"] != "1" or (want and r["series"] != want) or (skip and r["series"] == skip):
            continue
        d = os.path.join(root, "runs", r["run"])
        runs[r["series"]][int(r["seed"])] = ir.analyse(d)
        dirs[r["series"]][int(r["seed"])] = d
    return runs, dirs


def market(run_dir):
    return ir.read(run_dir, "market")


def med(xs):
    xs = [x for x in xs if x is not None]
    return statistics.median(xs) if xs else None


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    base_name = sys.argv[sys.argv.index("--base") + 1] if "--base" in sys.argv else "LEGACY"
    args = [a for a in args if a != base_name]
    if len(args) < 2:
        print(__doc__)
        sys.exit(2)
    base_runs, _ = load(args[0], want=base_name)
    if base_name not in base_runs:
        print(f"INVALID: baseline {base_name} missing in {args[0]}")
        sys.exit(1)
    base = base_runs[base_name]
    print("| matrix | §8 | §8-d viol. | §8-d | deals < 384 | buyers < 384 | lic. builds < 384 | deals all | FRA G1 | FRA G2 | SUN G2 | SUN G3 | GBR G3 |")
    print("|---|---|---|---|---|---|---|---|---|---|---|---|---|")
    details = []
    for root in args[1:]:
        runs, dirs = load(root, skip=base_name)
        if not runs:
            print(f"INVALID: no series in {root}")
            sys.exit(1)
        name = next(iter(runs))
        var, vdirs = runs[name], dirs[name]
        v8 = tm.tolerance_s8(var, base)
        vd = ir.tolerance_s8d(var, base)
        deals_early, deals_all, builds_early = 0, 0, 0
        buyers = Counter()
        listing = []
        for s in sorted(vdirs):
            for r in market(vdirs[s]):
                t = int(r["t"])
                if r["kind"] == "DEAL":
                    deals_all += 1
                    listing.append((s, t, r["buyer"], r["seller"], r["gen"], r["price"], r["ref"], r["m"], r["buyer_profile"]))
                    if t < tm.H_CALIB:
                        deals_early += 1
                        buyers[r["buyer"]] += 1
                elif r["kind"] == "LBUILT" and t < tm.H_CALIB:
                    builds_early += 1
        fu = lambda a, g: med([var[s]["first_unit"].get((a, g)) for s in var])
        cells = [fu("FRA", 1), fu("FRA", 2), fu("SUN", 2), fu("SUN", 3), fu("GBR", 3)]
        print(f"| {os.path.basename(root.rstrip('/'))} | {len(v8)} | {len(vd)} | {'PASS' if not vd else 'FAIL'} | {deals_early} | "
              f"{' '.join(f'{k}:{n}' for k, n in sorted(buyers.items())) or '-'} | {builds_early} | {deals_all} | "
              + " | ".join("-" if c is None else f"{c:g}" for c in cells) + " |")
        details.append((root, vd, listing))
    fb = lambda a, g: med([base[s]["first_unit"].get((a, g)) for s in base])
    print(f"\nbaseline {base_name} first unit medians: FRA G1 {fb('FRA', 1)}, FRA G2 {fb('FRA', 2)}, SUN G2 {fb('SUN', 2)}, SUN G3 {fb('SUN', 3)}, "
          f"GBR G3 {fb('GBR', 3)}")
    for root, vd, listing in details:
        if "--detail" in sys.argv and vd:
            print(f"\n## {root} §8-d\n" + "\n".join("- " + x for x in vd))
        if "--deals" in sys.argv:
            print(f"\n## {root} deals (seed, month, year, buyer, seller, gen, price, ref, m, buyer profile)")
            for s, t, b, se, g, p, ref, m, prof in sorted(listing):
                print(f"- s{s} {t} ({1945 + (t + 8) // 12}) {b} <- {se} G{g} price {p} ref {ref} m {m} prof {prof}")


if __name__ == "__main__":
    main()
