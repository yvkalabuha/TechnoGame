#!/usr/bin/env python3
"""TASK-0178: first unit of any actor per generation (gen_times.csv t_first_unit, median and range over seeds) per series, against the historical
windows of knowledge-pacing-proposal.md C4 (G2 1947-52 = months 16-87, G3 1954-61 = 100-195, G4 1967-75 = 256-363), and per actor the median.
Usage: python3 lab/analysis/ai_goals_history.py DIR [DIR ...]  (series of every DIR, labelled DIR:series)
"""
import csv
import os
import statistics
import sys
from collections import defaultdict

WINDOWS = {2: (16, 87), 3: (100, 195), 4: (256, 363)}


def main():
    rows = defaultdict(lambda: defaultdict(list))  # label -> gen -> [first unit of any actor per seed]
    for d in sys.argv[1:]:
        tag = os.path.basename(os.path.normpath(d))
        series = {}
        with open(os.path.join(d, "runs.csv"), newline="") as f:
            for r in csv.DictReader(f):
                series[r["run"]] = (r["series"], int(r["seed"]))
        first = defaultdict(lambda: 10**9)
        with open(os.path.join(d, "gen_times.csv"), newline="") as f:
            for r in csv.DictReader(f):
                t = r["t_first_unit"]
                if t in ("", None) or int(t) < 0:
                    continue
                s, seed = series[r["run"]]
                key = (s, seed, int(r["gen"]))
                first[key] = min(first[key], int(t))
        for (s, seed, g), t in first.items():
            rows[f"{tag}:{s}"][g].append(t)
    print("| series | " + " | ".join(f"G{g} any actor: median [min..max], in window" for g in WINDOWS) + " |")
    print("|---" * (len(WINDOWS) + 1) + "|")
    for label in sorted(rows):
        cells = []
        for g, (lo, hi) in WINDOWS.items():
            v = rows[label][g]
            if not v:
                cells.append("-")
                continue
            inside = sum(1 for t in v if lo <= t <= hi)
            cells.append(f"{statistics.median(v):g} [{min(v)}..{max(v)}], {inside}/{len(v)}")
        print(f"| {label} | " + " | ".join(cells) + " |")


if __name__ == "__main__":
    main()
