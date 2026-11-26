#!/usr/bin/env python3
"""TASK-0187: gates of the slot science model (SCIENCE_SLOTS.md §10, #566; pre-registration slot-model-pilot.md §7.1).

Reads matrix directories of lab/manifests/TASK-0187-core.json / TASK-0187-player.json and, for every series with the slot counters
(SlotProbe, model_info slot_*), checks each pre-registered gate and prints PASS / FAIL per gate and per actor:
  LIVE0   idle AI slot-months while a workable frontier node was left = 0, per AI actor (invariant of the core);
  LIVE5   the same as a share of slot-months <= 5 % per AI actor (median over seeds; SCIENCE_SLOTS §10 smoke);
  IDLE90  idle slot-months 1945-1990 <= 5 % per AI actor (median over seeds; slot-model-pilot.md §7.1);
  GAP36   months between two discoveries of an AI actor <= 36 in 1947-1990 (Orchestrator, 2026-11-26), until its workable frontier runs out (every seed);
  RET20   money returned to the budget <= 20 % of the research money in 1947-1990 (Orchestrator, 2026-11-26), per AI actor (median over seeds);
          after 1990 both are printed as information (the 1990-2030 tree is a task of its own);
  CEIL    upkeep share of the institutes built by a project <= the ceiling (slot_upkeep_ceiling_ppm), every actor and month;
  BANDS   first unit of any actor, median over seeds: G2 1948-1962, G3 1952-1970, G4 1962-1985 (both bounds, whole years);
  LEAD77  leader of 1977 (summary.lead_all, majority of the months of 1977) is USA or SUN in >= 70 % of seeds;
  SPREAD  p90 - p10 of the first G4 <= 15 years;  G4BY2030  first G4 before 2031 in >= 90 % of seeds;
  TIME    median run time of the slot series <= 1.5 x the shares series of the same directory (when there is one);
  DET     --l2 DIR: the threads 1/2 matrix has thread_mismatch = 0 and valid = true.
Printed, not gated: the C4 windows +-8/+-10 years, per country dates, idle after 1990, focus months, money shortage.
Usage: python3 lab/analysis/slot_core.py DIR [DIR ...] [--l2 DIR] [--countries]. Exit 0 = every gate PASS, 1 = a FAIL or a run not ok,
2 = usage. Tests: lab/analysis/test_slot_core.py.
"""
import csv
import json
import math
import os
import sys
from collections import defaultdict

INF = math.inf
GENS = (2, 3, 4)
BANDS = {2: (1948.0, 1963.0), 3: (1952.0, 1971.0), 4: (1962.0, 1986.0)}   # SCIENCE_SLOTS §10, whole years inclusive
C4_10 = {2: (-INF, 1963.0), 3: (1944.0, 1972.0), 4: (1957.0, 1986.0)}       # printed only
C4_8 = {2: (-INF, 1961.0), 3: (1946.0, 1970.0), 4: (1959.0, 1984.0)}
LEADERS = ("USA", "SUN")
PERIOD1_LAST = 543   # 1990-12 when month 0 is 1945-09
GATE_FIRST = 16      # 1947-01: the window of GAP36 and RET20 is 1947-01..1990-12 (Orchestrator, 2026-11-26)
YEAR1977 = (376, 388)
LIMITS = {"LIVE5": 0.05, "IDLE90": 0.05, "GAP36": 36, "RET20": 0.20, "LEAD77": 0.70, "SPREAD": 15.0, "G4BY2030": 0.90, "TIME": 1.5}


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


def read_csv(d, name):
    path = os.path.join(d, name)
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8") as f:
        return list(csv.DictReader(f))


def load(d):
    runs = {r["run"]: r for r in read_csv(d, "runs.csv")}
    first = defaultdict(dict)
    for r in read_csv(d, "gen_times.csv"):
        t = int(r["t_first_unit"]) if r["t_first_unit"] not in ("", None) else -1
        first[r["run"]][(r["actor"], int(r["gen"]))] = INF if t < 0 else year_of(t)
    known = defaultdict(lambda: defaultdict(list))
    for r in read_csv(d, "node_times.csv"):
        if r["t_known"] not in ("", None) and int(r["t_known"]) >= 0:
            known[r["run"]][r["actor"]].append(int(r["t_known"]))
    summary = {r["run"]: r for r in read_csv(d, "summary.csv")}
    info, elapsed = {}, {}
    for run in runs:
        with open(os.path.join(d, "runs", run, "run.json"), encoding="utf-8") as f:
            j = json.load(f)
        info[run] = j.get("model_info", {})
        elapsed[run] = j.get("elapsed_ms")
    actors = [k[len("prof_"):] for k in (next(iter(summary.values())).keys() if summary else []) if k.startswith("prof_")]
    return runs, first, known, summary, info, elapsed, actors


def max_gap(months, workable_last, start=0, end=None):
    """Largest gap in months between discoveries in [start; end] (the window opens at start), up to the last month the workable
    frontier was not empty (or end, whichever is first)."""
    stop = workable_last if end is None else min(workable_last, end)
    if stop < start:
        return 0
    pts = [start] + sorted(m for m in months if start < m <= stop)
    gaps = [b - a for a, b in zip(pts, pts[1:])]
    gaps.append(stop - pts[-1])
    return max(gaps)


def leader_1977(lead_all, actors):
    cells = lead_all[YEAR1977[0]:YEAR1977[1]]
    counts = defaultdict(int)
    for c in cells:
        if c.isdigit() and int(c) < len(actors):
            counts[actors[int(c)]] += 1
    return max(sorted(counts), key=lambda a: counts[a]) if counts else None


def gates_for_series(rs, first, known, summary, info, actors):
    """[(gate, subject, ok, detail)] for the runs rs of one slot series."""
    out = []
    player = info[rs[0]].get("slot_player", "")
    ai = [a for a in actors if a != player]

    def c(r, p, a, k):
        return int(info[r][f"slot_p{p}_{a}_{k}"])

    for a in ai:
        free = [c(r, 1, a, "idle_free") + c(r, 2, a, "idle_free") for r in rs]
        out.append(("LIVE0", a, all(x == 0 for x in free), f"max {max(free)}"))
        share = med([(c(r, 1, a, "idle_free") + c(r, 2, a, "idle_free")) / max(1, c(r, 1, a, "slot_months") + c(r, 2, a, "slot_months")) for r in rs])
        out.append(("LIVE5", a, share <= LIMITS["LIVE5"], f"{share:.1%}"))
        idle90 = med([c(r, 1, a, "idle") / max(1, c(r, 1, a, "slot_months")) for r in rs])
        out.append(("IDLE90", a, idle90 <= LIMITS["IDLE90"], f"{idle90:.1%}"))
        wl = {r: int(info[r][f"slot_{a}_workable_last"]) for r in rs}
        gaps = [max_gap(known[r][a], wl[r], GATE_FIRST, PERIOD1_LAST) for r in rs]
        after = [max_gap(known[r][a], wl[r], PERIOD1_LAST + 1) for r in rs]
        out.append(("GAP36", a, max(gaps) <= LIMITS["GAP36"], f"1947–1990 max {max(gaps)} мес.; после 1990 (печать) max {max(after)} мес."))
        ret = med([int(info[r][f"slot_w47_{a}_returned_raw"]) / max(1, int(info[r][f"slot_w47_{a}_science_raw"])) for r in rs])
        ret2 = med([int(info[r][f"slot_p2_{a}_returned_raw"]) / max(1, int(info[r][f"slot_p2_{a}_science_raw"])) for r in rs])
        out.append(("RET20", a, ret <= LIMITS["RET20"], f"1947–1990 {ret:.1%}; после 1990 (печать) {ret2:.1%}"))
    for a in actors:
        ceil = int(info[rs[0]]["slot_upkeep_ceiling_ppm"])
        up = max(int(info[r][f"slot_{a}_upkeep_max_ppm"]) for r in rs)
        out.append(("CEIL", a, up <= ceil, f"max {up / 1e4:.1f} % при потолке {ceil / 1e4:.1f} %"))
    any_first = {g: [min(first[r].get((a, g), INF) for a in actors) for r in rs] for g in GENS}
    for g in GENS:
        m = med(any_first[g])
        out.append(("BANDS", f"G{g}", inside(m, BANDS[g]), f"{fmt(m)} в [{BANDS[g][0]:.0f}; {BANDS[g][1] - 1:.0f}]"))
    leaders = [leader_1977(summary[r]["lead_all"], actors) if r in summary else None for r in rs]
    share = sum(1 for x in leaders if x in LEADERS) / len(rs)
    out.append(("LEAD77", "USA|SUN", share >= LIMITS["LEAD77"], f"{share:.0%} ({', '.join(str(x) for x in leaders)})"))
    spread = pct(any_first[4], 0.9) - pct(any_first[4], 0.1)
    out.append(("SPREAD", "G4", spread <= LIMITS["SPREAD"], f"{spread:.1f} лет"))
    by = sum(1 for y in any_first[4] if y < 2031.0) / len(rs)
    out.append(("G4BY2030", "G4", by >= LIMITS["G4BY2030"], f"{by:.0%}"))
    return out


def time_gate(series, elapsed):
    shares = [s for s, rs in series.items() if s.startswith("SHARES") or s.endswith("_SH")]
    out = []
    for s, rs in series.items():
        if s in shares or not shares:
            continue
        base = med([elapsed[r] for r in series[shares[0]] if elapsed[r] is not None])
        cur = med([elapsed[r] for r in rs if elapsed[r] is not None])
        if base and cur:
            out.append((s, "TIME", "ms", cur <= LIMITS["TIME"] * base, f"{cur} мс против {base} мс ({shares[0]})"))
    return out


def det_gate(d):
    with open(os.path.join(d, "matrix.json"), encoding="utf-8") as f:
        m = json.load(f)
    mismatch = m.get("thread_mismatch")
    none = mismatch == 0 or mismatch == []   # the matrix writes the list of mismatching runs
    ok = none and m.get("valid") is True
    return ("DET", "threads", ok, f"thread_mismatch {m.get('thread_mismatch')}, valid {m.get('valid')}")


def report(d, countries, out=print):
    runs, first, known, summary, info, elapsed, actors = load(d)
    bad = [r for r, v in runs.items() if v["status"] != "ok"]
    series = defaultdict(list)
    for r, v in sorted(runs.items()):
        if v["threads"] == "1" and v["status"] == "ok":
            series[v["series"]].append(r)
    ok = bool(runs) and not bad
    out(f"== {d}: {len(runs)} runs; {'PASS' if ok else 'FAIL'} RUNS (не ok: {bad})" + ("; нет ни одного прогона" if not runs else ""))
    for s, rs in sorted(series.items()):
        out(f"-- {s} ({len(rs)} seeds)")
        any_first = {g: [min(first[r].get((a, g), INF) for a in actors) for r in rs] for g in GENS}
        out("  печать, окна C4: " + "; ".join(
            f"G{g} {fmt(med(any_first[g]))} [±10 {'да' if inside(med(any_first[g]), C4_10[g]) else 'нет'}, ±8 {'да' if inside(med(any_first[g]), C4_8[g]) else 'нет'}]" for g in GENS))
        if countries:
            for a in actors:
                out(f"  печать {a}: " + " / ".join(fmt(med([first[r].get((a, g), INF) for r in rs])) for g in GENS))
        if not all("slot_ai_idle_with_frontier" in info[r] for r in rs):
            out("  (режим долей: ворот слотов нет)")
            continue
        for gate, subject, passed, detail in gates_for_series(rs, first, known, summary, info, actors):
            out(f"  {'PASS' if passed else 'FAIL'} {gate:8} {subject:8} {detail}")
            ok &= passed
        for a in actors:
            p2 = med([int(info[r][f"slot_p2_{a}_idle"]) / max(1, int(info[r][f"slot_p2_{a}_slot_months"])) for r in rs])
            f1 = med([int(info[r][f"slot_p1_{a}_focus1"]) for r in rs]), med([int(info[r][f"slot_p1_{a}_focus2"]) for r in rs])
            sh = med([int(info[r][f"slot_p1_{a}_short_months"]) / max(1, int(info[r][f"slot_p1_{a}_months"])) for r in rs])
            out(f"  печать {a}: простой после 1990 {p2:.1%}; фокус 1/2 до 1990 {f1[0]}/{f1[1]} мес.; недостача денег до 1990 {sh:.0%}")
    for s, gate, subject, passed, detail in time_gate(series, elapsed):
        out(f"  {'PASS' if passed else 'FAIL'} {gate:8} {s:8} {detail}")
        ok &= passed
    return ok


def main(argv, out=print):
    countries = "--countries" in argv
    args = [a for a in argv if a != "--countries"]
    l2 = None
    if "--l2" in args:
        i = args.index("--l2")
        if i + 1 >= len(args):
            out(__doc__)
            return 2
        l2 = args[i + 1]
        del args[i:i + 2]
    if not args or any(a.startswith("--") for a in args):
        out(__doc__)
        return 2
    ok = True
    for d in args:
        ok &= report(d, countries, out)
    if l2:
        gate, subject, passed, detail = det_gate(l2)
        out(f"== {l2}\n  {'PASS' if passed else 'FAIL'} {gate:8} {subject:8} {detail}")
        ok &= passed
    out("ИТОГ: " + ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
