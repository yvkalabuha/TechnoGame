#!/usr/bin/env python3
"""TASK-0147: 2x2 probe chains (A = 81 nodes, B = 99 nodes) x ahead-of-time cost (off, k02, k05, k12), docs/simulation/g5-g6-probe.md §10.

Reads, for each data axis, the matrix directory and the calib-print directory of lab/manifests/TASK-0147-ahead-probe.json. Per point it
reuses point_metrics of mastery_probe.py (same definitions as §9: P1, P2, years in service, leader) and prints the world-first discovery
years against the targets, the chain-only shifts C1/C2, the G2-G4 APP side shift S2 and the selection of k by the rule of §10.4.

Usage: python3 lab/analysis/ahead_probe.py --a MATRIX PRINT --b MATRIX PRINT [--json OUT]
"""
import argparse
import csv
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from g5g6_probe import fmt, med  # noqa: E402
from mastery_probe import point_metrics, series_names  # noqa: E402
from tm_horizon_2030 import ACTORS, year_of  # noqa: E402

TARGETS = [("76", "G5 APP", (1980, 1985)), ("78", "AESA", (1995, 2000)), ("77", "G6 APP", (2000, 2005)),
           ("79", "CMC", (2008, 2015)), ("81", "adaptive", (2015, 2022))]
PRINT_ONLY = [("80", "CMC fab", (2008, 2015))]
SIDE = [("59", "G2 APP"), ("60", "G3 APP"), ("61", "G4 APP")]
NEVER = 2031
K_POINTS = ["k02", "k05", "k12"]


def first_years(out, node):
    """World-first year per seed; a node never known by 2030 counts as NEVER (§10.3 D)."""
    ys = []
    for o in out:
        t = o["first"].get(node)
        ys.append(year_of(t) if t is not None and t >= 0 else NEVER)
    return ys


def provenance(mdir):
    """Build and data identity of a matrix: one value per field over its ok runs, else a list of problems (Codex review, train 2)."""
    vals, problems, dirty = {}, [], []
    runs = [r for r in rows_csv(os.path.join(mdir, "runs.csv")) if r["status"] == "ok"]
    for r in runs:
        with open(os.path.join(mdir, "runs", r["run"], "run.json")) as f:
            info = json.load(f)
        mi = info.get("model_info", {})
        rec = dict(model=info.get("model"), core_commit=info.get("core_commit"), build_commit=info.get("build_commit"),
                   test_api_version=mi.get("test_api_version"), scenario_hash=mi.get("scenario_hash"), data_hash=mi.get("data_hash"))
        if info.get("dirty") is not False:
            dirty.append(r["run"])
        for k, v in rec.items():
            vals.setdefault(k, set()).add(v)
    prov = {"dirty_runs": len(dirty)}
    for k, s in vals.items():
        if len(s) != 1:
            problems.append(f"{k} differs within the matrix: {sorted(map(str, s))}")
        prov[k] = sorted(map(str, s))[0]
    return prov, problems


def cross_check(provs, same_data, other_data, same_build=True):
    """provs: name -> provenance. Every matrix must share the model, Session surface and scenario (and, with same_build, the core and build
    commits); the names in each group of same_data must have one data_hash; the pairs of other_data must differ (the chains axis)."""
    problems = []
    keys = ["model", "test_api_version", "scenario_hash"] + (["core_commit", "build_commit"] if same_build else [])
    for k in keys:
        s = {n: p[k] for n, p in provs.items()}
        if len(set(s.values())) != 1:
            problems.append(f"{k} differs between matrices: {s}")
    for group in same_data:
        if len({provs[n]["data_hash"] for n in group}) != 1:
            problems.append(f"data_hash differs between {group}: {[provs[n]['data_hash'] for n in group]}")
    for a, b in other_data:
        if provs[a]["data_hash"] == provs[b]["data_hash"]:
            problems.append(f"{a} and {b} have the same data_hash (the chains axis needs two data sets)")
    return problems


def rows_csv(path):
    with open(path, newline="") as f:
        return list(csv.DictReader(f))


def axis(mdir, pdir):
    names = series_names(mdir)
    res, val = {}, []
    for p in sorted(names):
        out, v, _ = point_metrics(mdir, pdir, p, None)
        res[names[p]] = out
        val += [f"{names[p]}: {x}" for x in v]
    dh = sorted({o["x"]["data_hash"] for out in res.values() for o in out})
    if len(dh) != 1:
        val.append(f"data_hash {dh}")
    return res, val, dh


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--a", nargs=2, required=True, metavar=("MATRIX", "PRINT"))
    ap.add_argument("--b", nargs=2, required=True, metavar=("MATRIX", "PRINT"))
    ap.add_argument("--s", nargs=2, metavar=("MATRIX", "PRINT"), help="sensitivity matrix (§10.5a, TASK-0147-ahead-sens.json)")
    ap.add_argument("--json")
    args = ap.parse_args()
    data = {}
    allv = []
    axes = [("A", args.a), ("B", args.b)] + ([("S", args.s)] if args.s else [])
    for lab, (m, p) in axes:
        res, val, dh = axis(m, p)
        data[lab] = res
        allv += [f"{lab} {x}" for x in val]
        first = next(iter(res.values()))[0]["x"]
        print(f"{lab}: core_commit={first['core']} data_hash={dh} manifest_hash={first['manifest_hash']} points={list(res)} seeds={[o['seed'] for o in next(iter(res.values()))]}")
    provs = {lab: provenance(m)[0] for lab, (m, _) in axes}
    allv += [f"{lab} {x}" for lab, (m, _) in axes for x in provenance(m)[1]]
    allv += cross_check(provs, same_data=[["B", "S"]] if "S" in provs else [], other_data=[("A", "B")])
    # dirty = some tracked file differed from HEAD (any file, docs and lab/analysis included); build_commit and data_hash pin what ran, so it is printed, not fatal
    print("dirty runs per matrix:", {lab: p["dirty_runs"] for lab, p in provs.items()})
    print("VALIDITY:", "OK" if not allv else "FAIL " + " | ".join(allv))

    summary = {}
    print("\n## World-first discovery year, median (min…max); never known = 2031")
    print("| Data / point | " + " | ".join(f"{l} ({n}) {lo}–{hi}" for n, l, (lo, hi) in TARGETS + PRINT_ONLY) + " | hits | years outside |")
    print("|---|" + "---|" * (len(TARGETS) + len(PRINT_ONLY) + 2))
    for d in data:
        for pt, out in data[d].items():
            cells, hits, outside, meds = [], 0, 0, {}
            for n, l, (lo, hi) in TARGETS + PRINT_ONLY:
                ys = first_years(out, n)
                m = med(ys)
                meds[n] = m
                never = sum(1 for y in ys if y == NEVER)
                cells.append(f"{fmt(ys, '{:.0f}')}" + (f" [never {never}]" if never else ""))
                if (n, l, (lo, hi)) in TARGETS:
                    hits += lo <= m <= hi
                    outside += max(0, lo - m, m - hi)
            side = {n: med(first_years(out, n)) for n, _ in SIDE}
            summary[f"{d}/{pt}"] = dict(meds=meds, hits=hits, outside=outside, side=side,
                                        g5=med([o["g5"] for o in out]), g4=med([o["g4_great"] for o in out]))
            print(f"| {d}/{pt} | " + " | ".join(cells) + f" | {hits}/5 | {outside:.1f} |")

    print("\n## C1/C2 chain-only shift (B/off - A/off), and chain shift at each k")
    for pt in ["off"] + K_POINTS:
        a, b = summary[f"A/{pt}"]["meds"], summary[f"B/{pt}"]["meds"]
        print(f"- {pt}: G5 APP {b['76'] - a['76']:+.1f} y, AESA {b['78'] - a['78']:+.1f} y" +
              (f" -> C1 {'yes' if b['76'] - a['76'] >= 3 else 'NO'}, C2 {'yes' if b['78'] - a['78'] >= 8 else 'NO'}" if pt == "off" else ""))

    print("\n## S2: world-first year of G2-G4 APP (median) and shift against off of the same data")
    def off_of(d, pt):
        return f"{d}/off" + (pt[pt.index("_"):] if "_" in pt else "")

    for d in data:
        for pt in data[d]:
            s, s0 = summary[f"{d}/{pt}"]["side"], summary[off_of(d, pt)]["side"]
            print(f"- {d}/{pt}: " + ", ".join(f"{l} {s[n]:.0f} ({s[n] - s0[n]:+.1f})" for n, l in SIDE))

    print("\n## P1/P2, leader, in service (as §9.17)")
    for d in data:
        for pt, out in data[d].items():
            n = len(out)
            g5 = [o["g5"] for o in out]
            g4 = [o["g4_great"] for o in out]
            yr = lambda t: None if t is None else year_of(t)
            per = []
            for a in ("USA", "SUN", "FRA", "GBR", "SWE"):
                f4 = [yr(o["fielded"].get((a, 4))) for o in out]
                f5 = [yr(o["fielded"].get((a, 5))) for o in out]
                c4 = sum(1 for v in f4 if v is not None)
                c5 = sum(1 for v in f5 if v is not None)
                per.append(f"{a} G4 {fmt(f4, '{:.0f}')}[{c4}] G5 {fmt(f5, '{:.0f}')}[{c5}]")
            lead = {}
            for o in out:
                lead[o["x"]["lead_final"]] = lead.get(o["x"]["lead_final"], 0) + 1
            usa0 = [o["zero_y"]["USA"] for o in out]
            ins5 = {a: [o["in_service"][(a, 5)] for o in out] for a in ("USA", "SUN", "FRA")}
            print(f"- {d}/{pt}: P1 G5 countries {fmt(g5, '{:.0f}')} [{sum(1 for x in g5 if x >= 2)}/{n}] {'yes' if med(g5) >= 2 else 'NO'}; "
                  f"P2 G4 of 4 great {fmt(g4, '{:.0f}')} [{sum(1 for x in g4 if x >= 3)}/{n}] {'yes' if med(g4) >= 3 else 'NO'}; "
                  f"leader 2030-12 {dict(sorted(lead.items(), key=lambda kv: -kv[1]))}; USA years G=0 1978-2030 {fmt(usa0, '{:.1f}')}; "
                  f"years G>=5 in service " + ", ".join(f"{a} {fmt(v, '{:.1f}')}" for a, v in ins5.items()))
            print("    " + " | ".join(per))
            summary[f"{d}/{pt}"]["g5_year"] = {a: med([yr(o["fielded"].get((a, 5))) for o in out]) for a in ("USA", "SUN", "FRA")}
            summary[f"{d}/{pt}"]["g5_count"] = {a: sum(1 for o in out if o["fielded"].get((a, 5)) is not None) for a in ("USA", "SUN", "FRA")}

    print("\n## Selection of k (§10.4) on B")
    s0 = summary["B/off"]["side"]["61"]
    elig = []
    for pt in K_POINTS:
        s = summary[f"B/{pt}"]
        ok = s["g5"] >= 2 and s["g4"] >= 3 and s["side"]["61"] - s0 <= 5
        print(f"- {pt}: P1 {s['g5']} P2 {s['g4']} G4 APP shift {s['side']['61'] - s0:+.1f} -> {'eligible' if ok else 'not eligible'}; hits {s['hits']}/5; outside {s['outside']:.1f} y")
        if ok:
            elig.append((-s["hits"], s["outside"], K_POINTS.index(pt), pt))
    rec = sorted(elig)[0][3] if elig else None
    print("RECOMMENDED (§10.4):", rec or "none (needs-decision)")
    if args.s:
        speeds = {"x1": ("B", ""), "x0.8": ("S", "_s080"), "x1.25": ("S", "_s125")}

        def at(pt, sp):
            d, suf = speeds[sp]
            return summary[f"{d}/{pt}{suf}"], summary[f"{d}/off{suf}"]

        print("\n## §10.5a sensitivity: median shift against speed x1 of the same point (discovery 76/78/77/79/81; G5 fielded year USA/SUN/FRA [seeds])")
        for pt in ["off"] + K_POINTS:
            base = summary[f"B/{pt}"]
            for sp in ("x0.8", "x1.25"):
                s, _ = at(pt, sp)
                dd = ", ".join(f"{n} {s['meds'][n]:.0f} ({s['meds'][n] - base['meds'][n]:+.1f})" for n, _, _ in TARGETS)
                g5 = ", ".join(f"{a} {s['g5_year'][a] if s['g5_year'][a] is None else round(s['g5_year'][a])}[{s['g5_count'][a]}]"
                               + ("" if s["g5_year"][a] is None or base["g5_year"][a] is None else f" ({s['g5_year'][a] - base['g5_year'][a]:+.1f})")
                               for a in ("USA", "SUN", "FRA"))
                print(f"- {pt} {sp}: {dd}; hits {s['hits']}/5, outside {s['outside']:.1f}; P1 {s['g5']} P2 {s['g4']}; G5 fielded {g5}")
        stat = {}
        for sp in speeds:
            best = max(at(pt, sp)[0]["hits"] for pt in K_POINTS)
            for pt in K_POINTS:
                s, s0 = at(pt, sp)
                ok = s["g5"] >= 2 and s["g4"] >= 3 and s["side"]["61"] - s0["side"]["61"] <= 5
                stat[(pt, sp)] = (ok, s["hits"], s["outside"], best)
                print(f"- eligibility {pt} {sp}: {'eligible' if ok else 'not eligible'}; hits {s['hits']} (best {best}); outside {s['outside']:.1f}")
        robust = rec is not None and all(stat[(rec, sp)][0] and stat[(rec, sp)][1] >= stat[(rec, sp)][3] - 1 for sp in ("x0.8", "x1.25"))
        print("ROBUST (§10.5a):", "yes" if robust else "no")
        if not robust:
            cand = [(-min(stat[(pt, sp)][1] for sp in speeds), max(stat[(pt, sp)][2] for sp in speeds), K_POINTS.index(pt), pt)
                    for pt in K_POINTS if all(stat[(pt, sp)][0] for sp in speeds)]
            print("RECOMMENDED (§10.5a maximin):", sorted(cand)[0][3] if cand else "none (needs-decision)")
    if args.json:
        json.dump(summary, open(args.json, "w"), indent=1)
    return 0 if not allv else 1


if __name__ == "__main__":
    sys.exit(main())
