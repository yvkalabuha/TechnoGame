#!/usr/bin/env python3
"""EXP-0002 analysis: HYP-0002 pre-registered decision rule (slice-jet-aviation.md v0.6, section 7).

Python stdlib only. Deterministic: every bootstrap / permutation stream has its own
random.Random seeded from (config seed, statistic label), so results do not depend on the
order of computation. Input format: docs/simulation/slice-csv-format.md.

Usage:
    python3 lab/analysis/exp0002.py DIR [DIR ...] [--config cfg.json] [--out report.md] [--json report.json]
    python3 lab/analysis/exp0002.py --calib DIR [--config cfg.json] [--out calib.md] [--json control_tables.json]
    python3 lab/analysis/exp0002.py --dump-config
    python3 lab/analysis/exp0002.py --selftest [--quick]
"""
import argparse
import collections
import copy
import csv
import hashlib
import json
import math
import os
import random
import statistics
import sys
import tempfile

# Pre-registered thresholds (slice spec v0.6 section 7, #45, #48). Frozen by the manifest
# (meta.analysis.config); changing them after the freeze needs a new EXP.
DEFAULTS = {
    "seed": 20261103,
    "n_months": 384,
    "window_last": 359,
    "hold_months": 24,
    "start_year": 1945,
    "start_month": 9,
    "actors": ["USA", "GBR", "SUN", "FRA", "SWE"],
    "great": ["USA", "GBR", "SUN"],
    "classes": {"USA": "great", "GBR": "great", "SUN": "great", "FRA": "middle", "SWE": "small"},
    "small_actor": "SWE",
    "middle_actor": "FRA",
    "profiles": ["P1", "P2", "P3"],
    "wilson_z": 1.959963984540054,
    "ci_level": 0.95,
    "boot_reps": 2000,
    "perm_reps": 2000,
    "split_reps": 2000,
    "p0_passed": None,
    "series": {"main": "main", "layer_prefix": "layer/", "old_prefix": "old/",
               "channel_prefix": "channel/", "source_prefix": "source/", "grid_prefix": "grid/",
               "start_prefix": "start/", "prof_prefix": "prof/", "lic": "lic/knows", "det": "det",
               "calib": "calib"},
    "p1": {"pass_lo": 0.30, "pass_hi": 0.40, "fail_lo": 0.25, "fail_hi": 0.45},
    "p2": {"max_share": 0.05, "tail_counts": True},
    "p3": {"layers": ["L_K", "L_C", "L_P"], "delta_thr": 0.10, "tvd_thr": 0.15, "split_p95_max": 0.10},
    "p4": {"max_first_share": 0.70, "min_layer_share": 0.05, "min_eng_top3": 0.80, "tol": 0.05,
           "short_months_min": 6, "top_k": 3, "eng_cause": "ENGINEERS",
           "layers": ["KNOWLEDGE", "CAPABILITY", "PRODUCT", "ECONOMY"], "b_na": "ignore"},
    "p5": {"levels": ["lo", "mid", "hi"], "delta_point": 0.20, "delta_ci_lo": 0.10},
    "s1": {"lo": 0.25, "hi": 0.75},
    "s2": {"min_share": 0.20, "ratio": 1.5, "star_prc_gen": {"49": 1, "40": 2, "41": 3, "51": 3, "52": 4}},
    "s3": {"max_share_3plus": 0.05, "min_tenure_p50": 120},
    "doctrine": {"threshold": 0.60},
    "d1": {"factor": 1.5},
    "d2": {"close_within": 36, "lead_within": 60},
    "sources": {"fix_a": "FIX_A", "fix_a_share_flag": 0.5, "chg_low": 0.30},
    "start": {"pert": "PERT", "chg_shift_flag": 0.10},
    "factorial": {"eta2_flag": 0.5},
    "channels": {"kcatch": "kcatch0", "catchup": ["SUN", "FRA", "SWE"], "rel_shift_flag": 0.30,
                 "kd_year_t": 120, "cost_gen": 2},
    "lic": {"engine_nodes": [19, 23]},
    "extremes": {"n": 5},
    "fv_years_t": [120, 240, 348],
    "calib": {"app_nodes": {"1": 58, "2": 59, "3": 60, "4": 61}, "cens_never_share": 0.5,
              "c2_tier4": [40, 49], "c2_tier5": [41, 51, 52], "c2_years": [2, 8],
              "c4_windows": {"2": [1947, 1952], "3": [1954, 1961], "4": [1967, 1975]}, "c4_min_share": 0.5,
              "forbidden_files": ["summary.csv", "actor_year.csv", "whynot.csv", "requirements.csv"],
              "forbidden_substrings": ["lead", "chg", "fv", "why", "key_", "volume"]},
}

# Node table of section 1.3: id -> (type, generation, tier or 0).
NODES = {}
for _i, _t, _g in [(1, "FND", 0), (2, "FND", 0), (3, "FND", 0), (4, "FND", 0), (5, "FND", 2), (6, "FND", 2),
                   (7, "FND", 3), (8, "FND", 3), (9, "PRN", 0), (10, "PRN", 1), (11, "PRN", 1),
                   (12, "PRN", 2), (13, "PRN", 2), (14, "PRN", 2), (15, "PRN", 3), (16, "PRN", 3),
                   (17, "ENG", 1), (18, "ENG", 1), (19, "ENG", 2), (20, "ENG", 2), (21, "ENG", 2),
                   (22, "ENG", 2), (23, "ENG", 3), (24, "ENG", 3), (25, "ENG", 3), (26, "ENG", 3),
                   (27, "ENG", 4), (28, "ENG", 4), (29, "ENG", 4), (30, "CMP", 1), (31, "CMP", 0),
                   (32, "CMP", 3), (33, "CMP", 1), (34, "CMP", 3), (35, "CMP", 4), (36, "CMP", 2),
                   (37, "CMP", 2), (58, "APP", 1), (59, "APP", 2), (60, "APP", 3), (61, "APP", 4)]:
    NODES[_i] = (_t, _g, 0)
for _i, _g, _k in [(38, 0, 3), (39, 1, 4), (40, 2, 4), (41, 3, 5), (42, 3, 5), (43, 4, 5), (44, 0, 3),
                   (45, 1, 4), (46, 3, 4), (47, 0, 3), (48, 1, 4), (49, 1, 4), (50, 2, 4), (51, 3, 5),
                   (52, 4, 5), (53, 0, 3), (54, 1, 4), (55, 2, 4), (56, 3, 4), (57, 2, 4)]:
    NODES[_i] = ("PRC", _g, _k)

FX = 1000000
RUNS_COLS = ["run", "series", "seed", "threads", "status", "golden_hash"]
SUMMARY_COLS = ["run", "prof_USA", "prof_GBR", "prof_SUN", "prof_FRA", "prof_SWE", "lead", "lead_all", "lead_field"]
ACTOR_YEAR_COLS = ["run", "t", "actor", "key_G", "key_T", "key_N", "key_P", "fv", "volume", "budget",
                   "eng_short_months", "kd_aero", "kd_prop", "kd_metal", "kd_chem", "kd_mech"]
WHYNOT_COLS = ["run", "t", "actor", "target", "rank", "layer", "cause", "subject", "eta", "lever",
               "lever_valid", "closed_t"]
NODE_TIMES_COLS = ["run", "actor", "node", "t_known", "t_first", "t_07", "t_mastered", "lic"]
GEN_TIMES_COLS = ["run", "actor", "gen", "t_req", "t_first_unit", "t_fielded", "cost_u100"]
REQ_COLS = ["run", "req", "actor", "gen", "t", "n_variants", "fv_tech", "fv_super", "chosen"]
EVENTS_COLS = ["run", "t", "kind", "actor", "other", "value"]
PRC_YEAR_COLS = ["run", "t", "actor", "node", "known", "admitted", "yield", "ucm"]
CALIB_SUMMARY_COLS = ["run", "prof_USA", "prof_GBR", "prof_SUN", "prof_FRA", "prof_SWE"]
PRC_MONTH_COLS = ["run", "t", "actor", "node", "known", "admitted", "yield", "ucm", "v"]
ACTOR_MONTH_COLS = ["run", "t", "actor", "budget", "proc_budget", "out_g1", "out_g2", "out_g3", "out_g4",
                    "active_g1", "active_g2", "active_g3", "active_g4"]


class InputError(Exception):
    pass


# ---------------------------------------------------------------- config / utils

def merge_config(base, override, path=""):
    out = copy.deepcopy(base)
    for k, v in override.items():
        if k not in base:
            raise InputError("unknown config key: %s%s" % (path, k))
        if isinstance(base[k], dict) and base[k] and not all(kk.isdigit() for kk in base[k]):
            if not isinstance(v, dict):
                raise InputError("config key %s%s must be an object" % (path, k))
            out[k] = merge_config(base[k], v, path + k + ".")
        else:
            out[k] = v
    return out


def canonical_json(obj):
    return json.dumps(obj, sort_keys=True, separators=(",", ":"), ensure_ascii=True)


def sha256_text(s):
    return hashlib.sha256(s.encode("utf-8")).hexdigest()


def rng_for(cfg, label):
    """Independent deterministic stream per statistic label."""
    return random.Random(int(sha256_text("%d|%s" % (cfg["seed"], label))[:16], 16))


def fmt(x, nd=4):
    if x is None:
        return "—"
    if isinstance(x, bool):
        return "да" if x else "нет"
    if isinstance(x, int):
        return str(x)
    if isinstance(x, float):
        if math.isinf(x):
            return "inf"
        if math.isnan(x):
            return "nan"
        return ("%%.%df" % nd) % x
    return str(x)


def fci(ci, nd=4):
    return "[%s; %s]" % (fmt(ci[0], nd), fmt(ci[1], nd)) if ci else "—"


def year_of(cfg, t):
    return cfg["start_year"] + (cfg["start_month"] - 1 + t) // 12


# ---------------------------------------------------------------- statistics

def quantile(values, q):
    """Type-7 (linear) quantile; None for an empty list."""
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


def pq(values, qs=(0.05, 0.5, 0.95)):
    return {"p%d" % round(q * 100): quantile(values, q) for q in qs}


def wilson(k, n, z):
    if n == 0:
        return (None, None)
    p = k / n
    z2 = z * z
    denom = 1.0 + z2 / n
    centre = (p + z2 / (2.0 * n)) / denom
    half = z * math.sqrt(p * (1.0 - p) / n + z2 / (4.0 * n * n)) / denom
    return (max(0.0, centre - half), min(1.0, centre + half))


def share(k, n, cfg):
    return {"k": k, "n": n, "p": (k / n if n else None), "ci": wilson(k, n, cfg["wilson_z"])}


def pct_ci(boots, cfg):
    a = (1.0 - cfg["ci_level"]) / 2.0
    return (quantile(boots, a), quantile(boots, 1.0 - a)) if boots else (None, None)


def abs_lo(ci):
    """Lower bound of |x| implied by a CI of x: 0 if the CI contains 0."""
    lo, hi = ci
    if lo is None:
        return None
    if lo > 0:
        return lo
    if hi < 0:
        return -hi
    return 0.0


def paired_delta(x, y, cfg, label):
    """Delta = mean(x) - mean(y) over paired seeds with a paired percentile bootstrap CI."""
    n = len(x)
    if n == 0:
        return None
    d = [a - b for a, b in zip(x, y)]
    rng = rng_for(cfg, label)
    boots = [sum(rng.choices(d, k=n)) / n for _ in range(cfg["boot_reps"])]
    ci = pct_ci(boots, cfg)
    return {"n": n, "full": sum(x) / n, "off": sum(y) / n, "delta": sum(d) / n, "ci": ci, "abs_lo": abs_lo(ci)}


def tvd_counts(ca, cb, n):
    return 0.5 * sum(abs(ca[i] - cb[i]) for i in range(len(ca))) / n


def tvd_bc(a, b, ncat, cfg, label):
    """Bias-corrected TVD of two paired categorical samples (spec 6.1).

    b = mean TVD over within-pair label permutations; CI = paired bootstrap of TVD* - b."""
    n = len(a)
    if n == 0:
        return None
    ca, cb = [0] * ncat, [0] * ncat
    for u, v in zip(a, b):
        ca[u] += 1
        cb[v] += 1
    obs = tvd_counts(ca, cb, n)
    base = [0] * ncat
    disc = []
    for u, v in zip(a, b):
        if u == v:
            base[u] += 1
        else:
            disc.append((u, v))
    rng = rng_for(cfg, label + "|perm")
    m = len(disc)
    tot = 0.0
    for _ in range(cfg["perm_reps"]):
        fa, fb = list(base), list(base)
        bits = rng.getrandbits(m) if m else 0
        for k, (u, v) in enumerate(disc):
            if (bits >> k) & 1:
                fa[v] += 1
                fb[u] += 1
            else:
                fa[u] += 1
                fb[v] += 1
        tot += tvd_counts(fa, fb, n)
    bias = tot / cfg["perm_reps"]
    codes = [u * ncat + v for u, v in zip(a, b)]
    rng = rng_for(cfg, label + "|boot")
    boots = []
    for _ in range(cfg["boot_reps"]):
        c = collections.Counter(rng.choices(codes, k=n))
        xa, xb = [0] * ncat, [0] * ncat
        for code, cnt in c.items():
            xa[code // ncat] += cnt
            xb[code % ncat] += cnt
        boots.append(tvd_counts(xa, xb, n) - bias)
    return {"n": n, "tvd_obs": obs, "bias": bias, "tvd_bc": obs - bias, "ci": pct_ci(boots, cfg)}


def entropy_norm(counts):
    n = sum(counts)
    if n == 0 or len(counts) < 2:
        return None
    h = -sum(c / n * math.log(c / n) for c in counts if c > 0)
    return h / math.log(len(counts))


def bern_var(xs):
    if not xs:
        return None
    p = sum(xs) / len(xs)
    return p * (1.0 - p)


def eta2(cells):
    """Between-cell share of the variance of a 0/1 indicator; cells: list of lists."""
    allx = [x for c in cells for x in c]
    n = len(allx)
    if n == 0:
        return None
    m = sum(allx) / n
    sst = sum((x - m) ** 2 for x in allx)
    if sst == 0:
        return None
    ssb = sum(len(c) * (sum(c) / len(c) - m) ** 2 for c in cells if c)
    return ssb / sst


# ---------------------------------------------------------------- leadership semantics (section 5.2)

def runs_of(trace):
    """Maximal runs (start, end_exclusive, value) of a sequence."""
    out, i, n = [], 0, len(trace)
    while i < n:
        j = i
        while j < n and trace[j] == trace[i]:
            j += 1
        out.append((i, j, trace[i]))
        i = j
    return out


def counted_changes(trace, last_start, hold):
    """Counted leader changes: a run of a new leader starting at s <= last_start that lasts
    >= hold months (it may extend into the confirmation tail). The reference leader is the last
    confirmed one, so blips shorter than `hold` are not changes and do not reset anything."""
    confirmed = trace[0]
    out = []
    for s, e, v in runs_of(trace):
        if s == 0 or v == confirmed:
            continue
        if s <= last_start and e - s >= hold:
            out.append((s, v, confirmed))
            confirmed = v
    return out


def tenures(trace, changes, window_last):
    starts = [0] + [s for s, _, _ in changes]
    out = []
    for i, s in enumerate(starts):
        nxt = starts[i + 1] if i + 1 < len(starts) else window_last + 1
        out.append(min(nxt, window_last + 1) - s)
    return out


def held_run(trace, value, last_start, hold, tail):
    """True if `value` holds >= hold consecutive months, starting at s <= last_start.
    tail=False: only months 0..last_start count."""
    for s, e, v in runs_of(trace):
        if v != value or s > last_start:
            continue
        length = (e - s) if tail else (min(e, last_start + 1) - s)
        if length >= hold:
            return True
    return False


# ---------------------------------------------------------------- loading

def _read_csv(path, required, optional=False):
    if not os.path.exists(path):
        if optional:
            return None
        raise InputError("missing file: %s" % path)
    with open(path, newline="", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        cols = reader.fieldnames or []
        miss = [c for c in required if c not in cols]
        if miss:
            raise InputError("%s: missing columns %s" % (os.path.basename(path), miss))
        return list(reader)


def _oi(s):
    s = (s or "").strip()
    return None if s == "" else int(s)


def file_hashes(dirs):
    out = {}
    for k, d in enumerate(dirs):
        for name in sorted(os.listdir(d)):
            if name.endswith(".csv"):
                with open(os.path.join(d, name), "rb") as f:
                    out["%d:%s" % (k, name)] = hashlib.sha256(f.read()).hexdigest()
    return out


class Data:
    def __init__(self):
        self.runs = {}            # rid -> dict
        self.series = collections.defaultdict(dict)   # series -> seed -> rid (lowest threads)
        self.failed = []
        self.actor_year = collections.defaultdict(dict)   # rid -> (t, actor) -> row
        self.whynot = collections.defaultdict(list)       # rid -> rows
        self.node_times = collections.defaultdict(dict)   # rid -> (actor, node) -> row
        self.gen_times = collections.defaultdict(dict)    # rid -> (actor, gen) -> row
        self.reqs = collections.defaultdict(list)
        self.events = collections.defaultdict(list)
        self.prc_year = collections.defaultdict(dict)     # rid -> (t, actor, node) -> row
        self.present = set()


def load(dirs, cfg):
    D = Data()
    n = cfg["n_months"]
    acts = cfg["actors"]
    for k, d in enumerate(dirs):
        pre = "%d:" % k
        for r in _read_csv(os.path.join(d, "runs.csv"), RUNS_COLS):
            rid = pre + r["run"]
            if rid in D.runs:
                raise InputError("duplicate run %s" % rid)
            rec = {"run": rid, "series": r["series"], "seed": int(r["seed"]), "threads": int(r["threads"]),
                   "status": r["status"], "golden": r["golden_hash"]}
            D.runs[rid] = rec
            if r["status"] != "ok":
                D.failed.append(rid)
        summ = _read_csv(os.path.join(d, "summary.csv"), SUMMARY_COLS, optional=True) or []
        for r in summ:
            rid = pre + r["run"]
            rec = D.runs.get(rid)
            if rec is None:
                raise InputError("summary.csv: unknown run %s" % r["run"])
            lead = [int(ch) for ch in r["lead"]]
            lead_all = [int(ch) for ch in r["lead_all"]]
            if len(lead) != n or len(lead_all) != n or len(r["lead_field"]) != n:
                raise InputError("summary.csv: run %s: traces must have %d months" % (r["run"], n))
            if any(v >= len(cfg["great"]) for v in lead) or any(v >= len(acts) for v in lead_all):
                raise InputError("summary.csv: run %s: actor index out of range" % r["run"])
            rec["prof"] = {a: int(r["prof_" + a]) for a in acts}
            rec["lead"], rec["lead_all"], rec["lead_field"] = lead, lead_all, r["lead_field"]
        tables = [
            ("actor_year.csv", ACTOR_YEAR_COLS, "actor_year"), ("whynot.csv", WHYNOT_COLS, "whynot"),
            ("node_times.csv", NODE_TIMES_COLS, "node_times"), ("gen_times.csv", GEN_TIMES_COLS, "gen_times"),
            ("requirements.csv", REQ_COLS, "reqs"), ("events.csv", EVENTS_COLS, "events"),
            ("prc_year.csv", PRC_YEAR_COLS, "prc_year")]
        for fname, cols, attr in tables:
            rows = _read_csv(os.path.join(d, fname), cols, optional=True)
            if rows is None:
                continue
            D.present.add(attr)
            store = getattr(D, attr)
            for r in rows:
                rid = pre + r["run"]
                if rid not in D.runs:
                    raise InputError("%s: unknown run %s" % (fname, r["run"]))
                if attr == "actor_year":
                    store[rid][(int(r["t"]), r["actor"])] = {
                        "key": (int(r["key_G"]), int(r["key_T"]), int(r["key_N"]), int(r["key_P"])),
                        "fv": int(r["fv"]), "volume": int(r["volume"]), "budget": int(r["budget"]),
                        "eng_short": int(r["eng_short_months"]),
                        "kd": sum(int(r[c]) for c in ("kd_aero", "kd_prop", "kd_metal", "kd_chem", "kd_mech"))}
                elif attr == "whynot":
                    store[rid].append({"t": int(r["t"]), "actor": r["actor"], "target": r["target"],
                                       "rank": int(r["rank"]), "layer": r["layer"], "cause": r["cause"],
                                       "subject": r["subject"], "eta": _oi(r["eta"]), "lever": r["lever"],
                                       "lever_valid": int(r["lever_valid"]), "closed_t": _oi(r["closed_t"])})
                elif attr == "node_times":
                    store[rid][(r["actor"], int(r["node"]))] = {
                        "t_known": _oi(r["t_known"]), "t_first": _oi(r["t_first"]), "t_07": _oi(r["t_07"]),
                        "t_mastered": _oi(r["t_mastered"]), "lic": int(r["lic"] or 0)}
                elif attr == "gen_times":
                    store[rid][(r["actor"], int(r["gen"]))] = {
                        "t_req": _oi(r["t_req"]), "t_first_unit": _oi(r["t_first_unit"]),
                        "t_fielded": _oi(r["t_fielded"]), "cost_u100": _oi(r["cost_u100"])}
                elif attr == "reqs":
                    store[rid].append({"actor": r["actor"], "gen": int(r["gen"]), "t": int(r["t"]),
                                       "n_variants": int(r["n_variants"]), "fv_tech": int(r["fv_tech"]),
                                       "fv_super": int(r["fv_super"]), "chosen": r["chosen"]})
                elif attr == "events":
                    store[rid].append({"t": int(r["t"]), "kind": r["kind"], "actor": r["actor"],
                                       "other": r["other"], "value": _oi(r["value"])})
                elif attr == "prc_year":
                    store[rid][(int(r["t"]), r["actor"], int(r["node"]))] = {
                        "known": int(r["known"]), "admitted": int(r["admitted"]), "yield": int(r["yield"]),
                        "ucm": int(r["ucm"])}
    # derived per-run leadership
    for rid, rec in D.runs.items():
        if rec["status"] != "ok":
            continue
        if "lead" not in rec:
            if rec["series"] != cfg["series"]["calib"]:
                raise InputError("run %s has no summary.csv row" % rid)
            continue
        derive(rec, cfg)
    for rid in sorted(D.runs, key=lambda r: (D.runs[r]["threads"], r)):
        rec = D.runs[rid]
        if rec["status"] == "ok" and "lead" in rec and rec["series"] != cfg["series"]["det"]:
            D.series[rec["series"]].setdefault(rec["seed"], rid)
    return D


def derive(rec, cfg):
    wl, hold = cfg["window_last"], cfg["hold_months"]
    ch = counted_changes(rec["lead"], wl, hold)
    rec["changes"] = ch
    rec["n_changes"] = len(ch)
    rec["chg"] = 1 if ch else 0
    rec["lead359"] = rec["lead"][wl]
    rec["tenure"] = tenures(rec["lead"], ch, wl)
    acts = cfg["actors"]
    rec["small"] = 1 if held_run(rec["lead_all"], acts.index(cfg["small_actor"]), wl, hold,
                                 cfg["p2"]["tail_counts"]) else 0
    rec["middle"] = 1 if held_run(rec["lead_all"], acts.index(cfg["middle_actor"]), wl, hold,
                                  cfg["p2"]["tail_counts"]) else 0


# ---------------------------------------------------------------- helpers over series

def paired(D, s_a, s_b, seeds=None):
    """Seeds present in both series (optionally restricted), sorted."""
    A, B = D.series.get(s_a, {}), D.series.get(s_b, {})
    common = sorted(set(A) & set(B))
    if seeds is not None:
        common = [s for s in common if s in seeds]
    return common, [D.runs[A[s]] for s in common], [D.runs[B[s]] for s in common]


def layer_test(D, cfg, s_full, s_off, label, seeds=None):
    sd, ra, rb = paired(D, s_full, s_off, seeds)
    if not sd:
        return None
    p = cfg["p3"]
    dl = paired_delta([r["chg"] for r in ra], [r["chg"] for r in rb], cfg, label + "|delta")
    tv = tvd_bc([r["lead359"] for r in ra], [r["lead359"] for r in rb], len(cfg["great"]), cfg, label + "|tvd")
    d_pass = dl["abs_lo"] >= p["delta_thr"]
    d_fail = -p["delta_thr"] < dl["ci"][0] and dl["ci"][1] < p["delta_thr"]
    t_pass = tv["ci"][0] >= p["tvd_thr"]
    t_fail = tv["ci"][1] < p["tvd_thr"]
    return {"seeds": len(sd), "delta": dl, "tvd": tv, "d_pass": d_pass, "d_fail": d_fail,
            "t_pass": t_pass, "t_fail": t_fail}


def layer_verdict(lt, tvd_informative):
    if lt is None:
        return "missing"
    if tvd_informative:
        if lt["d_pass"] or lt["t_pass"]:
            return "pass"
        if lt["d_fail"] and lt["t_fail"]:
            return "fail"
        return "borderline"
    if lt["d_pass"]:
        return "pass"
    if lt["d_fail"]:
        return "fail"
    return "borderline"


def lead_dist(runs, cfg):
    c = [0] * len(cfg["great"])
    for r in runs:
        c[r["lead359"]] += 1
    n = len(runs)
    return {g: share(c[i], n, cfg) for i, g in enumerate(cfg["great"])}, c


# ---------------------------------------------------------------- criteria

def crit_p1(main, cfg):
    p = cfg["p1"]
    k, n = sum(r["chg"] for r in main), len(main)
    s = share(k, n, cfg)
    lo, hi = s["ci"]
    if p["pass_lo"] <= s["p"] <= p["pass_hi"]:
        v = "pass"
    elif hi < p["fail_lo"] or lo > p["fail_hi"]:
        v = "fail"
    else:
        v = "borderline"
    return {"verdict": v, "chg": s}


def crit_p2(main, cfg):
    p = cfg["p2"]
    s = share(sum(r["small"] for r in main), len(main), cfg)
    lo, hi = s["ci"]
    if hi <= p["max_share"]:
        v = "pass"
    elif lo > p["max_share"]:
        v = "fail"
    else:
        v = "borderline"   # point <= 0.05 < upper, or point > 0.05 >= lower (not covered by the table)
    mid = share(sum(r["middle"] for r in main), len(main), cfg)
    return {"verdict": v, "small_lead": s, "middle_lead": mid,
            "note": "sanity check: a pass is not evidence for the hypothesis (critic 15)"}


def split_half(main_runs, cfg):
    n = len(main_runs)
    if n < 4:
        return None
    labels = [r["lead359"] for r in main_runs]
    ncat = len(cfg["great"])
    half = n // 2

    def tv(idx_a, idx_b):
        ca, cb = [0] * ncat, [0] * ncat
        for i in idx_a:
            ca[labels[i]] += 1
        for i in idx_b:
            cb[labels[i]] += 1
        return 0.5 * sum(abs(ca[j] / len(idx_a) - cb[j] / len(idx_b)) for j in range(ncat))

    fixed = tv(range(half), range(half, n))
    rng = rng_for(cfg, "split_half")
    idx = list(range(n))
    vals = []
    for _ in range(cfg["split_reps"]):
        rng.shuffle(idx)
        vals.append(tv(idx[:half], idx[half:]))
    return {"fixed": fixed, "mean": sum(vals) / len(vals), "p95": quantile(vals, 0.95)}


def crit_p3(D, cfg, main_runs):
    sp = split_half(main_runs, cfg)
    informative = sp is not None and sp["p95"] < cfg["p3"]["split_p95_max"]
    layers = {}
    for L in cfg["p3"]["layers"]:
        lt = layer_test(D, cfg, cfg["series"]["main"], cfg["series"]["layer_prefix"] + L, "P3|" + L)
        layers[L] = {"test": lt, "verdict": layer_verdict(lt, informative)}
    vs = [x["verdict"] for x in layers.values()]
    if "missing" in vs:
        v = "missing"
    elif "fail" in vs:
        v = "fail"
    elif all(x == "pass" for x in vs):
        v = "pass"
    else:
        v = "borderline"
    return {"verdict": v, "layers": layers, "split_half": sp, "tvd_informative": informative,
            "note": "OR over two statistics: false pass at the threshold <= 0.05 (accepted, spec 6.1)"}


def whynot_index(D, rid):
    idx = collections.defaultdict(list)
    for w in D.whynot.get(rid, []):
        idx[(w["t"], w["actor"])].append(w)
    for v in idx.values():
        v.sort(key=lambda w: w["rank"])
    return idx


def crit_p4(D, cfg, main_ids):
    p = cfg["p4"]
    if "whynot" not in D.present:
        return {"verdict": "missing"}
    first = collections.Counter()
    by_class = collections.defaultdict(collections.Counter)
    n_ay = 0
    b_k = b_n = 0
    links = null_eta = invalid = 0
    for rid in main_ids:
        idx = whynot_index(D, rid)
        ay = D.actor_year.get(rid, {})
        for (t, a), lst in idx.items():
            lst1 = [w for w in lst if w["rank"] >= 1]
            if not lst1:
                continue
            n_ay += 1
            first[lst1[0]["layer"]] += 1
            by_class[cfg["classes"][a]][lst1[0]["layer"]] += 1
            for w in lst1:
                links += 1
                null_eta += w["eta"] is None
                invalid += w["lever_valid"] == 0
            row = ay.get((t, a))
            if row is not None and row["eng_short"] >= p["short_months_min"]:
                b_n += 1
                if any(w["cause"] == p["eng_cause"] for w in lst1[:p["top_k"]]):
                    b_k += 1
    shares = {L: (first[L] / n_ay if n_ay else None) for L in p["layers"]}
    if n_ay == 0:
        return {"verdict": "missing", "n_actor_years": 0}
    mx = max(shares.values())
    mn = min(shares.values())
    if mx <= p["max_first_share"] and mn >= p["min_layer_share"]:
        va = "pass"
    elif mx <= p["max_first_share"] + p["tol"] and mn >= p["min_layer_share"] - p["tol"]:
        va = "borderline"
    else:
        va = "fail"
    bs = share(b_k, b_n, cfg)
    if b_n == 0:
        vb = "na"
    elif bs["p"] >= p["min_eng_top3"]:
        vb = "pass"
    elif bs["p"] >= p["min_eng_top3"] - p["tol"]:
        vb = "borderline"
    else:
        vb = "fail"
    parts = [va] + ([vb] if vb != "na" or p["b_na"] != "ignore" else [])
    parts = ["borderline" if x == "na" else x for x in parts]
    if "fail" in parts:
        v = "fail"
    elif "borderline" in parts:
        v = "borderline"
    else:
        v = "pass"
    return {"verdict": v, "a": va, "b": vb, "first_layer_share": shares, "n_actor_years": n_ay,
            "first_by_class": {c: dict(cnt) for c, cnt in sorted(by_class.items())},
            "eng_top3": bs, "eta_null_share": (null_eta / links if links else None), "links": links,
            "invalid_levers": invalid,
            "note": "tol 0.05 on the 5% floor makes (a)-min never fail (pre-registered as written)"}


def grid_name(cfg, i, j, k):
    lv = cfg["p5"]["levels"]
    return cfg["series"]["grid_prefix"] + "%s_%s_%s" % (lv[i], lv[j], lv[k])


def crit_p5(D, cfg):
    lv = cfg["p5"]["levels"]
    pts = {}
    for i in range(3):
        for j in range(3):
            for k in range(3):
                name = grid_name(cfg, i, j, k)
                if name in D.series:
                    pts[(i, j, k)] = name
    centre = (1, 1, 1)
    if centre not in pts and len(pts) == 26:
        pts[centre] = cfg["series"]["main"]
    if len(pts) < 27:
        return {"verdict": "missing", "points": len(pts)}
    grid_seeds = set(D.series[pts[(0, 0, 0)]])
    table = {}
    for key, name in sorted(pts.items()):
        rs = [D.runs[D.series[name][s]] for s in sorted(grid_seeds) if s in D.series[name]]
        table["%s_%s_%s" % (lv[key[0]], lv[key[1]], lv[key[2]])] = share(sum(r["chg"] for r in rs), len(rs), cfg)
    pairs = []
    flag = False
    for key in sorted(pts):
        for ax in range(3):
            if key[ax] == 2:
                continue
            nb = list(key)
            nb[ax] += 1
            nb = tuple(nb)
            res = layer_delta(D, cfg, pts[key], pts[nb], "P5|%s|%s" % (key, nb), grid_seeds)
            hit = abs(res["delta"]) > cfg["p5"]["delta_point"] and res["abs_lo"] > cfg["p5"]["delta_ci_lo"]
            flag |= hit
            pairs.append({"a": pts[key], "b": pts[nb], "delta": res["delta"], "ci": res["ci"], "knife": hit})
    return {"verdict": "fail" if flag else "pass", "knife_edge": flag, "points": table, "pairs": pairs,
            "n_pairs": len(pairs)}


def layer_delta(D, cfg, s_a, s_b, label, seeds=None):
    _, ra, rb = paired(D, s_a, s_b, seeds)
    return paired_delta([r["chg"] for r in ra], [r["chg"] for r in rb], cfg, label)


def decide(res, cfg):
    v = {k: res[k]["verdict"] for k in ("P1", "P2", "P3", "P4", "P5")}
    if any(x == "missing" for x in v.values()):
        outcome = "incomplete"
    elif v["P1"] == "fail" or v["P2"] == "fail" or v["P3"] == "fail":
        outcome = "refutes"
    else:
        nb = sum(1 for k in ("P1", "P2", "P3") if v[k] == "borderline")
        ok = all(v[k] in ("pass", "borderline") for k in ("P1", "P2", "P3", "P4"))
        outcome = "supports" if ok and nb <= 1 and v["P5"] == "pass" else "inconclusive"
    p0 = cfg["p0_passed"]
    if p0 is False:
        final = "not_run (P0 failed)"
    elif p0 is None:
        final = outcome + " (P0 not confirmed in config)"
    else:
        final = outcome
    return {"verdicts": v, "outcome": outcome, "final": final}


# ---------------------------------------------------------------- secondary (S1-S3) and diagnostics

def secondary(D, cfg, main_ids):
    out = {}
    # S1
    if "reqs" in D.present:
        win = tie = n = 0
        by_gen = collections.defaultdict(lambda: [0, 0])
        for rid in main_ids:
            for q in D.reqs.get(rid, []):
                if q["n_variants"] < 2:
                    continue
                n += 1
                w = q["fv_tech"] > q["fv_super"]
                win += w
                tie += q["fv_tech"] == q["fv_super"]
                by_gen[q["gen"]][0] += w
                by_gen[q["gen"]][1] += 1
        s = share(win, n, cfg)
        ok = s["p"] is not None and cfg["s1"]["lo"] <= s["p"] <= cfg["s1"]["hi"]
        out["S1"] = {"ok": ok if n else None, "tech_wins": s, "ties": tie,
                     "by_gen": {g: share(a, b, cfg) for g, (a, b) in sorted(by_gen.items())}}
    else:
        out["S1"] = {"ok": None}
    # S2
    if "prc_year" in D.present:
        stars = {int(k): g for k, g in cfg["s2"]["star_prc_gen"].items()}
        great = cfg["great"]
        k = n = k_pos = n_pos = 0
        for rid in main_ids:
            py = D.prc_year.get(rid, {})
            ts = sorted({t for (t, _, _) in py})
            for t in ts:
                for i in range(len(great)):
                    for j in range(i + 1, len(great)):
                        a, b = great[i], great[j]
                        both = [nd for nd in stars
                                if py.get((t, a, nd), {}).get("known") and py.get((t, b, nd), {}).get("known")]
                        if not both:
                            continue
                        nd = max(both, key=lambda x: (stars[x], NODES[x][2], x))
                        ya, yb = py[(t, a, nd)]["yield"], py[(t, b, nd)]["yield"]
                        hi_, lo_ = max(ya, yb), min(ya, yb)
                        if hi_ == 0:
                            continue
                        n += 1
                        hit = lo_ == 0 or hi_ >= cfg["s2"]["ratio"] * lo_
                        k += hit
                        if lo_ > 0:
                            n_pos += 1
                            k_pos += hit
        s = share(k, n, cfg)
        out["S2"] = {"ok": (s["p"] >= cfg["s2"]["min_share"]) if n else None, "share": s,
                     "share_both_positive": share(k_pos, n_pos, cfg)}
    else:
        out["S2"] = {"ok": None}
    # S3
    runs = [D.runs[r] for r in main_ids]
    k3 = sum(1 for r in runs if r["n_changes"] >= 3)
    ten = [x for r in runs for x in r["tenure"]]
    s3 = share(k3, len(runs), cfg)
    p50 = quantile(ten, 0.5)
    ok = s3["p"] <= cfg["s3"]["max_share_3plus"] or (p50 is not None and p50 >= cfg["s3"]["min_tenure_p50"])
    out["S3"] = {"ok": ok, "share_3plus": s3, "tenure": pq(ten, (0.1, 0.5, 0.9)),
                 "flag": None if ok else "дребезг лидерства"}
    return out


def leadership_prints(D, cfg, main_ids):
    runs = [D.runs[r] for r in main_ids]
    great = cfg["great"]
    nch = [r["n_changes"] for r in runs]
    firsts = [r["changes"][0][0] for r in runs if r["changes"]]
    new_leader = collections.Counter(great[v] for r in runs for _, v, _ in r["changes"])
    fields = collections.Counter(r["lead_field"][s] for r in runs for s, _, _ in r["changes"])
    hist = collections.Counter(year_of(cfg, s) for s in firsts)
    dist, _ = lead_dist(runs, cfg)
    total_new = sum(new_leader.values())
    vol = collections.Counter()
    for rid in main_ids:
        ay = D.actor_year.get(rid, {})
        t = max((t for (t, _) in ay if t <= cfg["window_last"]), default=None)
        if t is None:
            continue
        best = max(cfg["actors"], key=lambda a: (ay.get((t, a), {"volume": -1})["volume"], -cfg["actors"].index(a)))
        vol[best] += 1
    fvr = {}
    for t in cfg["fv_years_t"]:
        for i in range(len(great)):
            for j in range(i + 1, len(great)):
                a, b = great[i], great[j]
                xs, zero = [], 0
                for rid in main_ids:
                    ay = D.actor_year.get(rid, {})
                    if (t, a) not in ay or (t, b) not in ay:
                        continue
                    fb = ay[(t, b)]["fv"]
                    if fb == 0:
                        zero += 1
                        continue
                    xs.append(ay[(t, a)]["fv"] / fb)
                if xs or zero:
                    fvr["%d:%s/%s" % (year_of(cfg, t), a, b)] = dict(pq(xs), n=len(xs), zero_den=zero)
    return {"n_changes": dict(pq(nch, (0.05, 0.5, 0.95)), mean=(sum(nch) / len(nch) if nch else None)),
            "first_change_t": pq(firsts), "first_change_year_hist": dict(sorted(hist.items())),
            "new_leader_share": {g: (new_leader[g] / total_new if total_new else None) for g in great},
            "change_field": dict(sorted(fields.items())), "lead359": dist,
            "volume_lead_share": {a: vol[a] / max(1, sum(vol.values())) for a in cfg["actors"]},
            "fv_ratios": fvr}


def node_time_prints(D, cfg, main_ids):
    if "node_times" not in D.present:
        return None
    out = {}
    stars = sorted(int(k) for k in cfg["s2"]["star_prc_gen"])
    for a in cfg["actors"]:
        for nd in stars:
            row = {}
            for col in ("t_known", "t_first", "t_07", "t_mastered"):
                vals, cens, left = [], 0, 0
                for rid in main_ids:
                    x = D.node_times.get(rid, {}).get((a, nd))
                    if x is None:
                        continue
                    v = x[col]
                    if v is None:
                        cens += 1
                    elif v < 0:
                        left += 1
                    else:
                        vals.append(v)
                tot = len(vals) + cens
                row[col] = dict(pq(vals, (0.1, 0.5, 0.9)), right_cens=(cens / tot if tot else None), left=left)
            out["%s:%d" % (a, nd)] = row
    return out


def doctrine(D, cfg, main_ids):
    great = cfg["great"]
    runs = [D.runs[r] for r in main_ids]
    n = len(runs)
    out = {}
    flag = False
    tot_changes = sum(r["n_changes"] for r in runs)
    for p in range(1, len(cfg["profiles"]) + 1):
        k_lead = sum(1 for r in runs if r["prof"][great[r["lead359"]]] == p)
        k_ch = sum(1 for r in runs for _, v, _ in r["changes"] if r["prof"][great[v]] == p)
        n_gp = sum(1 for r in runs for g in great if r["prof"][g] == p)
        k_gp = sum(1 for r in runs if r["prof"][great[r["lead359"]]] == p)
        a = share(k_lead, n, cfg)
        b = share(k_ch, tot_changes, cfg)
        flag |= (a["p"] or 0) > cfg["doctrine"]["threshold"] or (b["p"] or 0) > cfg["doctrine"]["threshold"]
        out["P%d" % p] = {"lead359_share": a, "change_share": b,
                          "profile_share_great": (n_gp / (n * len(great)) if n else None),
                          "p_lead_given_profile": share(k_gp, n_gp, cfg)}
    return {"profiles": out, "dominant_doctrine": flag}


def diag_d1(D, cfg, main_ids):
    if "whynot" not in D.present:
        return None
    last = cfg["n_months"] - 1
    f = cfg["d1"]["factor"]
    hit = n = excl = 0
    ratios = []
    for rid in main_ids:
        for w in D.whynot.get(rid, []):
            if w["rank"] < 1 or w["eta"] is None:
                continue
            c = w["closed_t"]
            if c is None:
                if w["t"] + f * w["eta"] > last:
                    excl += 1
                    continue
                n += 1
                continue
            n += 1
            if c - w["t"] <= f * w["eta"]:
                hit += 1
            if w["eta"] > 0:
                ratios.append((c - w["t"]) / w["eta"])
    return {"eta_hit": share(hit, n, cfg), "excluded_censored": excl, "ratio": pq(ratios, (0.1, 0.5, 0.9))}


def diag_d2(D, cfg, main_ids):
    if "whynot" not in D.present:
        return None
    great = cfg["great"]
    cw, lw = cfg["d2"]["close_within"], cfg["d2"]["lead_within"]
    last = cfg["n_months"] - 1
    per_seed = []
    no_link = 0
    for rid in main_ids:
        r = D.runs[rid]
        idx = whynot_index(D, rid)
        units = []
        y = 0
        while 12 * y + lw <= last:
            t0 = 12 * y
            for gi, g in enumerate(great):
                if r["lead"][t0] == gi:
                    continue
                lst = [w for w in idx.get((t0, g), []) if w["rank"] >= 1]
                if not lst:
                    no_link += 1
                closed = bool(lst) and lst[0]["closed_t"] is not None and lst[0]["closed_t"] - t0 <= cw
                became = any(v == gi and t0 < s <= t0 + lw for s, v, _ in r["changes"])
                units.append((1 if closed else 0, 1 if became else 0))
            y += 1
        per_seed.append(units)

    def rates(clusters):
        a = [0, 0]
        b = [0, 0]
        for units in clusters:
            for c, o in units:
                if c:
                    a[0] += o
                    a[1] += 1
                else:
                    b[0] += o
                    b[1] += 1
        return a, b

    a, b = rates(per_seed)
    diff = (a[0] / a[1] - b[0] / b[1]) if a[1] and b[1] else None
    rng = rng_for(cfg, "D2|cluster")
    boots = []
    for _ in range(cfg["boot_reps"]):
        x, y_ = rates(rng.choices(per_seed, k=len(per_seed)))
        if x[1] and y_[1]:
            boots.append(x[0] / x[1] - y_[0] / y_[1])
    return {"closed": share(a[0], a[1], cfg), "not_closed": share(b[0], b[1], cfg), "diff": diff,
            "diff_ci": pct_ci(boots, cfg), "units_without_link": no_link,
            "note": "correlation in an AI world, not causation"}


def sources(D, cfg):
    sp = cfg["series"]["source_prefix"]
    names = sorted(s for s in D.series if s.startswith(sp))
    if not names:
        return None
    out = {"variants": {}}
    main = cfg["series"]["main"]
    for s in names:
        sd, rf, rv = paired(D, main, s)
        if s.endswith("FIX_ALL"):
            continue
        _, cnt = lead_dist(rv, cfg)
        vf, vv = bern_var([r["chg"] for r in rf]), bern_var([r["chg"] for r in rv])
        out["variants"][s[len(sp):]] = {
            "seeds": len(sd), "chg": share(sum(r["chg"] for r in rv), len(rv), cfg),
            "chg_main_same_seeds": share(sum(r["chg"] for r in rf), len(rf), cfg),
            "lead359_entropy": entropy_norm(cnt),
            "var_share_removed": (1.0 - vv / vf) if vf else None}
    fa = out["variants"].get(cfg["sources"]["fix_a"], {})
    out["fix_a_dominates"] = (fa.get("var_share_removed") or 0) > cfg["sources"]["fix_a_share_flag"]
    fall = sp + "FIX_ALL"
    if fall in D.series:
        hs = {D.runs[r]["golden"] for r in D.series[fall].values()}
        out["fix_all_unique_hashes"] = len(hs)
        out["fix_all_ok"] = len(hs) == 1
    return out


def compare_series(D, cfg, prefix, label, seeds=None):
    names = sorted(s for s in D.series if s.startswith(prefix))
    out = {}
    for s in names:
        lt = layer_test(D, cfg, cfg["series"]["main"], s, label + "|" + s, seeds)
        if lt is None:
            continue
        _, _, rv = paired(D, cfg["series"]["main"], s, seeds)
        dist, _ = lead_dist(rv, cfg)
        out[s[len(prefix):]] = {"seeds": lt["seeds"], "chg": share(sum(r["chg"] for r in rv), len(rv), cfg),
                                "delta": lt["delta"], "tvd": lt["tvd"], "lead359": dist}
    return out


def start_diag(D, cfg):
    out = compare_series(D, cfg, cfg["series"]["start_prefix"], "START")
    if not out:
        return None
    pert = out.get(cfg["start"]["pert"])
    sens = pert is not None and abs(pert["delta"]["delta"]) > cfg["start"]["chg_shift_flag"]
    return {"variants": out, "sensitive_to_start": sens}


def old_controls(D, cfg):
    return compare_series(D, cfg, cfg["series"]["old_prefix"], "OLD") or None


def mastering_p50(D, cfg, rids, actor):
    xs = []
    for rid in rids:
        for nd in (int(k) for k in cfg["s2"]["star_prc_gen"]):
            x = D.node_times.get(rid, {}).get((actor, nd))
            if x and x["t_first"] is not None and x["t_first"] >= 0:
                end = x["t_07"] if x["t_07"] is not None else cfg["n_months"]
                xs.append(end - x["t_first"])
    return quantile(xs, 0.5)


def channels(D, cfg):
    pre = cfg["series"]["channel_prefix"]
    base = compare_series(D, cfg, pre, "CH")
    if not base:
        return None
    c = cfg["channels"]
    flag = False
    for name, v in base.items():
        sd, rf, rv = paired(D, cfg["series"]["main"], pre + name)
        fid, vid = [r["run"] for r in rf], [r["run"] for r in rv]
        mt = {}
        for a in cfg["actors"]:
            m0, m1 = mastering_p50(D, cfg, fid, a), mastering_p50(D, cfg, vid, a)
            rel = (m1 - m0) / m0 if (m0 and m1 is not None) else None
            mt[a] = {"main": m0, "variant": m1, "rel": rel}
            if name == c["kcatch"] and a in c["catchup"] and rel is not None and abs(rel) > c["rel_shift_flag"]:
                flag = True
        v["mastering_p50"] = mt

        def kd(ids):
            xs = [D.actor_year[r][(c["kd_year_t"], g)]["kd"] / FX for r in ids for g in cfg["great"]
                  if (c["kd_year_t"], g) in D.actor_year.get(r, {})]
            return sum(xs) / len(xs) if xs else None

        def cost(ids):
            xs = [D.gen_times[r][(a, c["cost_gen"])]["cost_u100"] / FX for r in ids for a in cfg["actors"]
                  if D.gen_times.get(r, {}).get((a, c["cost_gen"]), {}).get("cost_u100") is not None]
            return quantile(xs, 0.5)
        v["kd_great_mean"] = {"main": kd(fid), "variant": kd(vid)}
        v["cost_u100_p50"] = {"main": cost(fid), "variant": cost(vid)}
    return {"variants": base, "kcatch_access_flag": flag}


def factorial(D, cfg, main_ids):
    pre = cfg["series"]["prof_prefix"]
    names = sorted(s for s in D.series if s.startswith(pre))
    if not names:
        return None
    great = cfg["great"]
    cells = {}
    for s in names:
        cells[s[len(pre):]] = [D.runs[r]["chg"] for _, r in sorted(D.series[s].items())]
    e = eta2(list(cells.values()))
    rng = rng_for(cfg, "FACT|eta2")
    keys = sorted(cells)
    boots = []
    for _ in range(cfg["boot_reps"]):
        b = eta2([rng.choices(cells[k], k=len(cells[k])) for k in keys])
        if b is not None:
            boots.append(b)
    marg = {}
    for gi, g in enumerate(great):
        for p in cfg["profiles"]:
            xs = [x for k in keys if k.split("_")[gi] == p for x in cells[k]]
            marg["%s=%s" % (g, p)] = share(sum(xs), len(xs), cfg)
    mc = collections.defaultdict(list)
    for rid in main_ids:
        r = D.runs[rid]
        mc[tuple(r["prof"][g] for g in great)].append(r["chg"])
    em = eta2(list(mc.values()))
    flag = (e or 0) > cfg["factorial"]["eta2_flag"]
    return {"cells": {k: share(sum(v), len(v), cfg) for k, v in cells.items()}, "eta2": e,
            "eta2_ci": pct_ci(boots, cfg), "marginal": marg, "eta2_main": em, "eta2_main_cells": len(mc),
            "profiles_main_source": flag}


def lic_diag(D, cfg):
    s = cfg["series"]["lic"]
    if s not in D.series:
        return None
    lt = layer_test(D, cfg, cfg["series"]["main"], s, "LIC")
    sd, rf, rv = paired(D, cfg["series"]["main"], s)

    def buys(rec):
        return [e for e in D.events.get(rec["run"], []) if e["kind"] == "LIC_BUY"]

    nb_l = sum(len(buys(r)) for r in rv)
    nb_m = sum(len(buys(r)) for r in rf)
    shift = {}
    for nd in cfg["lic"]["engine_nodes"]:
        xm, xl = [], []
        for a_rec, b_rec in zip(rf, rv):
            for e in buys(b_rec):
                for rec, xs in ((a_rec, xm), (b_rec, xl)):
                    x = D.node_times.get(rec["run"], {}).get((e["actor"], nd))
                    if x is not None:
                        v = x["t_known"]
                        xs.append(cfg["n_months"] if v is None else max(v, 0))
        p0, p1 = quantile(xm, 0.5), quantile(xl, 0.5)
        shift[nd] = {"main_p50": p0, "lic_p50": p1, "shift": (p1 - p0) if (p0 is not None and p1 is not None) else None,
                     "n": len(xl)}
    great = cfg["great"]

    def new_leader_bought(recs):
        k = n = 0
        for r in recs:
            bl = buys(r)
            for st, v, _ in r["changes"]:
                n += 1
                k += any(e["actor"] == great[v] and e["t"] < st for e in bl)
        return share(k, n, cfg)

    return {"delta": lt["delta"], "tvd": lt["tvd"], "licences": {"lic_knows": nb_l, "main": nb_m, "seeds": len(sd)},
            "engine_t_known_shift": shift, "new_leaders_bought": new_leader_bought(rv),
            "new_leaders_bought_main": new_leader_bought(rf)}


def determinism(D, cfg):
    det = [r for r in D.runs.values() if r["series"] == cfg["series"]["det"]]
    if not det:
        return None
    main = D.series.get(cfg["series"]["main"], {})
    by_seed = collections.defaultdict(set)
    for r in det:
        by_seed[r["seed"]].add(r["golden"])
    bad = []
    for s, hs in sorted(by_seed.items()):
        if len(hs) != 1 or (s in main and D.runs[main[s]]["golden"] not in hs):
            bad.append(s)
    return {"seeds": len(by_seed), "threads": sorted({r["threads"] for r in det}), "mismatch_seeds": bad,
            "ok": not bad}


def extremes(D, cfg, main_ids):
    n = cfg["extremes"]["n"]
    great = cfg["great"]
    runs = [D.runs[r] for r in main_ids]
    by_ch = sorted(runs, key=lambda r: (-r["n_changes"], r["seed"]))[:n]

    def gap(r):
        ay = D.actor_year.get(r["run"], {})
        best = None
        for (t, a) in ay:
            if t > cfg["window_last"] or a != great[r["lead"][t]]:
                continue
            others = [ay[(t, g)]["key"] for g in great if g != a and (t, g) in ay]
            if not others:
                continue
            o = max(others)
            d = tuple(x - y for x, y in zip(ay[(t, a)]["key"], o))
            if best is None or d > best:
                best = d
        return best or (0, 0, 0, 0)

    by_gap = sorted(runs, key=lambda r: (tuple(-x for x in gap(r)), r["seed"]))[:n]

    def tape(r):
        periods = ["%s %d–%d" % (great[v], s, e - 1) for s, e, v in runs_of(r["lead"])]
        idx = whynot_index(D, r["run"])
        wn = []
        for (t, a), lst in sorted(idx.items()):
            lst = [w for w in lst if w["rank"] >= 1]
            if a in great and lst:
                wn.append("%d %s: %s/%s/%s eta=%s" % (year_of(cfg, t), a, lst[0]["layer"], lst[0]["cause"],
                                                     lst[0]["subject"], fmt(lst[0]["eta"])))
        return {"seed": r["seed"], "n_changes": r["n_changes"], "gap": list(gap(r)), "lead_periods": periods,
                "whynot_first": wn}
    return {"most_changes": [tape(r) for r in by_ch], "largest_gap": [tape(r) for r in by_gap]}


# ---------------------------------------------------------------- top level

def analyze(D, cfg):
    main_s = cfg["series"]["main"]
    main_map = D.series.get(main_s, {})
    main_ids = [main_map[s] for s in sorted(main_map)]
    main_runs = [D.runs[r] for r in main_ids]
    res = {"problems": {"failed_runs": len(D.failed)}}
    if not main_runs:
        res["decision"] = {"verdicts": {}, "outcome": "incomplete", "final": "incomplete (no main)"}
        return res
    res["P1"] = crit_p1(main_runs, cfg)
    res["P2"] = crit_p2(main_runs, cfg)
    res["P3"] = crit_p3(D, cfg, main_runs)
    res["P4"] = crit_p4(D, cfg, main_ids)
    res["P5"] = crit_p5(D, cfg)
    res["decision"] = decide(res, cfg)
    if D.failed:
        res["decision"]["final"] += "; WARNING: %d failed runs" % len(D.failed)
    res["secondary"] = secondary(D, cfg, main_ids)
    res["leadership"] = leadership_prints(D, cfg, main_ids)
    res["node_times"] = node_time_prints(D, cfg, main_ids)
    res["doctrine"] = doctrine(D, cfg, main_ids)
    res["D1"] = diag_d1(D, cfg, main_ids)
    res["D2"] = diag_d2(D, cfg, main_ids)
    res["sources"] = sources(D, cfg)
    if res["sources"] is not None and res["P1"]["chg"]["p"] < cfg["sources"]["chg_low"]:
        res["sources"]["attach_to_report"] = True
    res["start"] = start_diag(D, cfg)
    res["old_controls"] = old_controls(D, cfg)
    res["channels"] = channels(D, cfg)
    res["factorial"] = factorial(D, cfg, main_ids)
    res["lic_knows"] = lic_diag(D, cfg)
    res["determinism"] = determinism(D, cfg)
    res["extremes"] = extremes(D, cfg, main_ids)
    res["main"] = {"seeds": len(main_runs),
                   "golden_hash": sha256_text("".join("%d %s\n" % (D.runs[r]["seed"], D.runs[r]["golden"])
                                                      for r in main_ids))}
    return res


def jsonable(x):
    if isinstance(x, dict):
        return {str(k): jsonable(v) for k, v in x.items()}
    if isinstance(x, (list, tuple)):
        return [jsonable(v) for v in x]
    if isinstance(x, float):
        if math.isinf(x) or math.isnan(x):
            return str(x)
        return round(x, 6)
    return x


# ---------------------------------------------------------------- rendering

def fs(s):
    if not s or s.get("n") in (None, 0):
        return "—"
    return "%s %s (k=%d, n=%d)" % (fmt(s["p"]), fci(s["ci"]), s["k"], s["n"])


def render(res, cfg, hashes):
    L = ["# EXP-0002 — анализ HYP-0002 (правило решения §7, slice-jet-aviation.md v0.6)", ""]
    L.append("- config_sha256: `%s`" % sha256_text(canonical_json(cfg)))
    with open(os.path.abspath(__file__), "rb") as f:
        L.append("- script_sha256: `%s`" % hashlib.sha256(f.read()).hexdigest())
    for k in sorted(hashes):
        L.append("- %s sha256: `%s`" % (k, hashes[k]))
    dec = res["decision"]
    L += ["", "## Исход", "", "**%s**" % dec["final"], ""]
    if "main" in res:
        L.append("main: seeds %d, golden_hash (sha256 «seed golden\\n» по возрастанию seed) `%s`; упавших прогонов %d."
                 % (res["main"]["seeds"], res["main"]["golden_hash"], res["problems"]["failed_runs"]))
    if "P1" not in res:
        return "\n".join(L) + "\n"
    L += ["", "| критерий | значение | итог |", "|---|---|---|"]
    L.append("| P1 `chg` | %s | %s |" % (fs(res["P1"]["chg"]), res["P1"]["verdict"]))
    L.append("| P2 `small_lead` (sanity) | %s | %s |" % (fs(res["P2"]["small_lead"]), res["P2"]["verdict"]))
    L.append("| P3 слои | %s | %s |" % (", ".join("%s=%s" % (k, v["verdict"]) for k, v in res["P3"]["layers"].items()),
                                         res["P3"]["verdict"]))
    p4 = res["P4"]
    if p4["verdict"] != "missing":
        L.append("| P4 читаемость | (a) %s: %s; (b) %s: %s | %s |" % (
            p4["a"], ", ".join("%s %s" % (k, fmt(v)) for k, v in p4["first_layer_share"].items()),
            p4["b"], fs(p4["eng_top3"]), p4["verdict"]))
    else:
        L.append("| P4 читаемость | нет данных | missing |")
    p5 = res["P5"]
    L.append("| P5 сетка | %s | %s |" % ("knife_edge=%s, пар %s" % (fmt(p5.get("knife_edge")), p5.get("n_pairs", "—")),
                                         p5["verdict"]))
    L += ["", "P2 — проверка здравого смысла: проход не свидетельствует в пользу гипотезы. FRA ≥ 24 мес.: %s." %
          fs(res["P2"]["middle_lead"])]
    L += ["", "## P3 по слоям", "",
          "split-half TVD main: фикс. 1…N/2 против N/2+1…N %s; случайные разбиения: среднее %s, p95 %s → TVD %s." % (
              fmt(res["P3"]["split_half"] and res["P3"]["split_half"]["fixed"]),
              fmt(res["P3"]["split_half"] and res["P3"]["split_half"]["mean"]),
              fmt(res["P3"]["split_half"] and res["P3"]["split_half"]["p95"]),
              "информативна" if res["P3"]["tvd_informative"] else "НЕинформативна (P3 только по Δ)"), "",
          "| слой | seeds | chg full | chg off | Δ | CI Δ | |Δ| low | TVD obs | b | TVD_bc | CI TVD_bc | итог |",
          "|---|---|---|---|---|---|---|---|---|---|---|---|"]
    for k, v in res["P3"]["layers"].items():
        t = v["test"]
        if t is None:
            L.append("| %s | — | | | | | | | | | | missing |" % k)
            continue
        L.append("| %s | %d | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s |" % (
            k, t["seeds"], fmt(t["delta"]["full"]), fmt(t["delta"]["off"]), fmt(t["delta"]["delta"]),
            fci(t["delta"]["ci"]), fmt(t["delta"]["abs_lo"]), fmt(t["tvd"]["tvd_obs"]), fmt(t["tvd"]["bias"]),
            fmt(t["tvd"]["tvd_bc"]), fci(t["tvd"]["ci"]), v["verdict"]))
    L.append("")
    L.append("«Или» по двум статистикам: ложный проход у порога ≤ 0,05 (принято, §6.1).")
    if p5["verdict"] != "missing":
        L += ["", "## P5 сетка (`k_catch` × `k_diff` × `h_max`)", "", "| точка | chg |", "|---|---|"]
        for k, v in p5["points"].items():
            L.append("| %s | %s |" % (k, fs(v)))
        flagged = [p for p in p5["pairs"] if p["knife"]]
        L += ["", "Пары с флагом: %s" % (", ".join("%s↔%s Δ=%s CI %s" % (p["a"], p["b"], fmt(p["delta"]), fci(p["ci"]))
                                                    for p in flagged) or "нет")]
    sec = res["secondary"]
    L += ["", "## S1–S3 (на исход не влияют)", ""]
    L.append("- S1 доля побед «технологичного» по FV: %s; ничьих %s; ок=%s" % (
        fs(sec["S1"].get("tech_wins")), sec["S1"].get("ties", "—"), fmt(sec["S1"]["ok"])))
    L.append("- S2 «то же знание, другой выход»: %s (обе > 0: %s); ок=%s" % (
        fs(sec["S2"].get("share")), fs(sec["S2"].get("share_both_positive")), fmt(sec["S2"]["ok"])))
    s3 = sec["S3"]
    L.append("- S3 доля seeds с ≥ 3 сменами %s; tenure p10/p50/p90 %s/%s/%s; ок=%s%s" % (
        fs(s3["share_3plus"]), fmt(s3["tenure"]["p10"]), fmt(s3["tenure"]["p50"]), fmt(s3["tenure"]["p90"]),
        fmt(s3["ok"]), "" if s3["ok"] else " — флаг «дребезг лидерства»"))
    ld = res["leadership"]
    L += ["", "## Лидерство (main, §5.1–5.3, §5.7)", "",
          "- число смен p5/p50/p95: %s/%s/%s; среднее %s" % (fmt(ld["n_changes"]["p5"]), fmt(ld["n_changes"]["p50"]),
                                                            fmt(ld["n_changes"]["p95"]), fmt(ld["n_changes"]["mean"])),
          "- первая смена (месяц) p5/p50/p95: %s/%s/%s" % (fmt(ld["first_change_t"]["p5"]),
                                                          fmt(ld["first_change_t"]["p50"]), fmt(ld["first_change_t"]["p95"])),
          "- гистограмма первой смены по годам: %s" % (", ".join("%d:%d" % kv for kv in ld["first_change_year_hist"].items()) or "—"),
          "- новый лидер: %s" % ", ".join("%s %s" % (k, fmt(v)) for k, v in ld["new_leader_share"].items()),
          "- поле ключа, решившее смену: %s" % (", ".join("%s:%d" % kv for kv in ld["change_field"].items()) or "—"),
          "- лидер на тике 359: %s" % ", ".join("%s %s" % (k, fs(v)) for k, v in ld["lead359"].items()),
          "- `volume_lead` (печать): %s" % ", ".join("%s %s" % (k, fmt(v)) for k, v in ld["volume_lead_share"].items())]
    for k, v in ld["fv_ratios"].items():
        L.append("- FV %s: p5/p50/p95 %s/%s/%s (n=%d, нулевой знаменатель %d)" % (
            k, fmt(v["p5"]), fmt(v["p50"]), fmt(v["p95"]), v["n"], v["zero_den"]))
    if res["node_times"]:
        L += ["", "## Время освоения ★-PRC (§5.4; калибровочный якорь, не тест)", "",
              "| актор:узел | t_known p50 | t_first p50 | t_07 p10/p50/p90 | цензура t_07 | t_mastered p50 |",
              "|---|---|---|---|---|---|"]
        for k, v in res["node_times"].items():
            L.append("| %s | %s | %s | %s/%s/%s | %s | %s |" % (
                k, fmt(v["t_known"]["p50"]), fmt(v["t_first"]["p50"]), fmt(v["t_07"]["p10"]), fmt(v["t_07"]["p50"]),
                fmt(v["t_07"]["p90"]), fmt(v["t_07"]["right_cens"]), fmt(v["t_mastered"]["p50"])))
    doc = res["doctrine"]
    L += ["", "## Доминирующая доктрина (§5.8, диагностика)", "",
          "| профиль | lead359_share | change_share | доля профиля среди великих | P(lead359 | профиль) |", "|---|---|---|---|---|"]
    for p, v in doc["profiles"].items():
        L.append("| %s | %s | %s | %s | %s |" % (p, fs(v["lead359_share"]), fs(v["change_share"]),
                                                 fmt(v["profile_share_great"]), fs(v["p_lead_given_profile"])))
    L.append("")
    L.append("`dominant_doctrine` = %s (дефект игрового значения → game-designer, на исход не влияет)." % fmt(doc["dominant_doctrine"]))
    if res["P4"]["verdict"] != "missing":
        L += ["", "## why_not (§5.6)", "",
              "- первое звено по классам: %s" % "; ".join("%s: %s" % (c, ", ".join("%s %d" % kv for kv in sorted(v.items())))
                                                          for c, v in p4["first_by_class"].items()),
              "- доля `eta = null`: %s (звеньев %d); рычагов вне сборки: %d" % (fmt(p4["eta_null_share"]), p4["links"],
                                                                                p4["invalid_levers"]),
              "- примечание: %s" % p4["note"]]
    if res["D1"]:
        d = res["D1"]
        L += ["", "## D1 `eta_hit`, D2 `why_not → смена` (диагностика)", "",
              "- D1: %s; исключено (цензура) %d; факт/eta p10/p50/p90 %s/%s/%s" % (
                  fs(d["eta_hit"]), d["excluded_censored"], fmt(d["ratio"]["p10"]), fmt(d["ratio"]["p50"]), fmt(d["ratio"]["p90"]))]
    if res["D2"]:
        d = res["D2"]
        L.append("- D2: «закрыл» %s; «не закрыл» %s; разность %s CI %s (кластерный bootstrap по seeds); единиц без звена %d. %s" % (
            fs(d["closed"]), fs(d["not_closed"]), fmt(d["diff"]), fci(d["diff_ci"]), d["units_without_link"], d["note"]))
    if res["sources"]:
        s = res["sources"]
        L += ["", "## Источники разброса (§6.3)", "", "| вариант | seeds | chg | chg main | энтропия лидера | доля снятого разброса |",
              "|---|---|---|---|---|---|"]
        for k, v in s["variants"].items():
            L.append("| %s | %d | %s | %s | %s | %s |" % (k, v["seeds"], fs(v["chg"]), fs(v["chg_main_same_seeds"]),
                                                       fmt(v["lead359_entropy"]), fmt(v["var_share_removed"])))
        L.append("")
        if s["fix_a_dominates"]:
            L.append("**`FIX_A` снимает > 50% разброса: разброс модели в основном — из назначения доктрин ИИ.**")
        if "fix_all_ok" in s:
            L.append("`FIX_ALL`: уникальных хешей %d → %s." % (s["fix_all_unique_hashes"], "OK" if s["fix_all_ok"] else "НАРУШЕНИЕ"))
    for title, key in (("Стартовые условия (§6.5)", "start"), ("Старые контроли L_K0/L_C0 (печать)", "old_controls"),
                       ("Каналы (§6.2)", "channels")):
        blk = res[key]
        if not blk:
            continue
        vs = blk["variants"] if "variants" in blk else blk
        L += ["", "## " + title, "", "| вариант | seeds | chg | Δ (main − вариант) | CI Δ | TVD_bc | CI TVD_bc |",
              "|---|---|---|---|---|---|---|"]
        for k, v in vs.items():
            L.append("| %s | %d | %s | %s | %s | %s | %s |" % (k, v["seeds"], fs(v["chg"]), fmt(v["delta"]["delta"]),
                                                              fci(v["delta"]["ci"]), fmt(v["tvd"]["tvd_bc"]), fci(v["tvd"]["ci"])))
        if key == "start" and blk["sensitive_to_start"]:
            L.append("\n**Вывод чувствителен к непроверенному старту** (`START_PERT` сдвигает `chg` > 0,10).")
        if key == "channels":
            for k, v in vs.items():
                L.append("- %s: освоение ★-PRC p50 (main→вариант) %s; K_d великих 1955 %s→%s; цена 100-й ед. G2 p50 %s→%s" % (
                    k, ", ".join("%s %s→%s" % (a, fmt(m["main"], 1), fmt(m["variant"], 1)) for a, m in v["mastering_p50"].items()),
                    fmt(v["kd_great_mean"]["main"]), fmt(v["kd_great_mean"]["variant"]),
                    fmt(v["cost_u100_p50"]["main"]), fmt(v["cost_u100_p50"]["variant"])))
            if blk["kcatch_access_flag"]:
                L.append("\n**`k_catch = 0` сдвигает p50 освоения у догоняющих > 30%: поднять RR-0006 «Разногласия» п.1.**")
    if res["factorial"]:
        f = res["factorial"]
        L += ["", "## Профили ИИ, факторный план (§6.6)", "",
              "η² = %s CI %s; η² по main = %s (ячеек %d)%s" % (fmt(f["eta2"]), fci(f["eta2_ci"]), fmt(f["eta2_main"]),
                                                            f["eta2_main_cells"],
                                                            " — **главный источник разброса**" if f["profiles_main_source"] else ""),
              "", "| ячейка USA_GBR_SUN | chg |", "|---|---|"]
        for k, v in f["cells"].items():
            L.append("| %s | %s |" % (k, fs(v)))
        L.append("")
        L.append("Маргинально: " + "; ".join("%s %s" % (k, fmt(v["p"])) for k, v in f["marginal"].items()))
    if res["lic_knows"]:
        l = res["lic_knows"]
        L += ["", "## `lic_knows` (§6.7)", "",
              "- Δ (main − lic_knows) %s CI %s; TVD_bc %s CI %s" % (fmt(l["delta"]["delta"]), fci(l["delta"]["ci"]),
                                                                 fmt(l["tvd"]["tvd_bc"]), fci(l["tvd"]["ci"])),
              "- лицензий: lic_knows %d, main %d (seeds %d)" % (l["licences"]["lic_knows"], l["licences"]["main"], l["licences"]["seeds"]),
              "- сдвиг p50 `t_known` двигателя у покупателей: %s" % ", ".join(
                  "узел %s: %s→%s (Δ %s, n=%d)" % (k, fmt(v["main_p50"]), fmt(v["lic_p50"]), fmt(v["shift"]), v["n"])
                  for k, v in l["engine_t_known_shift"].items()),
              "- доля новых лидеров, купивших лицензию: lic_knows %s; main %s" % (fs(l["new_leaders_bought"]),
                                                                                 fs(l["new_leaders_bought_main"]))]
    if res["determinism"]:
        d = res["determinism"]
        L += ["", "## Детерминизм L2", "", "seeds %d, threads %s: %s" % (
            d["seeds"], d["threads"], "OK" if d["ok"] else "НЕСОВПАДЕНИЕ на seeds %s" % d["mismatch_seeds"])]
    ex = res["extremes"]
    L += ["", "## Крайние seeds (§5.7)"]
    for title, key in (("наибольшее число смен", "most_changes"), ("наибольший отрыв лидера (ΔG, ΔT, ΔN, ΔP)", "largest_gap")):
        L += ["", "### " + title]
        for e in ex[key]:
            L.append("- seed %d: смен %d, отрыв %s; лидер: %s" % (e["seed"], e["n_changes"], e["gap"], "; ".join(e["lead_periods"])))
            if e["whynot_first"]:
                L.append("  - why_not (первое звено): " + " | ".join(e["whynot_first"][:12]) +
                         (" …" if len(e["whynot_first"]) > 12 else ""))
    return "\n".join(L) + "\n"


# ---------------------------------------------------------------- calibration build (control tables, C2, C4)

def calib_header_check(d, cfg):
    c = cfg["calib"]
    bad = []
    for name in sorted(os.listdir(d)):
        if not name.endswith(".csv"):
            continue
        if name in c["forbidden_files"]:
            bad.append("file %s" % name)
            continue
        with open(os.path.join(d, name), newline="", encoding="utf-8") as f:
            header = next(csv.reader(f), [])
        for col in header:
            if any(s in col.lower() for s in c["forbidden_substrings"]):
                bad.append("%s:%s" % (name, col))
    return bad


def median_low(xs):
    return statistics.median_low(xs) if xs else None


def calib_tables(d, cfg):
    bad = calib_header_check(d, cfg)
    if bad:
        raise InputError("calibration build exposes leadership output: %s" % bad)
    c = cfg["calib"]
    runs = {r["run"]: r for r in _read_csv(os.path.join(d, "runs.csv"), RUNS_COLS) if r["status"] == "ok"}
    nt = collections.defaultdict(dict)
    for r in _read_csv(os.path.join(d, "node_times.csv"), NODE_TIMES_COLS):
        if r["run"] in runs:
            nt[r["run"]][(r["actor"], int(r["node"]))] = (_oi(r["t_known"]), _oi(r["t_first"]), _oi(r["t_07"]))
    gt = collections.defaultdict(dict)
    for r in _read_csv(os.path.join(d, "gen_times.csv"), GEN_TIMES_COLS):
        if r["run"] in runs:
            gt[r["run"]][(r["actor"], int(r["gen"]))] = (_oi(r["t_req"]), _oi(r["t_first_unit"]))
    rids = sorted(runs, key=lambda x: int(runs[x]["seed"]))
    nm = cfg["n_months"]
    out = {"seeds": len(rids), "lk": {}, "delta": {}, "Y": {}, "U": {}, "T_design": {}, "rho": {}}
    for a in cfg["actors"]:
        for g_s, app in c["app_nodes"].items():
            g = int(g_s)
            vals, cens, start = [], 0, 0
            for rid in rids:
                tk = nt[rid].get((a, app), (None,))[0]
                if tk is None:
                    cens += 1
                elif tk < 0:
                    start += 1
                else:
                    vals.append(tk)
            n = len(rids)
            m = sum(vals) / len(vals) if vals else None
            s = statistics.stdev(vals) if len(vals) > 1 else (0.0 if vals else None)
            out["lk"]["%s:%d" % (a, g)] = {
                "p_cens_fx": (cens * FX) // n if n else None, "m": m, "s": s,
                "m_int": None if m is None else int(math.floor(m + 0.5)),
                "half": None if s is None else int(math.floor(math.sqrt(3.0) * s + 0.5)),
                "start_known": start, "n": len(vals)}
            for nd, (typ, ng, _) in sorted(NODES.items()):
                if ng != g or nd == app:
                    continue
                diffs, ncens = [], 0
                for rid in rids:
                    tk = nt[rid].get((a, nd), (None,))[0]
                    ta = nt[rid].get((a, app), (None,))[0]
                    if tk is None:
                        ncens += 1
                    elif tk >= 0 and ta is not None and ta >= 0:
                        diffs.append(tk - ta)
                never = ncens > c["cens_never_share"] * len(rids)
                out["delta"]["%s:%d" % (a, nd)] = {"never": never, "delta": None if never else median_low(diffs),
                                                    "n": len(diffs)}
    # Y_k, U_k from prc_month
    curves = collections.defaultdict(lambda: collections.defaultdict(lambda: ([], [])))
    path = os.path.join(d, "prc_month.csv")
    with open(path, newline="", encoding="utf-8") as f:
        rd = csv.DictReader(f)
        miss = [x for x in PRC_MONTH_COLS if x not in (rd.fieldnames or [])]
        if miss:
            raise InputError("prc_month.csv: missing columns %s" % miss)
        for r in rd:
            if r["run"] not in runs:
                continue
            nd = int(r["node"])
            tk = nt[r["run"]].get((r["actor"], nd), (None,))[0]
            if tk is None or tk < 0:
                continue
            tau = int(r["t"]) - tk
            if tau < 0:
                continue
            k = NODES[nd][2]
            ys, us = curves[k][tau]
            ys.append(int(r["yield"]))
            us.append(int(r["ucm"]))
    for k, byt in sorted(curves.items()):
        ylist, ulist, ly, lu = [], [], 0, FX
        for tau in range(nm):
            if tau in byt:
                ly, lu = median_low(byt[tau][0]), median_low(byt[tau][1])
            ylist.append(ly)
            ulist.append(lu)
        out["Y"][str(k)], out["U"][str(k)] = ylist, ulist
    for g in range(1, 5):
        xs = [tf - tr for rid in rids for a in cfg["actors"]
              for (tr, tf) in [gt[rid].get((a, g), (None, None))] if tr is not None and tf is not None and tr >= 0]
        out["T_design"][str(g)] = median_low(xs)
    am = _read_csv(os.path.join(d, "actor_month.csv"), ACTOR_MONTH_COLS)
    for g in range(1, 5):
        xs = []
        for r in am:
            if r["run"] in runs and int(r["active_g%d" % g]) and int(r["proc_budget"]) > 0:
                xs.append((int(r["out_g%d" % g]) * FX * FX) // int(r["proc_budget"]))
        out["rho"][str(g)] = median_low(xs)
    # C2, C4
    def mast(nodes):
        xs, cens = [], 0
        for rid in rids:
            for a in cfg["actors"]:
                for nd in nodes:
                    x = nt[rid].get((a, nd))
                    if not x or x[1] is None or x[1] < 0:
                        continue
                    if x[2] is None:
                        cens += 1
                    xs.append(((x[2] if x[2] is not None else nm) - x[1]) / 12.0)
        return {"p50_years": quantile(xs, 0.5), "n": len(xs), "right_censored": cens}
    c2 = {"tier4": mast(c["c2_tier4"]), "tier5": mast(c["c2_tier5"])}
    t4, t5 = c2["tier4"]["p50_years"], c2["tier5"]["p50_years"]
    c2["tier4_in_corridor"] = t4 is not None and c["c2_years"][0] <= t4 <= c["c2_years"][1]
    c2["tier5_longer"] = t4 is not None and t5 is not None and t5 > t4
    c4 = {}
    for g_s, (y0, y1) in c["c4_windows"].items():
        hit = 0
        for rid in rids:
            ts = [gt[rid][(a, int(g_s))][1] for a in cfg["actors"]
                  if (a, int(g_s)) in gt[rid] and gt[rid][(a, int(g_s))][1] is not None]
            if ts and y0 <= year_of(cfg, min(ts)) <= y1:
                hit += 1
        c4[g_s] = {"share": hit / len(rids) if rids else None, "ok": bool(rids) and hit / len(rids) >= c["c4_min_share"]}
    tables = {k: out[k] for k in ("lk", "delta", "Y", "U", "T_design", "rho")}
    return {"control_tables": tables, "control_tables_hash": sha256_text(canonical_json(jsonable(tables))),
            "seeds": out["seeds"], "C2": c2, "C4": c4}


# ---------------------------------------------------------------- self-test (synthetic data with known truth)

class Synth:
    """Writes Lab-shaped CSVs. Change indicators are assigned to exact seed sets (not drawn),
    so the ground truth for P1-P5 is exact, not 'in expectation'."""

    def __init__(self, cfg, seed):
        self.cfg = cfg
        self.rng = random.Random(seed)
        self.rows = collections.defaultdict(list)
        self.nrun = 0

    def trace(self, chg, leader_end, blip=False):
        """chg=0: USA all along (optionally a 10-month GBR blip, which is not a change);
        chg=1: USA until s in [12, 336), then `leader_end` (1 or 2) to the end."""
        n = self.cfg["n_months"]
        if not chg:
            tr = [0] * n
            if blip:
                s = self.rng.randrange(20, 300)
                for t in range(s, s + 10):
                    tr[t] = 1
            return tr
        s = self.rng.randrange(12, 336)
        return [0] * s + [leader_end] * (n - s)

    def add_run(self, series, seed, chg, leader_end, prof=None, small=False, threads=1, golden=None):
        self.nrun += 1
        rid = "r%06d" % self.nrun
        tr = self.trace(chg, leader_end, blip=(seed % 7 == 0))
        lead_all = list(tr)
        if small:
            for t in range(100, 140):
                lead_all[t] = 4
        prof = prof or [1 + (seed + i) % 3 for i in range(5)]
        field = "".join("G" if (t > 0 and tr[t] != tr[t - 1]) else "=" for t in range(len(tr)))
        self.rows["runs"].append([rid, series, seed, threads, "ok", golden or ("h%d" % seed)])
        self.rows["summary"].append([rid] + prof + ["".join(map(str, tr)), "".join(map(str, lead_all)), field])
        return rid, tr

    def write(self, d):
        hdr = {"runs": RUNS_COLS + ["point"], "summary": SUMMARY_COLS, "actor_year": ACTOR_YEAR_COLS,
               "whynot": WHYNOT_COLS, "node_times": NODE_TIMES_COLS, "gen_times": GEN_TIMES_COLS,
               "requirements": REQ_COLS, "events": EVENTS_COLS, "prc_year": PRC_YEAR_COLS}
        for name, h in hdr.items():
            with open(os.path.join(d, name + ".csv"), "w", newline="", encoding="utf-8") as f:
                w = csv.writer(f, lineterminator="\n")
                w.writerow(h)
                for r in self.rows[name]:
                    w.writerow(r + (["0"] if name == "runs" else []))


def le_of(seed):
    """New leader of a changing synthetic seed: GBR in 80%, SUN in 20%."""
    return 1 if seed % 5 else 2


def exact_set(n, k, rng):
    idx = list(range(n))
    rng.shuffle(idx)
    return set(idx[:k])


def make_dataset(d, cfg, chg=0.35, layer_effect=(0.20, 0.20, 0.20), tvd_only=None, small_share=0.0,
                 knife=False, n_main=1000, grid_seeds=100, dominant=False, extras=True):
    S = Synth(cfg, 777)
    rng = random.Random(4242)
    seeds = list(range(1, n_main + 1))
    chg_set = exact_set(n_main, int(round(chg * n_main)), rng)
    small_set = exact_set(n_main, int(round(small_share * n_main)), rng)
    main = {}
    for i, s in enumerate(seeds):
        c = 1 if i in chg_set else 0
        le = le_of(s) if c else 0
        prof = None
        if dominant and c:
            prof = [2, 3, 3, 1, 1]
            prof[le] = 1                       # every new leader has profile P1
        rid, tr = S.add_run("main", s, c, le, prof=prof, small=(i in small_set))
        main[s] = (c, le, rid, tr)
    # layers: off runs flip exactly |effect|*N indicators (plus symmetric noise flips)
    for L, eff in zip(cfg["p3"]["layers"], layer_effect):
        ones = [s for s in seeds if main[s][0] == 1]
        zeros = [s for s in seeds if main[s][0] == 0]
        r2 = random.Random(sum(map(ord, L)))
        noise = min(50, n_main // 20)
        k = int(round(abs(eff) * n_main))
        if eff >= 0:
            to0 = set(r2.sample(ones, k + noise))
            to1 = set(r2.sample(zeros, noise))
        else:
            to0 = set(r2.sample(ones, noise))
            to1 = set(r2.sample(zeros, k + noise))
        for s in seeds:
            c, le, _, _ = main[s]
            if s in to0:
                c, le = 0, 0
            elif s in to1:
                c, le = 1, le_of(s)
            if tvd_only == L and c == 1:
                le = 2                       # every new leader is SUN: Delta 0, large TVD
            S.add_run("layer/" + L, s, c, le)
    if extras:
        lv = cfg["p5"]["levels"]
        for i in range(3):
            for j in range(3):
                for k in range(3):
                    name = grid_name(cfg, i, j, k)
                    for s in range(1, grid_seeds + 1):
                        c, le = main[s][0], main[s][1]
                        if knife and (i, j, k) == (2, 2, 2):
                            c, le = (1, 1) if s % 10 < 8 else (c, le)   # chg jumps to >= 0.8 at one corner
                        S.add_run(name, s, c, le)
        for s in range(1, 401):
            c, le = main[s][0], main[s][1]
            S.add_run("source/FIX_A", s, c if s % 3 else 0, le if s % 3 else 0)
            S.add_run("start/PERT", s, c, le)
            S.add_run("old/L_K0", s, c, le)
            S.add_run("lic/knows", s, c, le)
        for s in range(1, 201):
            S.add_run("channel/kcatch0", s, main[s][0], main[s][1])
        cells_truth = {}
        for a in cfg["profiles"]:
            for b in cfg["profiles"]:
                for cc in cfg["profiles"]:
                    name = "%s_%s_%s" % (a, b, cc)
                    kk = {"P1": 30, "P2": 10, "P3": 20}[a]   # chg depends on USA profile only
                    cells_truth[name] = kk
                    for s in range(1, 41):
                        c = 1 if s <= kk else 0
                        S.add_run("prof/" + name, s, c, 1 if c else 0,
                                  prof=[int(a[1]), int(b[1]), int(cc[1]), 1, 1])
        for s in range(1, 21):
            for th in (1, 4, 8):
                S.add_run("det", s, main[s][0], main[s][1], threads=th, golden="h%d" % s)
    # main-only detail tables
    for s in seeds:
        c, le, rid, tr = main[s]
        for y in range(32):
            t = 12 * y
            for ai, a in enumerate(cfg["actors"]):
                key = [1 + (t >= 100) + (ai == tr[t] and ai < 3), 4, 2, FX]
                S.rows["actor_year"].append([rid, t, a] + key + [FX * (5 - ai), FX * (5 - ai), FX,
                                                                  6 if (s + y + ai) % 2 == 0 else 0] + [FX] * 5)
                if ai < 3 and t <= 336:
                    # first-link layer mix 40/30/20/10; ENGINEERS in top-3 for 90% of actor-years
                    u = (s * 31 + y * 7 + ai) % 10
                    layer = "KNOWLEDGE" if u < 4 else "CAPABILITY" if u < 7 else "PRODUCT" if u < 9 else "ECONOMY"
                    eta = 12 if u != 9 else None
                    closer = (s + y + ai) % 5 != 0       # 80% closed within eta
                    closed = (t + 12) if (closer and eta) else None
                    if ai != tr[t] and ai == tr[min(t + 30, 383)] and closed is None and eta:
                        closed = t + 10
                    S.rows["whynot"].append([rid, t, a, "app", 1, layer, "X", "n1", "" if eta is None else eta,
                                             "research_priority", 1, "" if closed is None else closed])
                    eng = (s + y + ai) % 10 != 0
                    S.rows["whynot"].append([rid, t, a, "app", 2, "PRODUCT", "ENGINEERS" if eng else "DEFECTS",
                                             "kb1", 6, "reassign_workforce", 1, t + 6])
                for nd, g in (cfg["s2"]["star_prc_gen"].items() if (ai < 3 and y % 4 == 0) else ()):
                    known = 1 if t >= 60 * (g - 1) else 0
                    yv = (FX if ai == 0 else FX // 2) if known else 0
                    S.rows["prc_year"].append([rid, t, a, nd, known, known, yv, FX])
        for ai, a in enumerate(cfg["actors"]):
            for nd in (int(x) for x in cfg["s2"]["star_prc_gen"]):
                S.rows["node_times"].append([rid, a, nd, 10 * ai, 10 * ai + 5, 10 * ai + 29, 10 * ai + 35, 0])
            S.rows["gen_times"].append([rid, a, 2, 24, 60, 70, 3 * FX])
        S.rows["requirements"].append([rid, "q1", "USA", 2, 24, 2, FX * (2 if s % 2 else 1), FX * 3 // 2, "TECH"])
        S.rows["requirements"].append([rid, "q0", "USA", 1, 0, 1, FX, FX, "SUPER"])
    S.write(d)
    return {"cells_truth": cells_truth if extras else None}


def selftest(quick=False):
    cfg = copy.deepcopy(DEFAULTS)
    cfg["p0_passed"] = True
    if quick:
        cfg["boot_reps"] = cfg["perm_reps"] = cfg["split_reps"] = 400
    results = []

    def check(name, cond, info=""):
        results.append((name, bool(cond), info))

    # unit checks
    check("wilson(35,100)", all(abs(a - b) < 1e-4 for a, b in zip(wilson(35, 100, cfg["wilson_z"]), (0.2636, 0.4475))),
          wilson(35, 100, cfg["wilson_z"]))
    tr = [0] * 50 + [1] * 10 + [0] * 100 + [2] * 30 + [0] * 194
    ch = counted_changes(tr, 359, 24)
    check("blip < 24 not counted; 30-month SUN run and the return to USA both counted",
          ch == [(160, 2, 0), (190, 0, 2)], ch)
    tr2 = [0] * 350 + [1] * 34
    check("change starting at 350 held into tail counted", counted_changes(tr2, 359, 24) == [(350, 1, 0)])
    tr3 = [0] * 362 + [1] * 22
    check("change starting in tail not counted", counted_changes(tr3, 359, 24) == [])
    check("tenures (last one cut at 359)", tenures(tr, ch, 359) == [160, 30, 170], tenures(tr, ch, 359))
    check("abs_lo", abs_lo((-0.05, 0.2)) == 0.0 and abs_lo((-0.3, -0.12)) == 0.12)

    scenarios = [
        ("A chg=0.35, layers 0.20 -> supports", dict(chg=0.35), {"outcome": "supports", "P1": "pass", "P3": "pass"}),
        ("B chg=0.20 -> P1 fail -> refutes", dict(chg=0.20, layer_effect=(-0.20, -0.20, -0.20)), {"outcome": "refutes", "P1": "fail"}),
        ("C L_C effect 0 -> P3 fail -> refutes", dict(chg=0.35, layer_effect=(0.20, 0.0, 0.20)),
         {"outcome": "refutes", "P3": "fail", "layer:L_C": "fail"}),
        ("D chg=0.27 -> P1 borderline, single -> supports", dict(chg=0.27), {"P1": "borderline", "outcome": "supports"}),
        ("E L_P effect 0.10 -> borderline layer + P1 borderline -> inconclusive",
         dict(chg=0.27, layer_effect=(0.20, 0.20, 0.10)), {"P3": "borderline", "outcome": "inconclusive"}),
        ("F L_P Delta 0 but leader swap -> pass via TVD", dict(chg=0.35, layer_effect=(0.20, 0.20, 0.0), tvd_only="L_P"),
         {"layer:L_P": "pass", "outcome": "supports"}),
        ("G small_lead 0.10 -> P2 fail -> refutes", dict(chg=0.35, small_share=0.10), {"P2": "fail", "outcome": "refutes"}),
        ("H knife edge in grid -> inconclusive", dict(chg=0.35, knife=True), {"P5": "fail", "outcome": "inconclusive"}),
        ("I layer effect -0.20 (off has more changes) -> pass", dict(chg=0.35, layer_effect=(-0.20, 0.20, 0.20)),
         {"layer:L_K": "pass", "outcome": "supports"}),
        ("J dominant doctrine flag", dict(chg=0.35, dominant=True), {"dominant": True}),
    ]
    detail = {}
    for name, kw, exp in scenarios:
        with tempfile.TemporaryDirectory() as d:
            truth = make_dataset(d, cfg, **kw)
            res = analyze(load([d], cfg), cfg)
        got = {"outcome": res["decision"]["outcome"], "P1": res["P1"]["verdict"], "P2": res["P2"]["verdict"],
               "P3": res["P3"]["verdict"], "P5": res["P5"]["verdict"], "dominant": res["doctrine"]["dominant_doctrine"]}
        for L, v in res["P3"]["layers"].items():
            got["layer:" + L] = v["verdict"]
        ok = all(got.get(k) == v for k, v in exp.items())
        check(name, ok, {k: got.get(k) for k in exp} if not ok else "")
        if name.startswith("A"):
            detail = res
            ct = truth["cells_truth"]
            cells = [[1] * k + [0] * (40 - k) for k in ct.values()]
            check("A: eta2 factorial equals exact value", abs(res["factorial"]["eta2"] - eta2(cells)) < 1e-12,
                  (res["factorial"]["eta2"], eta2(cells)))
            check("A: P4 pass (layers 40/30/20/10, ENGINEERS 90%)", res["P4"]["verdict"] == "pass", res["P4"].get("a"))
            check("A: D1 eta_hit = 0.8 (+closures for challengers)", res["D1"]["eta_hit"]["p"] >= 0.8, res["D1"]["eta_hit"]["p"])
            check("A: D2 closers lead more often", (res["D2"]["diff"] or 0) > 0, res["D2"]["diff"])
            check("A: S1 tech win share 0.5, single-variant excluded", abs(res["secondary"]["S1"]["tech_wins"]["p"] - 0.5) < 1e-9)
            check("A: S2 share 2/3 (USA yield = 2x others)", abs(res["secondary"]["S2"]["share"]["p"] - 2 / 3) < 1e-12,
                  res["secondary"]["S2"]["share"]["p"])
            check("A: determinism OK", res["determinism"]["ok"])
            check("A: FIX_A removes variance", (res["sources"]["variants"]["FIX_A"]["var_share_removed"] or 0) > 0)
            check("A: tvd split-half informative", res["P3"]["tvd_informative"])
            check("A: no dominant doctrine", res["doctrine"]["dominant_doctrine"] is False)
            check("A: 54 neighbour pairs in grid", res["P5"]["n_pairs"] == 54)
    # analysis determinism: same input twice -> identical JSON
    with tempfile.TemporaryDirectory() as d:
        make_dataset(d, cfg, chg=0.35, extras=False, n_main=300)
        c2 = copy.deepcopy(cfg)
        j1 = canonical_json(jsonable(analyze(load([d], c2), c2)))
        j2 = canonical_json(jsonable(analyze(load([d], c2), c2)))
        check("analysis is deterministic (same JSON twice)", j1 == j2)
    # calibration build header check
    with tempfile.TemporaryDirectory() as d:
        with open(os.path.join(d, "node_times.csv"), "w") as f:
            f.write(",".join(NODE_TIMES_COLS) + "\n")
        with open(os.path.join(d, "calib_summary.csv"), "w") as f:
            f.write(",".join(CALIB_SUMMARY_COLS + ["tech_lead"]) + "\n")
        check("calib header check rejects tech_lead column", calib_header_check(d, cfg) == ["calib_summary.csv:tech_lead"])
    check("calib control tables", calib_selftest(cfg) is True)
    return results, detail


def calib_selftest(cfg):
    with tempfile.TemporaryDirectory() as d:
        rows = collections.defaultdict(list)
        for s in range(1, 101):
            rid = "c%d" % s
            rows["runs"].append([rid, "calib", 10000 + s, 1, "ok", "h"])
            rows["calib_summary"].append([rid, 1, 2, 3, 1, 2])
            for a in cfg["actors"]:
                for g, app in cfg["calib"]["app_nodes"].items():
                    g = int(g)
                    tk = "" if (a == "SWE" and s <= 30) else (100 * (g - 1) + (s % 21) - 5)
                    if g == 1:
                        tk = -1
                    rows["node_times"].append([rid, a, app, tk, "", "", "", 0])
                    for nd, (_, ng, _) in NODES.items():
                        if ng == g and nd != app and NODES[nd][0] != "PRC":
                            rows["node_times"].append([rid, a, nd, "" if tk == "" else (tk if tk == -1 else tk - 7),
                                                       "", "", "", 0])
                for nd in (40, 49):
                    rows["node_times"].append([rid, a, nd, 0, 12, 12 + 48, "", 0])
                rows["gen_times"].append([rid, a, 2, 30, 30 + 24 + (s % 3) - 1, "", ""])
                for t in range(0, 12):
                    rows["prc_month"].append([rid, t, a, 40, 1, 1, t * 1000, FX])
                rows["actor_month"].append([rid, 50, a, FX, FX // 2, 0, 10, 0, 0, 0, 1, 0, 0])
        for name, h in (("runs", RUNS_COLS), ("calib_summary", CALIB_SUMMARY_COLS), ("node_times", NODE_TIMES_COLS),
                        ("gen_times", GEN_TIMES_COLS), ("prc_month", PRC_MONTH_COLS), ("actor_month", ACTOR_MONTH_COLS)):
            with open(os.path.join(d, name + ".csv"), "w", newline="") as f:
                w = csv.writer(f, lineterminator="\n")
                w.writerow(h)
                w.writerows(rows[name])
        r = calib_tables(d, cfg)
        lk = r["control_tables"]["lk"]
        m_exp = sum(100 + (s % 21) - 5 for s in range(1, 101)) / 100.0
        ok = abs(lk["USA:2"]["m"] - m_exp) < 1e-9 and lk["SWE:2"]["p_cens_fx"] == 300000
        ok &= r["control_tables"]["delta"]["USA:19"]["delta"] == -7
        ok &= r["control_tables"]["T_design"]["2"] == 24
        ok &= r["control_tables"]["rho"]["2"] == 20 * FX
        ok &= r["control_tables"]["Y"]["4"][5] == 5000
        ok &= abs(r["C2"]["tier4"]["p50_years"] - 4.0) < 1e-9 and r["C2"]["tier4_in_corridor"]
        return ok


# ---------------------------------------------------------------- CLI

def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("dirs", nargs="*")
    ap.add_argument("--config")
    ap.add_argument("--out")
    ap.add_argument("--json")
    ap.add_argument("--calib")
    ap.add_argument("--dump-config", action="store_true")
    ap.add_argument("--selftest", action="store_true")
    ap.add_argument("--quick", action="store_true", help="selftest with 400 resamples")
    a = ap.parse_args(argv)
    if a.dump_config:
        sys.stdout.write(json.dumps(DEFAULTS, indent=2, sort_keys=True) + "\n")
        return 0
    if a.selftest:
        results, _ = selftest(a.quick)
        bad = 0
        for name, ok, info in results:
            sys.stdout.write("%s  %s%s\n" % ("PASS" if ok else "FAIL", name, ("  " + str(info)) if (info and not ok) else ""))
            bad += not ok
        sys.stdout.write("selftest: %d/%d passed\n" % (len(results) - bad, len(results)))
        return 1 if bad else 0
    cfg = DEFAULTS
    if a.config:
        try:
            with open(a.config, encoding="utf-8") as f:
                cfg = merge_config(DEFAULTS, json.load(f))
        except (InputError, ValueError, OSError) as e:
            sys.stderr.write("error: config: %s\n" % e)
            return 2
    try:
        if a.calib:
            r = calib_tables(a.calib, cfg)
            text = json.dumps(jsonable(r), indent=1, sort_keys=True) + "\n"
            md = ("# Калибровочная сборка: таблицы контролей §6.1, C2, C4\n\n- control_tables_hash: `%s`\n- seeds: %d\n"
                  "- C2: %s\n- C4: %s\n" % (r["control_tables_hash"], r["seeds"], json.dumps(jsonable(r["C2"]), sort_keys=True),
                                           json.dumps(jsonable(r["C4"]), sort_keys=True)))
        else:
            if not a.dirs:
                ap.error("input dir(s) required")
            res = analyze(load(a.dirs, cfg), cfg)
            res["input_sha256"] = file_hashes(a.dirs)
            res["config_sha256"] = sha256_text(canonical_json(cfg))
            text = json.dumps(jsonable(res), indent=1, sort_keys=True) + "\n"
            md = render(res, cfg, res["input_sha256"])
    except InputError as e:
        sys.stderr.write("error: %s\n" % e)
        return 2
    if a.json:
        with open(a.json, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
    if a.out:
        with open(a.out, "w", encoding="utf-8", newline="\n") as f:
            f.write(md)
    else:
        sys.stdout.write(md)
    return 0


if __name__ == "__main__":
    sys.exit(main())
