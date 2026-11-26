#!/usr/bin/env python3
"""TASK-0178: per actor and node, the median month the node became known (node_times.csv t_known) in each series of a matrix, and the median paired
shift against a base series. Shows where a goal rule of the institutes moves the knowledge path (docs/simulation/ai-institute-goals.md).
Usage: python3 lab/analysis/ai_goals_nodes.py DIR [--base LEGACY] [--actor SUN] [--max-month 400] [--min-shift 12]
"""
import argparse
import csv
import os
import re
import statistics
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def node_names():
    names, cur = {}, None
    with open(os.path.join(ROOT, "mods", "core", "nodes.yaml")) as f:
        for line in f:
            m = re.match(r"\s*- id: (\S+)", line)
            if m:
                cur = m.group(1)
            m = re.match(r"\s+number: (\d+)", line)
            if m and cur:
                names[int(m.group(1))] = cur
                cur = None
    return names


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dir")
    ap.add_argument("--base", default="LEGACY")
    ap.add_argument("--actor", default=None)
    ap.add_argument("--max-month", type=int, default=400)
    ap.add_argument("--min-shift", type=int, default=12)
    a = ap.parse_args()
    series = {}
    with open(os.path.join(a.dir, "runs.csv"), newline="") as f:
        for r in csv.DictReader(f):
            series[r["run"]] = (r["series"], int(r["seed"]))
    known = defaultdict(dict)  # (series, actor, node) -> seed -> month (-1 = known at start, None = never)
    with open(os.path.join(a.dir, "node_times.csv"), newline="") as f:
        for r in csv.DictReader(f):
            s, seed = series[r["run"]]
            t = r["t_known"]
            known[(s, r["actor"], int(r["node"]))][seed] = int(t) if t not in ("", None) else None
    names = node_names()
    others = sorted({s for s, _ in series.values()} - {a.base})
    print("| actor | node | " + a.base + " | " + " | ".join(f"{s} (shift)" for s in others) + " |")
    print("|---|---|---" + "|---" * len(others) + "|")
    for (s, actor, node), b in sorted(known.items(), key=lambda kv: (kv[0][1], kv[0][2])):
        if s != a.base or (a.actor and actor != a.actor):
            continue
        bt = [t for t in b.values() if t is not None]
        if not bt or statistics.median(bt) < 0 or statistics.median(bt) > a.max_month:
            continue
        cells, big = [], False
        for o in others:
            v = known.get((o, actor, node), {})
            sh = [v[k] - b[k] for k in b if b[k] is not None and v.get(k) is not None]
            ov = [t for t in v.values() if t is not None]
            if not sh:
                cells.append("never")
                big = True
                continue
            m = statistics.median(sh)
            big |= abs(m) >= a.min_shift
            cells.append(f"{statistics.median(ov):g} ({m:+g})")
        if big:
            print(f"| {actor} | {names.get(node, node)} | {statistics.median(bt):g} | " + " | ".join(cells) + " |")


if __name__ == "__main__":
    main()
