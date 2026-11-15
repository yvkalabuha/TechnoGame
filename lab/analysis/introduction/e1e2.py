#!/usr/bin/env python3
"""HYP-0004 E1/E2 diagnosis (introduction-channel-sketch.md §7). Deterministic, stdlib only.

E1: classes of the C2 pool pairs (spec v0.30 §8.2 p.1, tier-4 star PRC 40/49, t_known <= 335) by where the chain
knowledge -> design -> series breaks, from node_times.csv of the sketch matrix and the design dump of dump_host.
E2: yield parts (g = prod g_d, ramp, X, X_half), units per month and the series-line binding after t_first, from the
dump_host yield file (host month t+1 = prc_month month t).
Usage: e1e2.py MATRIX_DIR SERIES HOST_PREFIX [--json OUT]
"""
import argparse
import collections
import csv
import json
import math
import os
import sys

ACT = ["USA", "GBR", "SUN", "FRA", "SWE"]
LAST, L_INTRO = 383, 48
NODES = (40, 49)
BIND = {"0": "none", "1": "rate", "2": "alloy", "3": "money"}


def oi(s):
    return None if s in ("", None) else int(s)


def q(xs, p):
    if not xs:
        return None
    s = sorted(xs)
    return s[int(math.floor(p * (len(s) - 1)))]


def hot(prc):
    """Rank of the hot part of a prc_set (chain.py of c2-diagnosis): 4 / 3 / 2 (needs 40) / 1 / 0."""
    if {41, 43, 52} <= prc:
        return 4
    if {41, 46, 51} <= prc:
        return 3
    if 40 in prc:
        return 2
    if 39 in prc:
        return 1
    return 0


def load_pool(mdir, series):
    runs = {r["run"]: int(r["seed"]) for r in csv.DictReader(open(os.path.join(mdir, "runs.csv"))) if r["series"] == series}
    pool = {}
    for r in csv.DictReader(open(os.path.join(mdir, "node_times.csv"))):
        if r["run"] in runs and int(r["node"]) in NODES:
            tk, tf, t7, tm = oi(r["t_known"]), oi(r["t_first"]), oi(r["t_07"]), oi(r["t_mastered"])
            if (tm is not None and tm < 0) or tk is None or tk > LAST - L_INTRO:
                continue
            pool[(runs[r["run"]], r["actor"], int(r["node"]))] = (tk, tf, t7)
    return pool


def classify(pool, designs):
    des = collections.defaultdict(list)
    for r in csv.DictReader(open(designs)):
        des[(int(r["seed"]), ACT[int(r["actor"])])].append(dict(
            tc=int(r["t_created"]), tp=int(r["t_proto"]), ts=int(r["t_series"]), tcan=int(r["t_cancel"]),
            prc={int(x) for x in r["prc"].split()}))
    cls = collections.Counter()
    by_actor = collections.defaultdict(collections.Counter)
    for (seed, a, n), (tk, tf, _t7) in sorted(pool.items()):
        ds = sorted(des[(seed, a)], key=lambda x: x["tc"])
        if tf is not None:
            c = "0 t_first"
        else:
            withn = [x for x in ds if n in x["prc"]]
            after = [x for x in ds if x["tc"] >= tk]
            if withn:
                x = withn[0]
                if x["tcan"] >= 0:
                    c = "K4a design with node withdrawn (A40)"
                elif x["ts"] >= 0:
                    c = "K4c design with node in series, no output"
                elif x["tp"] >= 0:
                    c = "K4b design with node in prototype at 383"
                else:
                    c = "K4b design with node in project at 383"
            elif not after:
                c = "K1 no new design after t_known"
            else:
                h = hot(after[0]["prc"])
                c = "K2 first design after t_known: lower rank" if h < (2 if n == 40 else 1) else "K3 first design after t_known: higher rank (skip)"
        cls[c] += 1
        by_actor[a][c[:3].strip()] += 1
    n = sum(cls.values())
    ai_pick = sum(v for k, v in cls.items() if k[:2] in ("K1", "K2", "K3"))
    k4 = sum(v for k, v in cls.items() if k.startswith("K4"))
    return {"pool": n, "classes": {k: [v, v / n] for k, v in sorted(cls.items())},
            "ai_pick_share": ai_pick / n, "design_no_series_share": k4 / n, "ai_path_share": (ai_pick + k4) / n,
            "by_actor": {a: dict(v) for a, v in sorted(by_actor.items())}}


def e2(pool, yfile):
    first = {(s, a, n): v[1] for (s, a, n), v in pool.items() if v[1] is not None}
    rows = collections.defaultdict(dict)
    for r in csv.DictReader(open(yfile)):
        k = (int(r["seed"]), ACT[int(r["actor"])], int(r["node"]))
        if k in first:
            rows[k][int(r["t"]) - 1] = r      # host month t+1 = prc_month month t
    out = {}
    for lag in (0, 48, 96):
        g, ramp, xr, units, bind, hold_ramp, train, xneed = [], [], [], [], collections.Counter(), 0, 0, []
        n = 0
        for k, tf in first.items():
            t = max(tf, 0) + lag
            if t > LAST or t not in rows[k]:
                continue
            r = rows[k][t]
            n += 1
            gv, rv = float(r["g"]), float(r["ramp"])
            x, xh = float(r["x"]), float(r["x_half"])
            g.append(gv)
            ramp.append(rv)
            xr.append(x / xh if xh > 0 else 0.0)
            t0 = max(t - 12, max(tf, 0))
            if t > t0 and t0 in rows[k]:
                units.append((x - float(rows[k][t0]["x"])) / (t - t0))
            bind[BIND.get(r["bind"], "no line") if r["line"] != "" else "no line"] += 1
            if gv >= 0.9 and rv < 0.7:
                hold_ramp += 1
            if r["line"] != "" and r["training_target"] == r["line"]:
                train += 1
            # months to X = 4/3 X_half (ramp 0.7 at lambda0 0.3) at the last-12-month unit rate
            if units and units[-1] > 0:
                xneed.append(max(0.0, (4.0 / 3.0) * xh - x) / units[-1])
        out[str(lag)] = {"n": n,
                         "g_p10_p50_p90": [q(g, .1), q(g, .5), q(g, .9)], "ramp_p10_p50_p90": [q(ramp, .1), q(ramp, .5), q(ramp, .9)],
                         "x_over_xhalf_p50": q(xr, .5), "units_per_month_p10_p50_p90": [q(units, .1), q(units, .5), q(units, .9)],
                         "line_bind": dict(bind), "share_g_ge_0_9_and_ramp_lt_0_7": hold_ramp / n if n else None,
                         "share_training_series_on_line": train / n if n else None,
                         "months_to_ramp_0_7_at_rate_p50": q(xneed, .5)}
    return out


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("matrix")
    ap.add_argument("series")
    ap.add_argument("host_prefix")
    ap.add_argument("--json")
    a = ap.parse_args(argv)
    pool = load_pool(a.matrix, a.series)
    res = {"series": a.series, "E1": classify(pool, a.host_prefix + "_designs.csv"), "E2": e2(pool, a.host_prefix + "_yield.csv")}
    text = json.dumps(res, indent=1, sort_keys=True)
    if a.json:
        with open(a.json, "w") as f:
            f.write(text + "\n")
    print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
