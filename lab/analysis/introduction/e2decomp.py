#!/usr/bin/env python3
"""TASK-0059: E2 decomposition of HYP-0004 round 3 and the gate prints (introduction-channel-sketch.md §11). Deterministic, stdlib only.

Inputs: the round-3 matrix (node_times, runs, calib_summary, gen_times of 'test/core', 'test/VEXPOC', 'test/VEXPOC+RANK'), the TASK-0059 matrix
('test/RANK' = C-B + PILOT_CHOICE=RANK) and the dump_host outputs of the four arms on seeds 30201-30300 (PREFIX_{yield,designs,closes,lines,compose}.csv).
Usage: e2decomp.py R3_MATRIX T59_MATRIX HOST_DIR [--json OUT]
"""
import argparse
import collections
import csv
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from e1e2 import ACT, LAST, load_pool, classify, hot  # noqa: E402

LAMBDA0, Y07, EARLY = 0.3, 0.7, LAST - 120
ARMS = {"CB": (0, "test/core"), "OC": (0, "test/VEXPOC"), "OCR": (0, "test/VEXPOC+RANK"), "R": (1, "test/RANK")}
REASON = {"1": "A40C", "2": "YProd", "3": "LaunchAll", "4": "LaunchCandidate", "-1": "Derive"}
BIND = {"0": "none", "1": "rate", "2": "alloy", "3": "money"}


def q(xs, p):
    if not xs:
        return None
    s = sorted(xs)
    return s[int(math.floor(p * (len(s) - 1)))]


def r3(x):
    return None if x is None else round(x, 3)


def km(times):
    """Kaplan-Meier of (duration, event) -> (median, p25, p75 of the event time; None = not reached) and S at 48/96/120."""
    ev = sorted(times)
    at_risk, s, curve = len(ev), 1.0, []
    i = 0
    while i < len(ev):
        t = ev[i][0]
        d = sum(1 for x in ev if x[0] == t and x[1])
        c = sum(1 for x in ev if x[0] == t)
        if d:
            s *= 1 - d / at_risk
            curve.append((t, s))
        at_risk -= c
        i += c

    def at_risk_at(t0):
        return sum(1 for x in ev if x[0] >= t0) if t0 is not None else None

    def first_below(level):
        for t, v in curve:
            if v <= level:
                return t
        return None

    def s_at(t0):
        v = 1.0
        for t, x in curve:
            if t <= t0:
                v = x
        return v
    return {"n": len(ev), "events": sum(1 for x in ev if x[1]), "p25": first_below(0.75), "median": first_below(0.5), "p75": first_below(0.25),
            "at_risk_at_median": at_risk_at(first_below(0.5)), "S48": r3(s_at(48)), "S96": r3(s_at(96)), "S120": r3(s_at(120)), "S150": r3(s_at(150)), "S180": r3(s_at(180)), "last_event": curve[-1][0] if curve else None}


def load_times(mdir, series):
    runs = {r["run"]: int(r["seed"]) for r in csv.DictReader(open(os.path.join(mdir, "runs.csv"))) if r["series"] == series}
    t7 = {}
    for r in csv.DictReader(open(os.path.join(mdir, "node_times.csv"))):
        if r["run"] in runs and int(r["node"]) in (40, 49):
            t7[(runs[r["run"]], r["actor"], int(r["node"]))] = None if r["t_07"] == "" else int(r["t_07"])
    return t7


def host_rows(prefix, keys):
    rows = collections.defaultdict(dict)
    for r in csv.DictReader(open(prefix + "_yield.csv")):
        k = (int(r["seed"]), ACT[int(r["actor"])], int(r["node"]))
        if k in keys:
            rows[k][int(r["t"]) - 1] = r      # host month t+1 = prc_month month t
    return rows


def designs(prefix):
    des = collections.defaultdict(dict)
    for r in csv.DictReader(open(prefix + "_designs.csv")):
        des[(int(r["seed"]), ACT[int(r["actor"])])][int(r["design"])] = dict(
            gen=int(r["gen"]), prc={int(x) for x in r["prc"].split()}, tc=int(r["t_created"]), ts=int(r["t_series"]),
            ti=int(r["t_inservice"]), tcan=int(r["t_cancel"]))
    return des


def closes(prefix):
    out = collections.defaultdict(list)
    for r in csv.DictReader(open(prefix + "_closes.csv")):
        out[(int(r["seed"]), ACT[int(r["actor"])])].append((int(r["t"]), int(r["design"]), REASON[r["reason"]]))
    return out


def counterfactual(rows, t_close, g_of):
    """yield at 383 if the line had kept running after t_close: X grows by the last 12-month unit rate, (a) constant, (b) scaled by
    yield/yield_close (money-bound line: unit cost ~ 1/yield). g is taken as observed each month (a lower bound: with the line culture
    would not be lower). Returns the first month yield >= 0.7 under (a) and (b), or None."""
    r = rows[t_close]
    x, xh, y0 = float(r["x"]), float(r["x_half"]), float(r["yield"])
    t0 = max(t_close - 12, min(rows))
    rate = (x - float(rows[t0]["x"])) / max(1, t_close - t0)
    res = []
    for scaled in (False, True):
        xx, hit = x, None
        for t in range(t_close + 1, LAST + 1):
            g = g_of(t)
            ramp = LAMBDA0 + (1 - LAMBDA0) * xx / (xx + xh)
            y = g * ramp
            if y >= Y07:
                hit = t
                break
            xx += rate * (y / y0 if scaled and y0 > 0 else 1.0)
        res.append(hit)
    return rate, res


def early_causes(pool, t7, rows, des, cls):
    """Every pair with t_first <= 263 and no t_07: one primary cause (see §11.1 of the sketch for the tree)."""
    out, table = [], collections.Counter()
    for k, (tk, tf, _t) in sorted(pool.items()):
        if tf is None or tf > EARLY or t7.get(k) is not None:
            continue
        seed, a, n = k
        R = rows[k]
        last = R[max(R)]
        months = [t for t in range(max(tf, 0), LAST + 1) if t in R]
        line_months = [t for t in months if R[t]["line"] != ""]
        t_last_line = max(line_months) if line_months else None
        y, g, ramp = float(last["yield"]), float(last["g"]), float(last["ramp"])
        rec = dict(seed=seed, actor=a, node=n, t_first=tf, y383=round(y, 3), g383=round(g, 3), ramp383=round(ramp, 3),
                   x383=round(float(last["x"])), months_without_line=len(months) - len(line_months), t_last_line=t_last_line)
        bind = collections.Counter(BIND[R[t]["bind"]] for t in line_months)
        rec["bind"] = dict(bind)
        if last["line"] == "":
            # the line that carried the process was closed: which rule, was the process in the design that replaced it
            d_line = int(R[t_last_line]["line"])
            why = [c for c in cls[(seed, a)] if c[1] == d_line and t_last_line - 2 <= c[0] <= t_last_line + 1]
            newer = [d for d in des[(seed, a)].values() if d["ts"] >= 0 and abs(d["ts"] - (t_last_line + 1)) <= 1 and d["ts"] > 0]
            rec["close_reason"] = why[0][2] if why else "?"
            rec["replaced_by_has_node"] = any(n in d["prc"] for d in newer) if newer else None
            rec["replaced_by_hot_rank"] = [hot(d["prc"]) for d in newer]
            rec["y_at_close"] = round(float(R[t_last_line]["yield"]), 3)
            rate, (hit_c, hit_s) = counterfactual(R, t_last_line, lambda t: float(R[t]["g"]) if t in R else g)
            rec["rate_before_close"] = round(rate, 2)
            rec["cf_t07_const_rate"], rec["cf_t07_rate_by_yield"] = hit_c, hit_s
            if hit_c is not None:
                cause = "A1 line closed by AI below 0.7, would reach 0.7 at the pre-close rate"
            elif hit_s is not None:
                cause = "A2 line closed by AI below 0.7, would reach 0.7 only if output grew with yield"
            else:
                cause = "A3 line closed by AI below 0.7, slow output: would not reach 0.7 by 383 even with the line (" + bind.most_common(1)[0][0] + ")"
        else:
            fix_g = ramp >= Y07
            src = None
            if g < 0.999:
                ds = [d for d in range(3) if int(last["active"]) >> d & 1]
                dmin = min(ds, key=lambda d: float(last["g%d" % d]))
                src = "M" if int(last["m_src"]) >> dmin & 1 else "H"
                rec["g_min_dim"], rec["g_min"], rec["gap"] = dmin, round(float(last["g%d" % dmin]), 3), round(float(last["c%d" % dmin]) - float(last["req%d" % dmin]), 3)
            rec["rate383"] = float(last["rate"])
            tail = [R[t] for t in line_months if t >= max(R) - 24]
            rec["rate_last24_max"] = round(max(float(x["rate"]) for x in tail), 2) if tail else None
            if fix_g:
                cause = "C %s: the line runs, ramp >= 0.7, g < 1 holds yield" % src
            elif g >= 0.95:
                cause = "B output: the line runs, g ~ 1, ramp < 0.7 (" + BIND[last["bind"]] + " binds)"
            else:
                cause = "D mixed: the line runs, ramp < 0.7 and g < 0.95 (%s)" % src
        rec["cause"] = cause
        table[cause.split(" (")[0]] += 1
        if cause.startswith("A"):
            table["A* of them: process in the replacing series design"] += bool(rec["replaced_by_has_node"])
        out.append(rec)
    return out, dict(sorted(table.items()))


def km_block(pool, t7, rows, cls):
    """KM of t_07 - t_first: (a) all pairs with t_first, censored at 383; (b) pairs whose process lost its last line before 383 censored
    at that month (cause-specific: abandonment treated as non-informative - an optimistic bound); (c) t_first <= 263 only."""
    a, b, c = [], [], []
    for k, (tk, tf, _t) in pool.items():
        if tf is None:
            continue
        e = t7.get(k)
        if e is not None:
            a.append((e - tf, True))
            b.append((e - tf, True))
            if tf <= EARLY:
                c.append((e - tf, True))
            continue
        a.append((LAST - max(tf, 0), False))
        if tf <= EARLY:
            c.append((LAST - max(tf, 0), False))
        R = rows.get(k, {})
        if R and R[max(R)]["line"] == "":
            ln = [t for t in R if t >= tf and R[t]["line"] != ""]
            b.append(((max(ln) if ln else tf) - max(tf, 0), False))
        else:
            b.append((LAST - max(tf, 0), False))
    return {"all_censored_at_383": km(a), "abandoned_censored_at_close": km(b), "t_first_le_263": km(c),
            "median_among_reached": q([x[0] for x in a if x[1]], .5)}


def output_block(pool, rows):
    out = {}
    for lag in (48, 96):
        u, noline, n = [], 0, 0
        for k, (tk, tf, _t) in pool.items():
            if tf is None or k not in rows:
                continue
            t = max(tf, 0) + lag
            R = rows[k]
            if t not in R:
                continue
            n += 1
            t0 = max(t - 12, max(tf, 0))
            u.append((float(R[t]["x"]) - float(R[t0]["x"])) / max(1, t - t0))
            noline += R[t]["line"] == ""
        out[str(lag)] = {"n": n, "units_p10_p50_p90": [r3(q(u, .1)), r3(q(u, .5)), r3(q(u, .9))], "no_line_share": r3(noline / n) if n else None}
    return out


def matched_output(poolA, rowsA, poolB, rowsB):
    """Units per month at +48/+96 on the pairs that have t_first in both arms (same seed, actor, node), each at its own t_first."""
    keys = [k for k in poolA if poolA[k][1] is not None and k in poolB and poolB[k][1] is not None]
    out = {"pairs": len(keys)}
    for lag in (48, 96):
        ua, ub = [], []
        for k in keys:
            ta, tb = max(poolA[k][1], 0) + lag, max(poolB[k][1], 0) + lag
            if ta not in rowsA[k] or tb not in rowsB[k]:
                continue
            for rows, t, tf, acc in ((rowsA, ta, poolA[k][1], ua), (rowsB, tb, poolB[k][1], ub)):
                t0 = max(t - 12, max(tf, 0))
                acc.append((float(rows[k][t]["x"]) - float(rows[k][t0]["x"])) / max(1, t - t0))
        out[str(lag)] = {"n": len(ua), "A_p50": r3(q(ua, .5)), "B_p50": r3(q(ub, .5))}
    return out


def money_block(prefix):
    """E money: share to processes only in О designs; series lines >= 36 months old: least yield and unit cost (p10/p50/p90)."""
    e_all = e_o = e_c = 0.0
    ymin, uc = [], []
    by_period = collections.defaultdict(lambda: [0.0, 0.0])
    for r in csv.DictReader(open(prefix + "_lines.csv")):
        t = int(r["t"])
        if r["kind"] == "E":
            a, o, c = float(r["a"]), float(r["b"]), float(r["c"])
            e_all += a
            e_o += o
            e_c += c
            p = by_period[min(t // 96, 3)]
            p[0] += a
            p[1] += o
        elif int(r["c"]) >= 0 and t - int(r["c"]) >= 36 and float(r["d"]) > 0:
            ymin.append(float(r["f"]))
            uc.append(float(r["e"]))
    return {"E_share_O_only": r3(e_o / e_all) if e_all else None, "E_share_series": r3(e_c / e_all) if e_all else None,
            "E_share_O_by_96m": {str(k * 96): r3(v[1] / v[0]) if v[0] else None for k, v in sorted(by_period.items())},
            "mature_lines": {"n_line_months": len(ymin), "ymin_p10_p50_p90": [r3(q(ymin, .1)), r3(q(ymin, .5)), r3(q(ymin, .9))],
                             "unit_cost_p10_p50_p90": [r3(q(uc, .1)), r3(q(uc, .5)), r3(q(uc, .9))]}}


def profile_block(mdir, series, pool, t7, compose_file):
    runs = {r["run"]: int(r["seed"]) for r in csv.DictReader(open(os.path.join(mdir, "runs.csv"))) if r["series"] == series}
    prof = {}
    for r in csv.DictReader(open(os.path.join(mdir, "calib_summary.csv"))):
        if r["run"] in runs:
            for a in ACT:
                prof[(runs[r["run"]], a)] = int(r["prof_" + a])
    out = {}
    fld = collections.defaultdict(list)
    for r in csv.DictReader(open(os.path.join(mdir, "gen_times.csv"))):
        if r["run"] in runs and r["actor"] in ("USA", "GBR", "SUN"):
            fld[(prof[(runs[r["run"]], r["actor"])], int(r["gen"]))].append(int(r["t_fielded"] or -1))
    for p in (1, 2, 3):
        ks = [k for k in pool if prof[(k[0], k[1])] == p]
        n = len(ks)
        g = {}
        for gen in (3, 4):
            xs = fld[(p, gen)]
            done = [x for x in xs if x >= 0]
            g["gen%d_fielded_share" % gen] = r3(len(done) / len(xs)) if xs else None
            g["gen%d_t_fielded_p50" % gen] = q(done, .5)
        out["P%d" % p] = dict(pool=n, t_first=r3(sum(1 for k in ks if pool[k][1] is not None) / n) if n else None,
                              t_07=r3(sum(1 for k in ks if t7.get(k) is not None) / n) if n else None, **g)
    sup = collections.Counter()
    for r in csv.DictReader(open(compose_file)):
        if int(r["count"]) == 2:
            sup[(r["profile"], "n")] += 1
            sup[(r["profile"], "fired")] += int(r["pilot_fired"])
    out["compose_two_variants"] = {"P%s" % p: [sup[(p, "n")], sup[(p, "fired")]] for p in "123"}
    return out


def h2_block(pool, t7):
    d = sorted((t7[k] - pool[k][1], k) for k in pool if pool[k][1] is not None and t7.get(k) is not None)
    return {"n": len(d), "durations": [x[0] for x in d], "pairs": ["%d %s %d t_first %d" % (k[0], k[1], k[2], pool[k][1]) for _, k in d]}


def k3_block(e1):
    return {k: v for k, v in e1["classes"].items() if k.startswith("K3") or k.startswith("K2")} | {"by_actor_K3": {a: c.get("K3", 0) for a, c in e1["by_actor"].items()}}


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("r3")
    ap.add_argument("t59")
    ap.add_argument("host")
    ap.add_argument("--json")
    a = ap.parse_args(argv)
    mdirs = [a.r3, a.t59]
    res = {}
    data = {}
    for arm, (mi, series) in ARMS.items():
        pool = load_pool(mdirs[mi], series)
        t7 = load_times(mdirs[mi], series)
        prefix = os.path.join(a.host, arm)
        rows = host_rows(prefix, set(pool))
        des, cls = designs(prefix), closes(prefix)
        data[arm] = (pool, rows)
        e1 = classify(pool, prefix + "_designs.csv")
        causes, table = early_causes(pool, t7, rows, des, cls)
        n_f = sum(1 for v in pool.values() if v[1] is not None)
        res[arm] = {"pool": len(pool), "with_t_first": n_f, "E2": r3(sum(1 for k, v in pool.items() if v[1] is not None and t7.get(k) is None) / n_f),
                    "early": {"n": sum(1 for v in pool.values() if v[1] is not None and v[1] <= EARLY), "censored": len(causes), "causes": table},
                    "early_pairs": causes, "km": km_block(pool, t7, rows, cls), "output": output_block(pool, rows), "money": money_block(prefix),
                    "profiles": profile_block(mdirs[mi], series, pool, t7, prefix + "_compose.csv"), "h2": h2_block(pool, t7), "k2_k3": k3_block(e1),
                    "closes": dict(collections.Counter(c[2] for v in cls.values() for c in v)),
                    "derive_commands": sum(1 for v in cls.values() for c in v if c[2] == "Derive")}
    res["matched_output_OC_vs_OCR"] = matched_output(*data["OC"], *data["OCR"])
    res["matched_output_CB_vs_R"] = matched_output(*data["CB"], *data["R"])
    text = json.dumps(res, indent=1, sort_keys=True)
    if a.json:
        with open(a.json, "w") as f:
            f.write(text + "\n")
    for arm in ARMS:
        r = res[arm]
        print(arm, "pool", r["pool"], "t_first", r["with_t_first"], "E2", r["E2"], "early", r["early"], "\n   km", r["km"], "\n   out", r["output"],
              "\n   money", r["money"], "\n   closes", r["closes"], "derive", r["derive_commands"])
    print("matched OC/OCR", res["matched_output_OC_vs_OCR"], "\nmatched CB/R", res["matched_output_CB_vs_R"])
    return 0


if __name__ == "__main__":
    sys.exit(main())
