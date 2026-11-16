#!/usr/bin/env python3
"""T12 C2 prints on the calibration build (TASK-0056; spec v0.32 §8.2 p.1 C2, #241, #244, #247; RISK-0022). Prints, not criteria:
the C2 distance itself is exp0002.py --calib.

Inputs: CALIB_DIR - one point of a JetSliceCalib matrix (runs.csv, node_times.csv, gen_times.csv, prc_month.csv, a40.csv);
PRINT_DIR - 'Singularity.Lab calib-print' of the same manifest point and seeds (fielded_actor.csv, fielded_spells.csv, designs.csv,
fielded_runs.csv). Deterministic, stdlib only.
Usage: t12_c2_print.py CALIB_DIR PRINT_DIR [--json OUT]
"""
import argparse
import collections
import csv
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "introduction"))
import sketch as SK  # noqa: E402
from e2decomp import km  # noqa: E402
from e1e2 import hot  # noqa: E402

LAST = 383
T_FIRST_MAX = 263          # #241 mastery sub-pool
MODE_SPLIT = 78            # print only (sketch §11.2: gap 56-100 months between the modes)
GREAT = ("USA", "GBR", "SUN")


def oi(s):
    return None if s in ("", None) else int(s)


def r3(x):
    return None if x is None else round(x, 3)


def rows(path):
    with open(path, newline="") as f:
        yield from csv.DictReader(f)


def load_designs(pdir):
    des = collections.defaultdict(list)
    for r in rows(os.path.join(pdir, "designs.csv")):
        des[(int(r["seed"]), r["actor"])].append(dict(
            design=int(r["design"]), gen=int(r["gen"]), prc={int(x) for x in r["prc"].split()},
            tc=oi(r["t_created"]), tp=oi(r["t_proto"]), ts=oi(r["t_series"]), ti=oi(r["t_inservice"]), tw=oi(r["t_withdrawn"]),
            cancelled=r["cancelled"] == "1"))
    return des


def alive_at(x, t):
    """Design in П, О or С at month t (post view), from the first months of its stages."""
    if x["tc"] is None or x["tc"] > t:
        return False
    for end in (x["ti"], x["tw"]):
        if end is not None and end <= t:
            return False
    return True


def k_class(tk, tf, n, ds):
    """K-classes of the C2 pool (e1e2.classify of the sketch §7.1 on the Lab design dump)."""
    if tf is not None:
        return "0 t_first"
    ds = sorted(ds, key=lambda x: x["tc"])
    withn = [x for x in ds if n in x["prc"]]
    after = [x for x in ds if x["tc"] >= tk]
    if withn:
        x = withn[0]
        if x["cancelled"]:
            return "K4a design with node withdrawn"
        if x["ts"] is not None:
            return "K4c design with node in series, no output"
        return "K4b design with node in project/prototype at 383"
    if not after:
        return "K1 no new design after t_known"
    h = hot(after[0]["prc"])
    return "K2 first design after t_known: lower rank" if h < (2 if n == 40 else 1) else "K3 first design after t_known: higher rank (skip)"


def c2_block(nt, rids, seed_of, des, vlast):
    pool, never, late, full = SK.pool_pairs(nt, rids, SK.TIER4)
    with_first = [p for p in pool if p[4] is not None]
    sub = [p for p in with_first if p[4] <= T_FIRST_MAX]
    dur = [(p[5] - max(p[4], 0)) if p[5] is not None else math.inf for p in sub]
    lm = SK.q(dur, .5)
    events = [((p[5] - max(p[4], 0)) if p[5] is not None else LAST - max(p[4], 0), p[5] is not None) for p in sub]
    fin = [d for d in dur if d != math.inf]
    fast = [d for d in fin if d <= MODE_SPLIT]
    slow = [d for d in fin if d > MODE_SPLIT]
    # «process abandoned before t_07» (sketch §11.6 (a)): a censored sub-pool pair whose node is in no live design (П/О/С) of the actor
    # at 383 and has no output at 383; KM (b): such pairs censored at the last month with output (v > 0) instead of 383
    abandoned, km_b = [], []
    for p, e in zip(sub, events):
        if e[1]:
            km_b.append(e)
            continue
        rid, a, n = p[0], p[1], p[2]
        live = [x for x in des[(seed_of[rid], a)] if n in x["prc"] and alive_at(x, LAST)]
        last_v = vlast.get((rid, a, n))
        if not live and (last_v is None or last_v < LAST):
            abandoned.append((a, n, p[4], last_v))
            stop = last_v if last_v is not None else max(p[4], 0)
            km_b.append((max(0, stop - max(p[4], 0)), False))
        else:
            km_b.append(e)
    by_pair = collections.defaultdict(list)
    for p, e in zip(sub, events):
        by_pair["%s-%d" % (p[1], p[2])].append(e)
    cls = collections.Counter()
    cls_actor = collections.defaultdict(collections.Counter)
    for p in pool:
        rid, a, n, tk, tf = p[0], p[1], p[2], p[3], p[4]
        c = k_class(max(tk, -1), tf, n, des[(seed_of[rid], a)])
        cls[c] += 1
        cls_actor[a][c[:2]] += 1
    n = len(pool)
    by_actor = {}
    for a in GREAT + ("FRA", "SWE"):
        ap = [p for p in pool if p[1] == a]
        if ap:
            by_actor[a] = {"pool": len(ap), "t_first": sum(1 for p in ap if p[4] is not None),
                           "subpool": sum(1 for p in sub if p[1] == a), "t_07": sum(1 for p in ap if p[5] is not None)}
    return {
        "pool": n, "not_mastered_pairs": full, "never_known": never, "late_known": late,
        "no_t_first_share": r3(1 - len(with_first) / n) if n else None,
        "subpool": len(sub), "subpool_censored_share": r3(sum(1 for d in dur if d == math.inf) / len(sub)) if sub else None,
        "lower_median_months": "inf" if lm == math.inf else lm,
        "km": {k: (r3(v) if isinstance(v, float) else v) for k, v in km(events).items()},
        "km_abandoned_censored_at_last_output": {k: (r3(v) if isinstance(v, float) else v) for k, v in km(km_b).items()},
        "mode_fast_le78": {"n": len(fast), "p50": SK.q(fast, .5), "min_max": [min(fast), max(fast)] if fast else None},
        "mode_slow_gt78": {"n": len(slow), "p50": SK.q(slow, .5), "min_max": [min(slow), max(slow)] if slow else None},
        "gap_56_100": sum(1 for d in fin if 56 <= d <= 100),
        "km_median_by_pair": {g: {"n": len(v), "events": sum(1 for x in v if x[1]), "median": km(v)["median"]} for g, v in sorted(by_pair.items())},
        "abandoned": {"n": len(abandoned), "share_of_censored": r3(len(abandoned) / sum(1 for d in dur if d == math.inf)) if any(d == math.inf for d in dur) else None,
                      "by_pair": dict(collections.Counter("%s-%d" % (a, n) for a, n, _, _ in abandoned))},
        "K_classes": {k: [v, r3(v / n)] for k, v in sorted(cls.items())},
        "K_by_actor": {a: dict(v) for a, v in sorted(cls_actor.items())},
        "by_actor": by_actor,
    }


def tier5_print(nt, rids, t4_median):
    pool, _, _, _ = SK.pool_pairs(nt, rids, SK.TIER5)
    sub = [p for p in pool if p[4] is not None and p[4] <= T_FIRST_MAX]
    dur = [(p[5] - max(p[4], 0)) if p[5] is not None else math.inf for p in sub]
    m = SK.q(dur, .5)
    full = not sub or all(d == math.inf for d in dur)
    longer = None if full or t4_median in (None, "inf") else m > t4_median
    return {"pool": len(pool), "subpool": len(sub), "lower_median": "inf" if m == math.inf else m,
            "censored_share": r3(sum(1 for d in dur if d == math.inf) / len(sub)) if sub else None,
            "longer_than_tier4": "нет (пул пуст или весь цензурирован)" if full else longer}


def fielded_block(pdir, runs, gt):
    hashes = {int(r["seed"]): r["hash_final"] for r in rows(os.path.join(pdir, "fielded_runs.csv"))}
    gold = {int(r["seed"]): r["golden_hash"] for r in runs.values() if r["status"] == "ok"}
    same_hash = sum(1 for s, h in gold.items() if hashes.get(s) == h)
    act = list(rows(os.path.join(pdir, "fielded_actor.csv")))
    months = sum(int(r["months"]) for r in act)
    gt_ = sum(int(r["months_l_gt_r6"]) for r in act)
    lt_ = sum(int(r["months_r6_gt_l"]) for r in act)
    first_bad, gen_times_bad, first_checked = [], [], 0
    for r in act:
        for g in range(1, 5):
            fl, fr = oi(r["first_l_g%d" % g]), oi(r["first_r6_g%d" % g])
            first_checked += 1
            if fl != fr:
                first_bad.append((r["seed"], r["actor"], g, fl, fr))
            tf = gt.get((int(r["seed"]), r["actor"], g))
            if tf != fl:
                gen_times_bad.append((r["seed"], r["actor"], g, fl, tf))
    by_actor = collections.defaultdict(lambda: [0, 0, 0])
    for r in act:
        b = by_actor[r["actor"]]
        b[0] += int(r["months"])
        b[1] += int(r["months_l_gt_r6"])
        b[2] += 1 if int(r["months_l_gt_r6"]) > 0 else 0
    sp = list(rows(os.path.join(pdir, "fielded_spells.csv")))
    dur = [(int(r["end"]) if r["end"] else LAST) - int(r["start"]) + 1 for r in sp]
    cens = sum(1 for r in sp if not r["end"])
    kinds = collections.Counter("%s->%s" % (r["g_l_max"], r["g_r6_min"]) for r in sp)
    return {
        "runs_hash_equal_golden": "%d/%d" % (same_hash, len(gold)),
        "actor_months": months,
        "share_G_L_gt_G_r6": r3(gt_ / months) if months else None,
        "actor_months_G_L_gt_G_r6": gt_,
        "invariant_G_r6_le_G_L_violations": lt_,
        "first_entry_equal": "%d/%d" % (first_checked - len(first_bad), first_checked), "first_entry_mismatch": first_bad[:10],
        "first_entry_L_equals_gen_times_t_fielded": "%d/%d" % (first_checked - len(gen_times_bad), first_checked),
        "by_actor": {a: {"months": v[0], "G_L_gt_G_r6": v[1], "share": r3(v[1] / v[0]), "runs_with_any": v[2]} for a, v in sorted(by_actor.items())},
        "spells": {"n": len(sp), "right_censored_at_383": cens, "kinds_gL_to_gr6": dict(kinds),
                   "months_p10_p50_p90_max": [SK.q(dur, .1), SK.q(dur, .5), SK.q(dur, .9), max(dur) if dur else None],
                   "by_actor": dict(collections.Counter(r["actor"] for r in sp)),
                   "runs_with_any": len({r["seed"] for r in sp})},
    }


def a40_block(cdir, rids):
    c = collections.Counter()
    seeds = collections.defaultdict(set)
    for r in rows(os.path.join(cdir, "a40.csv")):
        if r["run"] in rids:
            c[(r["branch"], r["cls"])] += 1
            seeds[(r["branch"], r["cls"])].add(r["run"])
    tot = sum(c.values())
    proc = sum(v for (b, k), v in c.items() if k == "PROCESSES")
    return {"firings": tot, "by_branch_class": {"%s:%s" % k: v for k, v in sorted(c.items())},
            "processes": proc, "processes_share_of_firings": r3(proc / tot) if tot else None,
            "processes_per_seed": r3(proc / len(rids)) if rids else None,
            "seeds_with": {"%s:%s" % k: len(v) for k, v in sorted(seeds.items())}}


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("calib")
    ap.add_argument("print_dir")
    ap.add_argument("--json")
    a = ap.parse_args(argv)
    runs, nt, _ym, _a40 = SK.load(a.calib)
    ok = {rid: r for rid, r in runs.items() if r["status"] == "ok"}
    rids = sorted(ok, key=lambda x: int(ok[x]["seed"]))
    seed_of = {rid: int(ok[rid]["seed"]) for rid in rids}
    gt = {}
    for r in rows(os.path.join(a.calib, "gen_times.csv")):
        if r["run"] in ok:
            gt[(seed_of[r["run"]], r["actor"], int(r["gen"]))] = oi(r["t_fielded"])
    vlast = {}
    for r in rows(os.path.join(a.calib, "prc_month.csv")):
        if r["run"] in ok and int(r["node"]) in SK.TIER4 and int(r["v"]) > 0:
            k = (r["run"], r["actor"], int(r["node"]))
            vlast[k] = max(vlast.get(k, -1), int(r["t"]))
    des = load_designs(a.print_dir)
    c2 = c2_block(nt, rids, seed_of, des, vlast)
    out = {"runs": len(rids), "C2": c2, "tier5_print": tier5_print(nt, rids, c2["lower_median_months"]),
           "A40": a40_block(a.calib, set(rids)), "fielded_L_247": fielded_block(a.print_dir, ok, gt)}
    text = json.dumps(out, indent=1, sort_keys=True, default=str, ensure_ascii=False)
    if a.json:
        with open(a.json, "w") as f:
            f.write(text + "\n")
    print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
