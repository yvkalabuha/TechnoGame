#!/usr/bin/env python3
"""HYP-0004 sketch analysis (docs/simulation/introduction-channel-sketch.md §2-§3, pre-registered before the runs).

Reads one matrix directory of lab/manifests/HYP-0004-sketch.json (merged runs.csv, node_times.csv, prc_month.csv, a40.csv) and
prints, per series, the pre-registered criteria E1, E2, E3, H1, H2 and the prints of §2. Deterministic, no RNG, stdlib only.
Pool: spec v0.30 §8.2 p.1 C2 - (actor, tier-4 star PRC 40/49) pairs not mastered at the start with t_known <= 383 - 48.
Usage: sketch.py MATRIX_DIR [--json OUT]
"""
import argparse
import collections
import csv
import json
import math
import os
import sys

FX = 1_000_000
LAST = 383
L_INTRO = 48
TIER4 = (40, 49)
TIER5 = (41, 51, 52)
Y_ADMIT = 0.25


def oi(s):
    return None if s in ("", None) else int(s)


def q(xs, p):
    """Lower empirical quantile (index floor(p*(n-1))) - the same 'lower median' convention as exp0002.median_low."""
    if not xs:
        return None
    s = sorted(xs)
    return s[int(math.floor(p * (len(s) - 1)))]


def load(d):
    runs = {}
    with open(os.path.join(d, "runs.csv"), newline="") as f:
        for r in csv.DictReader(f):
            runs[r["run"]] = r
    nt = collections.defaultdict(dict)
    with open(os.path.join(d, "node_times.csv"), newline="") as f:
        for r in csv.DictReader(f):
            nd = int(r["node"])
            if nd in TIER4 + TIER5:
                nt[r["run"]][(r["actor"], nd)] = (oi(r["t_known"]), oi(r["t_first"]), oi(r["t_07"]), oi(r["t_mastered"]))
    ym = {}
    with open(os.path.join(d, "prc_month.csv"), newline="") as f:
        for r in csv.DictReader(f):
            nd = int(r["node"])
            if nd in TIER4:
                ym[(r["run"], r["actor"], nd, int(r["t"]))] = int(r["yield"]) / FX
    a40 = collections.Counter()
    p = os.path.join(d, "a40.csv")
    if os.path.exists(p):
        with open(p, newline="") as f:
            for r in csv.DictReader(f):
                a40[(runs[r["run"]]["series"], r["branch"], r["cls"])] += 1
    return runs, nt, ym, a40


def pool_pairs(nt, rids, nodes):
    out, never, late, full = [], 0, 0, 0
    for rid in rids:
        for (a, nd), x in sorted(nt[rid].items()):
            if nd not in nodes or (x[3] is not None and x[3] < 0):
                continue
            full += 1
            if x[0] is None:
                never += 1
            elif x[0] > LAST - L_INTRO:
                late += 1
            else:
                out.append((rid, a, nd) + x)
    return out, never, late, full


def series_stats(name, rids, nt, ym):
    pool, never, late, full = pool_pairs(nt, rids, TIER4)
    n = len(pool)
    with_first = [p for p in pool if p[4] is not None]
    cens = [p for p in with_first if p[5] is None]
    dur = [(max(0, p[5] - p[4]) if p[5] is not None else math.inf) for p in with_first]
    e1 = 1 - len(with_first) / n if n else None
    e2 = len(cens) / len(with_first) if with_first else None
    traj = {}
    for k in (0, 24, 48, 96):
        ys = []
        for p in with_first:
            t = max(p[4], 0) + k
            if t <= LAST:
                ys.append(ym.get((p[0], p[1], p[2], t), 0.0))
        traj[k] = {"n": len(ys), "p10": q(ys, 0.1), "p50": q(ys, 0.5), "p90": q(ys, 0.9)}
    e3 = traj[48]["p50"]
    by_actor = collections.defaultdict(lambda: [0, 0, [], []])
    for p in pool:
        b = by_actor[p[1]]
        b[0] += 1
        if p[5] is not None:
            b[1] += 1
        if p[4] is not None:
            b[2].append(p[4])
        if p[5] is not None:
            b[3].append(p[5])
    actors = {a: {"pool": v[0], "t07_share": v[1] / v[0], "t_first_p50": q(v[2], 0.5), "t_07_p50": q(v[3], 0.5),
                  "t_first_share": len(v[2]) / v[0]} for a, v in sorted(by_actor.items())}
    big = [v["t07_share"] for v in actors.values() if v["pool"] >= 20]
    h1 = (max(big) - min(big)) if len(big) >= 2 else None
    fin = [d for d in dur if d != math.inf]
    h2 = q(fin, 0.1)
    p50 = q(dur, 0.5)
    pool5, _, _, _ = pool_pairs(nt, rids, TIER5)
    f5 = [p for p in pool5 if p[4] is not None]
    d5 = [(max(0, p[5] - p[4]) if p[5] is not None else math.inf) for p in f5]
    res = {
        "series": name, "runs": len(rids), "pool": n, "not_mastered_pairs": full, "never_known": never, "late_known": late,
        "E1_no_t_first_share": e1, "E2_censored_share": e2, "E3_yield_p50_at_48": e3,
        "escaped": e1 is not None and e1 <= 0.5 and e2 is not None and e2 < 0.5 and e3 is not None and e3 >= Y_ADMIT,
        "H1_t07_share_spread": h1, "H1_ok": h1 is not None and h1 >= 0.2,
        "H2_p10_t07_minus_t_first_months": h2, "H2_ok": h2 is not None and h2 >= 12,
        "c2_tier4_p50_years": None if p50 is None else (p50 / 12.0 if p50 != math.inf else "inf"),
        "tier5": {"pool": len(pool5), "no_t_first_share": (1 - len(f5) / len(pool5)) if pool5 else None,
                  "p50_years": None if not d5 else (q(d5, 0.5) / 12.0 if q(d5, 0.5) != math.inf else "inf"),
                  "censored_share": (sum(1 for x in d5 if x == math.inf) / len(d5)) if d5 else None},
        "trajectory_yield_after_t_first": traj, "actors": actors,
    }
    return res


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("dir")
    ap.add_argument("--json")
    a = ap.parse_args(argv)
    runs, nt, ym, a40 = load(a.dir)
    by = collections.defaultdict(list)
    for rid, r in runs.items():
        if r["status"] == "ok":
            by[r["series"]].append(rid)
    out = {"series": [], "a40": {"%s|%s|%s" % k: v for k, v in sorted(a40.items())}}
    for s in sorted(by):
        rids = sorted(by[s], key=lambda x: int(runs[x]["seed"]))
        if s.startswith("test/") or s.startswith("cal/"):
            out["series"].append(series_stats(s, rids, nt, ym))
    # G-golden: the Lab copy CB must reproduce C-B of Core per seed
    gold = collections.defaultdict(dict)
    for rid, r in runs.items():
        gold[r["series"]][int(r["seed"])] = r["golden_hash"]
    out["copy_equals_core"] = {t: gold.get(t + "/CB") == gold.get(t + "/core") for t in ("test", "cal")}
    text = json.dumps(out, indent=1, sort_keys=True, default=str)
    if a.json:
        with open(a.json, "w") as f:
            f.write(text + "\n")
    for r in out["series"]:
        print("%-10s pool %3d  E1 %.3f  E2 %s  E3 %s  escaped %s  H1 %s  H2 %s  C2 p50 %s  t5 %s" % (
            r["series"], r["pool"], r["E1_no_t_first_share"], None if r["E2_censored_share"] is None else round(r["E2_censored_share"], 3),
            None if r["E3_yield_p50_at_48"] is None else round(r["E3_yield_p50_at_48"], 3), r["escaped"],
            None if r["H1_t07_share_spread"] is None else round(r["H1_t07_share_spread"], 3), r["H2_p10_t07_minus_t_first_months"],
            r["c2_tier4_p50_years"], r["tier5"]["p50_years"]))
    print("copy CB == core golden:", out["copy_equals_core"])
    return 0


if __name__ == "__main__":
    sys.exit(main())
