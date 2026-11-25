#!/usr/bin/env python3
"""TASK-0187: the slot science model of the core (docs/simulation/slot-model-pilot.md §7). Reads matrix directories of
lab/manifests/TASK-0187-core.json / TASK-0187-player.json and prints per series: first unit G2-G4 (any actor, median year over seeds,
bands of #566 at +-10 and +-8 years around the C4 windows, p90-p10 of G4, G4 before 2031), per country, and the liveness counters slot_*
of SlotProbe (model_info): AI idle slot-months with a workable frontier node (must be 0), idle share 1945-1990 / 1991-2030, focus months,
frontier per slot, money shortage months. Usage: python3 lab/analysis/slot_core.py DIR [DIR ...] [--countries]. Exit 1 = a run not ok
or a liveness violation.
"""
import csv
import json
import math
import os
import statistics
import sys
from collections import defaultdict

ACTORS = ["USA", "SUN", "GBR", "FRA", "SWE"]
INF = math.inf
BANDS10 = {2: (-INF, 1963.0), 3: (1944.0, 1972.0), 4: (1957.0, 1986.0)}   # C4 windows +-10 years (upper bound = end of the year)
BANDS8 = {2: (-INF, 1961.0), 3: (1946.0, 1970.0), 4: (1959.0, 1984.0)}
HORIZON = 2031.0


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


def inside(y, band):
    return y is not None and band[0] <= y < band[1]


def load(d):
    runs = {}
    with open(os.path.join(d, "runs.csv"), encoding="utf-8") as f:
        for r in csv.DictReader(f):
            runs[r["run"]] = r
    first = defaultdict(dict)   # run -> (actor, gen) -> year
    with open(os.path.join(d, "gen_times.csv"), encoding="utf-8") as f:
        for r in csv.DictReader(f):
            t = int(r["t_first_unit"]) if r["t_first_unit"] not in ("", None) else -1
            first[r["run"]][(r["actor"], int(r["gen"]))] = INF if t < 0 else year_of(t)
    info = {}
    for run in runs:
        with open(os.path.join(d, "runs", run, "run.json"), encoding="utf-8") as f:
            info[run] = json.load(f).get("model_info", {})
    return runs, first, info


def report(d, countries):
    runs, first, info = load(d)
    bad = [r for r, v in runs.items() if v["status"] != "ok"]
    series = defaultdict(list)
    for r, v in runs.items():
        if v["threads"] == "1":
            series[v["series"]].append(r)
    ok = not bad
    print(f"== {d}: {len(runs)} runs, not ok {bad}")
    for s, rs in sorted(series.items()):
        print(f"-- {s} ({len(rs)} seeds)")
        any_first = {g: [min(first[r].get((a, g), INF) for a in ACTORS) for r in rs] for g in (2, 3, 4)}
        cells = []
        for g in (2, 3, 4):
            m = med(any_first[g])
            cells.append(f"G{g} {fmt(m)} [{'±10 ok' if inside(m, BANDS10[g]) else '±10 НЕТ'}; {'±8 ok' if inside(m, BANDS8[g]) else '±8 нет'}] p10-p90 {fmt(pct(any_first[g], 0.1))}-{fmt(pct(any_first[g], 0.9))}")
        g4 = any_first[4]
        spread = pct(g4, 0.9) - pct(g4, 0.1)
        print("  любой актор: " + "; ".join(cells))
        print(f"  G4 p90-p10 {spread:.1f} лет; G4 до 2031: {sum(1 for y in g4 if y < HORIZON)}/{len(g4)}")
        if countries:
            for a in ACTORS:
                print(f"  {a}: " + " / ".join(fmt(med([first[r].get((a, g), INF) for r in rs])) for g in (2, 3, 4)))
        if not any("slot_ai_idle_with_frontier" in info[r] for r in rs):
            print("  (без счётчиков слотов: режим долей)")
            continue
        viol = sum(int(info[r]["slot_ai_idle_with_frontier"]) for r in rs)
        firsts = sorted({info[r]["slot_ai_idle_with_frontier_first"] for r in rs} - {"-1"})
        print(f"  живость: простой ИИ при рабочем фронте = {viol} слото-мес. {firsts if firsts else ''}")
        ok &= viol == 0
        print("  страна: слотов p1 | простой p1 / p2 | простой при фронте (игрок) p1 | фронт/слот p1 / p2 | фокус 1/2 p1 | недостача мес. p1 / p2")
        for a in ACTORS:
            def m(p, k):
                return [int(info[r][f"slot_p{p}_{a}_{k}"]) for r in rs]
            def share(p, k):
                return med([x / y if y else 0.0 for x, y in zip(m(p, k), m(p, "slot_months"))])
            slots = med([x / y for x, y in zip(m(1, "slot_months"), m(1, "months"))])
            fr = [med([x / y if y else 0.0 for x, y in zip(m(p, "frontier"), m(p, "slot_months"))]) for p in (1, 2)]
            sh = [med([x / y for x, y in zip(m(p, "short_months"), m(p, "months"))]) for p in (1, 2)]
            print(f"  {a}: {slots:.2f} | {share(1, 'idle'):.1%} / {share(2, 'idle'):.1%} | {med(m(1, 'idle_free'))} | {fr[0]:.2f} / {fr[1]:.2f} | "
                  f"{med(m(1, 'focus1'))}/{med(m(1, 'focus2'))} | {sh[0]:.0%} / {sh[1]:.0%}")
    return ok


def main(argv):
    countries = "--countries" in argv
    dirs = [a for a in argv if not a.startswith("--")]
    if not dirs:
        print(__doc__)
        return 2
    ok = True
    for d in dirs:
        ok &= report(d, countries)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
