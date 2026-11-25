#!/usr/bin/env python3
"""TASK-0173: institutes P-7 (build everything early), P-8 finish dates, project limit, stretched project premium.

Reads the matrix directory of lab/manifests/TASK-0173-institutes-p7.json and prints Markdown (docs/simulation/institutes-p7-measurement.md §1-4).
Usage: python3 lab/analysis/institutes_p7.py DIR. Exit 1 = a validity rule failed (a run not ok, a script refusal).
"""
import csv
import json
import os
import statistics
import sys
from collections import defaultdict

PLAYERS = ["USA", "GBR", "FRA"]
ARMS = ["HIST", "EARLY", "HIST_M5", "HIST_P5", "NOBUILD", "NOGOAL", "EARLY_F35"]
# Key nodes (§1): generation-defining nodes G2-G5 of engine, airframe, hot alloy and the fighter.
KEY = {19: "turbojet_g2", 23: "turbojet_g3", 28: "turbofan_g4", 68: "turbofan_g5",
       20: "swept_wing_airframe", 25: "delta_wing_airframe", 29: "mach2_airframe_g4", 69: "composite_airframe",
       40: "superalloy_melt_g2", 12: "gamma_prime_alloy_design", 41: "vacuum_induction_melt", 73: "single_crystal_casting",
       59: "jet_fighter_g2", 60: "jet_fighter_g3", 61: "jet_fighter_g4", 76: "jet_fighter_g5"}
MONTHS = 664
CENSOR = 1945 + (8 + MONTHS) / 12


def year(t):
    return 1945 + (8 + t) / 12


def rows(d, name):
    p = os.path.join(d, name + ".csv")
    with open(p, newline="") as f:
        return list(csv.DictReader(f))


def q(xs, p):
    xs = sorted(xs)
    if not xs:
        return float("nan")
    k = (len(xs) - 1) * p
    lo = int(k)
    hi = min(lo + 1, len(xs) - 1)
    return xs[lo] + (xs[hi] - xs[lo]) * (k - lo)


def main(d):
    runs = rows(d, "runs")
    info = {r["run"]: r for r in runs}
    bad = [r["run"] for r in runs if r["status"] != "ok"]
    known = {}  # (run, actor, node) -> year (censored)
    for r in rows(d, "node_times"):
        t = r["t_known"]
        y = CENSOR if t == "" else (1945 + 8 / 12 if int(t) < 0 else year(int(t)))
        known[(r["run"], r["actor"], int(r["node"]))] = (y, t != "")
    by = {}  # (series, seed) -> run
    for r in runs:
        by[(r["series"], int(r["seed"]))] = r["run"]
    seeds = sorted({int(r["seed"]) for r in runs})
    refused = 0
    mi = {}
    for r in runs:
        with open(os.path.join(d, "runs", r["run"], "run.json")) as f:
            mi[r["run"]] = json.load(f).get("model_info") or {}
        refused += int(mi[r["run"]].get("inst_refused", "0"))
    out = [f"# TASK-0173 institutes P-7 ({len(runs)} runs, seeds {seeds[0]}-{seeds[-1]}, not ok: {len(bad)}, refused script orders: {refused})", ""]

    def key_mean(run, actor):
        return statistics.mean(known[(run, actor, n)][0] for n in KEY)

    # §1 P-7
    out += ["## P-7: mean year of the key nodes (gain = arm − EARLY, years; > 0 = the early path is faster)", "",
            "| actor | vs | median | p10 | p90 | max | seeds gain > 1 | mean HIST | mean EARLY | verdict |", "|---|---|---|---|---|---|---|---|---|---|"]
    verdict = {}
    for a in PLAYERS:
        for ref in ["HIST", "HIST_M5", "HIST_P5", "NOBUILD"]:
            g, mh, me = [], [], []
            for s in seeds:
                rh, re_ = by.get((f"{a}_{ref}", s)), by.get((f"{a}_EARLY", s))
                if rh and re_:
                    h, e = key_mean(rh, a), key_mean(re_, a)
                    g.append(h - e)
                    mh.append(h)
                    me.append(e)
            med = statistics.median(g)
            v = "PASS" if med <= 1.0 else "FAIL"
            if ref == "HIST":
                verdict[a] = v
            out.append(f"| {a} | {ref} | {med:+.2f} | {q(g, .1):+.2f} | {q(g, .9):+.2f} | {max(g):+.2f} | {sum(1 for x in g if x > 1)}/{len(g)} | "
                       f"{statistics.mean(mh):.2f} | {statistics.mean(me):.2f} | {v if ref == 'HIST' else '—'} |")
    out.append("")
    # per key node, pooled over players: median paired difference HIST − EARLY (months)
    out += ["Per key node, HIST − EARLY, months (pooled USA/GBR/FRA × seeds): median, p10, p90, share of pairs ≠ 0", "",
            "| node | median | p10 | p90 | ≠ 0 |", "|---|---|---|---|---|"]
    for n, name in KEY.items():
        dd = []
        for a in PLAYERS:
            for s in seeds:
                rh, re_ = by.get((f"{a}_HIST", s)), by.get((f"{a}_EARLY", s))
                if rh and re_:
                    dd.append(12 * (known[(rh, a, n)][0] - known[(re_, a, n)][0]))
        out.append(f"| {n} {name} | {statistics.median(dd):+.1f} | {q(dd, .1):+.1f} | {q(dd, .9):+.1f} | {sum(1 for x in dd if abs(x) > 1e-9)}/{len(dd)} |")
    out.append("")
    # sanity: the scripted HIST player against the AI playing the same actor
    out += ["Check: scripted HIST against the AI on the same actor (mean key year, HIST − AI): median, p10, p90", ""]
    for a in PLAYERS:
        g = [key_mean(by[(f"{a}_HIST", s)], a) - key_mean(by[("AI", s)], a) for s in seeds if (f"{a}_HIST", s) in by and ("AI", s) in by]
        out.append(f"- {a}: {statistics.median(g):+.2f} ({q(g, .1):+.2f} … {q(g, .9):+.2f})")
    out.append("")

    # §2 project load
    out += ["## Concurrent projects: largest share of research money taken (projects + upkeep) and most projects at once, over all runs", ""]
    for a in ["USA", "GBR", "SUN", "FRA", "SWE"]:
        take = [int(mi[r].get(f"inst_max_take_ppm_{a}", "0")) for r in mi]
        conc = [int(mi[r].get(f"inst_max_concurrent_{a}", "0")) for r in mi]
        out.append(f"- {a}: max take {max(take) / 1e4:.2f} %, max concurrent {max(conc)}")
    out.append("")

    # §3 P-8
    gm = sum(int(m.get("inst_p8_goal_months", 0)) for m in mi.values())
    sl = sum(int(m.get("inst_p8_slower", 0)) for m in mi.values())
    om = sum(int(m.get("inst_p8_off_months", 0)) for m in mi.values())
    osl = sum(int(m.get("inst_p8_off_slower", 0)) for m in mi.values())
    minr = min(int(m.get("inst_p8_min_ratio_ppm", 1_000_000)) for m in mi.values())
    out += ["## P-8 on the snapshot (rate with the goals against the old path without any goal, all actors, every month)", "",
            f"- goal-months {gm}, slower {sl}; off-profile goal-months {om}, slower {osl}; lowest ratio with/without {minr / 1e6:.6f}", ""]
    goals = defaultdict(list)
    for r in rows(d, "inst_goal"):
        goals[r["run"]].append(r)
    out += ["## P-8 trajectory: player goals in HIST, finish date against NOGOAL (same seed); goals finished as goals", "",
            "| actor | goals | later | later > 12 mo | earlier | median Δ mo | off-profile goals | off later |", "|---|---|---|---|---|---|---|---|"]
    for a in PLAYERS:
        dd, off = [], []
        for s in seeds:
            rh, rn = by.get((f"{a}_HIST", s)), by.get((f"{a}_NOGOAL", s))
            if not rh or not rn:
                continue
            for g in goals[rh]:
                if g["actor"] != a or g["known"] != "1":
                    continue
                n = int(g["node"])
                yh, _ = known[(rh, a, n)]
                yn, _ = known[(rn, a, n)]
                delta = 12 * (yh - yn)  # > 0: the goal finished later with the priority
                dd.append(delta)
                if float(g["mult"]) == 1.0:
                    off.append(delta)
        out.append(f"| {a} | {len(dd)} | {sum(1 for x in dd if x > 1e-9)} ({100 * sum(1 for x in dd if x > 1e-9) / max(1, len(dd)):.1f} %) | "
                   f"{sum(1 for x in dd if x > 12)} | {sum(1 for x in dd if x < -1e-9)} | {statistics.median(dd) if dd else float('nan'):+.1f} | "
                   f"{len(off)} | {sum(1 for x in off if x > 1e-9)} |")
    out.append("")

    # §4 stretched project
    out += ["## Stretched project (EARLY_F35 against HIST, the player's own candidate)", "",
            "| actor | id | arm | start | open year | planned year | premium paid | premium by open year | paid (rel. HIST) | months |", "|---|---|---|---|---|---|---|---|---|---|"]
    builds = rows(d, "inst_build")
    for a in PLAYERS:
        for arm in ["HIST", "EARLY", "EARLY_F35"]:
            per = defaultdict(list)
            for s in seeds:
                run = by.get((f"{a}_{arm}", s))
                hrun = by.get((f"{a}_HIST", s))
                for b in builds:
                    if b["run"] == run and b["actor"] == a and b["id"] in ("usa_darpa", "gbr_rre", "fra_cnes"):
                        hb = [x for x in builds if x["run"] == hrun and x["actor"] == a and x["id"] == b["id"]]
                        rel = float(b["paid"]) / float(hb[0]["paid"]) if hb and float(hb[0]["paid"]) > 0 else float("nan")
                        per[b["id"]].append((int(b["start"]), int(b["open_year"]), int(b["planned_year"]), float(b["premium"]), float(b["premium_actual"]), rel, int(b["months_run"])))
            for i, v in per.items():
                m = lambda k: statistics.median(x[k] for x in v)
                out.append(f"| {a} | {i} | {arm} | {m(0):.0f} | {m(1):.0f} | {m(2):.0f} | {m(3):.2f} | {m(4):.2f} | {m(5):.2f} | {m(6):.0f} |")
    out.append("")
    # what the cut costs the rest of research: key mean EARLY_F35 − EARLY
    out += ["EARLY_F35 − EARLY, mean key year (years): median (p10 … p90)", ""]
    for a in PLAYERS:
        g = [key_mean(by[(f"{a}_EARLY_F35", s)], a) - key_mean(by[(f"{a}_EARLY", s)], a) for s in seeds if (f"{a}_EARLY_F35", s) in by]
        out.append(f"- {a}: {statistics.median(g):+.2f} ({q(g, .1):+.2f} … {q(g, .9):+.2f})")
    out.append("")
    out.append("P-7 verdicts: " + ", ".join(f"{a} {v}" for a, v in verdict.items()))
    print("\n".join(out))
    return 1 if bad or refused else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
