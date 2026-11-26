#!/usr/bin/env python3
"""TASK-0178: series against the historical targets themselves, not against LEGACY.
C4 (exp0002.py, knowledge-pacing-proposal.md): first unit of any actor (gen_times.csv t_first_unit) per generation in the window
G2 1947-52, G3 1954-61, G4 1967-75; month t counts from 1945-09 (t = 0), year = 1945 + (8 + t) // 12 (exp0002.year_of), so G2 = months 16-87,
G3 = 100-195, G4 = 256-363. Knowledge side: the first month any actor knows the APP node of the generation (node_times.csv t_known) against its
anchor year in mods/core/nodes.yaml (G1 1944, G2 1947, G3 1953, G4 1972). Median and range over the seeds of each series, count in window.
Usage: python3 lab/analysis/ai_goals_history.py DIR[:SERIES=LABEL,...] [DIR ...]  (default: every series of DIR, labelled DIR:series)
"""
import csv
import os
import statistics
import sys
from collections import defaultdict

WINDOWS = {2: (1947, 1952), 3: (1954, 1961), 4: (1967, 1975)}
APP = {2: (59, 1947), 3: (60, 1953), 4: (61, 1972)}


def year_of(t):
    return 1945 + (8 + t) // 12


def month_of(y):
    return 12 * (y - 1945) - 8


def load(d):
    series = {}
    with open(os.path.join(d, "runs.csv"), newline="") as f:
        for r in csv.DictReader(f):
            series[r["run"]] = r["series"]
    unit = defaultdict(lambda: None)   # (run, gen) -> first unit of any actor
    with open(os.path.join(d, "gen_times.csv"), newline="") as f:
        for r in csv.DictReader(f):
            t = r["t_first_unit"]
            if t in ("", None) or int(t) < 0:
                continue
            k = (r["run"], int(r["gen"]))
            unit[k] = int(t) if unit[k] is None else min(unit[k], int(t))
    app = defaultdict(lambda: None)    # (run, gen) -> first month any actor knows APP(gen)
    nodes = {n: g for g, (n, _) in APP.items()}
    with open(os.path.join(d, "node_times.csv"), newline="") as f:
        for r in csv.DictReader(f):
            g = nodes.get(int(r["node"]))
            t = r["t_known"]
            if g is None or t in ("", None):
                continue
            k = (r["run"], g)
            app[k] = int(t) if app[k] is None else min(app[k], int(t))
    return series, unit, app


def cell(v, lo, hi):
    if not v:
        return "—"
    inside = sum(1 for t in v if t is not None and lo <= t <= hi)
    xs = [t for t in v if t is not None]
    if not xs:
        return "never, 0/%d" % len(v)
    return "%g (%d) [%d..%d], %d/%d" % (statistics.median(xs), year_of(int(statistics.median(xs))), min(xs), max(xs), inside, len(v))


def main():
    rows = []
    for arg in sys.argv[1:]:
        d, _, spec = arg.partition(":")
        pick = dict(x.split("=") for x in spec.split(",")) if spec else None
        series, unit, app = load(d)
        names = sorted(set(series.values()))
        for s in names:
            if pick is not None and s not in pick:
                continue
            label = pick[s] if pick else "%s:%s" % (os.path.basename(os.path.normpath(d)), s)
            runs = [r for r in series if series[r] == s]
            u = {g: [unit[(r, g)] for r in runs] for g in WINDOWS}
            a = {g: [app[(r, g)] for r in runs] for g in APP}
            rows.append((label, u, a, len(runs)))
    print("| rule | " + " | ".join("G%d first unit, window %d-%d (months %d-%d): median (year) [min..max], seeds in window" %
                                     (g, y0, y1, month_of(y0) if month_of(y0) > 0 else 0, month_of(y1) + 11) for g, (y0, y1) in WINDOWS.items())
          + " | items in window (median) |")
    print("|---" * (len(WINDOWS) + 2) + "|")
    for label, u, a, n in rows:
        cells, inside = [], 0
        for g, (y0, y1) in WINDOWS.items():
            lo, hi = max(0, month_of(y0)), month_of(y1) + 11
            cells.append(cell(u[g], lo, hi))
            xs = [t for t in u[g] if t is not None]
            inside += 1 if xs and lo <= statistics.median(xs) <= hi else 0
        print("| %s | %s | %d of %d |" % (label, " | ".join(cells), inside, len(WINDOWS)))
    print()
    print("| rule | " + " | ".join("APP G%d known by any actor (anchor %d, month %d): median (year) [min..max], seeds by anchor year end" %
                                     (g, y, month_of(y) + 11) for g, (_, y) in APP.items()) + " |")
    print("|---" * (len(APP) + 1) + "|")
    for label, u, a, n in rows:
        print("| %s | %s |" % (label, " | ".join(cell(a[g], -10**6, month_of(y) + 11) for g, (_, y) in APP.items())))


if __name__ == "__main__":
    main()
