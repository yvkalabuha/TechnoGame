#!/usr/bin/env python3
"""TASK-0147 §10.5c: cap AHEAD_MAX 10/20/30 x per-discipline research weight skew on data B, k 0.12 (docs/simulation/g5-g6-probe.md §10.5c).

Usage: python3 lab/analysis/ahead_ext.py MATRIX PRINT --b MATRIX PRINT   (--b: matrix of TASK-0147-ahead-probe.json on data B, for B/off and B/k12)
"""
import argparse
import csv
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ahead_probe import NEVER, SIDE, TARGETS, PRINT_ONLY, axis, first_years  # noqa: E402
from g5g6_probe import fmt, med  # noqa: E402
from tm_horizon_2030 import year_of  # noqa: E402

SKEWS = ["pm", "mp", "am"]
CAPS = [10, 20, 30]
NODES = TARGETS + PRINT_ONLY


def stats(out):
    yr = lambda t: None if t is None else year_of(t)
    meds = {n: med(first_years(out, n)) for n, _, _ in NODES}
    side = {n: med(first_years(out, n)) for n, _ in SIDE}
    hits = sum(1 for n, _, (lo, hi) in TARGETS if lo <= meds[n] <= hi)
    outside = sum(max(0, lo - meds[n], meds[n] - hi) for n, _, (lo, hi) in TARGETS)
    g5y = {a: med([yr(o["fielded"].get((a, 5))) for o in out]) for a in ("USA", "SUN", "FRA")}
    g5c = {a: sum(1 for o in out if o["fielded"].get((a, 5)) is not None) for a in ("USA", "SUN", "FRA")}
    return dict(meds=meds, side=side, hits=hits, outside=outside, g5=med([o["g5"] for o in out]), g4=med([o["g4_great"] for o in out]),
                g5y=g5y, g5c=g5c, usa0=med([o["zero_y"]["USA"] for o in out]), out=out)


def shift(v, base):
    return "—" if v is None or base is None else f"{v - base:+.1f}"


def golden(mdir):
    g = {}
    with open(os.path.join(mdir, "runs.csv"), newline="") as f:
        for r in csv.DictReader(f):
            g[(r["series"], int(r["seed"]))] = r["golden_hash"]
    return g


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("matrix")
    ap.add_argument("print_dir")
    ap.add_argument("--b", nargs=2, required=True)
    a = ap.parse_args()
    E, val, dh = axis(a.matrix, a.print_dir)
    B, valb, dhb = axis(*a.b)
    allv = val + valb
    if dh != dhb:
        allv.append(f"data_hash {dh} != B {dhb}")
    ge, gb = golden(a.matrix), golden(a.b[0])
    bad = [s for s in range(1, 11) if ge.get(("c10_ref", s)) != gb.get(("k12", s))]
    if bad:
        allv.append(f"c10_ref golden != B/k12 for seeds {bad}")
    first = next(iter(E.values()))[0]["x"]
    print(f"core_commit={first['core']} data_hash={dh} manifest_hash={first['manifest_hash']}")
    print("VALIDITY:", "OK" if not allv else "FAIL " + " | ".join(allv))
    if allv:
        return 1  # no tables or recommendation from an invalid comparison
    S = {k: stats(v) for k, v in E.items()}
    off = stats(B["off"])
    print("\n| Point | " + " | ".join(f"{l} ({n})" for n, l, _ in NODES) + " | G3/G4 APP | hits / outside | P1/P2 | G5 fielded USA/SUN/FRA [seeds] | USA y G=0 |")
    print("|---|" + "---|" * (len(NODES) + 5))
    for k, s in S.items():
        print(f"| {k} | " + " | ".join(fmt(first_years(s['out'], n), '{:.0f}') for n, _, _ in NODES)
              + f" | {s['side']['60']:.0f} / {s['side']['61']:.0f} ({s['side']['61'] - off['side']['61']:+.1f}) | {s['hits']} / {s['outside']:.1f} | {s['g5']:.0f}/{s['g4']:.0f} | "
              + " / ".join(f"{'—' if s['g5y'][c] is None else round(s['g5y'][c])}[{s['g5c'][c]}]" for c in ("USA", "SUN", "FRA")) + f" | {fmt([o['zero_y']['USA'] for o in s['out']], '{:.1f}')} |")
    print("\n## Cap choice (§10.5c)")
    elig = []
    for cap in CAPS:
        s = S[f"c{cap}_ref"]
        ok = s["g5"] >= 2 and s["g4"] >= 3 and s["side"]["61"] - off["side"]["61"] <= 5
        print(f"- cap {cap}: {'eligible' if ok else 'not eligible'}; hits {s['hits']}; outside {s['outside']:.1f}")
        if ok:
            elig.append((-s["hits"], s["outside"], cap))
    cap = sorted(elig)[0][2] if elig else None
    print("CAP CHOSEN:", cap)
    print("\n## Skew shifts against ref of the same cap (median years)")
    robust = {}
    for c in sorted({10, cap} - {None}):
        ref = S[f"c{c}_ref"]
        rb = True
        for sk in SKEWS:
            s = S[f"c{c}_{sk}"]
            ok = s["g5"] >= 2 and s["g4"] >= 3 and s["side"]["61"] - off["side"]["61"] <= 5
            d = {n: s["meds"][n] - ref["meds"][n] for n, _, _ in NODES}
            rb &= ok and abs(d["76"]) <= 3 and abs(d["78"]) <= 3
            g5 = ", ".join(f"{x} {shift(s['g5y'][x], ref['g5y'][x])}" for x in ("USA", "SUN", "FRA"))
            print(f"- cap {c} {sk}: " + ", ".join(f"{n} {v:+.1f}" for n, v in d.items())
                  + f"; G3 {s['side']['60'] - ref['side']['60']:+.1f}, G4 {s['side']['61'] - ref['side']['61']:+.1f}; G5 fielded {g5}; {'eligible' if ok else 'NOT eligible'}")
        robust[c] = rb
        print(f"ROBUST cap {c}: {'yes' if rb else 'no'}")
    rec = cap if cap is not None and robust.get(cap) else (10 if robust.get(10) else None)
    print("RECOMMENDED CAP:", rec if rec else "none (needs-decision)")
    return 0 if not allv else 1


if __name__ == "__main__":
    sys.exit(main())
