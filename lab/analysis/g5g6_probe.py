#!/usr/bin/env python3
"""TASK-0133 probe of the G5/G6 data (O-038): thresholds T1-T7 and calibration targets of docs/simulation/g5-g6-probe.md.

Reads a matrix directory (lab/manifests/TASK-0133-g5g6-probe.json or a calibration manifest of the same shape) and the calib-print
directory of the same manifest point (fielded_key.csv: monthly G by fielded_L per actor; fielded_runs.csv: hash_final = golden_hash).
Uses analyse_run of tm_horizon_2030.py for the narrow dead-time metric, ranks and leader shares, so the numbers are comparable to
the TM-2030 baseline on the 61-node data.

Usage: python3 lab/analysis/g5g6_probe.py MATRIX_DIR PRINT_DIR [--point P] [--expect-data-hash H]
"""
import argparse
import csv
import json
import os
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tm_horizon_2030 import ACTORS, T_1978, analyse_run, year_of, t_sept  # noqa: E402

T_1990 = 12 * (1990 - 1945) - 8  # 1990-01
MONTHS = 1024
BIG3 = ["USA", "SUN", "FRA"]
TARGETS = [  # node number as in node_times.node, label, target window of the world-first year (slice-g5-g6-content.md rev 2 §6)
    ("76", "G5 APP (76)", (1980, 1985)),
    ("78", "AESA (78)", (1995, 2000)),
    ("77", "G6 APP (77)", (2000, 2005)),
    ("79", "CMC (79)", (2008, 2015)),
    ("80", "CMC fab (80)", (2008, 2015)),
    ("81", "adaptive (81)", (2015, 2022)),
]


def rows(path):
    with open(path, newline="") as f:
        return list(csv.DictReader(f))


def num(v):
    return None if v in ("", None) else int(v)


def med(vals):
    vals = [v for v in vals if v is not None]
    return statistics.median(vals) if vals else None


def fmt(vals, f="{:.2f}"):
    vals = [v for v in vals if v is not None]
    if not vals:
        return "—"
    m, lo, hi = statistics.median(vals), min(vals), max(vals)
    return f.format(m) if lo == hi else f"{f.format(m)} ({f.format(lo)}…{f.format(hi)})"


def analyse(mdir, pdir, point, expect_data_hash):
    runs = [r for r in rows(os.path.join(mdir, "runs.csv")) if point is None or int(r["point"]) == point]
    matrix = json.load(open(os.path.join(mdir, "matrix.json")))
    validity = []
    bad = [r["run"] for r in runs if r["status"] != "ok"]
    if bad:
        validity.append(f"runs not ok: {bad}")
    if matrix.get("thread_mismatch"):
        validity.append(f"thread_mismatch: {matrix.get('thread_mismatch')}")
    if matrix.get("valid") is not True:
        validity.append(f"matrix valid = {matrix.get('valid')}")
    one = [r for r in runs if r["threads"] == "1" and r["status"] == "ok"]
    gold = {}
    for r in runs:
        if r["status"] == "ok":
            gold.setdefault(int(r["seed"]), set()).add(r["golden_hash"])
    if any(len(v) != 1 for v in gold.values()):
        validity.append("threads 1 and 4 give different golden_hash")
    R = [analyse_run(os.path.join(mdir, "runs", r["run"])) for r in one]
    if any(x["months"] != MONTHS for x in R):
        validity.append(f"months {sorted(set(x['months'] for x in R))}")
    # model_info.data_hash is GameData.DataHash (DataHashTests.CoreDataHash_IsFrozen); run.json data_hash is the Lab file hash
    dh = sorted(set(json.load(open(os.path.join(mdir, "runs", r["run"], "run.json")))["model_info"]["data_hash"] for r in one))
    if len(dh) != 1 or (expect_data_hash and not dh[0].startswith(expect_data_hash)):
        validity.append(f"model_info.data_hash {dh}, expected {expect_data_hash}")
    # monthly G from calib-print; its hash_final must be the golden of the same seed
    ph = {int(r["seed"]): r["hash_final"] for r in rows(os.path.join(pdir, "fielded_runs.csv"))
          if point is None or int(r["point"]) == point}
    for x in R:
        if ph.get(x["seed"]) != x["golden"]:
            validity.append(f"seed {x['seed']}: calib-print hash {ph.get(x['seed'])} != golden {x['golden']}")
    g = {}
    for r in rows(os.path.join(pdir, "fielded_key.csv")):
        if point is not None and int(r["point"]) != point:
            continue
        t = int(r["month"])
        if t >= 0:
            g[(int(r["seed"]), r["actor"], t)] = int(r["g_l"])
    for x in R:
        if any((x["seed"], a, t) not in g for a in ACTORS for t in range(MONTHS)):
            validity.append(f"seed {x['seed']}: fielded_key misses months")
    out = []
    for x in R:
        s = x["seed"]
        d = os.path.join(mdir, "runs", [r["run"] for r in one if int(r["seed"]) == s][0])
        nodes = rows(os.path.join(d, "node_times.csv"))
        gen = rows(os.path.join(d, "gen_times.csv"))
        leader = max(ACTORS, key=lambda a: (x["lead_share"][a], -ACTORS.index(a)))
        first = {}
        for r in nodes:
            tk = num(r["t_known"])
            if tk is not None:
                first[r["node"]] = min(first.get(r["node"], tk), tk)
        new_after_1978 = sum(1 for v in first.values() if v >= T_1978)
        fielded = {(r["actor"], int(r["gen"])): num(r["t_fielded"]) for r in gen}
        req = {(r["actor"], int(r["gen"])): num(r["t_req"]) for r in gen}
        unit = {(r["actor"], int(r["gen"])): num(r["t_first_unit"]) for r in gen}
        g5_by_2030 = sum(1 for a in ACTORS if fielded.get((a, 5)) is not None)
        g6_by_2030 = sum(1 for a in ACTORS if fielded.get((a, 6)) is not None)
        zero_y = {a: sum(1 for t in range(T_1978, MONTHS) if g[(s, a, t)] == 0) / 12 for a in ACTORS}
        zero_share_90 = {a: sum(1 for t in range(T_1990, MONTHS) if g[(s, a, t)] == 0) / (MONTHS - T_1990) for a in ACTORS}
        after_g4 = {}
        for a in ACTORS:
            t4 = next((t for t in range(MONTHS) if g[(s, a, t)] >= 4), None)
            if t4 is not None and t4 <= T_1990 + 11:  # G4 in service by 1990-12
                after_g4[a] = sum(1 for t in range(t4, MONTHS) if g[(s, a, t)] == 0) / 12
        out.append(dict(seed=s, leader=leader, x=x, first=first, new_after_1978=new_after_1978, fielded=fielded,
                        req=req, unit=unit, g5=g5_by_2030, g6=g6_by_2030, zero_y=zero_y, zero_share_90=zero_share_90, after_g4=after_g4,
                        g2030={a: g[(s, a, MONTHS - 1)] for a in ACTORS}))
    return out, validity, R


def report(out, validity, R, title):
    p = print
    p(f"# {title}")
    p(f"seeds={[o['seed'] for o in out]} core_commit={R[0]['core']} data_hash={R[0]['data_hash']} manifest_hash={R[0]['manifest_hash']}")
    p("golden:", {o["seed"]: o["x"]["golden"][:12] for o in out})
    p("VALIDITY:", "OK" if not validity else "FAIL " + "; ".join(validity))
    yr = lambda t: None if t is None else year_of(t)
    p("\n## Calibration targets: world-first year (any actor), median (min…max), seeds never known counted as missing")
    for nid, lab, (lo, hi) in TARGETS:
        ys = [yr(o["first"].get(nid)) if o["first"].get(nid, -2) >= 0 else None for o in out]
        miss = sum(1 for y in ys if y is None)
        p(f"| {lab} | {fmt(ys, '{:.0f}')} | target {lo}–{hi} | never known by 2030 in {miss}/{len(out)} seeds |")
    p("\n## Thresholds")
    t1 = [yr(o["x"]["last_node"][o["leader"]]) for o in out]
    p(f"T1 leader last new node year: {fmt(t1, '{:.0f}')}; seeds >= 2005: {sum(1 for y in t1 if y and y >= 2005)}/{len(out)}; "
      f"leaders {[o['leader'] for o in out]}; USA {fmt([yr(o['x']['last_node']['USA']) for o in out], '{:.0f}')}")
    p(f"T2 actors with G5 fielded by 2030-12: {fmt([o['g5'] for o in out], '{:.0f}')}; seeds >= 2: {sum(1 for o in out if o['g5'] >= 2)}/{len(out)}; "
      f"G6: {fmt([o['g6'] for o in out], '{:.0f}')}")
    for a in ACTORS:
        p(f"T3 dead narrow 1978-2030 {a}: {fmt([o['x']['dead'][a] for o in out])}")
    for a in ACTORS:
        p(f"T4 years with G=0 1978-2030 {a}: {fmt([o['zero_y'][a] for o in out], '{:.1f}')}")
    for a in ACTORS:
        v = [o["after_g4"].get(a) for o in out]
        p(f"T5 years with G=0 after reaching G4 (G4 by 1990) {a}: {fmt(v, '{:.1f}')} in {sum(1 for x in v if x is not None)} seeds")
    for a in ACTORS:
        p(f"T6 share of months 1990-2030 with G=0 {a}: {fmt([o['zero_share_90'][a] for o in out])}")
    p(f"T6 leader: {fmt([o['zero_share_90'][o['leader']] for o in out])}; T4 leader years: {fmt([o['zero_y'][o['leader']] for o in out], '{:.1f}')}")
    p("\n## Context")
    p(f"world-new nodes after 1977 per seed: {[o['new_after_1978'] for o in out]}")
    p(f"world last new node year per seed: {[yr(o['x']['world_last_node']) for o in out]}")
    for a in ACTORS:
        p(f"G in 2030-12 {a}: {[o['g2030'][a] for o in out]}; first G5 fielded year: {fmt([yr(o['fielded'].get((a, 5))) for o in out], '{:.0f}')}; "
          f"G6: {fmt([yr(o['fielded'].get((a, 6))) for o in out], '{:.0f}')}; last new node {fmt([yr(o['x']['last_node'][a]) for o in out], '{:.0f}')}")
    for gg in (4, 5, 6):
        p(f"G{gg} t_req year / first unit year / fielded year per actor (median; seeds with a value): " + "; ".join(
            f"{a} {fmt([yr(o['req'].get((a, gg))) for o in out], '{:.0f}')} / {fmt([yr(o['unit'].get((a, gg))) for o in out], '{:.0f}')} / "
            f"{fmt([yr(o['fielded'].get((a, gg))) for o in out], '{:.0f}')} [{sum(1 for o in out if o['req'].get((a, gg)) is not None)}]" for a in ACTORS))
    p(f"leader in 2030-12 per seed: {[o['x']['lead_final'] for o in out]}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("matrix_dir")
    ap.add_argument("print_dir")
    ap.add_argument("--point", type=int)
    ap.add_argument("--expect-data-hash")
    a = ap.parse_args()
    out, validity, R = analyse(a.matrix_dir, a.print_dir, a.point, a.expect_data_hash)
    report(out, validity, R, f"{a.matrix_dir} point {a.point}")
    return 0 if not validity else 1


if __name__ == "__main__":
    sys.exit(main())
