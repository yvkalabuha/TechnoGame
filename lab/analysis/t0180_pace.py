#!/usr/bin/env python3
"""TASK-0180 pace measurement (docs/simulation/slice-pace-regression.md, section "TASK-0180 замер"): prints for a run directory
(1) the nodes of a number range that USA knows before APP G4 (node_times.t_known, per seed: known before APP G4 of the same seed, median month),
(2) the mean number of nodes USA knows at a month, and with --ranks NEW OLD (two JetSlice detail-full dirs on the same seeds)
(3) country ranks in Sept 1960 / 1977 (tm_budget_rules.comp_rank on actor_year key_G/T/N/P): seeds with equal rank, median ranks.
Usage: t0180_pace.py nodes TREE DIR [--lo 62] [--hi 99] [--at 201] | t0180_pace.py frontier TREE DIR [--by 201] | t0180_pace.py ranks NEW_DIR OLD_DIR
"""
import csv
import math
import os
import re
import statistics
import sys
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tm_budget_rules as tm  # noqa: E402

INF = math.inf


def names(tree):
    text = open(os.path.join(tree, "mods", "core", "nodes.yaml"), encoding="utf-8").read()
    out = {}
    for block in re.split(r"\n  - id:", text)[1:]:
        num = re.search(r"\n    number: (\d+)", block)
        if num:
            out[int(num.group(1))] = block.split("\n", 1)[0].strip()
    return out


def rows(d, name):
    with open(os.path.join(d, name + ".csv"), newline="") as f:
        return list(csv.DictReader(f))


def nodes(tree, d, lo, hi, at):
    nm = names(tree)
    known = defaultdict(lambda: INF)
    runs = set()
    for r in rows(d, "node_times"):
        if r["actor"] != "USA":
            continue
        runs.add(r["run"])
        if r["t_known"] not in ("", None):
            known[(r["run"], int(r["node"]))] = max(0, int(r["t_known"]))
    runs = sorted(runs)
    app4 = {r: known[(r, 61)] for r in runs}
    print("USA APP G4 per seed:", [app4[r] for r in runs])
    allnodes = sorted({n for (_, n) in known})
    cnt = [sum(1 for n in allnodes if known[(r, n)] <= at) for r in runs]
    print("USA nodes known at month %d: mean %.1f, per seed %s" % (at, sum(cnt) / len(cnt), cnt))
    print("| node | id | USA t_known median | seeds known before APP G4 |")
    print("|---|---|---|---|")
    for n in range(lo, hi + 1):
        ts = [known[(r, n)] for r in runs]
        before = sum(1 for r in runs if known[(r, n)] < app4[r])
        m = statistics.median_low(ts)
        if before:
            print("| %d | %s | %s | %d/%d |" % (n, nm.get(n, "?"), "inf" if m == INF else m, before, len(runs)))


def ranks(d):
    key = {}
    for r in rows(d, "actor_year"):
        key[(r["run"], int(r["t"]), r["actor"])] = (int(r["key_G"]), int(r["key_T"]), int(r["key_N"]), int(r["key_P"]))
    seed = {r["run"]: r["seed"] for r in rows(d, "runs")}
    out = {}
    for run, s in seed.items():
        for y in (1960, 1977):
            t0 = tm.t_sept(y)
            rk = tm.comp_rank({a: key[(run, t0, a)] for a in tm.ACTORS})
            for a in tm.ACTORS:
                out[(s, y, a)] = rk[a]
    return out, sorted(set(seed.values()))


def ranks_cmp(new, old):
    rn, sn = ranks(new)
    ro, so = ranks(old)
    seeds = [s for s in sn if s in so]
    print("| year | actor | rank NEW median | rank OLD median | seeds equal | seeds differ by > 1 |")
    print("|---|---|---|---|---|---|")
    for y in (1960, 1977):
        for a in tm.ACTORS:
            eq = sum(1 for s in seeds if rn[(s, y, a)] == ro[(s, y, a)])
            far = sum(1 for s in seeds if abs(rn[(s, y, a)] - ro[(s, y, a)]) > 1)
            print("| %d | %s | %g | %g | %d/%d | %d |" % (y, a, statistics.median(rn[(s, y, a)] for s in seeds),
                                                      statistics.median(ro[(s, y, a)] for s in seeds), eq, len(seeds), far))


def reqs(tree):
    text = open(os.path.join(tree, "mods", "core", "nodes.yaml"), encoding="utf-8").read()
    ids, out = {}, {}
    for block in re.split(r"\n  - id:", text)[1:]:
        num = re.search(r"\n    number: (\d+)", block)
        if not num:
            continue
        n = int(num.group(1))
        ids[block.split("\n", 1)[0].strip()] = n
        r = re.search(r"\n    requires: \[([^\]]*)\]", block)
        out[n] = [x.strip() for x in r.group(1).split(",") if x.strip()] if r else None
    return {n: (None if v is None else [ids.get(x) for x in v]) for n, v in out.items()}


def frontier(tree, d, lo, hi, by):
    """Month a node enters the USA frontier (all flat requires known; None = requires not a flat list), median over seeds; nodes entering before 'by'."""
    nm, rq = names(tree), reqs(tree)
    known = defaultdict(lambda: INF)
    runs = set()
    for r in rows(d, "node_times"):
        if r["actor"] == "USA":
            runs.add(r["run"])
            if r["t_known"] not in ("", None):
                known[(r["run"], int(r["node"]))] = max(0, int(r["t_known"]))
    runs = sorted(runs)
    print("| node | id | enters frontier (median) | known (median) | seeds entering before month %d |" % by)
    print("|---|---|---|---|---|")
    for n in range(lo, hi + 1):
        if rq.get(n) is None or None in rq[n]:
            continue
        ent = [max([known[(r, p)] for p in rq[n]], default=0) for r in runs]
        k = [known[(r, n)] for r in runs]
        c = sum(1 for e in ent if e < by)
        if c:
            f = lambda v: "inf" if v == INF else "%g" % v
            print("| %d | %s | %s | %s | %d/%d |" % (n, nm[n], f(statistics.median_low(ent)), f(statistics.median_low(k)), c, len(runs)))


def main():
    a = sys.argv[1:]
    if a[0] == "nodes":
        opt = {a[i]: int(a[i + 1]) for i in range(3, len(a), 2)}
        nodes(a[1], a[2], opt.get("--lo", 62), opt.get("--hi", 99), opt.get("--at", 201))
    elif a[0] == "frontier":
        opt = {a[i]: int(a[i + 1]) for i in range(3, len(a), 2)}
        frontier(a[1], a[2], opt.get("--lo", 62), opt.get("--hi", 99), opt.get("--by", 201))
    elif a[0] == "ranks":
        ranks_cmp(a[1], a[2])


if __name__ == "__main__":
    main()
