#!/usr/bin/env python3
"""TASK-0185: slot model pilot (docs/simulation/slot-model-pilot.md §1.4). Reads matrix directories of lab/manifests/TASK-0185-slots.json
(one variant per directory: SLOT_* environment of the sandbox) and prints per variant: first unit G2-G4 (any actor and per country, median
year over seeds, bands of #566), slot idle share, frontier per slot by year, tree run-out year, money shortage, hazard slot-months,
direction multipliers. Usage: python3 lab/analysis/slot_pilot.py DIR [DIR ...] [--countries] [--slots]. Exit 1 = a run not ok.
"""
import csv
import glob
import math
import os
import statistics
import sys
from collections import defaultdict

ACTORS = ["USA", "SUN", "GBR", "FRA", "SWE"]
INF = math.inf
BANDS = {2: (-INF, 1963.0), 3: (1944.0, 1972.0), 4: (1957.0, 1986.0)}   # C4 windows +-10 years, upper bound = end of the year
MID = {2: 1949.9, 3: 1957.9, 4: 1971.4}
HORIZON = 2031.0
YEARS = (1950, 1960, 1975, 1990, 2010)


def year_of(month):
    return 1945 + (8 + month) / 12.0


def med(xs):
    xs = sorted(xs)
    return xs[(len(xs) - 1) // 2] if xs else None   # lower median, +inf counted


def pct(xs, q):
    xs = sorted(xs)
    return xs[min(len(xs) - 1, max(0, int(math.floor(q * (len(xs) - 1) + 0.5))))]


def fmt(y):
    return "-" if y is None else ("никогда" if y == INF else f"{y:.1f}")


def read(path):
    if not os.path.exists(path):
        return []
    with open(path, newline="") as f:
        return list(csv.DictReader(f))


def load(d):
    runs = [r for r in read(os.path.join(d, "runs.csv"))]
    bad = [r["run"] for r in runs if r["status"] != "ok"]
    seeds = {}
    for r in runs:
        rd = os.path.join(d, "runs", r["run"])
        fu = {}
        for g in read(os.path.join(rd, "gen_times.csv")):
            v = g["t_first_unit"]
            fu[(g["actor"], int(g["gen"]))] = year_of(int(v)) if v not in ("", "-1") else INF
        tr = read(os.path.join(rd, "slot_trace.csv"))
        seeds[int(r["seed"])] = dict(fu=fu, tr=tr)
    return bad, seeds


def f(x):
    return float(x) if x not in (None, "") else 0.0


def analyse(d):
    bad, seeds = load(d)
    out = dict(name=os.path.basename(d.rstrip("/")), bad=bad, n=len(seeds))
    anyg = {g: [min(s["fu"].get((a, g), INF) for a in ACTORS) for s in seeds.values()] for g in (2, 3, 4)}
    out["any"] = {g: med(v) for g, v in anyg.items()}
    g4 = anyg[4]
    out["g4_spread"] = pct(g4, 0.9) - pct(g4, 0.1) if all(x < INF for x in g4) else INF
    out["g4_reached"] = sum(1 for x in g4 if x < HORIZON)
    out["bands"] = all(out["any"][g] is not None and BANDS[g][0] <= out["any"][g] < BANDS[g][1] for g in (2, 3, 4))
    out["pass_m1"] = out["bands"] and out["g4_spread"] <= 15 and out["g4_reached"] >= 9
    out["dist"] = sum(abs(out["any"][g] - MID[g]) for g in (2, 3, 4)) if all(out["any"][g] < INF for g in (2, 3, 4)) else INF
    lead = 0
    for s in seeds.values():
        first = min(ACTORS, key=lambda a: (s["fu"].get((a, 4), INF), ACTORS.index(a)))
        lead += first in ("USA", "SUN") and s["fu"].get((first, 4), INF) < INF
    out["lead_usa_sun"] = lead
    out["country"] = {(a, g): med([s["fu"].get((a, g), INF) for s in seeds.values()]) for a in ACTORS for g in (2, 3, 4)}
    has = any(s["tr"] for s in seeds.values())
    out["trace"] = has
    if not has:
        return out
    # slot metrics
    agg = defaultdict(lambda: defaultdict(float))
    width = defaultdict(list)
    runout_lt = defaultdict(list)
    runout_all = defaultdict(list)
    bsim = defaultdict(list)
    for s in seeds.values():
        first_lt, first_all = {}, {}
        s_usa = defaultdict(float)
        for r in s["tr"]:
            if r["actor"] == "USA":
                s_usa[int(r["year"])] = f(r["s"])
        for r in s["tr"]:
            y, a, mo = int(r["year"]), r["actor"], f(r["months"])
            per = "1945-1990" if y <= 1990 else "1991-2030"
            for key in (("all", per), (a, per)):
                g = agg[key]
                g["slot_m"] += f(r["slots"]) * mo
                g["fill_m"] += f(r["filled"]) * mo
                g["idle_front"] += f(r["idle_front"]) * mo
                g["short"] += f(r["short_months"])
                g["months"] += mo
                g["unspent"] += f(r["unspent"]) * mo
                g["scale"] += f(r["scale"]) * mo
                g["mult"] += f(r["mult"]) * f(r["filled"]) * mo
                g["zero"] += f(r["zero_overlap"]) * f(r["filled"]) * mo
                g["haz"] += f(r.get("hazard", 0)) * mo
                g["real"] += f(r["real"]) * mo
            if y in YEARS or 1945 <= y <= 1960:
                width[(a, y)].append(f(r["frontier"]) / max(1e-9, f(r["slots"])))
            if 1946 <= y <= 1950 and s_usa[y] > 0:
                bsim[a].append(f(r["s"]) / s_usa[y])
            if a not in first_lt and f(r["frontier"]) < f(r["slots"]):
                first_lt[a] = y
            if a not in first_all and (int(r["known"]) >= 99 or f(r["frontier"]) == 0):
                first_all[a] = y
        for a in ACTORS:
            runout_lt[a].append(first_lt.get(a, INF))
            runout_all[a].append(first_all.get(a, INF))
    out["agg"] = agg
    out["width"] = {k: statistics.median(v) for k, v in width.items()}
    out["w4560"] = {a: statistics.median([statistics.median(width[(a, y)]) for y in range(1946, 1961) if width[(a, y)]]) for a in ACTORS}
    out["runout_lt"] = {a: med(v) for a, v in runout_lt.items()}
    out["runout_all"] = {a: med(v) for a, v in runout_all.items()}
    out["bsim"] = {a: statistics.median(v) for a, v in bsim.items() if v}
    return out


def ratio(g, k, base="slot_m"):
    return g[k] / g[base] if g.get(base) else 0.0


def main():
    dirs = [a for a in sys.argv[1:] if not a.startswith("--")]
    res = [analyse(d) for d in dirs]
    if any(r["bad"] for r in res):
        print("INVALID:", [(r["name"], r["bad"]) for r in res if r["bad"]])
        sys.exit(1)
    print("| вариант | G2 | G3 | G4 | p90−p10 G4 | G4 до 2030 | USA/SUN первые к G4 | M1 | Σ|−середина C4| | простой 45–90 | простой 91–30 | простой при свободном фронте | недостача 45–90 | scale 45–90 | неизрасх. 45–90 | опасные слото-мес. 45–90 | множитель 45–90 | без пересечения 45–90 |")
    print("|---|" + "---|" * 17)
    for r in res:
        line = [r["name"], fmt(r["any"][2]), fmt(r["any"][3]), fmt(r["any"][4]), fmt(r["g4_spread"]), f"{r['g4_reached']}/{r['n']}", f"{r['lead_usa_sun']}/{r['n']}",
                "PASS" if r["pass_m1"] else "FAIL", fmt(r["dist"])]
        if r["trace"]:
            a1, a2 = r["agg"][("all", "1945-1990")], r["agg"][("all", "1991-2030")]
            line += [f"{1 - ratio(a1, 'fill_m'):.1%}", f"{1 - ratio(a2, 'fill_m'):.1%}", f"{a1['idle_front'] + a2['idle_front']:.0f}",
                     f"{ratio(a1, 'short', 'months'):.0%}", f"{ratio(a1, 'scale', 'months'):.2f}", f"{ratio(a1, 'unspent', 'months'):.1%}", f"{ratio(a1, 'haz'):.1%}",
                     f"{ratio(a1, 'mult', 'fill_m'):.3f}", f"{ratio(a1, 'zero', 'fill_m'):.0%}"]
        else:
            line += ["-"] * 9
        print("| " + " | ".join(line) + " |")
    if "--countries" in sys.argv:
        print("\n| вариант | " + " | ".join(f"{a} G{g}" for a in ACTORS for g in (2, 3, 4)) + " |")
        print("|---|" + "---|" * 15)
        for r in res:
            print(f"| {r['name']} | " + " | ".join(fmt(r["country"][(a, g)]) for a in ACTORS for g in (2, 3, 4)) + " |")
    if "--slots" in sys.argv:
        for r in res:
            if not r["trace"]:
                continue
            print(f"\n### {r['name']}\n")
            print("| страна | слотов 45–90 (реальных) | простой 45–90 | простой 91–30 | недостача 45–90 | множитель | без пересечения | опасные | фронт/слот 1946–60 | " + " | ".join(f"фронт/слот {y}" for y in YEARS) + " | фронт < слотов | всё дерево | b_sim 46–50 |")
            print("|---|" + "---|" * (12 + len(YEARS)))
            for a in ACTORS:
                g1, g2 = r["agg"][(a, "1945-1990")], r["agg"][(a, "1991-2030")]
                cells = [a, f"{ratio(g1, 'slot_m', 'months'):.1f} ({ratio(g1, 'real', 'months'):.1f})", f"{1 - ratio(g1, 'fill_m'):.1%}", f"{1 - ratio(g2, 'fill_m'):.1%}",
                         f"{ratio(g1, 'short', 'months'):.0%}", f"{ratio(g1, 'mult', 'fill_m'):.3f}", f"{ratio(g1, 'zero', 'fill_m'):.0%}", f"{ratio(g1, 'haz'):.0%}",
                         f"{r['w4560'][a]:.2f}"]
                cells += [f"{r['width'].get((a, y), float('nan')):.2f}" for y in YEARS]
                cells += [fmt(r["runout_lt"][a]), fmt(r["runout_all"][a]), f"{r['bsim'].get(a, float('nan')):.3f}"]
                print("| " + " | ".join(cells) + " |")


if __name__ == "__main__":
    main()
