#!/usr/bin/env python3
"""TASK-0147 §10.12 (owner decision #464): weak ahead-of-time cost points and institution-like asymmetry (PROFILE_CELL P1 vs P3).

Usage: python3 lab/analysis/ahead_inst.py INST_MATRIX INST_PRINT --b B_MATRIX B_PRINT
  INST: lab/manifests/TASK-0147-ahead-inst.json; B: TASK-0147-ahead-probe.json on data B (points off, k02, k05 of §10.7).
"""
import argparse
import csv
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ahead_probe import TARGETS, axis, first_years  # noqa: E402
from g5g6_probe import fmt, med  # noqa: E402
from tm_horizon_2030 import ACTORS, T_1978, year_of  # noqa: E402

MONTHS = 1024
YEARS = (1990, 2010, 2030)


def rows(path):
    with open(path, newline="") as f:
        return list(csv.DictReader(f))


def run_dir(mdir, series, seed):
    for r in rows(os.path.join(mdir, "runs.csv")):
        if r["series"] == series and int(r["seed"]) == seed and r["threads"] == "1":
            return os.path.join(mdir, "runs", r["run"])
    raise KeyError((series, seed))


def actor_extra(d, actor):
    own = {}
    for r in rows(os.path.join(d, "node_times.csv")):
        if r["actor"] == actor and r["node"] in ("76", "77", "78") and r["t_known"] not in ("", "-1"):
            own[r["node"]] = year_of(int(r["t_known"]))
    fv = {}
    for r in rows(os.path.join(d, "actor_year.csv")):
        y = year_of(int(r["t"]))
        if y in YEARS:
            fv.setdefault(y, {})[r["actor"]] = int(r["fv"])
    rank = {y: 1 + sum(1 for a, v in fv[y].items() if v > fv[y][actor]) for y in fv}
    proc = sum(int(r["proc_budget"]) for r in rows(os.path.join(d, "supply_month.csv")) if r["actor"] == actor and int(r["t"]) >= T_1978) / 1e6
    return own, {y: fv[y][actor] / 1e6 for y in fv}, rank, proc


def summary(out):
    yr = lambda t: None if t is None else year_of(t)
    lead = {}
    for o in out:
        lead[o["x"]["lead_final"]] = lead.get(o["x"]["lead_final"], 0) + 1
    return dict(meds={n: med(first_years(out, n)) for n, _, _ in TARGETS},
                g5={a: (med([yr(o["fielded"].get((a, 5))) for o in out]), sum(1 for o in out if o["fielded"].get((a, 5)) is not None)) for a in ("USA", "SUN", "FRA")},
                lead=dict(sorted(lead.items(), key=lambda kv: -kv[1])), p1=med([o["g5"] for o in out]), p2=med([o["g4_great"] for o in out]))


def show(name, s):
    g5 = " / ".join(f"{a} {'—' if y is None else round(y)}[{c}]" for a, (y, c) in s["g5"].items())
    print(f"| {name} | " + " | ".join(f"{s['meds'][n]:.0f}" for n, _, _ in TARGETS) + f" | {g5} | {s['lead']} | {s['p1']:.0f}/{s['p2']:.0f} |")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("matrix")
    ap.add_argument("print_dir")
    ap.add_argument("--b", nargs=2, required=True)
    a = ap.parse_args()
    I, val, dh = axis(a.matrix, a.print_dir)
    B, valb, dhb = axis(*a.b)
    allv = val + valb + ([f"data_hash {dh} != B {dhb}"] if dh != dhb else [])
    first = next(iter(I.values()))[0]["x"]
    print(f"core_commit={first['core']} data_hash={dh} manifest_hash={first['manifest_hash']}")
    print("VALIDITY:", "OK" if not allv else "FAIL " + " | ".join(allv))
    print("\n| Point | " + " | ".join(f"{l} ({n}) {lo}-{hi}" for n, l, (lo, hi) in TARGETS) + " | G5 fielded USA/SUN/FRA [seeds] | leader 2030-12 | P1/P2 |")
    print("|---|" + "---|" * (len(TARGETS) + 3))
    for pt in ("off", "k02", "k05"):
        show(f"B/{pt} (profiles from AI)", summary(B[pt]))
    for pt in I:
        show(pt, summary(I[pt]))
    print("\n## Own actor against the same country in cell 26 (same k): median [min..max]")
    for k in ("k0", "k05"):
        for cell, actor in ((8, "USA"), (24, "SUN")):
            res = {}
            for c in (26, cell):
                series = f"c{c}_{k}"
                out = I[series]
                ex = [actor_extra(run_dir(a.matrix, series, o["seed"]), actor) for o in out]
                yr = lambda t: None if t is None else year_of(t)
                res[c] = dict(
                    own={n: med([e[0].get(n, 2031) for e in ex]) for n in ("76", "78", "77")},
                    g4=med([yr(o["fielded"].get((actor, 4))) for o in out]), g5=med([yr(o["fielded"].get((actor, 5))) for o in out]),
                    in4=[o["in_service"][(actor, 4)] for o in out], in5=[o["in_service"][(actor, 5)] for o in out],
                    fv={y: [e[1].get(y) for e in ex] for y in YEARS}, rank={y: [e[2].get(y) for e in ex] for y in YEARS},
                    proc=[e[3] for e in ex], lead=[o["x"]["lead_share"][actor] for o in out])
            b, f = res[26], res[cell]
            print(f"- {k} {actor} P1 (cell {cell}) vs P3 (cell 26): own discovery 76 {f['own']['76']:.0f} vs {b['own']['76']:.0f}, "
                  f"78 {f['own']['78']:.0f} vs {b['own']['78']:.0f}, 77 {f['own']['77']:.0f} vs {b['own']['77']:.0f}; "
                  f"G4 fielded {f['g4']} vs {b['g4']}; G5 {f['g5']} vs {b['g5']}; years G>=4 {fmt(f['in4'], '{:.1f}')} vs {fmt(b['in4'], '{:.1f}')}; "
                  f"G>=5 {fmt(f['in5'], '{:.1f}')} vs {fmt(b['in5'], '{:.1f}')}")
            print("    fv " + "; ".join(f"{y} {fmt(f['fv'][y], '{:.0f}')} vs {fmt(b['fv'][y], '{:.0f}')} (rank {fmt(f['rank'][y], '{:.0f}')} vs {fmt(b['rank'][y], '{:.0f}')})" for y in YEARS)
                  + f"; procurement budget 1978-2030 {fmt(f['proc'], '{:.0f}')} vs {fmt(b['proc'], '{:.0f}')}; lead share {fmt(f['lead'])} vs {fmt(b['lead'])}")
            early = (b["own"]["76"] - f["own"]["76"] >= 2) or (b["own"]["78"] - f["own"]["78"] >= 2)
            fvb, fvf = med(b["fv"][2010]), med(f["fv"][2010])
            paid = (med(b["in4"]) - med(f["in4"]) >= 1) or (fvb and fvf < 0.9 * fvb) or (med(f["rank"][2010]) > med(b["rank"][2010]))
            print(f"    -> early {'yes' if early else 'no'}; paid {'yes' if paid else 'no'}" + (" => early without a price" if early and not paid else ""))
    return 0 if not allv else 1


if __name__ == "__main__":
    sys.exit(main())
