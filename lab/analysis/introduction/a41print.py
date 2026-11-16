#!/usr/bin/env python3
"""Owner decision #244 print: HYP-0002 metrics and C2 / E1 / E3 / A40 / K3 prints for the A41 variants
(introduction-channel-sketch.md §12; manifests lab/manifests/HYP-0002-a41-print.json and HYP-0002-a41-print-calib.json).

A print, not a round with criteria: no verdicts are computed (the exp0002 criteria functions are called for their numbers only).
Deterministic (bootstrap streams of exp0002 are seeded per label), stdlib only.

Usage: a41print.py MAIN_DIR CALIB_DIR HOST_DIR [--json OUT]
  MAIN_DIR  - matrix of JetSlice (summary, actor_year, gen_times, requirements, prc_year)
  CALIB_DIR - matrix of JetSliceCalib (node_times, prc_month, a40, actor_month, gen_times, calib_summary)
  HOST_DIR  - dump_host outputs <series with '/' -> '_'>_designs.csv (K3 classes); missing files -> K3 not printed
"""
import argparse
import collections
import csv
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))
import exp0002 as X  # noqa: E402
import sketch as SK  # noqa: E402
import e1e2 as EE  # noqa: E402
from e2decomp import km  # noqa: E402

LAST = 383
T_FIRST_MAX = 263          # #241: mastery sub-pool
POOL_T_KNOWN = 335         # #238 V1: pool t_known <= 383 - 48
GREAT = ("USA", "GBR", "SUN")
MODE_SPLIT = 78            # print only: completed durations <= 78 months = fast mode, > 78 = slow mode (§11.2: gap 56-100)
FLEET_T = (180, 240, 300, 383)


def r3(x):
    return None if x is None else (x if isinstance(x, str) else round(x, 3))


def q(xs, p):
    return SK.q(xs, p)


def series_runs(d):
    by = collections.defaultdict(dict)
    with open(os.path.join(d, "runs.csv"), newline="") as f:
        for r in csv.DictReader(f):
            if r["status"] == "ok":
                by[r["series"]][int(r["seed"])] = r["run"]
    return by


def read(d, name):
    with open(os.path.join(d, name), newline="") as f:
        yield from csv.DictReader(f)


# ---------------------------------------------------------------- HYP-0002 (JetSlice)

def load_main(d, cfg):
    D = X.Data()
    pre = "0:"
    for r in read(d, "runs.csv"):
        D.runs[pre + r["run"]] = {"run": pre + r["run"], "series": r["series"], "seed": int(r["seed"]), "threads": int(r["threads"]),
                                  "status": r["status"], "golden": r["golden_hash"]}
    for r in read(d, "summary.csv"):
        rec = D.runs[pre + r["run"]]
        rec["prof"] = {a: int(r["prof_" + a]) for a in cfg["actors"]}
        rec["lead"] = [int(c) for c in r["lead"]]
        rec["lead_all"] = [int(c) for c in r["lead_all"]]
        rec["lead_field"] = r["lead_field"]
        X.derive(rec, cfg)
    for r in read(d, "actor_year.csv"):
        D.actor_year[pre + r["run"]][(int(r["t"]), r["actor"])] = {"fv": int(r["fv"]), "volume": int(r["volume"])}
    D.present.add("actor_year")
    for r in read(d, "requirements.csv"):
        D.reqs[pre + r["run"]].append({"actor": r["actor"], "gen": int(r["gen"]), "t": int(r["t"]), "n_variants": int(r["n_variants"]),
                                       "fv_tech": X._oi(r["fv_producible"]), "fv_super": X._oi(r["fv_superior"])})
    D.present.add("reqs")
    for r in read(d, "prc_year.csv"):
        D.prc_year[pre + r["run"]][(int(r["t"]), r["actor"], int(r["node"]))] = {"known": int(r["known"]), "yield": int(r["yield"])}
    D.present.add("prc_year")
    gt = collections.defaultdict(dict)
    for r in read(d, "gen_times.csv"):
        gt[pre + r["run"]][(r["actor"], int(r["gen"]))] = (X._oi(r["t_req"]), X._oi(r["t_first_unit"]), X._oi(r["t_fielded"]))
    for rid, rec in D.runs.items():
        if rec["status"] == "ok" and "lead" in rec:
            D.series[rec["series"]].setdefault(rec["seed"], rid)
    return D, gt


def hyp0002_block(D, cfg, s):
    rids = [D.series[s][k] for k in sorted(D.series[s])]
    runs = [D.runs[r] for r in rids]
    p1 = X.crit_p1(runs, cfg)["chg"]
    p2 = X.crit_p2(runs, cfg)
    sec = X.secondary(D, cfg, rids)
    lp = X.leadership_prints(D, cfg, rids)
    # leader profile at 359 (among the great powers): share of seeds whose leader has profile P1/P2/P3, and the base share of the profile
    lead_prof, base_prof = collections.Counter(), collections.Counter()
    for r in runs:
        lead_prof[r["prof"][GREAT[r["lead359"]]]] += 1
        for a in GREAT:
            base_prof[r["prof"][a]] += 1
    nb = sum(base_prof.values())
    # profile of the new leader of each counted change
    chg_prof = collections.Counter(r["prof"][GREAT[v]] for r in runs for _, v, _ in r["changes"])
    nc = sum(chg_prof.values())
    return {
        "seeds": len(runs),
        "chg": {"p": r3(p1["p"]), "ci": [r3(x) for x in p1["ci"]]},
        "small_lead": {"p": r3(p2["small_lead"]["p"]), "ci": [r3(x) for x in p2["small_lead"]["ci"]]},
        "middle_lead": r3(p2["middle_lead"]["p"]),
        "S1_tech_wins": r3(sec["S1"].get("tech_wins", {}).get("p")) if sec["S1"].get("tech_wins") else None,
        "S2_share": r3(sec["S2"]["share"]["p"]) if sec["S2"].get("share") else None,
        "S3_share_3plus": r3(sec["S3"]["share_3plus"]["p"]), "S3_tenure_p10_p50_p90": sec["S3"]["tenure"],
        "n_changes": lp["n_changes"], "first_change_t": lp["first_change_t"],
        "new_leader_share": {k: r3(v) for k, v in lp["new_leader_share"].items()},
        "change_field": lp["change_field"],
        "lead359": {k: r3(v["p"]) for k, v in lp["lead359"].items()},
        "lead359_profile_share": {"P%d" % p: r3(lead_prof[p] / len(runs)) for p in (1, 2, 3)},
        "great_profile_base_share": {"P%d" % p: r3(base_prof[p] / nb) for p in (1, 2, 3)},
        "new_leader_profile_share": {"P%d" % p: (r3(chg_prof[p] / nc) if nc else None) for p in (1, 2, 3)},
    }


def paired_vs_base(D, cfg, s, base):
    sd, ra, rb = X.paired(D, s, base)
    dl = X.paired_delta([r["chg"] for r in ra], [r["chg"] for r in rb], cfg, "a41|%s|chg" % s)
    tv = X.tvd_bc([r["lead359"] for r in ra], [r["lead359"] for r in rb], len(cfg["great"]), cfg, "a41|%s|tvd" % s)
    same = sum(1 for a, b in zip(ra, rb) if a["chg"] == b["chg"])
    return {"seeds": len(sd), "d_chg": r3(dl["delta"]), "d_chg_ci": [r3(x) for x in dl["ci"]],
            "tvd_lead359": r3(tv["tvd_bc"]), "tvd_ci": [r3(x) for x in tv["ci"]], "chg_same_seed_share": r3(same / len(sd))}


def fleet_block(D, gt, s, am):
    """Per profile (great powers): month generations 2/3 enter service (t_fielded), share fielded by 383, fleet shares by gen."""
    rids = [D.series[s][k] for k in sorted(D.series[s])]
    out = {}
    for p in (1, 2, 3):
        row = {}
        cells = [(rid, a) for rid in rids for a in GREAT if D.runs[rid]["prof"][a] == p]
        row["n"] = len(cells)
        for g in (2, 3, 4):
            ts = [gt[rid].get((a, g), (None, None, None))[2] for rid, a in cells]
            done = [t for t in ts if t is not None and t >= 0]
            row["gen%d_fielded_share" % g] = r3(len(done) / len(ts)) if ts else None
            row["gen%d_t_fielded_p10_p50_p90" % g] = [q(done, .1), q(done, .5), q(done, .9)]
            fu = [gt[rid].get((a, g), (None, None, None))[1] for rid, a in cells]
            fu = [t for t in fu if t is not None and t >= 0]
            row["gen%d_first_unit_p50" % g] = q(fu, .5)
        if am is not None:
            for t in FLEET_T:
                sh2, sh3 = [], []
                for rid, a in cells:
                    v = am.get((s, D.runs[rid]["seed"], a, t))
                    if v is None or sum(v) == 0:
                        continue
                    tot = sum(v)
                    sh2.append(sum(v[1:]) / tot)
                    sh3.append(sum(v[2:]) / tot)
                row["deliv_cum_t%d_share_gen_ge2_mean" % t] = r3(sum(sh2) / len(sh2)) if sh2 else None
                row["deliv_cum_t%d_share_gen_ge3_mean" % t] = r3(sum(sh3) / len(sh3)) if sh3 else None
        out["P%d" % p] = row
    return out


def load_actor_month(d, by):
    """Cumulative series output by generation (sum of out_g* over months 0..t) at the months FLEET_T: a proxy of the fleet by
    generation (deliveries, no retirement or sale; JetSliceCalib has no fleet stock)."""
    seed_of = {rid: (s, seed) for s, m in by.items() for seed, rid in m.items()}
    acc = collections.defaultdict(lambda: [0, 0, 0, 0])
    am = {}
    for r in read(d, "actor_month.csv"):
        if r["run"] not in seed_of:
            continue
        t = int(r["t"])
        s, seed = seed_of[r["run"]]
        c = acc[(s, seed, r["actor"])]
        for g in range(4):
            c[g] += int(r["out_g%d" % (g + 1)])
        if t in FLEET_T:
            am[(s, seed, r["actor"], t)] = list(c)
    return am


# ---------------------------------------------------------------- C2 (#241), E1/E3, A40, K3 (JetSliceCalib)

def c2_block(nt, rids):
    pool, never, late, full = SK.pool_pairs(nt, rids, SK.TIER4)
    n = len(pool)
    with_first = [p for p in pool if p[4] is not None]
    sub = [p for p in with_first if p[4] <= T_FIRST_MAX]
    dur = [(p[5] - p[4]) if p[5] is not None else math.inf for p in sub]
    lm = q(dur, .5)
    k = km([((p[5] - p[4]) if p[5] is not None else LAST - p[4], p[5] is not None) for p in sub])
    fin = [d for d in dur if d != math.inf]
    fast = [d for d in fin if d <= MODE_SPLIT]
    slow = [d for d in fin if d > MODE_SPLIT]
    groups = collections.defaultdict(list)
    for p in sub:
        groups["%s-%d" % (p[1], p[2])].append(((p[5] - p[4]) if p[5] is not None else LAST - p[4], p[5] is not None))
    return {
        "pool": n, "never_known": never, "late_known": late,
        "no_t_first_share": r3(1 - len(with_first) / n) if n else None,
        "subpool_t_first_le_263": len(sub),
        "subpool_censored_share": r3(sum(1 for d in dur if d == math.inf) / len(sub)) if sub else None,
        "lower_median_months": ("inf" if lm == math.inf else lm), "lower_median_years": ("inf" if lm == math.inf else r3(lm / 12)) if lm is not None else None,
        "km": {kk: (r3(v) if isinstance(v, float) else v) for kk, v in k.items()},
        "mode_fast_le78": {"n": len(fast), "p50": q(fast, .5), "min_max": [min(fast), max(fast)] if fast else None},
        "mode_slow_gt78": {"n": len(slow), "p50": q(slow, .5), "min_max": [min(slow), max(slow)] if slow else None},
        "gap_56_100": sum(1 for d in fin if 56 <= d <= 100),
        "km_median_by_pair": {g: (km(v)["median"], len(v), sum(1 for x in v if x[1])) for g, v in sorted(groups.items())},
    }


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("main")
    ap.add_argument("calib")
    ap.add_argument("host")
    ap.add_argument("--json")
    a = ap.parse_args(argv)
    cfg = X.merge_config(X.DEFAULTS, {})
    D, gt = load_main(a.main, cfg)
    cby = series_runs(a.calib)
    am = load_actor_month(a.calib, cby)
    runs_c, nt, ym, a40 = SK.load(a.calib)
    series = sorted(D.series)
    base = series[0]
    out = {"base": base, "series": {}}
    # identity check: node_times of JetSlice == JetSliceCalib per (series, seed) (same world, different output)
    mnt = collections.defaultdict(dict)
    for r in read(a.main, "node_times.csv"):
        mnt[r["run"]][(r["actor"], int(r["node"]))] = (r["t_known"], r["t_first"], r["t_07"], r["t_mastered"])
    cnt = collections.defaultdict(dict)
    for r in read(a.calib, "node_times.csv"):
        cnt[r["run"]][(r["actor"], int(r["node"]))] = (r["t_known"], r["t_first"], r["t_07"], r["t_mastered"])
    for s in series:
        same = sum(1 for seed, rid in D.series[s].items() if mnt[rid[2:]] == cnt[cby[s][seed]])
        rids_c = [cby[s][k] for k in sorted(cby[s])]
        st = SK.series_stats(s, rids_c, nt, ym)
        e1 = None
        hp = os.path.join(a.host, s.replace("/", "_") + "_designs.csv")
        if os.path.exists(hp):
            pool = EE.load_pool(a.calib, s)
            e1 = EE.classify(pool, hp)
        out["series"][s] = {
            "node_times_main_eq_calib": "%d/%d" % (same, len(D.series[s])),
            "hyp0002": hyp0002_block(D, cfg, s),
            "vs_base": None if s == base else paired_vs_base(D, cfg, s, base),
            "fleet_by_profile": fleet_block(D, gt, s, am),
            "c2": c2_block(nt, rids_c),
            "E1": r3(st["E1_no_t_first_share"]), "E2": r3(st["E2_censored_share"]), "E3": r3(st["E3_yield_p50_at_48"]),
            "E3_n": st["trajectory_yield_after_t_first"][48]["n"],
            "H1": r3(st["H1_t07_share_spread"]), "H2": st["H2_p10_t07_minus_t_first_months"],
            "actors": {k: {kk: r3(vv) for kk, vv in v.items()} for k, v in st["actors"].items()},
            "tier5": st["tier5"],
            "a40_O": {c: a40.get((s, "O", c), 0) for c in sorted({k[2] for k in a40 if k[0] == s and k[1] == "O"})},
            "K": None if e1 is None else {"classes": {k: [v[0], r3(v[1])] for k, v in e1["classes"].items()}, "by_actor": e1["by_actor"]},
        }
    text = json.dumps(out, indent=1, sort_keys=True, default=str)
    if a.json:
        with open(a.json, "w") as f:
            f.write(text + "\n")
    print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
