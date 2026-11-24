#!/usr/bin/env python3
"""TASK-0134 step 4: probe of Hcap(t) growth and production mastery, branches ref/B0/B1/B2 grid/B3 (docs/simulation/g5-g6-probe.md §9).

Reads the matrix directory and the calib-print directory of lab/manifests/TASK-0134-mastery-probe.json. Per point it reuses analyse() of
g5g6_probe.py (same definitions as the TASK-0133 probe: T1-T6, leader = largest share of months 1978-2030 in summary.lead_all) and adds
P2 (G4 fielded in USA, SUN, FRA, GBR), P6 (SWE not leader), the years of G4/G5/G6 in service per country, and the validity rule P7
(rows with t <= 387 of every CSV with a t column, and fielded_key months <= 387, equal across all points for each seed).

Usage: python3 lab/analysis/mastery_probe.py MATRIX_DIR PRINT_DIR [--expect-data-hash H] [--k2b K2B_CSV]
"""
import argparse
import csv
import hashlib
import json
import os
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from g5g6_probe import analyse, fmt, med, rows  # noqa: E402
from tm_horizon_2030 import ACTORS, T_1978, year_of  # noqa: E402

GREAT4 = ["USA", "SUN", "FRA", "GBR"]
LAST_1977 = T_1978 - 1  # month 387 = 1977-12
MONTHS = 1024


def g_monthly(pdir, point):
    g = {}
    for r in rows(os.path.join(pdir, "fielded_key.csv")):
        if int(r["point"]) == point and int(r["month"]) >= 0:
            g[(int(r["seed"]), r["actor"], int(r["month"]))] = int(r["g_l"])
    return g


# Columns of a row at month t that describe later months (format §4.2, §4.3, §4.14): an end month after 1977-12 or none (open at the horizon)
# is printed as LATE, and a
# row of actor_year sums the months t…t+11 (volume, eng_short_months), so it counts only when t + 11 <= 387 (pilot deviation, §9.6).
LATE_COLUMNS = {"closed_t", "t_end"}
SPAN11 = {"actor_year.csv"}


# Columns of a row at month t that describe later months (format §4.2, §4.3, §4.14): an end month after 1977-12 or none (open at the horizon)
# is printed as LATE, and a row of actor_year sums the months t…t+11 (volume, eng_short_months), so it counts only when t + 11 <= 387
# (pilot deviation, g5-g6-probe.md §9.6).
LATE_COLUMNS = {"closed_t", "t_end"}
SPAN11 = {"actor_year.csv"}


def early_digest(run_dir, pdir, point, seed):
    """Digest of everything printed for months <= 387: rows of CSVs with a t column (run column dropped) and fielded_key rows."""
    h = hashlib.sha256()
    for name in sorted(os.listdir(run_dir)):
        if not name.endswith(".csv") or name == "runs.csv":
            continue
        with open(os.path.join(run_dir, name), newline="") as f:
            rd = csv.reader(f)
            head = next(rd, None)
            if not head or "t" not in head:
                continue
            ti = head.index("t")
            keep = [i for i, c in enumerate(head) if c != "run"]
            h.update(("#" + name + ":" + ",".join(head[i] for i in keep) + "\n").encode())
            last = LAST_1977 - 11 if name in SPAN11 else LAST_1977
            late = {i for i, c in enumerate(head) if c in LATE_COLUMNS}
            for r in rd:
                if r[ti] != "" and int(r[ti]) <= last:
                    cells = ["LATE" if i in late and (r[i] == "" or int(r[i]) > LAST_1977) else r[i] for i in keep]
                    h.update((",".join(cells) + "\n").encode())
    with open(os.path.join(pdir, "fielded_key.csv"), newline="") as f:
        rd = csv.DictReader(f)
        h.update(("#fielded_key:" + ",".join(c for c in rd.fieldnames if c not in ("point", "run")) + "\n").encode())
        for r in rd:
            if int(r["point"]) == point and int(r["seed"]) == seed and int(r["month"]) <= LAST_1977:
                h.update((",".join(v for c, v in r.items() if c not in ("point", "run")) + "\n").encode())
    return h.hexdigest()


def point_metrics(mdir, pdir, point, expect):
    out, validity, R = analyse(mdir, pdir, point, expect)
    g = g_monthly(pdir, point)
    for o in out:
        s = o["seed"]
        o["g4_great"] = sum(1 for a in GREAT4 if o["fielded"].get((a, 4)) is not None)
        o["swe_final"] = o["x"]["lead_final"] == "SWE"
        o["swe_share"] = o["x"]["lead_share"]["SWE"]
        o["in_service"] = {(a, gg): sum(1 for t in range(T_1978, MONTHS) if g[(s, a, t)] >= gg) / 12 for a in ACTORS for gg in (4, 5, 6)}
        o["dead_big3"] = {a: o["x"]["dead"][a] for a in ("USA", "SUN", "FRA")}
    return out, validity, R


def series_names(mdir):
    names = {}
    for r in rows(os.path.join(mdir, "runs.csv")):
        names[int(r["point"])] = r["series"]
    return names


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("matrix_dir")
    ap.add_argument("print_dir")
    ap.add_argument("--expect-data-hash")
    ap.add_argument("--json", help="write per-point medians here")
    args = ap.parse_args()
    names = series_names(args.matrix_dir)
    res, val = {}, {}
    for p in sorted(names):
        out, v, R = point_metrics(args.matrix_dir, args.print_dir, p, args.expect_data_hash)
        res[p], val[p] = out, v
    # P7: early months equal across points per seed
    runs = [r for r in rows(os.path.join(args.matrix_dir, "runs.csv")) if r["threads"] == "1" and r["status"] == "ok"]
    dig = {}
    for r in runs:
        dig.setdefault(int(r["seed"]), {})[int(r["point"])] = early_digest(os.path.join(args.matrix_dir, "runs", r["run"]), args.print_dir, int(r["point"]), int(r["seed"]))
    p7 = [f"seed {s}: {len(set(d.values()))} distinct digests of months <= 387 over {len(d)} points" for s, d in sorted(dig.items()) if len(set(d.values())) != 1]
    first = res[min(res)][0]["x"]
    print(f"core_commit={first['core']} data_hash={first['data_hash']} manifest_hash={first['manifest_hash']} seeds={[o['seed'] for o in res[min(res)]]}")
    allv = [f"point {p} ({names[p]}): {x}" for p, v in val.items() for x in v] + p7
    print("VALIDITY:", "OK" if not allv else "FAIL " + " | ".join(allv))
    print("P7 1945-1977 rows equal across points:", "OK" if not p7 else "FAIL")
    yr = lambda t: None if t is None else year_of(t)
    summary = {}
    print("\n| Point | P1 G5 countries (seeds>=2) | P2 G4 of 4 great (seeds>=3) | P3 leader last node (>=2005) | P4 dead USA/SUN/FRA | P5 G=0 years leader / USA | P6 SWE final leader / share | G in 2030-12 USA |")
    print("|---|---|---|---|---|---|---|---|")
    for p in sorted(res):
        out = res[p]
        n = len(out)
        g5 = [o["g5"] for o in out]
        g4 = [o["g4_great"] for o in out]
        t1 = [yr(o["x"]["last_node"][o["leader"]]) for o in out]
        dead = {c: [o["dead_big3"][c] for o in out] for c in ("USA", "SUN", "FRA")}
        z_lead = [o["zero_y"][o["leader"]] for o in out]
        z_usa = [o["zero_y"]["USA"] for o in out]
        swe_f = sum(1 for o in out if o["swe_final"])
        swe_s = [o["swe_share"] for o in out]
        P1 = med(g5) >= 2
        P2 = med(g4) >= 3
        P3 = (med(t1) or 0) >= 2005
        P4 = all(med(dead[c]) < 0.8 for c in dead)
        P6 = swe_f <= n // 10 and med(swe_s) == 0
        summary[names[p]] = dict(P1=P1, P2=P2, P3=P3, P4=P4, P6=P6, g5=med(g5), g4=med(g4), z_lead=med(z_lead), z_usa=med(z_usa))
        ok = lambda b: "**yes**" if b else "no"
        print(f"| {names[p]} | {fmt(g5, '{:.0f}')} {ok(P1)} [{sum(1 for x in g5 if x >= 2)}/{n}] | {fmt(g4, '{:.0f}')} {ok(P2)} [{sum(1 for x in g4 if x >= 3)}/{n}] | "
              f"{fmt(t1, '{:.0f}')} {ok(P3)} [{sum(1 for y in t1 if y and y >= 2005)}/{n}] | "
              f"{' / '.join(fmt(dead[c]) for c in dead)} {ok(P4)} | {fmt(z_lead, '{:.1f}')} / {fmt(z_usa, '{:.1f}')} | "
              f"{swe_f}/{n} / {fmt(swe_s)} {ok(P6)} | {[o['g2030']['USA'] for o in out]} |")
    print("\n## Years per country (median over seeds; [seeds with a value]): G4 / G5 / G6 first fielded year; years in service 1978-2030 with G >= 4 / >= 5")
    for p in sorted(res):
        out = res[p]
        cells = []
        for a in ACTORS:
            f = lambda gg: [yr(o["fielded"].get((a, gg))) for o in out]
            cnt = lambda gg: sum(1 for o in out if o["fielded"].get((a, gg)) is not None)
            cells.append(f"{a} {fmt(f(4), '{:.0f}')}[{cnt(4)}] / {fmt(f(5), '{:.0f}')}[{cnt(5)}] / {fmt(f(6), '{:.0f}')}[{cnt(6)}]; "
                         f"{fmt([o['in_service'][(a, 4)] for o in out], '{:.1f}')} / {fmt([o['in_service'][(a, 5)] for o in out], '{:.1f}')} y")
        print(f"- {names[p]}: " + " | ".join(cells))
    print("\n## Leaders: share of months 1978-2030 and 2030-12 leader per seed")
    for p in sorted(res):
        out = res[p]
        print(f"- {names[p]}: leader(share) {[o['leader'] for o in out]}; 2030-12 {[o['x']['lead_final'] for o in out]}; "
              f"G=0 share 1990-2030 leader {fmt([o['zero_share_90'][o['leader']] for o in out])}; "
              f"G=0 years " + ", ".join(f"{a} {fmt([o['zero_y'][a] for o in out], '{:.1f}')}" for a in ACTORS))
    if args.json:
        json.dump(summary, open(args.json, "w"), indent=1, default=str)
    return 0 if not allv else 1


if __name__ == "__main__":
    sys.exit(main())
