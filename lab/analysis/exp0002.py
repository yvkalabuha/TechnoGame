#!/usr/bin/env python3
"""EXP-0002 analysis: HYP-0002 pre-registered decision rule (slice-jet-aviation.md v0.25 r1, section 7).

Input: slice-csv-format.md v0.5 r7 (JetSlice, JetSliceCalib, c1_stand.csv). T11 (TASK-0053): criteria P1-P5, S1-S3,
diagnostics (5.8, 5.9, 6.3, 6.5-6.7), control tables 6.1 (--calib), P0 prints of format section 7 (A40, A39, F17b,
licences, fleet) and C1 (--c1).

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

# Pre-registered thresholds (slice spec section 7: v0.6 values, unchanged to v0.25 r1; #45, #48). Frozen by the manifest
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
    # #77: tolerance 0.05 for the 70% bound and for (b), 0.01 for the 5% floor; (b) with no units -> n/a
    "p4": {"max_first_share": 0.70, "min_layer_share": 0.05, "min_eng_top3": 0.80,
           "tol_max": 0.05, "tol_min": 0.01, "tol_b": 0.05,
           "short_months_min": 6, "top_k": 3, "eng_cause": "ENGINEERS",
           "layers": ["KNOWLEDGE", "CAPABILITY", "PRODUCT", "ECONOMY"]},
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
    # v0.5 prints (format §7; spec §6.10-6.11, A39-A42); P0 flags, not criteria
    "a40": {"main_series": ["main"], "lo0_class": "DEFECTS", "lc0_class": "OTHER", "coexist_class": "COEXIST_MONEY"},
    "f17b": {"end_rules": ["YPROD", "REPLACE", "A40C_N", "A40C_W"], "horizon": "HORIZON"},
    "fleet": {"egen_min_gen_line": 3, "egen_share_min": 0.2, "egen_21_max": 0.8},
    "c1": {"dev_cost_max": 0.40, "dev_yield_lo": 0.50, "dev_yield_hi": 0.80, "mature_yield_max": 0.20,
           "mature_cost_max": 0.30, "wright_lo": 0.15, "wright_hi": 0.30},
    # P5 point labels (spec §7 P5, #190): k_diff and the price share r = 1 - k_diff of each level
    "p5_kdiff": {"lo": 0.75, "mid": 0.5, "hi": 0.0},
    "fv_years_t": [120, 240, 348],
    "calib": {"app_nodes": {"1": 58, "2": 59, "3": 60, "4": 61}, "cens_never_share": 0.5,
              "c2_tier4": [40, 49], "c2_tier5": [41, 51, 52], "c2_years": [2, 8],
              "c4_windows": {"2": [1947, 1952], "3": [1954, 1961], "4": [1967, 1975]}, "c4_min_share": 0.5,
              "tsk_y_min": 250000,
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
# v0.5 (slice-csv-format.md §4.6): fv_producible (former fv_tech) and fv_superior (former fv_super), Fx6 or empty; no chosen.
REQ_COLS = ["run", "req", "actor", "gen", "t", "n_variants", "fv_superior", "fv_producible"]
EVENTS_COLS = ["run", "t", "kind", "actor", "other", "value"]
PRC_YEAR_COLS = ["run", "t", "actor", "node", "known", "admitted", "yield", "ucm"]
CALIB_SUMMARY_COLS = ["run", "prof_USA", "prof_GBR", "prof_SUN", "prof_FRA", "prof_SWE"]
PRC_MONTH_COLS = ["run", "t", "actor", "node", "known", "admitted", "yield", "ucm", "v"]
ACTOR_MONTH_COLS = ["run", "t", "actor", "budget", "proc_budget", "out_g1", "out_g2", "out_g3", "out_g4",
                    "active_g1", "active_g2", "active_g3", "active_g4"]
# v0.5 r6 tables (slice-csv-format.md §4.9-4.14, §6; T11)
A40_COLS = ["run", "t", "actor", "design", "g_p", "profile", "preset", "branch", "cls", "t_stage_o", "t_launch", "force",
            "idle", "rep_before"]
A40_C_COLS = ["run", "t_fire", "actor", "design", "t", "min_yield", "argp", "alloy_supply", "deficit", "t_a", "left",
              "w_after_n", "training"]
LEDGER_COLS = ["run", "t", "actor", "rest", "b_cap", "invest", "taken_rest", "taken_cap", "rest_unspent", "t_a",
               "rest_returned", "n_part", "tool_unmet_MET", "tool_unmet_CHEM", "tool_unmet_MECH", "tool_unmet_AERO", "pool_exh"]
LINES_COLS = ["run", "t", "actor", "design", "g_p", "role", "y_min", "argp", "rate", "unit_cost", "idle", "close", "start",
              "close_rule", "close_core", "left_share", "w_after_n"]
FLEET_COLS = ["run", "t", "actor", "kind", "design", "gen_d", "gen_line", "count", "g_front", "line_bind"]
LICENCES_COLS = ["run", "t", "seller", "buyer", "gen", "profile", "price", "t_end"]
C1_COLS = ["setup", "arm", "k", "n_from", "n_to", "min_yield", "in_stat", "dlog_cost", "dlog_yield", "dlog_asm", "asm_drop", "asm_over_floor_drop"]


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
        # v0.5 r6 tables (T11); present only if the file exists in the directory
        self.a40 = collections.defaultdict(list)          # rid -> rows
        self.a40c = collections.defaultdict(lambda: collections.defaultdict(list))  # rid -> (t_fire, actor, design) -> months
        self.ledger = {}                                  # rid -> {"months": Counter(actor), "exh": Counter(actor), "bad": [..]}
        self.lines = collections.defaultdict(list)        # rid -> rows
        self.fleet = collections.defaultdict(list)        # rid -> rows
        self.licences = collections.defaultdict(list)     # rid -> rows
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
                                       "n_variants": int(r["n_variants"]), "fv_tech": _oi(r["fv_producible"]),
                                       "fv_super": _oi(r["fv_superior"])})
                elif attr == "events":
                    store[rid].append({"t": int(r["t"]), "kind": r["kind"], "actor": r["actor"],
                                       "other": r["other"], "value": _oi(r["value"])})
                elif attr == "prc_year":
                    store[rid][(int(r["t"]), r["actor"], int(r["node"]))] = {
                        "known": int(r["known"]), "admitted": int(r["admitted"]), "yield": int(r["yield"]),
                        "ucm": int(r["ucm"])}
        load_v05(D, d, pre)
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


def _stream(path, required):
    """Rows of an optional CSV as dicts, streamed; None if the file is missing."""
    if not os.path.exists(path):
        return None
    f = open(path, newline="", encoding="utf-8")
    reader = csv.DictReader(f)
    miss = [c for c in required if c not in (reader.fieldnames or [])]
    if miss:
        f.close()
        raise InputError("%s: missing columns %s" % (os.path.basename(path), miss))

    def gen():
        with f:
            for r in reader:
                yield r
    return gen()


def load_v05(D, d, pre, only_known=False):
    """a40, a40_c_months, tooling_ledger, lines, fleet_cmd, licences (format v0.5 r6 §4.9-4.14). only_known: rows of runs not
    in D.runs are skipped instead of being an error (--calib --point: the other points of the matrix directory)."""
    def _stream_k(path, cols):
        rows = _stream(path, cols)
        if rows is None or not only_known:
            return rows
        return (r for r in rows if pre + r["run"] in D.runs)

    def rid_of(fname, r):
        rid = pre + r["run"]
        if rid not in D.runs:
            raise InputError("%s: unknown run %s" % (fname, r["run"]))
        return rid

    rows = _stream_k(os.path.join(d, "a40.csv"), A40_COLS)
    if rows is not None:
        D.present.add("a40")
        for r in rows:
            D.a40[rid_of("a40.csv", r)].append({
                "t": int(r["t"]), "actor": r["actor"], "design": int(r["design"]), "g_p": int(r["g_p"]),
                "profile": int(r["profile"]), "branch": r["branch"], "cls": r["cls"], "t_launch": _oi(r["t_launch"]),
                "force": int(r["force"])})
    rows = _stream_k(os.path.join(d, "a40_c_months.csv"), A40_C_COLS)
    if rows is not None:
        D.present.add("a40c")
        for r in rows:
            D.a40c[rid_of("a40_c_months.csv", r)][(int(r["t_fire"]), r["actor"], int(r["design"]))].append({
                "t": int(r["t"]), "min_yield": int(r["min_yield"]), "left": _oi(r["left"]),
                "training": _oi(r["training"]), "w_after_n": int(r["w_after_n"])})
    rows = _stream_k(os.path.join(d, "tooling_ledger.csv"), LEDGER_COLS)
    if rows is not None:
        D.present.add("ledger")
        for r in rows:
            rid = rid_of("tooling_ledger.csv", r)
            g = D.ledger.get(rid)
            if g is None:
                g = D.ledger[rid] = {"months": collections.Counter(), "exh": collections.Counter(), "bad": 0,
                                     "bad_ex": []}
            a = r["actor"]
            g["months"][a] += 1
            g["exh"][a] += int(r["pool_exh"])
            rest, cap, inv = int(r["rest"]), int(r["b_cap"]), int(r["invest"])
            viol = []
            if inv > rest + cap:
                viol.append("invest > rest + b_cap")
            if int(r["taken_rest"]) > rest:
                viol.append("taken_rest > rest")
            if int(r["t_a"]) < 0:
                viol.append("t_a < 0")
            if viol:
                g["bad"] += 1
                if len(g["bad_ex"]) < 3:
                    g["bad_ex"].append("t=%s %s: %s" % (r["t"], a, ", ".join(viol)))
    rows = _stream_k(os.path.join(d, "lines.csv"), LINES_COLS)
    if rows is not None:
        D.present.add("lines")
        for r in rows:
            D.lines[rid_of("lines.csv", r)].append((
                int(r["t"]), r["actor"], int(r["design"]), int(r["g_p"]), r["role"], int(r["close"]), r["close_rule"],
                _oi(r["left_share"]), int(r["w_after_n"])))
    rows = _stream_k(os.path.join(d, "fleet_cmd.csv"), FLEET_COLS)
    if rows is not None:
        D.present.add("fleet")
        for r in rows:
            D.fleet[rid_of("fleet_cmd.csv", r)].append({
                "t": int(r["t"]), "actor": r["actor"], "kind": r["kind"], "gen_d": int(r["gen_d"]),
                "gen_line": int(r["gen_line"]), "g_front": int(r["g_front"]), "bind": r["line_bind"]})
    rows = _stream_k(os.path.join(d, "licences.csv"), LICENCES_COLS)
    if rows is not None:
        D.present.add("licences")
        for r in rows:
            D.licences[rid_of("licences.csv", r)].append({
                "t": int(r["t"]), "seller": r["seller"], "buyer": r["buyer"], "gen": int(r["gen"]),
                "profile": int(r["profile"]), "price": int(r["price"]), "t_end": _oi(r["t_end"])})


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
        v = "borderline"   # point <= 0.05 < upper, or point > 0.05 >= lower (#77)
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
    bs = share(b_k, b_n, cfg)
    v, va, vb = p4_verdict(shares, bs["p"] if b_n else None, cfg)
    return {"verdict": v, "a": va, "b": vb, "first_layer_share": shares, "n_actor_years": n_ay,
            "first_by_class": {c: dict(cnt) for c, cnt in sorted(by_class.items())},
            "eng_top3": bs, "eta_null_share": (null_eta / links if links else None), "links": links,
            "invalid_levers": invalid,
            "note": ("(b) не применимо: нет актор-лет с granted/n < 0,9 >= %d мес.; P4 решает (a) (#77)"
                     % p["short_months_min"]) if vb == "n/a" else ""}


def p4_verdict(shares, b_share, cfg):
    """P4 verdict (spec 7, #77). shares: first-link layer shares; b_share None = no units (n/a)."""
    p = cfg["p4"]
    mx, mn = max(shares.values()), min(shares.values())
    if mx <= p["max_first_share"] and mn >= p["min_layer_share"]:
        va = "pass"
    elif mx <= p["max_first_share"] + p["tol_max"] and mn >= p["min_layer_share"] - p["tol_min"]:
        va = "borderline"
    else:
        va = "fail"
    if b_share is None:
        vb = "n/a"
    elif b_share >= p["min_eng_top3"]:
        vb = "pass"
    elif b_share >= p["min_eng_top3"] - p["tol_b"]:
        vb = "borderline"
    else:
        vb = "fail"
    parts = [va] + ([vb] if vb != "n/a" else [])
    v = "fail" if "fail" in parts else "borderline" if "borderline" in parts else "pass"
    return v, va, vb


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
                if q["n_variants"] < 2 or q["fv_tech"] is None or q["fv_super"] is None:
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
            if x and x["t_first"] is not None and x["t_first"] >= 0 and not (x["t_07"] is not None and x["t_07"] < 0):
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


# ---------------------------------------------------------------- v0.5 prints: A40, A39, F17b, licences, fleet (format §7; P0, not criteria)

def runs_by_series(D, cfg, include_calib=True):
    """series -> rids (one per seed: the lowest thread count), all ok runs except det."""
    out = collections.defaultdict(dict)
    for rid in sorted(D.runs, key=lambda r: (D.runs[r]["threads"], r)):
        rec = D.runs[rid]
        if rec["status"] != "ok" or rec["series"] == cfg["series"]["det"]:
            continue
        if not include_calib and rec["series"] == cfg["series"]["calib"]:
            continue
        out[rec["series"]].setdefault(rec["seed"], rid)
    return {s: [m[k] for k in sorted(m)] for s, m in sorted(out.items())}


def coexist_ok(months):
    """COEXIST_MONEY (format §4.16 (б)) needs left = 0 and training = 0 in every month; the Lab also checks the pair,
    rate and Bind, which a40_c_months does not carry. A violation here is a P0 defect of the class."""
    return bool(months) and all(m["left"] == 0 and m["training"] == 0 for m in months)


def a40_prints(D, cfg):
    """Format §7 'A40': cancel_design by series, classes by actor, share of seeds with a C fire; L-O0, L-C0 (P0)."""
    if "a40" not in D.present:
        return None
    c = cfg["a40"]
    out = {"series": {}, "checks": {}}
    tab = set()
    bad_coexist, wn_fires, months_mismatch = [], 0, []
    lo0 = lc0 = coexist_main = 0
    for s, rids in runs_by_series(D, cfg).items():
        n = len(rids)
        cancel = 0
        by_cls = collections.Counter()
        seeds_c = 0
        for rid in rids:
            rows = D.a40.get(rid, [])
            cancel += sum(1 for r in rows if r["branch"] == "O")
            seeds_c += any(r["branch"] == "C" for r in rows)
            for r in rows:
                by_cls[(r["actor"], r["branch"], r["cls"])] += 1
                if r["branch"] != "C" or "a40c" not in D.present:
                    continue
                months = D.a40c.get(rid, {}).get((r["t"], r["actor"], r["design"]), [])
                tab.add(len(months))
                if r["cls"] == c["coexist_class"] and not coexist_ok(months):
                    bad_coexist.append("%s t=%d %s d=%d" % (rid, r["t"], r["actor"], r["design"]))
                if any(m["w_after_n"] for m in months):
                    wn_fires += 1
                if s in c["main_series"]:
                    coexist_main += r["cls"] == c["coexist_class"]
            if s in c["main_series"]:
                lo0 += sum(1 for r in rows if r["branch"] == "O" and r["cls"] == c["lo0_class"])
                lc0 += sum(1 for r in rows if r["branch"] == "C" and r["cls"] == c["lc0_class"])
        out["series"][s] = {"seeds": n, "cancel_design": cancel, "cancel_per_seed": cancel / n if n else None,
                            "seeds_with_c": share(seeds_c, n, cfg),
                            "per_seed": {"%s:%s:%s" % k: v / n for k, v in sorted(by_cls.items())}}
    if len(tab) > 1:
        months_mismatch = sorted(tab)
    have_main = any(s in runs_by_series(D, cfg) for s in c["main_series"])
    out["checks"] = {
        "L_O0": {"defects_main": lo0, "ok": (lo0 == 0) if have_main else None},
        "L_C0": {"other_main": lc0, "ok": (lc0 == 0) if have_main else None},
        "coexist_money_main": coexist_main,
        "coexist_class_violations": bad_coexist[:10], "coexist_class_violations_n": len(bad_coexist),
        "c_fires_with_w_after_n": wn_fires,
        "t_ab_months_per_fire": sorted(tab), "t_ab_mismatch": months_mismatch}
    return out


def ledger_prints(D, cfg):
    """Format §7 'A39': pool_exh share by actor per series; ledger invariants §4.11 (violation = P0 defect)."""
    if "ledger" not in D.present:
        return None
    out = {"series": {}, "violations": 0, "examples": []}
    for s, rids in runs_by_series(D, cfg).items():
        m, e = collections.Counter(), collections.Counter()
        for rid in rids:
            g = D.ledger.get(rid)
            if g is None:
                continue
            m.update(g["months"])
            e.update(g["exh"])
            out["violations"] += g["bad"]
            for x in g["bad_ex"]:
                if len(out["examples"]) < 5:
                    out["examples"].append("%s %s" % (rid, x))
        if m:
            out["series"][s] = {a: (e[a] / m[a] if m[a] else None) for a in cfg["actors"] if a in m}
    out["ok"] = out["violations"] == 0
    return out


def coexistences(rows, last):
    """Coexistences (W, N) of one run from lines.csv rows (format §4.12): per actor, maximal runs of consecutive months
    with the same pair of designs. End cause: close_rule of a line of the pair closed in the last month; HORIZON if the
    pair is still there in month `last` (censored)."""
    by = collections.defaultdict(lambda: collections.defaultdict(list))
    for (t, a, d, gp, role, close, rule, ls, wn) in rows:
        by[a][t].append((d, gp, role, close, rule))
    out = []
    for a, months in sorted(by.items()):
        cur = None          # (pair, start, prev_t, closes of the previous month)
        for t in sorted(months):
            ls = months[t]
            pair = None
            if len(ls) == 2 and {x[2] for x in ls} == {"W", "N"}:
                pair = tuple(sorted(x[0] for x in ls))
            if cur is not None and (pair != cur[0] or t != cur[2] + 1):
                out.append({"actor": a, "start": cur[1], "months": cur[2] - cur[1] + 1, "end": cur[3] or "NONE"})
                cur = None
            if pair is not None:
                rule = next((x[4] for x in ls if x[3] == 1 and x[4]), None)
                cur = (pair, t if cur is None else cur[1], t, rule)
        if cur is not None:
            end = "HORIZON" if cur[2] >= last else (cur[3] or "NONE")
            out.append({"actor": a, "start": cur[1], "months": cur[2] - cur[1] + 1, "end": end})
    return out


def launches_not_newer(rows):
    """Launches X with g_p(X) <= g_p(W) (spec A41 F17b 'Печать'): a design that first appears in lines after month 0
    while the actor had a line of g_p >= g_p(X) in the previous month."""
    seen = set()
    prev = collections.defaultdict(list)
    by_t = collections.defaultdict(list)
    for r in rows:
        by_t[r[0]].append(r)
    n = 0
    for t in sorted(by_t):
        cur = collections.defaultdict(list)
        for (_, a, d, gp, *_rest) in by_t[t]:
            cur[a].append((d, gp))
            if (a, d) not in seen:
                seen.add((a, d))
                if t > 0 and any(g2 >= gp for d2, g2 in prev.get(a, []) if d2 != d):
                    n += 1
        prev = cur
    return n


def f17b_prints(D, cfg):
    """Format §7 'F17b' from lines.csv: coexistences, durations (censored at 384), end causes, left_share of N,
    months with w_after_n = 1; invariant sum(left_share) per actor-month <= 10^6 (P0)."""
    if "lines" not in D.present:
        return None
    last = cfg["n_months"] - 1
    out = {"series": {}, "left_share_violations": 0}
    for s, rids in runs_by_series(D, cfg).items():
        co, lsn, wn = [], [], set()
        launches = 0
        rows_any = False
        for rid in rids:
            rows = D.lines.get(rid, [])
            if not rows:
                continue
            rows_any = True
            co += coexistences(rows, last)
            launches += launches_not_newer(rows)
            sums = collections.Counter()
            for (t, a, d, gp, role, close, rule, ls, w) in rows:
                if role == "N" and ls is not None:
                    lsn.append(ls / FX)
                if w:
                    wn.add((rid, t, a))
                if ls is not None:
                    sums[(t, a)] += ls
            out["left_share_violations"] += sum(1 for v in sums.values() if v > FX)
        if not rows_any:
            continue
        dur = [c["months"] for c in co]
        ends = collections.Counter(c["end"] for c in co)
        out["series"][s] = {"seeds": len(rids), "coexistences": len(co),
                            "per_seed_actor": dict(sorted(collections.Counter(c["actor"] for c in co).items())),
                            "months": dict(pq(dur, (0.5, 0.9)), max=max(dur) if dur else None,
                                           censored=ends.get("HORIZON", 0)),
                            "end": dict(sorted(ends.items())), "left_share_N": pq(lsn, (0.1, 0.5, 0.9)),
                            "w_after_n_actor_months": len(wn), "launches_gp_le_W": launches}
    out["ok"] = out["left_share_violations"] == 0
    return out


def licence_prints(D, cfg):
    """Spec §6.11 'Печать' from licences.csv and events.csv: licences by pair, term, refusals for money, resumes;
    licences.csv must match LIC_BUY events (format §4.14)."""
    if "licences" not in D.present:
        return None
    out = {"series": {}, "mismatch_runs": []}
    for s, rids in runs_by_series(D, cfg).items():
        pairs = collections.Counter()
        terms, open_, prices = [], 0, []
        first_fra = collections.defaultdict(list)
        refuse, resume = collections.Counter(), 0
        for rid in rids:
            lic = D.licences.get(rid, [])
            ev = D.events.get(rid, [])
            buys = sorted((e["t"], e["actor"], e["other"]) for e in ev if e["kind"] == "LIC_BUY")
            if "events" in D.present and buys != sorted((x["t"], x["buyer"], x["seller"]) for x in lic):
                out["mismatch_runs"].append(rid)
            for x in lic:
                pairs["%s>%s G%d" % (x["seller"], x["buyer"], x["gen"])] += 1
                prices.append(x["price"] / FX)
                if x["t_end"] is None:
                    open_ += 1
                else:
                    terms.append(x["t_end"] - x["t"])
            fr = [x for x in lic if x["buyer"] == cfg["middle_actor"]]
            if fr:
                f0 = min(fr, key=lambda x: x["t"])
                first_fra["P%d" % f0["profile"]].append(f0["t"])
            for e in ev:
                if e["kind"] == "LIC_REFUSE_MONEY":
                    refuse[e["actor"]] += 1
                resume += e["kind"] == "LIC_RESUME"
        n = len(rids)
        if not pairs and not refuse:
            out["series"][s] = {"seeds": n, "licences": 0}
            continue
        out["series"][s] = {"seeds": n, "licences": sum(pairs.values()),
                            "per_seed": sum(pairs.values()) / n if n else None,
                            "pairs": dict(pairs.most_common(8)), "term": dict(pq(terms, (0.5, 0.9)), open=open_),
                            "price_p50": quantile(prices, 0.5),
                            "fra_first_t_p50": {p: quantile(v, 0.5) for p, v in sorted(first_fra.items())},
                            "refuse_money": dict(sorted(refuse.items())), "resumes": resume}
    out["ok"] = not out["mismatch_runs"]
    return out


def fleet_prints(D, cfg):
    """Spec §6.10 from fleet_cmd.csv: E-gen distribution by (gen_line, gen_d), R-gen (gen_line <= gen_d: P0),
    commands at line_bind = ALLOY, #146 sales of laggards (gap = g_front - G >= 1, gen_d >= G; G = gen_line)."""
    if "fleet" not in D.present:
        return None
    f = cfg["fleet"]
    out = {"series": {}, "r_gen_violations": 0}
    for s, rids in runs_by_series(D, cfg).items():
        cmds = [(rid, x) for rid in rids for x in D.fleet.get(rid, [])]
        if not cmds:
            continue
        n = len(cmds)
        pairs = collections.Counter("(%d,%d)" % (x["gen_line"], x["gen_d"]) for _, x in cmds)
        rgen = sum(1 for _, x in cmds if x["gen_line"] <= x["gen_d"])
        out["r_gen_violations"] += rgen
        lag = [(rid, x) for rid, x in cmds
               if x["kind"] == "SELL" and x["g_front"] - x["gen_line"] >= 1 and x["gen_d"] >= x["gen_line"]]
        seeds_cmd = collections.defaultdict(set)
        for rid, x in cmds:
            seeds_cmd[x["actor"]].add(rid)
        egen = sum(1 for _, x in cmds if x["gen_line"] >= f["egen_min_gen_line"]) / n
        e21 = pairs.get("(2,1)", 0) / n
        out["series"][s] = {
            "seeds": len(rids), "commands": n, "kinds": dict(sorted(collections.Counter(x["kind"] for _, x in cmds).items())),
            "pairs_share": {k: v / n for k, v in sorted(pairs.items())},
            "egen_gen_line_ge3": egen, "egen_21": e21,
            "egen_expect": egen >= f["egen_share_min"] and e21 < f["egen_21_max"],
            "alloy_share": sum(1 for _, x in cmds if x["bind"] == "ALLOY") / n, "r_gen": rgen,
            "seeds_with_cmd": {a: len(v) / len(rids) for a, v in sorted(seeds_cmd.items())},
            "sold_gen_ge2_seeds": len({rid for rid, x in cmds if x["kind"] == "SELL" and x["gen_d"] >= 2}) / len(rids),
            "laggard_sales": {"n": len(lag), "actors": dict(sorted(collections.Counter(x["actor"] for _, x in lag).items())),
                              "t": pq([x["t"] for _, x in lag], (0.1, 0.5, 0.9))}}
    out["ok"] = out["r_gen_violations"] == 0
    return out


def series_identity(D, cfg):
    """Series whose golden_hash equals main on every common seed (idle switch); smoke check of DEBT-0032."""
    by = runs_by_series(D, cfg)
    main = {D.runs[r]["seed"]: D.runs[r]["golden"] for r in by.get(cfg["series"]["main"], [])}
    if not main:
        return None
    same, differ = [], []
    for s, rids in by.items():
        if s == cfg["series"]["main"]:
            continue
        common = [r for r in rids if D.runs[r]["seed"] in main]
        if not common:
            continue
        if all(D.runs[r]["golden"] == main[D.runs[r]["seed"]] for r in common):
            same.append(s)
        else:
            differ.append(s)
    return {"same_as_main": same, "differ": len(differ)}


def c1_stand(path, cfg):
    """C1 by c1_stand.csv (format §6 r8; spec §8.2 п.1, #184-#188). dlog_* = log c(n) - log c(2n): positive = the unit
    cost falls (sign fixed by TASK-0054). Statistics over the FULL arm, only rows with in_stat = 1 (DEV: min process
    yield >= 0.25 at the start of the doubling; MATURE: line_E start <= 400); empty set -> d = 1. The Wright P0 check
    (#187) is on the window mean in log of asm_over_floor_drop (contract §8 п.2 r9: the corridor holds for the mean,
    not for each doubling); other setups/arms are printed."""
    rows = _read_csv(path, C1_COLS)
    c = cfg["c1"]
    sel = collections.defaultdict(list)
    for r in rows:
        sel[(r["setup"], r["arm"])].append(r)

    def mean_drop(xs):
        return 1.0 - math.exp(sum(math.log(1.0 - x) for x in xs) / len(xs)) if xs else None

    def stats(setup, arm="FULL", only_stat=True):
        rs = [r for r in sel.get((setup, arm), []) if (r["in_stat"] == "1" or not only_stat)]
        if not rs:
            return {"n": 0, "cost_drop": None, "yield_share": None, "additivity": None}
        dc = [float(r["dlog_cost"]) for r in rs]
        dy = [float(r["dlog_yield"]) for r in rs]
        da = [float(r["dlog_asm"]) for r in rs]
        den = sum(dy) + sum(da)
        return {"n": len(rs), "cost_drop": 1.0 - math.exp(-sum(dc) / len(rs)),
                "yield_share": (sum(dy) / den) if den else None,
                "additivity": den / sum(dc) if sum(dc) else None,
                "min_yield": [float(r["min_yield"]) for r in rs],
                "per_doubling_cost_drop": [1.0 - math.exp(-x) for x in dc]}
    dev, mat = stats("DEV"), stats("MATURE")
    out = {"DEV": dev, "MATURE": mat}

    def d_corr(v, lo, hi):
        return 1.0 if v is None else max(0.0, lo - v, v - hi) / (hi - lo)
    out["C1a"] = {"value": dev["cost_drop"], "ok": dev["cost_drop"] is not None and dev["cost_drop"] <= c["dev_cost_max"],
                  "d": d_corr(dev["cost_drop"], 0.0, c["dev_cost_max"])}
    out["C1b"] = {"value": dev["yield_share"], "ok": dev["yield_share"] is not None
                  and c["dev_yield_lo"] <= dev["yield_share"] <= c["dev_yield_hi"],
                  "d": d_corr(dev["yield_share"], c["dev_yield_lo"], c["dev_yield_hi"])}
    out["C1d"] = {"value": mat["yield_share"], "ok": mat["yield_share"] is not None
                  and mat["yield_share"] <= c["mature_yield_max"],
                  "d": d_corr(mat["yield_share"], 0.0, c["mature_yield_max"])}
    e = mat["cost_drop"]
    out["C1e"] = {"value": e, "ok": e is not None and e <= c["mature_cost_max"],
                  "d": 1.0 if e is None else max(0.0, e - c["mature_cost_max"]) / c["mature_cost_max"]}
    w = [float(r["asm_over_floor_drop"]) for r in sel.get(("MATURE", "FULL"), []) if r["in_stat"] == "1"]
    wm = mean_drop(w)
    out["wright_P0"] = {"per_doubling": w, "window_mean": wm,
                        "ok": wm is not None and c["wright_lo"] <= wm <= c["wright_hi"]}
    out["loss_C1"] = sum(out[k]["d"] ** 2 for k in ("C1a", "C1b", "C1d", "C1e"))
    out["prints"] = {"%s/%s" % k: stats(k[0], k[1], only_stat=(k[0] != "MATURE_3X"))
                     for k in sorted(sel) if k[1] not in ("NO_YIELD", "NO_ASM", "NONE")}
    out["arms"] = sorted("%s/%s:%d" % (k[0], k[1], len(v)) for k, v in sel.items())
    return out


def p0_prints(D, cfg):
    return {"a40": a40_prints(D, cfg), "ledger": ledger_prints(D, cfg), "f17b": f17b_prints(D, cfg),
            "licences": licence_prints(D, cfg), "fleet": fleet_prints(D, cfg)}


# ---------------------------------------------------------------- top level

def analyze(D, cfg):
    main_s = cfg["series"]["main"]
    main_map = D.series.get(main_s, {})
    main_ids = [main_map[s] for s in sorted(main_map)]
    main_runs = [D.runs[r] for r in main_ids]
    res = {"problems": {"failed_runs": len(D.failed)}}
    res["p0"] = p0_prints(D, cfg)
    res["series_identity"] = series_identity(D, cfg)
    res["series_seeds"] = {s: len(r) for s, r in runs_by_series(D, cfg).items()}
    if not main_runs:
        res["decision"] = {"verdicts": {}, "outcome": "incomplete", "final": "incomplete (no main)"}
        res["determinism"] = determinism(D, cfg)
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


def fmt_d(x):
    """A distance or loss for the report: 4 decimals, but a positive value below 0.00005 is printed in full (never as 0)."""
    if isinstance(x, float) and 0 < abs(x) < 5e-5:
        return repr(x)
    return fmt(x)


def jsonable(x):
    if isinstance(x, dict):
        return {str(k): jsonable(v) for k, v in x.items()}
    if isinstance(x, (list, tuple)):
        return [jsonable(v) for v in x]
    if isinstance(x, float):
        if math.isinf(x) or math.isnan(x):
            return str(x)
        # 6 decimals for reading, but a non-zero value is never rounded to 0: distances d_k and losses L (spec §8.2 п.3-4)
        # decide the stop at L = 0, and a tiny positive d must stay positive in the JSON (TASK-0055 Codex)
        r = round(x, 6)
        return x if (r == 0 and x != 0) else r
    return x


# ---------------------------------------------------------------- rendering

def fs(s):
    if not s or s.get("n") in (None, 0):
        return "—"
    return "%s %s (k=%d, n=%d)" % (fmt(s["p"]), fci(s["ci"]), s["k"], s["n"])


def render(res, cfg, hashes):
    L = ["# EXP-0002 — анализ HYP-0002 (правило решения §7, slice-jet-aviation.md v0.25 r1; формат v0.5 r7)", ""]
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
        if res.get("determinism"):
            d = res["determinism"]
            L += ["", "## Детерминизм L2", "", "seeds %d, threads %s: %s" % (
                d["seeds"], d["threads"], "OK" if d["ok"] else "НЕСОВПАДЕНИЕ на seeds %s" % d["mismatch_seeds"])]
        return "\n".join(L + render_p0(res, cfg)) + "\n"
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
        L += ["", "## P5 сетка (`k_catch` × `k_diff` × `h_max`; ось `k_diff` через `r = 1 − k_diff`, #190)", "",
              "| точка | `k_diff` | `r` | chg |", "|---|---|---|---|"]
        kd = cfg["p5_kdiff"]
        for k, v in p5["points"].items():
            lvl = k.split("_")[1]
            L.append("| %s | %s | %s | %s |" % (k, fmt(kd[lvl], 2), fmt(1.0 - kd[lvl], 2), fs(v)))
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
          "- `volume_lead` (печать; годовая сумма строки t = 348, формат §4.2): %s" % ", ".join("%s %s" % (k, fmt(v)) for k, v in ld["volume_lead_share"].items())]
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
              "- примечание: %s" % (p4["note"] or "—")]
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
    return "\n".join(L + render_p0(res, cfg)) + "\n"


def flag(ok):
    return "—" if ok is None else ("OK" if ok else "**НАРУШЕНИЕ (P0)**")


def render_p0(res, cfg):
    """Format §7 prints of v0.5 tables; P0 flags and prints, not criteria (they do not change the outcome)."""
    L = []
    ss = res.get("series_seeds") or {}
    if ss:
        L += ["", "## Серии во входе", "", ", ".join("%s: %d" % kv for kv in ss.items())]
    si = res.get("series_identity")
    if si:
        L.append("")
        L.append("Серии с golden = main на всех общих seeds (холостой переключатель): %s; отличаются: %d." % (
            ", ".join(si["same_as_main"]) or "нет", si["differ"]))
    p0 = res.get("p0") or {}
    a = p0.get("a40")
    if a:
        ch = a["checks"]
        L += ["", "## A40 (печать, P0; формат §4.9–4.10, §7)", "",
              "- L-O0 (`DEFECTS` в main = 0): %d → %s" % (ch["L_O0"]["defects_main"], flag(ch["L_O0"]["ok"])),
              "- L-C0 (`OTHER` в main = 0): %d → %s" % (ch["L_C0"]["other_main"], flag(ch["L_C0"]["ok"])),
              "- `COEXIST_MONEY` в main (вне L-C0, ожидание [Предп.] 0): %d" % ch["coexist_money_main"],
              "- класс `COEXIST_MONEY` без `left = 0` и `training = 0` во всех месяцах: %d → %s%s" % (
                  ch["coexist_class_violations_n"], flag(ch["coexist_class_violations_n"] == 0),
                  (" (" + "; ".join(ch["coexist_class_violations"]) + ")") if ch["coexist_class_violations"] else ""),
              "- срабатываний С с `w_after_n = 1` хотя бы в одном месяце (оговорка (г)): %d" % ch["c_fires_with_w_after_n"],
              "- месяцев `a40_c_months` на срабатывание: %s%s" % (ch["t_ab_months_per_fire"] or "—",
                                                                   " — **разное число (P0)**" if ch["t_ab_mismatch"] else ""),
              "", "| серия | seeds | `cancel_design` | на seed | seeds с С | на seed по (актор:ветка:класс) |", "|---|---|---|---|---|---|"]
        for s, v in a["series"].items():
            L.append("| %s | %d | %d | %s | %s | %s |" % (s, v["seeds"], v["cancel_design"], fmt(v["cancel_per_seed"], 2),
                                                        fs(v["seeds_with_c"]), ", ".join("%s %s" % (k, fmt(x, 2)) for k, x in v["per_seed"].items()) or "—"))
    g = p0.get("ledger")
    if g:
        L += ["", "## A39 леджер (P0; формат §4.11)", "",
              "- инварианты (`invest ≤ rest + b_cap`, `taken_rest ≤ rest`, `t_a ≥ 0`): нарушений %d → %s%s" % (
                  g["violations"], flag(g["ok"]), (" (" + "; ".join(g["examples"]) + ")") if g["examples"] else ""),
              "", "| серия | доля месяцев `pool_exh` по актору |", "|---|---|"]
        for s, v in g["series"].items():
            L.append("| %s | %s |" % (s, ", ".join("%s %s" % (k, fmt(x, 3)) for k, x in v.items())))
    f = p0.get("f17b")
    if f:
        L += ["", "## F17b: сосуществования линий (печать; формат §4.12, §7)", "",
              "- Σ `left_share` по линиям актор-месяца ≤ 10⁶: нарушений %d → %s" % (f["left_share_violations"], flag(f["ok"])),
              "", "| серия | seeds | сосуществований | мес. p50/p90/max (цензура 384) | конец | `left_share` N p10/p50/p90 | `w_after_n` актор-мес. | запуски `g_p(X) ≤ g_p(W)` |",
              "|---|---|---|---|---|---|---|---|"]
        for s, v in f["series"].items():
            m = v["months"]
            L.append("| %s | %d | %d | %s/%s/%s (%d) | %s | %s/%s/%s | %d | %d |" % (
                s, v["seeds"], v["coexistences"], fmt(m["p50"], 1), fmt(m["p90"], 1), fmt(m["max"]), m["censored"],
                ", ".join("%s %d" % kv for kv in v["end"].items()) or "—", fmt(v["left_share_N"]["p10"], 3),
                fmt(v["left_share_N"]["p50"], 3), fmt(v["left_share_N"]["p90"], 3), v["w_after_n_actor_months"], v["launches_gp_le_W"]))
    li = p0.get("licences")
    if li:
        L += ["", "## Лицензии (печать §6.11; `licences.csv`, `events.csv`)", "",
              "- `licences.csv` = `LIC_BUY` по каждому прогону: %s%s" % (
                  flag(li["ok"]), (" (прогоны: %s)" % ", ".join(li["mismatch_runs"][:5])) if li["mismatch_runs"] else ""),
              "", "| серия | seeds | лицензий | на seed | пары (топ) | срок p50/p90 (открыты) | цена p50 | FRA 1-я лицензия p50 по профилю | отказы по деньгам | возобновления |",
              "|---|---|---|---|---|---|---|---|---|---|"]
        for s, v in li["series"].items():
            if not v["licences"] and "refuse_money" not in v:
                L.append("| %s | %d | 0 | | | | | | | |" % (s, v["seeds"]))
                continue
            L.append("| %s | %d | %d | %s | %s | %s/%s (%d) | %s | %s | %s | %d |" % (
                s, v["seeds"], v["licences"], fmt(v["per_seed"], 2), ", ".join("%s %d" % kv for kv in v["pairs"].items()),
                fmt(v["term"]["p50"], 1), fmt(v["term"]["p90"], 1), v["term"]["open"], fmt(v["price_p50"], 3),
                ", ".join("%s %s" % (k, fmt(x, 1)) for k, x in v["fra_first_t_p50"].items()) or "—",
                ", ".join("%s %d" % kv for kv in v["refuse_money"].items()) or "—", v["resumes"]))
    fl = p0.get("fleet")
    if fl:
        L += ["", "## Парк: команды A42.4 (печать §6.10; `fleet_cmd.csv`)", "",
              "- R-gen (`gen_line ≤ gen_d` = 0): %d → %s" % (fl["r_gen_violations"], flag(fl["ok"])),
              "- #146: продажа «отстающим» — `SELL` при `g_front − G ≥ 1` и `gen_d ≥ G`, `G` = `gen_line` [Предп.]",
              "", "| серия | seeds | команд | виды | E-gen: `gen_line ≥ 3` / (2,1) / ожидание | при `ALLOY` | seeds с проданным G≥2 | «отстающие» n, акторы, t p50 |",
              "|---|---|---|---|---|---|---|---|"]
        for s, v in fl["series"].items():
            lg = v["laggard_sales"]
            L.append("| %s | %d | %d | %s | %s / %s / %s | %s | %s | %d, %s, %s |" % (
                s, v["seeds"], v["commands"], ", ".join("%s %d" % kv for kv in v["kinds"].items()),
                fmt(v["egen_gen_line_ge3"], 3), fmt(v["egen_21"], 3), fmt(v["egen_expect"]), fmt(v["alloy_share"], 3),
                fmt(v["sold_gen_ge2_seeds"], 3), lg["n"], ", ".join("%s %d" % kv for kv in lg["actors"].items()) or "—",
                fmt(lg["t"]["p50"], 1)))
    return L


# ---------------------------------------------------------------- calibration build (control tables, C2, C4)

def render_control_tables(t, cfg):
    """Readable print of the control tables of spec §6.1 (the JSON is the artefact; this is for review)."""
    L = ["", "## Таблицы контролей §6.1", "", "### `L_K`: (актор, поколение) — `p_cens`, `m`, `s`, `⌊√3·s⌉`, n, известно на старте", "",
         "| актор:G | p_cens | m | s | half | n | старт |", "|---|---|---|---|---|---|---|"]
    for k, v in t["lk"].items():
        L.append("| %s | %s | %s | %s | %s | %d | %d |" % (k, fmt(None if v["p_cens_fx"] is None else v["p_cens_fx"] / FX, 3),
                                                         fmt(v["m"], 1), fmt(v["s"], 1), fmt(v["half"]), v["n"], v["start_known"]))
    never = sum(1 for v in t["delta"].values() if v["never"])
    ds = [v["delta"] for v in t["delta"].values() if v["delta"] is not None]
    L += ["", "`δ[a, n]`: узлов %d, «никогда» %d, δ p10/p50/p90 %s/%s/%s." % (
        len(t["delta"]), never, fmt(quantile(ds, 0.1)), fmt(quantile(ds, 0.5)), fmt(quantile(ds, 0.9)))]
    taus = (0, 12, 24, 60, 120, 240, 384, 444, 624)
    L += ["", "### `L_C`: `Y_k(τ)`, `U_k(τ)` (Fx6 → доли) при τ = %s (τ ≥ 384 — стартовое знание, участок Б)" % ", ".join(map(str, taus)),
          "", "| tier | Y | U |", "|---|---|---|"]
    for k in sorted(t["Y"]):
        L.append("| %s | %s | %s |" % (k, " / ".join(fmt(t["Y"][k][x] / FX, 3) for x in taus if x < len(t["Y"][k])),
                                       " / ".join(fmt(t["U"][k][x] / FX, 3) for x in taus if x < len(t["U"][k]))))
    L += ["", "### `L_P`: `T_design[g]` (мес.), `ρ[g]` (Fx6 raw)", "",
          "- T_design: %s" % ", ".join("G%s %s" % (g, fmt(v)) for g, v in sorted(t["T_design"].items())),
          "- ρ: %s" % ", ".join("G%s %s" % (g, fmt(v)) for g, v in sorted(t["rho"].items())), ""]
    return "\n".join(L) + "\n"


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


def calib_tables(d, cfg, point=None):
    """point: only the runs of that matrix point (runs.csv column `point`; T12: one calibration point of a stage manifest,
    TASK-0055 Codex r2); None = every run of the directory."""
    bad = calib_header_check(d, cfg)
    if bad:
        raise InputError("calibration build exposes leadership output: %s" % bad)
    c = cfg["calib"]
    rows = _read_csv(os.path.join(d, "runs.csv"), RUNS_COLS + ([] if point is None else ["point"]))
    if point is not None:
        rows = [r for r in rows if r["point"] == str(point)]
        if not rows:
            raise InputError("runs.csv: no run of point %s" % point)
    runs = {r["run"]: r for r in rows if r["status"] == "ok"}
    nt = collections.defaultdict(dict)
    for r in _read_csv(os.path.join(d, "node_times.csv"), NODE_TIMES_COLS):
        if r["run"] in runs:
            nt[r["run"]][(r["actor"], int(r["node"]))] = (_oi(r["t_known"]), _oi(r["t_first"]), _oi(r["t_07"]),
                                                          _oi(r["t_mastered"]))
    gt = collections.defaultdict(dict)
    for r in _read_csv(os.path.join(d, "gen_times.csv"), GEN_TIMES_COLS):
        if r["run"] in runs:
            gt[r["run"]][(r["actor"], int(r["gen"]))] = (_oi(r["t_req"]), _oi(r["t_first_unit"]))
    rids = sorted(runs, key=lambda x: int(runs[x]["seed"]))
    nm = cfg["n_months"]
    sk = -nm      # t_known of start knowledge (spec §6.1 v0.24 r2 erratum): CSV -1 -> -384 in every table

    def tko(v):
        return None if v is None else (sk if v < 0 else v)
    out = {"seeds": len(rids), "lk": {}, "delta": {}, "Y": {}, "U": {}, "T_design": {}, "rho": {}}
    for a in cfg["actors"]:
        for g_s, app in c["app_nodes"].items():
            g = int(g_s)
            vals, cens, start = [], 0, 0
            for rid in rids:
                tk = tko(nt[rid].get((a, app), (None,))[0])
                if tk is None:
                    cens += 1
                else:
                    start += tk == sk
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
                    tk = tko(nt[rid].get((a, nd), (None,))[0])
                    ta = tko(nt[rid].get((a, app), (None,))[0])
                    if tk is None:
                        ncens += 1
                    elif ta is not None:
                        diffs.append(tk - ta)
                never = ncens > c["cens_never_share"] * len(rids)
                out["delta"]["%s:%d" % (a, nd)] = {"never": never, "delta": None if never else median_low(diffs),
                                                    "n": len(diffs)}
    # Y_k, U_k from prc_month on every integer tau 0..2*384-1: part A (known in the horizon) tau = t - t_known,
    # part B (start knowledge) tau = t + 384 (spec §6.1 v0.24 r2)
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
            tk = tko(nt[r["run"]].get((r["actor"], nd), (None,))[0])
            if tk is None:
                continue
            tau = int(r["t"]) - tk
            # no filter on `known`: tau = 0 is the month of discovery, its yield/ucm are computed for the not-yet-known
            # process (spec A24 v0.7: "в compute месяца t_known процесс ещё не в снимке; yield и 1/yc считаются")
            if tau < 0:
                continue
            # #232 (spec §6.1 v0.27): part B (start knowledge, tau >= H) is the median over pairs WITH output (v > 0) only;
            # idle start processes (SUN/FRA/SWE tier 3) would switch the median to a decaying population
            if tau >= nm and int(r["v"]) <= 0:
                continue
            k = NODES[nd][2]
            ys, us = curves[k][tau]
            ys.append(int(r["yield"]))
            us.append(int(r["ucm"]))
    tsk = {}
    for k, byt in sorted(curves.items()):
        parts = {}
        for lo in (0, nm):
            taus = [x for x in range(lo, lo + nm) if x in byt]
            if not taus:
                parts[lo] = None
                continue
            ys, us = [], []
            ly, lu = median_low(byt[taus[0]][0]), median_low(byt[taus[0]][1])   # before the first observation: its value [Предп.]
            for tau in range(lo, lo + nm):
                if tau in byt:
                    ly, lu = median_low(byt[tau][0]), median_low(byt[tau][1])
                ys.append(ly)
                us.append(lu)
            parts[lo] = (ys, us)
        a_, b_ = parts[0], parts[nm]
        if a_ is None:
            a_ = ([b_[0][0]] * nm, [b_[1][0]] * nm)
        if b_ is None:
            b_ = ([a_[0][0]] * nm, [a_[1][0]] * nm)   # spec: a part without observations takes the first value of the other
        out["Y"][str(k)], out["U"][str(k)] = a_[0] + b_[0], a_[1] + b_[1]
        tsk[str(k)] = {"obs_A": sum(len(byt[x][0]) for x in byt if x < nm), "obs_B": sum(len(byt[x][0]) for x in byt if x >= nm),
                       "taus_A": sum(1 for x in byt if x < nm), "taus_B": sum(1 for x in byt if x >= nm),
                       "Y384": out["Y"][str(k)][nm],
                       "B_min": None if parts[nm] is None else min(parts[nm][0]),
                       "B_all_admitted": parts[nm] is not None and min(parts[nm][0]) >= cfg["calib"]["tsk_y_min"]}
    # T-SK (spec §6.1 v0.24 r2; #232 v0.27): every tier with a start PRC at USA or GBR has Y_k >= 0.25 (serial admission)
    # on all of part B and Y_k(384) >= 0.7
    start_tiers = set()
    for rid in rids:
        for (a, nd), x in nt[rid].items():
            if a in ("USA", "GBR") and NODES.get(nd, ("",))[0] == "PRC" and x[0] is not None and x[0] < 0:
                start_tiers.add(str(NODES[nd][2]))
    tsk_bad = [k for k in sorted(start_tiers) if k not in tsk or not tsk[k]["B_all_admitted"] or tsk[k]["Y384"] < 700000]
    # every PRC tier of the node table has observations in at least one part (otherwise there is no curve)
    all_tiers = sorted({str(k) for (typ, _, k) in NODES.values() if typ == "PRC"})
    no_obs = [k for k in all_tiers if k not in tsk or tsk[k]["obs_A"] + tsk[k]["obs_B"] == 0]
    tsk_check = {"tiers": tsk, "start_tiers": sorted(start_tiers), "failed_tiers": tsk_bad, "all_tiers": all_tiers,
                 "tiers_without_obs": no_obs, "ok": not tsk_bad and not no_obs}
    # T_design[g] (spec §6.1 L_P): median over calibration seeds and all actors of Requirement -> first unit in service.
    # t_req = -1 (a project of generation g at the start, format §9.1) is measured from month 0 (the earliest Requirement).
    # A start project already in service at the start (t_first_unit = -1) has no observable design time: it is counted
    # in `T_design_start_in_service` and left out (0 - 0 or -1 - 0 would be a fleet, not a design, time).
    tds = {}
    for g in range(1, 5):
        xs, from0, in_service = [], 0, 0
        for rid in rids:
            for a in cfg["actors"]:
                tr, tf = gt[rid].get((a, g), (None, None))
                if tr is None or tf is None:
                    continue
                if tf < 0:
                    in_service += 1
                    continue
                if tr < 0:
                    from0 += 1
                xs.append(tf - max(tr, 0))
        out["T_design"][str(g)] = median_low(xs)
        tds[str(g)] = {"n": len(xs), "start_from_month0": from0, "start_in_service": in_service}
    am = _read_csv(os.path.join(d, "actor_month.csv"), ACTOR_MONTH_COLS)
    for g in range(1, 5):
        xs = []
        for r in am:
            if r["run"] in runs and int(r["active_g%d" % g]) and int(r["proc_budget"]) > 0:
                xs.append((int(r["out_g%d" % g]) * FX * FX) // int(r["proc_budget"]))
        out["rho"][str(g)] = median_low(xs)
    # C2, C4
    def mast(nodes):
        # spec §8.2 C2: pool = (actor, star PRC) pairs over calibration seeds, pairs MASTERED at the start (t_mastered = -1)
        # excluded; a pair with t_first (also -1: in use at the start) enters with t_07 - t_first, or +inf if no t_07 by
        # 383 (lower median); pairs without t_first stay out of the p50, and if they are > 50% of the pool, d = 1.
        xs, cens, no_first, pool = [], 0, 0, 0
        for rid in rids:
            for a in cfg["actors"]:
                for nd in nodes:
                    x = nt[rid].get((a, nd))
                    if x is None:
                        continue
                    if x[3] is not None and x[3] < 0:
                        continue
                    pool += 1
                    if x[1] is None:
                        no_first += 1
                    elif x[2] is None:
                        cens += 1
                        xs.append(math.inf)
                    else:
                        xs.append(max(0, x[2] - x[1]) / 12.0)   # t_07 = -1 with t_first = 0 (V = 0 at the start): 0
        no_first_share = no_first / pool if pool else None
        return {"p50_years": median_low(xs), "n": len(xs), "pool": pool, "no_t_first": no_first,
                "no_t_first_share": no_first_share, "right_censored": cens,
                "censored_share": cens / len(xs) if xs else None,
                "d_is_1": not pool or no_first_share > 0.5}
    c2 = {"tier4": mast(c["c2_tier4"]), "tier5": mast(c["c2_tier5"])}
    t4, t5 = c2["tier4"]["p50_years"], c2["tier5"]["p50_years"]
    c2["tier4_in_corridor"] = (not c2["tier4"]["d_is_1"]) and t4 is not None and c["c2_years"][0] <= t4 <= c["c2_years"][1]
    # "tier-5 longer" with an empty or fully censored tier-5 pool is not met (spec §8.2 C2)
    full5 = c2["tier5"]["n"] == 0 or c2["tier5"]["right_censored"] == c2["tier5"]["n"]
    c2["tier5_longer"] = (not full5) and t4 is not None and t5 is not None and t5 > t4
    # §8.2 п.3: d_k = max(0, lo - s, s - hi) / (hi - lo); > 50% of the pool without t_first -> d = 1; p50 = +inf gives
    # d = inf (the formula, not capped: the spec does not cap it); the order tier-5 > tier-4 is binary (0 / 1)
    lo4, hi4 = c["c2_years"]
    c2["d_tier4"] = 1.0 if (c2["tier4"]["d_is_1"] or t4 is None) else max(0.0, lo4 - t4, t4 - hi4) / (hi4 - lo4)
    c2["d_order"] = 0.0 if (c2["tier5_longer"] and not c2["tier5"]["d_is_1"]) else 1.0
    c4 = {}
    for g_s, (y0, y1) in c["c4_windows"].items():
        hit = 0
        for rid in rids:
            ts = [gt[rid][(a, int(g_s))][1] for a in cfg["actors"]
                  if (a, int(g_s)) in gt[rid] and gt[rid][(a, int(g_s))][1] is not None]
            if ts and y0 <= year_of(cfg, min(ts)) <= y1:
                hit += 1
        sh = hit / len(rids) if rids else None
        lo = c["c4_min_share"]
        c4[g_s] = {"share": sh, "ok": sh is not None and sh >= lo,
                   "d": 1.0 if sh is None else max(0.0, lo - sh, sh - 1.0) / (1.0 - lo)}   # §8.2 п.3, corridor [0.5; 1]
    tables = {k: out[k] for k in ("lk", "delta", "Y", "U", "T_design", "rho")}
    try:
        tables_file, tables_error = control_tables_file(tables, cfg), None
    except InputError as e:     # an incomplete table (e.g. a tier without a curve) has no CONTROL_TABLES file and no hash
        tables_file, tables_error = None, str(e)
    # P0 of the calibration build (format §5): A40 L-O0/L-C0 and the A39 ledger invariants on calibration seeds
    D = Data()
    for rid, r in runs.items():
        D.runs[rid] = {"run": rid, "series": "calib-P0", "seed": int(r["seed"]), "threads": int(r["threads"]),
                       "status": "ok", "golden": r["golden_hash"]}
    load_v05(D, d, "", only_known=point is not None)
    c0 = copy.deepcopy(cfg)
    c0["a40"]["main_series"] = ["calib-P0"]     # L-O0 / L-C0 on calibration seeds (format §5: P0, not leadership)
    p0 = {"a40": a40_prints(D, c0), "ledger": ledger_prints(D, c0)}
    return {"control_tables": tables, "control_tables_file": tables_file, "control_tables_hash": None if tables_file is None else sha256_text(tables_file),
            "control_tables_error": tables_error,
            "seeds": out["seeds"], "C2": c2, "C4": c4, "p0": p0, "T_SK": tsk_check, "T_design_obs": tds}


CONTROL_TABLES_FORMAT = "control_tables/1"


def control_tables_file(tables, cfg):
    """The CONTROL_TABLES file (format §3.1, TASK-0054): integers only, canonical JSON (sorted keys, no whitespace, no
    trailing newline), so sha256(file bytes) = control_tables_hash in Python and in the Lab. lk: p_cens (Fx6 raw), mean
    (m rounded half up), half (round(sqrt3*s)); an all-censored (actor, g) has mean = half = 0 and p_cens = 1. delta per
    actor and node of G1-G4: months or null ("never", or no seed with both times); the APP node of g is 0. lc: Y_k, U_k
    raw on tau 0..2H-1 for tiers 3-5. lp: T_design (months), rho (raw)."""
    nm = cfg["n_months"]
    apps = {int(v): int(g) for g, v in cfg["calib"]["app_nodes"].items()}
    lk, delta = {}, {}
    for a in cfg["actors"]:
        lk[a], delta[a] = {}, {}
        for g in range(1, 5):
            e = tables["lk"]["%s:%d" % (a, g)]
            allc = e["m_int"] is None
            lk[a][str(g)] = {"p_cens": FX if allc else e["p_cens_fx"], "mean": 0 if allc else e["m_int"],
                             "half": 0 if allc else e["half"]}
        for nd, (typ, ng, _) in sorted(NODES.items()):
            if not 1 <= ng <= 4:
                continue
            if nd in apps:
                delta[a][str(nd)] = 0
                continue
            x = tables["delta"].get("%s:%d" % (a, nd))
            delta[a][str(nd)] = None if (x is None or x["never"] or x["delta"] is None) else int(x["delta"])
    for k in ("3", "4", "5"):
        for c in ("Y", "U"):
            if k not in tables[c] or len(tables[c][k]) != 2 * nm:
                raise InputError("control tables: curve %s_%s missing or not %d knots" % (c, k, 2 * nm))
    for g in ("1", "2", "3", "4"):
        if tables["T_design"].get(g) is None or tables["rho"].get(g) is None:
            raise InputError("control tables: T_design/rho of G%s not observed" % g)
    doc = {"format": CONTROL_TABLES_FORMAT, "horizon": nm, "actors": list(cfg["actors"]), "max_node": max(NODES),
           "lk": lk, "delta": delta,
           "lc": {"Y": {k: [int(v) for v in tables["Y"][k]] for k in ("3", "4", "5")},
                  "U": {k: [int(v) for v in tables["U"][k]] for k in ("3", "4", "5")}},
           "lp": {"T_design": {g: int(tables["T_design"][g]) for g in ("1", "2", "3", "4")},
                  "rho": {g: int(tables["rho"][g]) for g in ("1", "2", "3", "4")}}}
    return canonical_json(doc)


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
        S.rows["requirements"].append([rid, 1, "USA", 2, 24, 2, FX * 3 // 2, FX * (2 if s % 2 else 1)])
        S.rows["requirements"].append([rid, 0, "USA", 1, 0, 1, FX, ""])
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

    # #77: P2 and P4 edge rules
    def p2v(k):
        return crit_p2([{"small": 1 if i < k else 0, "middle": 0} for i in range(1000)], cfg)["verdict"]
    check("P2 point 0.04 upper > 0.05 -> borderline", p2v(40) == "borderline", p2v(40))
    check("P2 point 0.06 lower <= 0.05 -> borderline (#77)", p2v(60) == "borderline", p2v(60))
    check("P2 point 0.07 lower > 0.05 -> fail", p2v(70) == "fail", p2v(70))
    check("P2 point 0.02 upper <= 0.05 -> pass", p2v(20) == "pass", p2v(20))

    def p4v(mn, mx=0.40, b=0.90):
        sh = {"KNOWLEDGE": mx, "CAPABILITY": 1 - mx - 2 * mn - 0.0, "PRODUCT": mn, "ECONOMY": mn}
        return p4_verdict(sh, b, cfg)
    check("P4 (a) min 0.045 -> borderline (1 p.p. tolerance)", p4v(0.045)[1] == "borderline", p4v(0.045))
    check("P4 (a) min 0.035 -> fail", p4v(0.035)[1] == "fail", p4v(0.035))
    check("P4 (a) min 0.05 -> pass", p4v(0.05)[1] == "pass", p4v(0.05))
    check("P4 (a) max 0.74 -> borderline; 0.76 -> fail",
          p4v(0.06, mx=0.74)[1] == "borderline" and p4v(0.06, mx=0.76)[1] == "fail")
    check("P4 (b) 0.77 -> borderline; 0.74 -> fail",
          p4v(0.10, b=0.77)[0] == "borderline" and p4v(0.10, b=0.74)[0] == "fail")
    check("P4 (b) no units -> n/a, P4 decided by (a)",
          p4v(0.10, b=None) == ("pass", "pass", "n/a") and p4v(0.045, b=None) == ("borderline", "borderline", "n/a"))

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
    ap.add_argument("--point", type=int, help="with --calib: only the runs of this matrix point (runs.csv `point`)")
    ap.add_argument("--tables-out", help="with --calib: write the CONTROL_TABLES file (format §3.1, control_tables/1)")
    ap.add_argument("--c1", help="c1_stand.csv (format §6): C1a, C1b, C1d, C1e and the Wright P0 check")
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
        if a.c1:
            r = c1_stand(a.c1, cfg)
            text = json.dumps(jsonable(r), indent=1, sort_keys=True) + "\n"
            md = "# C1 по `c1_stand.csv` (спец. §8.2 п.1; формат §6)\n\n" + "".join(
                "- %s: %s → %s\n" % (k, fmt(r[k]["value"]), "в коридоре" if r[k]["ok"] else "вне коридора")
                for k in ("C1a", "C1b", "C1d", "C1e")) + "- d_C1e: %s\n- Wright P0 (`asm_over_floor_drop` MATURE 15–30%%): %s %s\n- плечи: %s\n" % (
                fmt_d(r["C1e"]["d"]), "OK" if r["wright_P0"]["ok"] else "НАРУШЕНИЕ", [round(x, 4) for x in r["wright_P0"]["per_doubling"]],
                ", ".join(r["arms"]))
        elif a.calib:
            r = calib_tables(a.calib, cfg, a.point)
            if a.tables_out:
                if r["control_tables_file"] is None:
                    raise InputError("no CONTROL_TABLES file: %s" % r["control_tables_error"])
                with open(a.tables_out, "w", encoding="ascii", newline="") as f:
                    f.write(r["control_tables_file"])
            r = {k: v for k, v in r.items() if k != "control_tables_file"}
            text = json.dumps(jsonable(r), indent=1, sort_keys=True) + "\n"
            md = ("# Калибровочная сборка: таблицы контролей §6.1, C2, C4\n\n- control_tables_hash: `%s`\n- seeds: %d\n"
                  "- C2: %s\n- C4: %s\n" % (r["control_tables_hash"], r["seeds"], json.dumps(jsonable(r["C2"]), sort_keys=True),
                                           json.dumps(jsonable(r["C4"]), sort_keys=True)))
            md += render_control_tables(r["control_tables"], cfg)
            md += "- T_design, наблюдений: %s\n" % ", ".join("G%s n=%d (старт от мес. 0: %d; в строю на старте, вне медианы: %d)" % (
                g, v["n"], v["start_from_month0"], v["start_in_service"]) for g, v in sorted(r["T_design_obs"].items()))
            ts = r["T_SK"]
            md += "\n## Проверка T-SK (§6.1 v0.24 r2)\n\n- tier со стартовым PRC у USA/GBR: %s; провал: %s; tier без наблюдений: %s → %s\n" % (
                ", ".join(ts["start_tiers"]) or "—", ", ".join(ts["failed_tiers"]) or "нет", ", ".join(ts["tiers_without_obs"]) or "нет",
                "OK" if ts["ok"] else "ОШИБКА ТАБЛИЦ")
            md += "".join("- tier %s: наблюдений А %d (τ %d), Б %d (τ %d); Y(384) %s\n" % (
                k, v["obs_A"], v["taus_A"], v["obs_B"], v["taus_B"], fmt(v["Y384"] / FX, 3)) for k, v in sorted(ts["tiers"].items()))
            c2r = r["C2"]
            md += "\n## Расстояния §8.2 п.3\n\n- d_C2 (tier-4 p50 в [%s; %s] лет) = %s; d_C2,порядок (tier-5 > tier-4) = %s\n- %s\n" % (
                cfg["calib"]["c2_years"][0], cfg["calib"]["c2_years"][1], fmt_d(c2r["d_tier4"]), fmt_d(c2r["d_order"]),
                "; ".join("d_C4,G%s = %s (доля %s)" % (g, fmt_d(v["d"]), fmt(v["share"], 2)) for g, v in sorted(r["C4"].items())))
            md += "\n".join(render_p0({"p0": r["p0"]}, cfg)) + "\n"
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
