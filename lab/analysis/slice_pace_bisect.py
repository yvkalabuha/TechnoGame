#!/usr/bin/env python3
"""Slice pace regression (docs/simulation/slice-pace-regression.md): per run directory, the month APP G2/G3/G4 is known
(node_times.t_known; by USA and by any actor) and the first unit of any actor (gen_times.t_first_unit) against the C4 windows.
Medians count a never-known / never-built seed as +inf (censored at the horizon), so a median of 'inf' means more than half censored.
APP node numbers are read from the tree's mods/core/nodes.yaml (type APP, gen 2..4) when --tree is given, else 59/60/61.
Usage: python3 lab/analysis/slice_pace_bisect.py [--tree TREE] LABEL=DIR [LABEL=DIR ...]   (--tree applies to the following dirs)
"""
import csv
import math
import os
import re
import statistics
import sys
from collections import defaultdict

WINDOWS = {2: (16, 87), 3: (100, 195), 4: (256, 363)}
INF = math.inf


def app_nodes(tree):
    if not tree:
        return {59: 2, 60: 3, 61: 4}
    text = open(os.path.join(tree, "mods", "core", "nodes.yaml"), encoding="utf-8").read()
    out = {}
    for block in re.split(r"\n  - id:", text)[1:]:
        num = re.search(r"\n    number: (\d+)", block)
        typ = re.search(r"\n    type: (\w+)", block)
        gen = re.search(r"\n    gen: (\d+)", block)
        if num and typ and gen and typ.group(1) == "APP" and 2 <= int(gen.group(1)) <= 4:
            out[int(num.group(1))] = int(gen.group(1))
    return out


def med(xs):
    if not xs:
        return "-"
    m = statistics.median_low(sorted(xs))
    return "inf" if m == INF else "%g" % m


def summarize(d, tree):
    nodes = app_nodes(tree)
    runs = []
    with open(os.path.join(d, "runs.csv"), newline="") as f:
        runs = [r["run"] for r in csv.DictReader(f)]
    usa = defaultdict(lambda: INF)
    anyk = defaultdict(lambda: INF)
    with open(os.path.join(d, "node_times.csv"), newline="") as f:
        for r in csv.DictReader(f):
            g = nodes.get(int(r["node"]))
            t = r["t_known"]
            if g is None or t in ("", None):
                continue
            t = int(t)
            if t < 0:
                t = 0
            k = (r["run"], g)
            anyk[k] = min(anyk[k], t)
            if r["actor"] == "USA":
                usa[k] = min(usa[k], t)
    unit = defaultdict(lambda: INF)
    with open(os.path.join(d, "gen_times.csv"), newline="") as f:
        for r in csv.DictReader(f):
            t = r["t_first_unit"]
            if t in ("", None) or int(t) < 0:
                continue
            k = (r["run"], int(r["gen"]))
            unit[k] = min(unit[k], int(t))
    row = {"n": len(runs), "app": "/".join(str(n) for n in sorted(nodes))}
    for g in (2, 3, 4):
        row["usaK%d" % g] = med([usa[(r, g)] for r in runs])
        row["anyK%d" % g] = med([anyk[(r, g)] for r in runs])
        u = [unit[(r, g)] for r in runs]
        lo, hi = WINDOWS[g]
        row["unit%d" % g] = "%s %d/%d" % (med(u), sum(1 for t in u if lo <= t <= hi), len(u))
    return row


def main():
    tree = None
    cols = ["n", "app"] + ["usaK%d" % g for g in (2, 3, 4)] + ["anyK%d" % g for g in (2, 3, 4)] + ["unit%d" % g for g in (2, 3, 4)]
    print("| point | " + " | ".join(cols) + " |")
    print("|---" * (len(cols) + 1) + "|")
    args = sys.argv[1:]
    i = 0
    while i < len(args):
        if args[i] == "--tree":
            tree = args[i + 1]
            i += 2
            continue
        label, _, d = args[i].partition("=")
        row = summarize(d, tree)
        print("| %s | %s |" % (label, " | ".join(str(row[c]) for c in cols)))
        i += 1


if __name__ == "__main__":
    main()
