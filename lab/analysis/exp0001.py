#!/usr/bin/env python3
"""EXP-0001 analysis: HYP-0001 criteria 1-7 from CSV run results.

Python stdlib only. Output is deterministic (sorted keys, fixed number
formatting, fixed bootstrap seed). Input format: docs/simulation/exp0001-csv-format.md.

Usage:
    python3 lab/analysis/exp0001.py INPUT_DIR [--config thresholds.json] [--out report.md]
    python3 lab/analysis/exp0001.py --dump-config
"""
import argparse
import copy
import csv
import hashlib
import json
import math
import os
import random
import statistics
import sys

# All thresholds are parameters. Values follow HYP-0001 rev 2 ("Критерии успеха");
# they are frozen by passing a config JSON (pre-registration, EXP-0001 «Ревью» п.1).
DEFAULTS = {
    "years": {"start": 1910, "end": 1950},
    "n_ticks": 492,
    "countries": ["A1", "A2", "A3", "A4", "A5", "A6"],
    "science_archetype": "A1",
    "catchup_archetype": "A6",
    "primary_model": "A",
    "main_series": "main",
    "grid_series": "grid",
    "wilson_z": 1.959963984540054,
    "ci_level": 0.95,
    "c1": {"max_win_share": 0.40, "min_top2_share": 0.05, "exclude_from_min_top2": ["A6"]},
    "c2": {"min_median_changes": 1.0, "min_new_leader_share": 0.30,
           "hysteresis_years": 2, "hysteresis_margin": 0.05, "final_leader": "raw"},
    "c3": {"min_lag_ratio": 1.5, "bootstrap_reps": 1000, "bootstrap_seed": 20261102,
           "others_exclude": [], "max_left_censored_share": 0.30},
    "c4": {"top2_share_min": 0.10, "top2_share_max": 0.40, "min_ks_d": 0.3, "max_ks_p": 0.01,
           "e_growth_years": [1910, 1930]},
    "c5": {"lag_threshold": 0.8, "min_year": 1915, "top2_share_explained": 0.7,
           "min_explained_fraction": 0.80, "noise_unexplained_share": 0.3,
           "bootstrap_reps": 1000, "bootstrap_seed": 20261103},
    "c6": {"max_diversity_ratio": 0.7, "control_models": ["B", "C"]},
    "c7": {"from_window": [1929, 1931], "to_window": [1948, 1950],
           "gain1": 1, "min_share_gain1": 0.50, "gain2": 2, "min_share_gain2": 0.15},
    "decision": {"grid_min_points": 5, "refute_criteria": ["1", "6"],
                 "main_borderline_inconclusive": True},
    "d_trigger": {"top2_share_below": 0.10, "min_points": 5},
}

CRITERIA = ["1", "2", "3", "4", "5", "6", "7"]
LABELS = ["SCIENCE", "ENGINEERS", "MACHINE_PARK", "CAPITAL", "RESOURCES", "NOISE"]
FEATURES = ["mult_S", "mult_E", "mult_Pl", "mult_Nl", "E_growth", "B_end",
            "eng_binding_total", "shock_mean"]
YEARLY_COLS = ["run", "year", "country", "K", "A", "T", "P", "R", "E", "N", "Y_year", "B",
               "eng_binding_months", "shock"]
RUNS_COLS = ["run", "series", "model", "nu", "beta", "gamma", "t_ref", "seed"]
COUNTRIES_COLS = ["run", "country", "mult_S", "mult_E", "mult_Pl", "mult_Nl"]
MILESTONES_COLS = ["run", "country", "j", "q_j", "m_K", "m_A", "m_T"]


class InputError(Exception):
    pass


# ---------------------------------------------------------------- config

def merge_config(base, override, path=""):
    out = copy.deepcopy(base)
    for k, v in override.items():
        if k not in base:
            raise InputError("unknown config key: %s%s" % (path, k))
        if isinstance(base[k], dict):
            if not isinstance(v, dict):
                raise InputError("config key %s%s must be an object" % (path, k))
            out[k] = merge_config(base[k], v, path + k + ".")
        else:
            out[k] = v
    return out


def canonical_json(obj):
    return json.dumps(obj, sort_keys=True, separators=(",", ":"), ensure_ascii=True)


# ---------------------------------------------------------------- statistics

def quantile(values, q):
    """Type-7 (linear) quantile of a non-empty list."""
    xs = sorted(values)
    if not xs:
        return None
    h = (len(xs) - 1) * q
    lo = int(math.floor(h))
    f = h - lo
    if lo + 1 >= len(xs) or f == 0.0 or xs[lo] == xs[lo + 1]:
        return xs[lo]
    a, b = xs[lo], xs[lo + 1]
    if not (math.isfinite(a) and math.isfinite(b)):
        return a if f < 0.5 else b
    return a + (b - a) * f


def wilson(k, n, z):
    if n == 0:
        return (None, None)
    p = k / n
    z2 = z * z
    denom = 1.0 + z2 / n
    centre = (p + z2 / (2.0 * n)) / denom
    half = z * math.sqrt(p * (1.0 - p) / n + z2 / (4.0 * n * n)) / denom
    return (max(0.0, centre - half), min(1.0, centre + half))


def crosses(ci, threshold):
    lo, hi = ci
    if lo is None or hi is None:
        return False
    return lo < threshold < hi


def ks_2samp(x, y):
    """Two-sample Kolmogorov-Smirnov: (D, asymptotic p) with Stephens correction."""
    a, b = sorted(x), sorted(y)
    n, m = len(a), len(b)
    if n == 0 or m == 0:
        return (None, None)
    i = j = 0
    d = 0.0
    while i < n and j < m:
        v = a[i] if a[i] <= b[j] else b[j]
        while i < n and a[i] == v:
            i += 1
        while j < m and b[j] == v:
            j += 1
        d = max(d, abs(i / n - j / m))
    en = math.sqrt(n * m / (n + m))
    return (d, kolmogorov_q((en + 0.12 + 0.11 / en) * d))


def kolmogorov_q(lam):
    """Q_KS(lambda) = 2 sum (-1)^(k-1) exp(-2 k^2 lambda^2) (Numerical Recipes probks)."""
    if lam < 1e-3:
        return 1.0
    a2 = -2.0 * lam * lam
    total, fac, prev = 0.0, 2.0, 0.0
    for k in range(1, 101):
        term = fac * math.exp(a2 * k * k)
        total += term
        if abs(term) <= 1e-3 * prev or abs(term) <= 1e-8 * total:
            return min(1.0, max(0.0, total))
        fac = -fac
        prev = abs(term)
    return 1.0


def norm_entropy(shares, n):
    h = 0.0
    for p in shares:
        if p > 0:
            h -= p * math.log(p)
    return h / math.log(n) if n > 1 else 0.0


def safe_ln_ratio(a, b, eps=1e-12):
    return math.log(max(a, eps) / max(b, eps))


# ---------------------------------------------------------------- loading

def _read_csv(path, required, optional=False):
    if not os.path.exists(path):
        if optional:
            return []
        raise InputError("missing file: %s" % path)
    with open(path, newline="", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        cols = reader.fieldnames or []
        miss = [c for c in required if c not in cols]
        if miss:
            raise InputError("%s: missing columns %s" % (os.path.basename(path), miss))
        return list(reader)


def _opt_int(s):
    s = (s or "").strip()
    return None if s == "" else int(s)


def file_hashes(input_dir):
    out = {}
    for name in ("runs.csv", "yearly.csv", "milestones.csv", "countries.csv"):
        p = os.path.join(input_dir, name)
        if os.path.exists(p):
            with open(p, "rb") as f:
                out[name] = hashlib.sha256(f.read()).hexdigest()
    return out


def load(input_dir, cfg):
    countries = cfg["countries"]
    y0, y1 = cfg["years"]["start"], cfg["years"]["end"]
    years = list(range(y0, y1 + 1))
    runs = {}
    for r in _read_csv(os.path.join(input_dir, "runs.csv"), RUNS_COLS):
        rid = r["run"]
        if rid in runs:
            raise InputError("duplicate run in runs.csv: %s" % rid)
        runs[rid] = {
            # Lab series "<group>/<sub>" (e.g. "main/B/nu0.25") belong to group "main"
            "run": rid, "series": r["series"].split("/", 1)[0], "model": r["model"],
            "nu": float(r["nu"]), "beta": float(r["beta"]), "gamma": float(r["gamma"]),
            "t_ref": float(r["t_ref"]), "seed": int(r["seed"]),
            "yearly": {}, "mult": {}, "milestones": {c: [] for c in countries},
        }
    for r in _read_csv(os.path.join(input_dir, "yearly.csv"), YEARLY_COLS):
        run = runs.get(r["run"])
        if run is None:
            raise InputError("yearly.csv: unknown run %s" % r["run"])
        key = (int(r["year"]), r["country"])
        if key in run["yearly"]:
            raise InputError("yearly.csv: duplicate row %s %s" % (r["run"], key))
        run["yearly"][key] = {c: float(r[c]) for c in YEARLY_COLS[3:]}
    for rid, run in runs.items():
        for y in years:
            for c in countries:
                if (y, c) not in run["yearly"]:
                    raise InputError("yearly.csv: run %s lacks year %d country %s" % (rid, y, c))
    for r in _read_csv(os.path.join(input_dir, "countries.csv"), COUNTRIES_COLS):
        run = runs.get(r["run"])
        if run is None:
            raise InputError("countries.csv: unknown run %s" % r["run"])
        run["mult"][r["country"]] = {c: float(r[c]) for c in COUNTRIES_COLS[2:]}
    for r in _read_csv(os.path.join(input_dir, "milestones.csv"), MILESTONES_COLS, optional=True):
        run = runs.get(r["run"])
        if run is None:
            raise InputError("milestones.csv: unknown run %s" % r["run"])
        if r["country"] in run["milestones"]:
            run["milestones"][r["country"]].append(
                (int(r["j"]), _opt_int(r["m_K"]), _opt_int(r["m_T"])))
    return runs


# ---------------------------------------------------------------- per-run derived

def order_by(values, countries):
    """Countries sorted best-first; ties broken by country index."""
    return sorted(countries, key=lambda c: (-values[c], countries.index(c)))


def derive_run(run, cfg):
    countries = cfg["countries"]
    years = list(range(cfg["years"]["start"], cfg["years"]["end"] + 1))
    power = {y: {c: run["yearly"][(y, c)]["Y_year"] for c in countries} for y in years}
    argmax = {y: order_by(power[y], countries)[0] for y in years}
    # leader changes with hysteresis (EXP-0001 «Ревью» п.3)
    hy, margin = cfg["c2"]["hysteresis_years"], cfg["c2"]["hysteresis_margin"]
    leader, changes = argmax[years[0]], 0
    for idx in range(1, len(years)):
        y = years[idx]
        c = argmax[y]
        if c == leader:
            continue
        ok = power[y][c] > (1.0 + margin) * power[y][leader]
        if not ok and idx + hy - 1 < len(years):
            ok = all(argmax[years[idx + k]] == c for k in range(hy))
        if ok:
            leader, changes = c, changes + 1
    final_order = order_by(power[years[-1]], countries)
    final_leader = final_order[0] if cfg["c2"]["final_leader"] == "raw" else leader

    def window_rank(win):
        ws = [y for y in years if win[0] <= y <= win[1]]
        mean = {c: sum(power[y][c] for y in ws) / len(ws) for c in countries}
        o = order_by(mean, countries)
        return {c: o.index(c) + 1 for c in countries}

    return {
        "power": power, "argmax": argmax, "changes": changes,
        "top1": final_order[0], "top2": set(final_order[:2]),
        "leader_start": argmax[years[0]], "leader_end": final_leader,
        "rank_from": window_rank(cfg["c7"]["from_window"]),
        "rank_to": window_rank(cfg["c7"]["to_window"]),
    }


# ---------------------------------------------------------------- criteria

def crit1(runs, der, cfg):
    p, z = cfg["c1"], cfg["wilson_z"]
    n = len(runs)
    rows, border = {}, False
    for c in cfg["countries"]:
        k1 = sum(1 for d in der if d["top1"] == c)
        k2 = sum(1 for d in der if c in d["top2"])
        rows[c] = {"win": k1 / n, "win_ci": wilson(k1, n, z),
                   "top2": k2 / n, "top2_ci": wilson(k2, n, z)}
    max_c = max(cfg["countries"], key=lambda c: (rows[c]["win"], -cfg["countries"].index(c)))
    elig = [c for c in cfg["countries"] if c not in p["exclude_from_min_top2"]]
    min_c = min(elig, key=lambda c: (rows[c]["top2"], cfg["countries"].index(c)))
    ok = rows[max_c]["win"] <= p["max_win_share"] and rows[min_c]["top2"] >= p["min_top2_share"]
    for c in cfg["countries"]:
        border |= crosses(rows[c]["win_ci"], p["max_win_share"])
    for c in elig:
        border |= crosses(rows[c]["top2_ci"], p["min_top2_share"])
    return {"pass": ok, "borderline": border, "rows": rows,
            "max_win": (max_c, rows[max_c]["win"]), "min_top2": (min_c, rows[min_c]["top2"]),
            "value": "max win %s=%.4f; min top2 %s=%.4f" % (max_c, rows[max_c]["win"], min_c,
                                                          rows[min_c]["top2"])}


def crit2(runs, der, cfg):
    p, z = cfg["c2"], cfg["wilson_z"]
    n = len(der)
    med = statistics.median([d["changes"] for d in der])
    k = sum(1 for d in der if d["leader_end"] != d["leader_start"])
    ci = wilson(k, n, z)
    ok = med >= p["min_median_changes"] and k / n >= p["min_new_leader_share"]
    return {"pass": ok, "borderline": crosses(ci, p["min_new_leader_share"]),
            "median_changes": med, "new_leader_share": k / n, "new_leader_ci": ci,
            "value": "median changes=%s; new leader=%.4f" % (fmt(med), k / n)}


def _lags_for(run, country, n_ticks, stats):
    out = []
    for (_j, mk, mt) in run["milestones"].get(country, []):
        stats["total"] += 1
        if mk is None:
            stats["undiscovered"] += 1
        elif mk == 0 or mt == 0:
            stats["left"] += 1
        elif mt is None:
            stats["right"] += 1
            out.append(n_ticks - mk)
        elif mt < mk:
            stats["import"] += 1
        else:
            out.append(mt - mk)
    return out


def _ratio(a1, others):
    if not a1 or not others:
        return None
    ma, mo = statistics.median(a1), statistics.median(others)
    if mo == 0:
        return math.inf if ma > 0 else None
    return ma / mo


def crit3(runs, der, cfg):
    p = cfg["c3"]
    sci = cfg["science_archetype"]
    others = [c for c in cfg["countries"] if c != sci and c not in p["others_exclude"]]
    zero = {"total": 0, "undiscovered": 0, "left": 0, "right": 0, "import": 0}
    st_a, st_o = dict(zero), dict(zero)
    per_run = []
    for run in runs:
        a = _lags_for(run, sci, cfg["n_ticks"], st_a)
        o = []
        for c in others:
            o.extend(_lags_for(run, c, cfg["n_ticks"], st_o))
        per_run.append((a, o))
    stats = {k: st_a[k] + st_o[k] for k in zero}
    a_all = [x for a, _ in per_run for x in a]
    o_all = [x for _, o in per_run for x in o]
    ratio = _ratio(a_all, o_all)
    # left-censoring share per group; undiscovered milestones are not in the denominator
    left_share = {}
    for name, st in (("a1", st_a), ("others", st_o)):
        den = st["total"] - st["undiscovered"]
        left_share[name] = st["left"] / den if den else 0.0
    left_flag = max(left_share.values()) > p["max_left_censored_share"]
    if ratio is None:
        return {"pass": None, "borderline": False, "stats": stats, "stats_a1": st_a,
                "stats_others": st_o, "left_share": left_share, "value": "N/A (no lags)"}
    rng = random.Random(p["bootstrap_seed"])
    n = len(per_run)
    boots = []
    for _ in range(p["bootstrap_reps"]):
        ba, bo = [], []
        for _i in range(n):
            a, o = per_run[int(rng.random() * n)]
            ba.extend(a)
            bo.extend(o)
        r = _ratio(ba, bo)
        if r is not None:
            boots.append(r)
    alpha = (1.0 - cfg["ci_level"]) / 2.0
    ci = (quantile(boots, alpha), quantile(boots, 1.0 - alpha)) if boots else (None, None)
    border = crosses(ci, p["min_lag_ratio"]) or left_flag
    return {"pass": ratio >= p["min_lag_ratio"], "borderline": border, "ratio": ratio, "ci": ci,
            "median_a1": statistics.median(a_all), "median_others": statistics.median(o_all),
            "stats": stats, "stats_a1": st_a, "stats_others": st_o, "left_share": left_share,
            "value": "ratio=%s CI=[%s; %s]; left-censored A1=%.4f others=%.4f" % (
                fmt(ratio), fmt(ci[0]), fmt(ci[1]), left_share["a1"], left_share["others"])}


def run_features(run, cfg):
    sci = cfg["science_archetype"]
    years = list(range(cfg["years"]["start"], cfg["years"]["end"] + 1))
    yr = lambda y: run["yearly"][(y, sci)]
    g0, g1 = cfg["c4"]["e_growth_years"]
    m = run["mult"].get(sci, {})
    e0 = yr(g0)["E"]
    return {
        "mult_S": m.get("mult_S"), "mult_E": m.get("mult_E"),
        "mult_Pl": m.get("mult_Pl"), "mult_Nl": m.get("mult_Nl"),
        "E_growth": yr(g1)["E"] / e0 if e0 > 0 else None,
        "B_end": yr(years[-1])["B"],
        "eng_binding_total": sum(yr(y)["eng_binding_months"] for y in years),
        "shock_mean": sum(yr(y)["shock"] for y in years) / len(years),
    }


def crit4(runs, der, cfg):
    p, z = cfg["c4"], cfg["wilson_z"]
    sci = cfg["science_archetype"]
    n = len(runs)
    inside = {i for i, d in enumerate(der) if sci in d["top2"]}
    share = len(inside) / n
    ci = wilson(len(inside), n, z)
    feats = [run_features(r, cfg) for r in runs]
    res = []
    for f in FEATURES:
        xa = [feats[i][f] for i in range(n) if i in inside and feats[i][f] is not None]
        xb = [feats[i][f] for i in range(n) if i not in inside and feats[i][f] is not None]
        d, pv = ks_2samp(xa, xb)
        res.append({"feature": f, "D": d, "p": pv,
                    "mean_in": statistics.fmean(xa) if xa else None,
                    "mean_out": statistics.fmean(xb) if xb else None})
    res.sort(key=lambda r: (-(r["D"] if r["D"] is not None else -1.0), FEATURES.index(r["feature"])))
    sig = [r for r in res if r["D"] is not None and r["D"] >= p["min_ks_d"] and r["p"] < p["max_ks_p"]]
    in_range = p["top2_share_min"] <= share <= p["top2_share_max"]
    border = crosses(ci, p["top2_share_min"]) or crosses(ci, p["top2_share_max"])
    return {"pass": in_range and bool(sig), "borderline": border, "share": share, "ci": ci,
            "features": res, "significant": [r["feature"] for r in sig],
            "value": "A1 top2=%.4f; KS-significant: %s" % (
                share, ",".join(r["feature"] for r in sig) or "none")}


def decompose(lead, lag, beta, gamma, t_ref):
    """Bottleneck decomposition of ln(Power_L/Power_i) (model §6, EXP «Ревью» п.5)."""
    gap = safe_ln_ratio(lead["Y_year"], lag["Y_year"])
    c_tech = gamma * math.log((1 + lead["T"] / t_ref) / (1 + lag["T"] / t_ref))
    c_cap = beta * safe_ln_ratio(lead["P"], lag["P"])
    c_res = (1 - beta) * safe_ln_ratio(lead["N"], lag["N"])
    comp = dict.fromkeys(LABELS, 0.0)
    comp["NOISE"] = max(0.0, gap - (c_tech + c_cap + c_res))
    comp["CAPITAL"] = max(0.0, c_cap)
    comp["RESOURCES"] = max(0.0, c_res)
    if c_tech > 0:
        e = lambda r: r["A"] / r["K"] if r["K"] > 0 else 0.0
        m = lambda r: r["T"] / r["A"] if r["A"] > 0 else 0.0
        parts = [max(0.0, safe_ln_ratio(lead["K"], lag["K"])),
                 max(0.0, safe_ln_ratio(e(lead), e(lag))),
                 max(0.0, safe_ln_ratio(m(lead), m(lag)))]
        s = sum(parts)
        if s > 0:
            for lab, x in zip(LABELS[:3], parts):
                comp[lab] = c_tech * x / s
        else:
            comp["MACHINE_PARK"] = c_tech
    return gap, comp


def rank_components(comp):
    return sorted(LABELS, key=lambda l: (-comp[l], LABELS.index(l)))


def crit5(runs, der, cfg):
    p = cfg["c5"]
    cases = {c: [] for c in cfg["countries"]}  # (share, explained, top_label)
    per_run = []                               # (explained, cases) per seed: cluster bootstrap
    for run, d in zip(runs, der):
        k_run = n_run = 0
        for y in sorted(d["power"]):
            if y < p["min_year"]:
                continue
            L = d["argmax"][y]
            for c in cfg["countries"]:
                if c == L or not d["power"][y][c] < p["lag_threshold"] * d["power"][y][L]:
                    continue
                gap, comp = decompose(run["yearly"][(y, L)], run["yearly"][(y, c)],
                                      run["beta"], run["gamma"], run["t_ref"])
                order = rank_components(comp)
                top2 = order[:2]
                share = min(1.0, (comp[top2[0]] + comp[top2[1]]) / gap)
                # DECISIONS_LOG #17: unexplained only if NOISE >= noise_unexplained_share * gap
                # (replaces "NOISE in top-2 -> unexplained"); top-2 share rule unchanged.
                expl = (share >= p["top2_share_explained"]
                        and comp["NOISE"] < p["noise_unexplained_share"] * gap)
                cases[c].append((share, expl, order[0]))
                k_run += 1 if expl else 0
                n_run += 1
        per_run.append((k_run, n_run))
    allc = [x for c in cfg["countries"] for x in cases[c]]
    if not allc:
        return {"pass": None, "borderline": False, "value": "N/A (no lagging cases)", "per": {}}
    frac = sum(1 for x in allc if x[1]) / len(allc)
    rng = random.Random(p["bootstrap_seed"])
    n = len(per_run)
    boots = []
    for _ in range(p["bootstrap_reps"]):
        kk = nn = 0
        for _i in range(n):
            k_run, n_run = per_run[int(rng.random() * n)]
            kk += k_run
            nn += n_run
        if nn:
            boots.append(kk / nn)
    alpha = (1.0 - cfg["ci_level"]) / 2.0
    ci = (quantile(boots, alpha), quantile(boots, 1.0 - alpha)) if boots else (None, None)
    per = {}
    for c in cfg["countries"]:
        xs = cases[c]
        if not xs:
            continue
        sh = [x[0] for x in xs]
        per[c] = {"n": len(xs), "explained": sum(1 for x in xs if x[1]) / len(xs),
                  "p5": quantile(sh, 0.05), "p50": quantile(sh, 0.5), "p95": quantile(sh, 0.95),
                  "labels": {l: sum(1 for x in xs if x[2] == l) / len(xs) for l in LABELS}}
    return {"pass": frac >= p["min_explained_fraction"],
            "borderline": crosses(ci, p["min_explained_fraction"]),
            "fraction": frac, "ci": ci, "n_cases": len(allc), "per": per,
            "value": "explained=%.4f of %d cases CI=[%s; %s]" % (frac, len(allc), fmt(ci[0]),
                                                                  fmt(ci[1]))}


def diversity(der, cfg):
    n = len(der)
    shares = [sum(1 for d in der if d["top1"] == c) / n for c in cfg["countries"]]
    return (norm_entropy(shares, len(cfg["countries"])),
            statistics.median([d["changes"] for d in der]))


def crit7(runs, der, cfg):
    p, z = cfg["c7"], cfg["wilson_z"]
    a6 = cfg["catchup_archetype"]
    n = len(der)
    gains = [d["rank_from"][a6] - d["rank_to"][a6] for d in der]
    k1 = sum(1 for g in gains if g >= p["gain1"])
    k2 = sum(1 for g in gains if g >= p["gain2"])
    ci1, ci2 = wilson(k1, n, z), wilson(k2, n, z)
    ok = k1 / n >= p["min_share_gain1"] and k2 / n >= p["min_share_gain2"]
    return {"pass": ok,
            "borderline": crosses(ci1, p["min_share_gain1"]) or crosses(ci2, p["min_share_gain2"]),
            "share_gain1": k1 / n, "share_gain2": k2 / n, "ci1": ci1, "ci2": ci2,
            "median_gain": statistics.median(gains),
            "value": "gain>=%d: %.4f; gain>=%d: %.4f; median=%s" % (
                p["gain1"], k1 / n, p["gain2"], k2 / n, fmt(statistics.median(gains)))}


# ---------------------------------------------------------------- driver

def group_key(run):
    return (run["series"], run["model"], run["nu"], run["beta"], run["gamma"])


def analyze(runs_by_id, cfg):
    groups = {}
    for rid in sorted(runs_by_id):
        run = runs_by_id[rid]
        groups.setdefault(group_key(run), []).append(run)
    res = {}
    for key in sorted(groups):
        runs = sorted(groups[key], key=lambda r: (r["seed"], r["run"]))
        der = [derive_run(r, cfg) for r in runs]
        g = {"key": key, "n": len(runs), "der": der, "runs": runs,
             "1": crit1(runs, der, cfg), "2": crit2(runs, der, cfg), "3": crit3(runs, der, cfg),
             "4": crit4(runs, der, cfg), "5": crit5(runs, der, cfg), "7": crit7(runs, der, cfg)}
        g["diversity"] = diversity(der, cfg)
        g["extras"] = extras(runs, der, cfg)
        res[key] = g
    # criterion 6: primary model vs each control (same series+point, else main series, same nu)
    for key, g in res.items():
        series, model, nu, beta, gamma = key
        if model != cfg["primary_model"]:
            g["6"] = {"pass": None, "borderline": False, "value": "N/A (control)", "controls": {}}
            continue
        dA, mA = g["diversity"]
        ctrl, ok_all, missing = {}, True, []
        for cm in cfg["c6"]["control_models"]:
            ck = (series, cm, nu, beta, gamma)
            if ck not in res:
                cands = [k for k in res if k[0] == cfg["main_series"] and k[1] == cm and k[2] == nu]
                ck = cands[0] if len(cands) == 1 else None
            if ck is None:
                missing.append(cm)
                continue
            dC, mC = res[ck]["diversity"]
            ok = dC <= cfg["c6"]["max_diversity_ratio"] * dA and mC < mA
            ctrl[cm] = {"D": dC, "median_changes": mC, "pass": ok, "group": ck}
            ok_all &= ok
        if missing:
            g["6"] = {"pass": None, "borderline": False, "controls": ctrl,
                      "value": "N/A (missing control %s)" % ",".join(missing)}
        else:
            g["6"] = {"pass": ok_all, "borderline": False, "controls": ctrl,
                      "value": "D_A=%.4f med_A=%s; " % (dA, fmt(mA)) + "; ".join(
                          "D_%s=%.4f med_%s=%s" % (cm, v["D"], cm, fmt(v["median_changes"]))
                          for cm, v in sorted(ctrl.items()))}
    return {"groups": res, "decision": decide(res, cfg)}


def point_status(g):
    """(all_pass, clean_pass, failed list) over criteria 1-7; N/A counts as not passed."""
    failed = [c for c in CRITERIA if g[c]["pass"] is not True]
    all_pass = not failed
    clean = all_pass and not any(g[c]["borderline"] for c in CRITERIA)
    return all_pass, clean, failed


def decide(res, cfg):
    dc = cfg["decision"]
    pm = cfg["primary_model"]
    nus = sorted({k[2] for k in res if k[1] == pm})
    per_nu = {}
    for nu in nus:
        main = [res[k] for k in res if k[0] == cfg["main_series"] and k[1] == pm and k[2] == nu]
        grid = [res[k] for k in sorted(res) if k[0] == cfg["grid_series"] and k[1] == pm and k[2] == nu]
        m = main[0] if len(main) == 1 else None
        m_all, m_clean, m_failed = point_status(m) if m else (False, False, list(CRITERIA))
        pts = [(g["key"][3], g["key"][4]) + point_status(g) for g in grid]
        n_pass = sum(1 for p in pts if p[2])
        n_clean = sum(1 for p in pts if p[3])
        crit_pass = {c: sum(1 for g in grid if g[c]["pass"] is True) for c in CRITERIA}
        sci = cfg["science_archetype"]
        d_low = sum(1 for g in grid
                    if g["1"]["rows"][sci]["top2"] < cfg["d_trigger"]["top2_share_below"])
        has_na = (m is not None and any(m[c]["pass"] is None for c in CRITERIA)) or any(
            g[c]["pass"] is None for g in grid for c in CRITERIA)
        refuted = any((m is not None and m[c]["pass"] is False)
                      or crit_pass[c] < dc["grid_min_points"] for c in dc["refute_criteria"])
        if m is None or not grid or has_na:
            outcome = "incomplete"   # missing data (e.g. control B/C absent) never refutes
        elif refuted:
            outcome = "refutes"
        elif m_all and n_pass >= dc["grid_min_points"]:
            borderline_only = n_clean < dc["grid_min_points"] or (
                dc["main_borderline_inconclusive"] and not m_clean)
            outcome = "inconclusive" if borderline_only else "supports"
        else:
            outcome = "rework"
        per_nu[nu] = {"main_present": m is not None, "main_pass": m_all, "main_clean": m_clean,
                      "main_failed": m_failed, "has_na": has_na, "points": pts, "n_points": len(grid),
                      "n_pass": n_pass, "n_clean": n_clean, "crit_pass": crit_pass,
                      "d_trigger": d_low >= cfg["d_trigger"]["min_points"], "d_low_points": d_low,
                      "outcome": outcome}
    outs = [v["outcome"] for v in per_nu.values()]
    if not outs or "incomplete" in outs:
        overall = "incomplete"
    elif "refutes" in outs:
        overall = "refutes"
    elif all(o == "supports" for o in outs):
        overall = "supports"
    elif "rework" in outs:
        overall = "rework"
    else:
        overall = "inconclusive"
    return {"per_nu": per_nu, "overall": overall,
            "d_trigger": any(v["d_trigger"] for v in per_nu.values())}


def extras(runs, der, cfg):
    end = cfg["years"]["end"]
    out, lnv = {}, {}
    for c in cfg["countries"]:
        vals = [(d["power"][end][c], r["seed"]) for r, d in zip(runs, der)]
        xs = [v for v, _ in vals]
        mn = min(vals, key=lambda t: (t[0], t[1]))
        mx = max(vals, key=lambda t: (t[0], -t[1]))
        out[c] = {"p5": quantile(xs, 0.05), "p50": quantile(xs, 0.5), "p95": quantile(xs, 0.95),
                  "min": mn, "max": mx}
        lnv[c] = [math.log(v) for v in xs if v > 0]
    within = statistics.fmean([statistics.pvariance(v) for v in lnv.values() if len(v) > 0])
    between = statistics.pvariance([statistics.fmean(v) for v in lnv.values() if v])
    return {"quantiles": out, "within_var_ln": within, "between_var_ln": between}


# ---------------------------------------------------------------- report

def fmt(x):
    if x is None:
        return "—"
    if isinstance(x, bool):
        return "yes" if x else "no"
    if isinstance(x, int):
        return str(x)
    if isinstance(x, float):
        if math.isinf(x):
            return "inf"
        if x == int(x) and abs(x) < 1e15:
            return "%d" % int(x)
        return "%.4f" % x
    return str(x)


def pf(c):
    if c["pass"] is None:
        return "N/A"
    s = "PASS" if c["pass"] else "FAIL"
    return s + (" (на грани)" if c["borderline"] else "")


def gname(key):
    return "%s/%s ν=%s β=%s γ=%s" % (key[0], key[1], fmt(key[2]), fmt(key[3]), fmt(key[4]))


def render(result, cfg, hashes):
    L = ["# EXP-0001 — анализ критериев HYP-0001", ""]
    L.append("- config_sha256: `%s`" % hashlib.sha256(canonical_json(cfg).encode()).hexdigest())
    for name in sorted(hashes):
        L.append("- %s sha256: `%s`" % (name, hashes[name]))
    dec = result["decision"]
    L += ["", "## Решение (модель %s)" % cfg["primary_model"], "",
          "| ν | main | main failed | grid pass / points | clean | триггер D | итог |",
          "|---|---|---|---|---|---|---|"]
    for nu, v in sorted(dec["per_nu"].items()):
        L.append("| %s | %s | %s | %d / %d | %d | %s | %s |" % (
            fmt(nu), "PASS" if v["main_pass"] else "FAIL", ",".join(v["main_failed"]) or "—",
            v["n_pass"], v["n_points"], v["n_clean"],
            "да (%d)" % v["d_low_points"] if v["d_trigger"] else "нет (%d)" % v["d_low_points"],
            v["outcome"]))
    L += ["", "**Итог:** `%s`; триггер D: %s" % (dec["overall"], "да" if dec["d_trigger"] else "нет"), ""]
    L += ["## Сетка β×γ: точки, где критерий проходит", "",
          "| ν | " + " | ".join(CRITERIA) + " |", "|---|" + "---|" * len(CRITERIA)]
    for nu, v in sorted(dec["per_nu"].items()):
        L.append("| %s | " % fmt(nu) + " | ".join(
            "%d/%d" % (v["crit_pass"][c], v["n_points"]) for c in CRITERIA) + " |")
    L += ["", "| ν | β | γ | все 1–7 | без «на грани» | провалены |", "|---|---|---|---|---|---|"]
    for nu, v in sorted(dec["per_nu"].items()):
        for b, gm, ap, cl, failed in v["points"]:
            L.append("| %s | %s | %s | %s | %s | %s |" % (fmt(nu), fmt(b), fmt(gm), fmt(ap),
                                                          fmt(cl), ",".join(failed) or "—"))
    for key, g in sorted(result["groups"].items()):
        L += ["", "## %s (n=%d)" % (gname(key), g["n"]), "",
              "| критерий | значение | итог |", "|---|---|---|"]
        for c in CRITERIA:
            L.append("| %s | %s | %s |" % (c, g[c]["value"], pf(g[c])))
        L += ["", "| архетип | win | win CI | top2 | top2 CI | P1950 p5 | p50 | p95 | min (seed) | max (seed) |",
              "|---|---|---|---|---|---|---|---|---|---|"]
        for c in cfg["countries"]:
            r, q = g["1"]["rows"][c], g["extras"]["quantiles"][c]
            L.append("| %s | %.4f | [%s; %s] | %.4f | [%s; %s] | %s | %s | %s | %s (%d) | %s (%d) |" % (
                c, r["win"], fmt(r["win_ci"][0]), fmt(r["win_ci"][1]), r["top2"],
                fmt(r["top2_ci"][0]), fmt(r["top2_ci"][1]), fmt(q["p5"]), fmt(q["p50"]),
                fmt(q["p95"]), fmt(q["min"][0]), q["min"][1], fmt(q["max"][0]), q["max"][1]))
        e = g["extras"]
        L.append("")
        L.append("Разброс ln Power_%d: внутри архетипа %s, между архетипами %s." % (
            cfg["years"]["end"], fmt(e["within_var_ln"]), fmt(e["between_var_ln"])))
        s = g["3"]["stats"]
        L.append("Вехи (п.3): всего %d, левая цензура %d, правая %d, импорт %d, не открыто %d." % (
            s["total"], s["left"], s["right"], s["import"], s["undiscovered"]))
        L += ["", "| признак (п.4) | KS-D | p | mean A1∈top2 | mean прочие |", "|---|---|---|---|---|"]
        for f in g["4"]["features"]:
            L.append("| %s | %s | %s | %s | %s |" % (f["feature"], fmt(f["D"]), fmt(f["p"]),
                                                     fmt(f["mean_in"]), fmt(f["mean_out"])))
        L += ["", "Примечание: p — асимптотическое приближение KS (Stephens), для малых групп "
              "приблизительно; поправки на множественные сравнения (%d признаков) нет." % len(FEATURES)]
        if g["5"]["per"]:
            L += ["", "| архетип (п.5) | n | объяснено | доля top2 p5 | p50 | p95 | " +
                  " | ".join(LABELS) + " |", "|---|---|---|---|---|---|" + "---|" * len(LABELS)]
            for c, v in sorted(g["5"]["per"].items(), key=lambda t: cfg["countries"].index(t[0])):
                L.append("| %s | %d | %.4f | %s | %s | %s | " % (
                    c, v["n"], v["explained"], fmt(v["p5"]), fmt(v["p50"]), fmt(v["p95"])) +
                    " | ".join("%.4f" % v["labels"][l] for l in LABELS) + " |")
    return "\n".join(L) + "\n"


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("input_dir", nargs="?")
    ap.add_argument("--config", help="JSON with threshold overrides")
    ap.add_argument("--out", help="write markdown here instead of stdout")
    ap.add_argument("--dump-config", action="store_true", help="print default config and exit")
    a = ap.parse_args(argv)
    if a.dump_config:
        sys.stdout.write(json.dumps(DEFAULTS, indent=2, sort_keys=True) + "\n")
        return 0
    if not a.input_dir:
        ap.error("input_dir is required")
    cfg = DEFAULTS
    if a.config:
        try:
            with open(a.config, encoding="utf-8") as f:
                cfg = merge_config(DEFAULTS, json.load(f))
        except (InputError, ValueError, OSError) as e:
            sys.stderr.write("error: config: %s\n" % e)
            return 2
    try:
        result = analyze(load(a.input_dir, cfg), cfg)
    except InputError as e:
        sys.stderr.write("error: %s\n" % e)
        return 2
    text = render(result, cfg, file_hashes(a.input_dir))
    if a.out:
        with open(a.out, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
    else:
        sys.stdout.write(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
