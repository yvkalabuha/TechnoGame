#!/usr/bin/env python3
"""TM-budget-rules (TASK-0123): the budget rules NOW, A, B1, B2 of docs/game-design/economy/slice-budget-convergence.md §7, §8.

Reads a `matrix` directory of lab/manifests/TM-budget-rules.json (JetSlice FULL, 1024 months; series NOW, A, B1, B2; seeds; threads 1 and 2)
and prints Markdown for the measurement report:
  - validity: every run ok and 1024 months, all four series, threads 1 = 2 by golden hash per (series, seed) (L2), NOW = TM-horizon-2030 goldens;
  - control of the rule (§7 item 6): the monthly budget of supply_month against an exact integer replica of BudgetMath (Fx6 floor arithmetic)
    and against the real closed forms of the note (largest relative gap);
  - ceiling months of B1, B2 (first month whose budget differs from NOW) against the note's closed form y = ln(c/r0)/d;
  - B1 = NOW before the first ceiling month: every monthly row of the run tables equal up to that month;
  - per series, actor and year 1960, 1977, 1990, 2010, 2030 (§7 items 1-6): rank, catch-up win, key_G, generations fielded, workforce,
    alloy cap, largest monthly line output, total output and volume, limiter shares, unused tooling share, budget;
  - §8 tolerances of A and B2 against NOW; the "money limits" criterion of §7.
Only threads = 1 runs are read for the tables (threads 2 must have the same golden; checked).

Usage: python3 lab/analysis/tm_budget_rules.py lab/results/tm-budget-rules [--pilot]
(--pilot accepts a partial matrix of `matrix --seeds N`; the series run must be a valid matrix.) Exit code 1 = a validity rule failed.
"""
import csv
import json
import math
import os
import re
import statistics
import sys
from collections import defaultdict

ACTORS = ["USA", "GBR", "SUN", "FRA", "SWE"]
SERIES = ["NOW", "A", "B1", "B2"]
CHECK_YEARS = [1960, 1977, 1990, 2010, 2030]
MONTHS = 1024
H_CALIB = 384  # 1945-09 … 1977-08: the calibrated horizon of §8 ("до 1977")
FX = 1_000_000
SHIFT_MAX = 24  # §8: months

# Presets of src/Singularity.Core/Policy/BudgetRule.cs (BudgetRulePlan.PresetA / PresetB), raw Fx6: the measurement constants of the note.
PRESET_A = {"GBR": (120_000, 25_000), "SUN": (440_000, 60_000), "FRA": (170_000, 80_000), "SWE": (27_000, 60_000)}
PRESET_B = {"B1": {"SUN": 520_000, "FRA": 230_000, "SWE": 36_000}, "B2": {"SUN": 430_000, "FRA": 196_000, "SWE": 32_000}}
FRONTIER = "USA"

# docs/simulation/m-play-horizon-2030.md (TM-horizon-2030, 2026-11-19): golden prefixes of seeds 1-5, main = NOW.
TM2030_GOLDEN = {1: "63a31fe3", 2: "bc71036c", 3: "19b5fd18", 4: "ade2efb9", 5: "c5802802"}

# Monthly tables whose rows carry month t; columns filled from later months are dropped (licences.t_end; a40 and actor_year look ahead, summary
# and hash_years are whole-run, so they are not compared).
MONTHLY_TABLES = {"supply_month": [], "lines": [], "tooling_ledger": [], "events": [], "fleet_cmd": [], "licences": ["t_end"]}


# ---------------------------------------------------------------- Fx6 replica (src/Singularity.Kernel/Fx6.cs, PolicyProfile.cs BudgetMath)

def fmul(a, b):
    return (a * b) // FX  # FloorDiv of the 128-bit product


def monthly(per_year):
    return per_year // 12  # BudgetMath.Monthly: floor toward −∞ (Python // is floor)


def next_now(b, g):
    return fmul(b, FX + monthly(g))


def next_a(b, f, gf, s, lam):
    """Rule A step (BudgetMath.NextConverging). The ONE place to change when the Core formula of rule A changes (TASK-0122 follow-up)."""
    pulled = b + fmul(monthly(lam), fmul(s, f) - b)
    nxt = fmul(pulled, FX + monthly(gf))
    # Clamp (810e12a): the share never crosses s. Exact comparison with the real target s·B_F; the next target is s·Next(B_F, g_F).
    target_next = s * next_now(f, gf)
    if b * FX >= s * f:
        nxt = max(nxt, -(-target_next // FX))  # ceil
    else:
        nxt = min(nxt, target_next // FX)  # floor
    return max(nxt, 1)


def next_b(b, g, f, gf, c):
    own = next_now(b, g)
    cap = fmul(c, next_now(f, gf))
    return cap if cap < own else own


def replica(rule, b0, growth, months):
    """Budget by actor and month t = 0 … months−1 under `rule` (exact integers, as PolicySystem.NextBudget)."""
    out = {a: [b0[a]] for a in ACTORS}
    for t in range(months - 1):
        f = out[FRONTIER][t]
        gf = growth[FRONTIER]
        for a in ACTORS:
            b = out[a][t]
            if a == FRONTIER or rule == "NOW":
                n = next_now(b, growth[a])
            elif rule == "A":
                n = next_a(b, f, gf, *PRESET_A[a]) if a in PRESET_A else next_now(b, growth[a])
            else:
                c = PRESET_B[rule].get(a)
                n = next_b(b, growth[a], f, gf, c) if c else next_now(b, growth[a])
            out[a].append(n)
    return out


def closed_form(rule, a, t, b0, growth):
    """Real closed forms of the note (§3, §4): the fixed-point recurrence without rounding."""
    gu = monthly(growth[FRONTIER]) / FX
    fu = b0[FRONTIER] / FX * (1 + gu) ** t
    old = b0[a] / FX * (1 + monthly(growth[a]) / FX) ** t
    if a == FRONTIER or rule == "NOW":
        return old
    if rule == "A":
        if a not in PRESET_A:
            return old
        s, lam = PRESET_A[a][0] / FX, monthly(PRESET_A[a][1]) / FX
        r0 = b0[a] / b0[FRONTIER]
        return (s - (s - r0) * (1 - lam) ** t) * fu
    c = PRESET_B[rule].get(a)
    if not c:
        return old
    # B(t) = min(old(t), c·F(t)) once the ceiling binds; the old path is monotone in share, so the min of the two paths is the closed form.
    return min(old, c / FX * fu)


# §4 table of the note, "Срабатывает" as a month t (t = 0 is 1945-09): B1 SUN 1978-10, FRA 1978-08, SWE 1980-02; B2 SUN 1971-02, FRA 1974-03,
# SWE 1970-02.
NOTE_CEILING = {"B1": {"SUN": 397, "FRA": 395, "SWE": 413}, "B2": {"SUN": 305, "FRA": 342, "SWE": 293}}


def real_ceiling_month(rule, a, b0, growth):
    """The same crossing with the model's monthly compounding (1 + ⌊g/12⌋) and no rounding of the products: the first t whose budget is capped."""
    c = PRESET_B[rule].get(a)
    if not c:
        return None
    r0 = b0[a] / b0[FRONTIER]
    d_m = math.log(1 + monthly(growth[a]) / FX) - math.log(1 + monthly(growth[FRONTIER]) / FX)
    # budget(t) = min(old(t), c·F(t)); the first capped budget is at the first integer t > ln(c/r0)/d_m.
    return math.floor(math.log(c / FX / r0) / d_m) + 1


# ---------------------------------------------------------------- scenario

def scenario_growth(repo):
    """growth_pa of the actors from mods/core/scenario_1945.yaml (raw Fx6), read by line (no YAML dependency)."""
    path = os.path.join(repo, "mods", "core", "scenario_1945.yaml")
    growth, cur = {}, None
    with open(path, encoding="utf-8") as f:
        lines = f.readlines()
    for line in lines:
        m = re.match(r"\s*- id: (\w+)\s*$", line)
        if m and m.group(1) in ACTORS:
            cur = m.group(1)
        m = re.match(r"\s*growth_pa: ([0-9.]+)", line)
        if m and cur and cur not in growth:
            whole, _, frac = m.group(1).partition(".")
            growth[cur] = int(whole) * FX + int((frac + "000000")[:6])
    missing = [a for a in ACTORS if a not in growth]
    if missing:
        raise SystemExit(f"growth_pa missing for {missing} in {path}")
    return growth


# ---------------------------------------------------------------- one run

def year_of(t):
    return 1945 + (8 + t) // 12


def t_sept(year):
    return 12 * (year - 1945)


def read(run_dir, name):
    with open(os.path.join(run_dir, name + ".csv"), newline="") as f:
        return list(csv.DictReader(f))


def num(v):
    return None if v in ("", None) else int(v)


def comp_rank(keys):
    return {a: 1 + sum(1 for b in keys if keys[b] > keys[a]) for a in keys}


def line_starts(lines):
    """Per actor: months in which a line (actor, design, g_p) appears that was not there the month before (as tm_horizon_2030.py)."""
    by_t = defaultdict(set)
    for r in lines:
        by_t[int(r["t"])].add((r["actor"], r["design"], r["g_p"]))
    starts, prev = defaultdict(list), set()
    for t in sorted(by_t):
        cur = by_t[t]
        for k in sorted(cur - prev):
            starts[k[0]].append(t)
        prev = cur
    return starts


def analyse_run(run_dir):
    with open(os.path.join(run_dir, "run.json")) as f:
        info = json.load(f)
    months = int(info["model_info"]["months"])
    ay = read(run_dir, "actor_year")
    key, volume = {}, {}
    for r in ay:
        t, a = int(r["t"]), r["actor"]
        key[(t, a)] = (int(r["key_G"]), int(r["key_T"]), int(r["key_N"]), int(r["key_P"]))
        volume[(t, a)] = int(r["volume"])
    years = sorted({t for t, _ in key})
    rank = {t: comp_rank({a: key[(t, a)] for a in ACTORS}) for t in years}
    sm = read(run_dir, "supply_month")
    budget = {a: [None] * months for a in ACTORS}
    month = {}
    for r in sm:
        t, a = int(r["t"]), r["actor"]
        budget[a][t] = int(r["budget"])
        month[(t, a)] = r
    gen = read(run_dir, "gen_times")
    first_unit = {(r["actor"], int(r["gen"])): num(r["t_first_unit"]) for r in gen}
    t_req = {(r["actor"], int(r["gen"])): num(r["t_req"]) for r in gen}
    fielded = defaultdict(list)
    for r in gen:
        v = num(r["t_fielded"])
        if v is not None:
            fielded[r["actor"]].append(v)
    starts = line_starts(read(run_dir, "lines"))

    yearly = {}
    for y in CHECK_YEARS:
        t0 = t_sept(y)
        window = [t for t in range(t0, min(t0 + 12, months))]
        for a in ACTORS:
            rows = [month[(t, a)] for t in window]
            m0 = month[(t0, a)]
            n = len(rows)
            wf_lim = sum(1 for r in rows if int(r["eng_granted"]) < int(r["eng_n"]) or int(r["sk_granted"]) < int(r["sk_n"]))
            rest = sum(int(r["tool_rest"]) for r in rows)
            yearly[(y, a)] = dict(
                months=n,
                rank=rank[t0][a], key_g=key[(t0, a)][0], gens=sum(1 for v in fielded[a] if v <= t0),
                budget=int(m0["budget"]) / FX, wf_eng=int(m0["wf_eng"]), wf_skilled=int(m0["wf_skilled"]),
                alloy_cap=int(m0["alloy_cap"]) / FX, alloy_supply=int(m0["alloy_supply"]) / FX,
                rate_max=max(int(r["rate_max"]) for r in rows) / FX, output=sum(int(r["rate_sum"]) for r in rows) / FX,
                volume=volume[(t0, a)] / FX,
                lines=sum(1 for r in rows if int(r["lines"]) > 0) / n,
                lim_wf=wf_lim / n,
                lim_alloy=sum(1 for r in rows if int(r["bind_alloy"]) > 0) / n,
                lim_money=sum(1 for r in rows if int(r["bind_money"]) > 0) / n,
                unspent=(sum(int(r["tool_unspent"]) for r in rows) / rest) if rest else None,
            )
    output_total = {a: sum(int(month[(t, a)]["rate_sum"]) for t in range(months)) / FX for a in ACTORS}
    r0, r1 = rank[0], rank[t_sept(2030)]
    win = {a: (r0[a] - r1[a] >= 1 or r1[a] == 1) for a in ACTORS}
    return dict(seed=int(info["seed"]), months=months, golden=info["golden_hash"], data_hash=info["data_hash"],
                manifest_hash=info["manifest_hash"], core=info["core_commit"], dirty=info.get("dirty"), budget=budget,
                yearly=yearly, output_total=output_total, rank=rank, win=win, first_unit=first_unit, t_req=t_req, starts=starts, dir=run_dir)


# ---------------------------------------------------------------- checks

def first_budget_diff(run, base, actor):
    for t in range(MONTHS):
        if run["budget"][actor][t] != base["budget"][actor][t]:
            return t
    return None


def rows_before(run_dir, table, t_end):
    """Rows with t < t_end as (t, cells) without the run id and the look-ahead columns of MONTHLY_TABLES."""
    rows = []
    with open(os.path.join(run_dir, table + ".csv"), newline="") as f:
        rd = csv.reader(f)
        header = next(rd)
        ti = header.index("t")
        keep = [i for i, c in enumerate(header) if i > 0 and c not in MONTHLY_TABLES[table]]
        for r in rd:
            if int(r[ti]) < t_end:
                rows.append((int(r[ti]), tuple(r[i] for i in keep)))
    return rows


def first_world_diff(run_dir, base_dir, horizon):
    """First month t < horizon at which a monthly table differs between two runs (None = equal before horizon)."""
    first = None
    for table in MONTHLY_TABLES:
        a, b = rows_before(run_dir, table, horizon), rows_before(base_dir, table, horizon)
        if a == b:
            continue
        ta = defaultdict(list)
        tb = defaultdict(list)
        for t, r in a:
            ta[t].append(r)
        for t, r in b:
            tb[t].append(r)
        t = min(t for t in set(ta) | set(tb) if ta[t] != tb[t])
        first = t if first is None else min(first, t)
    return first


def tolerance_s8(var, base):
    """§8 for one variant on paired seeds. Returns a list of violations (str); empty = passes."""
    seeds = sorted(set(var) & set(base))
    out = []
    need = math.ceil(0.8 * len(seeds))
    for y in (1960, 1977):
        for a in ACTORS:
            same = sum(1 for s in seeds if var[s]["yearly"][(y, a)]["rank"] == base[s]["yearly"][(y, a)]["rank"])
            far = [s for s in seeds if abs(var[s]["yearly"][(y, a)]["rank"] - base[s]["yearly"][(y, a)]["rank"]) > 1]
            if same < need:
                out.append(f"rank {a} {y}: equal in {same} of {len(seeds)} seeds (need {need})")
            if far:
                out.append(f"rank {a} {y}: differs by more than 1 in seeds {far}")
    for a in ACTORS:
        mv = statistics.median(var[s]["yearly"][(1977, a)]["key_g"] for s in seeds)
        mb = statistics.median(base[s]["yearly"][(1977, a)]["key_g"] for s in seeds)
        if mv != mb:
            out.append(f"key_G {a} 1977: median {mv} vs {mb}")
    for s in seeds:
        v, b = var[s], base[s]
        for name, col in (("first unit", "first_unit"), ("project t_req", "t_req")):
            for a, g in sorted(set(b[col]) | set(v[col])):
                verdict = timing(v[col].get((a, g)), b[col].get((a, g)))
                if verdict:
                    out.append(f"seed {s} {a} G{g} {name}: {verdict}")
        for a in ACTORS:
            lv, lb = v["starts"][a], b["starts"][a]
            # §8 has two separate conditions. (1) Shift: the k-th line start of each side is one item, compared by timing().
            for k in range(max(len(lv), len(lb))):
                x = lv[k] if k < len(lv) else None
                z = lb[k] if k < len(lb) else None
                verdict = timing(x, z)
                if verdict:
                    tag = " (FRA first line, #169)" if a == "FRA" and k == 0 else ""
                    out.append(f"seed {s} {a} line {k + 1} (shift): {verdict}{tag}")
            # (2) "The number of lines does not change": the count of starts before H_CALIB, strictly. A start that crosses the cutoff within
            # SHIFT_MAX is annotated, but it is still a count violation (the letter of §8).
            nv, nb = sum(1 for t in lv if t < H_CALIB), sum(1 for t in lb if t < H_CALIB)
            if nv != nb:
                cross = [f"line {k + 1} {lv[k]} vs {lb[k]}" for k in range(min(len(lv), len(lb)))
                         if (lv[k] < H_CALIB) != (lb[k] < H_CALIB) and abs(lv[k] - lb[k]) <= SHIFT_MAX]
                note = f"; boundary crossing within {SHIFT_MAX} months: {', '.join(cross)}" if cross else ""
                out.append(f"seed {s} {a} lines before {H_CALIB} (count change): {nv} vs {nb}{note}")
    return sorted(set(out))


def timing(tv, tb):
    """§8 date rule for one item (variant date tv, NOW date tb; None = never, −1 = at the start). The item is in scope when EITHER date is before
    H_CALIB; then it violates when one side has no date or the dates differ by more than SHIFT_MAX. Returns the violation text or None."""
    dates = [t for t in (tv, tb) if t is not None]
    if not dates or min(dates) >= H_CALIB:
        return None
    if tv is None or tb is None:
        return f"{tv} vs {tb} (present on one side only)"
    return f"{tv} vs {tb} (shift {tv - tb})" if abs(tv - tb) > SHIFT_MAX else None


PLACES_DEFINITION = ("place difference = |median over seeds of the variant's rank − median over seeds of NOW's rank| at a check year "
                     "(a difference of medians, not a per-seed difference); wins = catch-up wins among the paired seeds")


def places_or_wins(var, base):
    """§7 criterion "money limits": places differ by ≥ 2 (PLACES_DEFINITION) at some check year or wins differ by ≥ 3 for some country.
    Only places and wins: the line limiter Bind cannot separate the rules (the asked rate r_AI is itself ⌊B_proc/unit_cost⌋, a function of the
    budget, so a line is money-bound under every rule)."""
    seeds = sorted(set(var) & set(base))
    hits = []
    for a in ACTORS:
        for y in CHECK_YEARS:
            mv = statistics.median(var[s]["yearly"][(y, a)]["rank"] for s in seeds)
            mb = statistics.median(base[s]["yearly"][(y, a)]["rank"] for s in seeds)
            if abs(mv - mb) >= 2:
                hits.append(f"{a} {y}: median rank {mv} vs {mb}")
        wv = sum(var[s]["win"][a] for s in seeds)
        wb = sum(base[s]["win"][a] for s in seeds)
        if abs(wv - wb) >= 3:
            hits.append(f"{a}: catch-up wins {wv} vs {wb} of {len(seeds)}")
    return hits


MIN_SEEDS = 10  # §7: ≥ 10 seeds per series for a decision


def seed_problems(seeds, pilot):
    """A series run (not --pilot) with fewer than MIN_SEEDS seeds per series is not valid."""
    if pilot or len(seeds) >= MIN_SEEDS:
        return []
    return [f"{len(seeds)} seed(s) per series, the series needs ≥ {MIN_SEEDS} (§7); use --pilot for a pilot"]


def b1_compare_end(tc):
    """B1 = NOW is required for months t < tc − 1, tc = first month whose budget differs. The products step of month t reads the next budget
    (PolicySystem: ComputeProducts(..., _budget.Next) = budget(t + 1)), so month tc − 1 may already differ."""
    return tc - 1


def b1_problem(seed, tc, tw):
    end = b1_compare_end(tc)
    if tw is not None and tw < end:
        return f"B1 seed {seed} differs from NOW at t = {tw}, before month {end} (first budget difference t = {tc}, minus the month the products step reads ahead)"
    return None


# ---------------------------------------------------------------- print

def med_rng(vals, fmt="{:.2f}"):
    vals = [v for v in vals if v is not None]
    if not vals:
        return "—"
    m, lo, hi = statistics.median(vals), min(vals), max(vals)
    return fmt.format(m) if lo == hi else f"{fmt.format(m)} ({fmt.format(lo)}…{fmt.format(hi)})"


FIELDS_PLACE = [
    ("rank", "место (ранг TechKey, сентябрь)", "{:.0f}"),
    ("key_g", "поколение на вооружении key_G", "{:.0f}"),
    ("gens", "поколений когда-либо в строю", "{:.0f}"),
]
# What the budget changes in volume (§6 of the note): pools, alloy and output.
FIELDS_VOLUME = [
    ("budget", "бюджет (USA 1945 = 1)", "{:.3f}"),
    ("wf_eng", "кадры: инженеры WorkforceAvailable", "{:.0f}"),
    ("wf_skilled", "кадры: квалифицированные WorkforceAvailable", "{:.0f}"),
    ("alloy_cap", "AlloyCap, кг/мес.", "{:.0f}"),
    ("alloy_supply", "поставка сплавов, кг/мес.", "{:.0f}"),
    ("rate_max", "наибольший месячный выпуск линии", "{:.1f}"),
    ("output", "выпуск линий за год", "{:.0f}"),
    ("volume", "volume (tier ≥ 4, yield ≥ 0,7)", "{:.0f}"),
]
# Descriptive only: not evidence that money limits (see places_or_wins).
FIELDS_DESCRIPTIVE = [
    ("lines", "доля месяцев с линией", "{:.2f}"),
    ("lim_wf", "доля месяцев: кадров выдано меньше запроса", "{:.2f}"),
    ("lim_alloy", "доля месяцев: линия упёрлась в сплавы", "{:.2f}"),
    ("lim_money", "доля месяцев: линия упёрлась в деньги", "{:.2f}"),
    ("unspent", "неиспользованный остаток оснастки", "{:.2f}"),
]


def main():
    root = sys.argv[1]
    repo = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
    growth = scenario_growth(repo)
    with open(os.path.join(root, "runs.csv"), newline="") as f:
        runs_csv = list(csv.DictReader(f))
    with open(os.path.join(root, "matrix.json")) as f:
        matrix = json.load(f)
    problems = []
    pilot = "--pilot" in sys.argv[2:]
    # A pilot (matrix --seeds 1) is a partial matrix by definition; the series itself must be a valid matrix (ADR-0008 п. 6).
    if matrix.get("valid") is not True and not (pilot and all(r.startswith("partial matrix") for r in matrix.get("invalid_reasons") or [])):
        problems.append(f"matrix not valid: {matrix.get('invalid_reasons')}")

    # validity: status, months, series, L2
    bad = [r["run"] for r in runs_csv if r["status"] != "ok"]
    if bad:
        problems.append(f"runs not ok: {bad}")
    goldens = defaultdict(dict)
    for r in runs_csv:
        goldens[(r["series"], int(r["seed"]))][int(r["threads"])] = r["golden_hash"]
    l2 = {k: len(set(v.values())) == 1 and len(v) >= 2 for k, v in goldens.items()}
    if not all(l2.values()):
        problems.append(f"L2 threads mismatch or single thread: {[k for k, ok in l2.items() if not ok]}")
    if matrix.get("thread_mismatch"):
        problems.append(f"matrix thread_mismatch: {matrix['thread_mismatch']}")
    runs = defaultdict(dict)  # series -> seed -> analysis
    for r in runs_csv:
        if r["threads"] == "1" and r["status"] == "ok":
            runs[r["series"]][int(r["seed"])] = analyse_run(os.path.join(root, "runs", r["run"]))
    missing = [s for s in SERIES if s not in runs]
    if missing:
        problems.append(f"series missing: {missing}")
    seeds = sorted(runs["NOW"]) if "NOW" in runs else []
    problems += seed_problems(seeds, pilot)
    for s_name in SERIES:
        if sorted(runs.get(s_name, {})) != seeds:
            problems.append(f"series {s_name} seeds {sorted(runs.get(s_name, {}))} != NOW seeds {seeds}")
        for x in runs.get(s_name, {}).values():
            if x["months"] != MONTHS:
                problems.append(f"{s_name} seed {x['seed']}: months {x['months']}")
    # Codex r3 on TASK-0122: every later section pairs each series with NOW by seed. A series without a successful threads-1 run for a NOW seed
    # (or no NOW at all) cannot be paired: report the recorded problems and stop instead of indexing a missing run.
    unpaired = unpaired_seeds(runs, seeds)
    if unpaired:
        problems.append(f"cannot pair the series with NOW (no successful threads-1 run): {unpaired}")
        return report_validity(problems)
    one = next(iter(runs["NOW"].values()))
    print(f"matrix: valid={matrix.get('valid')} invalid_reasons={matrix.get('invalid_reasons')} golden_set_hash={matrix.get('golden_set_hash')}")
    print(f"core_commit={one['core']} dirty={one['dirty']} data_hash={one['data_hash']} manifest_hash={one['manifest_hash']} seeds={seeds}")
    print("\n## L2 and goldens (threads 1 = 2 per series and seed)")
    for s_name in SERIES:
        print(f"- {s_name}: " + ", ".join(f"s{sd} {goldens[(s_name, sd)].get(1, '')[:12]} {'=' if l2.get((s_name, sd)) else '≠'}" for sd in seeds))
    tm = {sd: runs["NOW"][sd]["golden"].startswith(p) for sd, p in TM2030_GOLDEN.items() if sd in runs["NOW"]}
    print(f"- NOW = TM-horizon-2030 golden (seeds with a recorded baseline): {tm}")
    if not all(tm.values()):
        problems.append(f"NOW differs from the TM-horizon-2030 goldens: {tm}")

    # control of the rule: exact replica and closed forms
    print("\n## Control of the rule (§7 item 6): supply_month.budget vs the exact Fx6 replica and the real closed form")
    for s_name in SERIES:
        for sd, x in sorted(runs[s_name].items()):
            b0 = {a: x["budget"][a][0] for a in ACTORS}
            rep = replica(s_name, b0, growth, MONTHS)
            diff = [(a, t) for a in ACTORS for t in range(MONTHS) if x["budget"][a][t] != rep[a][t]]
            gap = max(abs(x["budget"][a][t] / FX - closed_form(s_name, a, t, b0, growth)) / closed_form(s_name, a, t, b0, growth)
                      for a in ACTORS for t in range(MONTHS))
            print(f"- {s_name} s{sd}: replica mismatches {len(diff)}{(' first ' + str(diff[0])) if diff else ''}; "
                  f"largest relative gap to the closed form {gap:.2e}")
            if diff:
                problems.append(f"budget of {s_name} seed {sd} differs from the replica at {diff[0]}")

    # ceiling months and B1 = NOW before them
    print("\n## Ceiling of B1, B2: first month t whose budget differs from NOW (the ceiling bound in compute t - 1; t = 0 is 1945-09)")
    b1_until = {}
    for s_name in ("B1", "B2"):
        for a in ("SUN", "FRA", "SWE", "GBR"):
            obs = sorted({first_budget_diff(runs[s_name][sd], runs["NOW"][sd], a) for sd in seeds}, key=lambda v: (v is None, v))
            b0 = {k: runs["NOW"][seeds[0]]["budget"][k][0] for k in ACTORS}
            note, real = NOTE_CEILING[s_name].get(a), real_ceiling_month(s_name, a, b0, growth)
            print(f"- {s_name} {a}: observed first capped month {obs}; real closed form without rounding {real}; note §4 table {note or '—'}")
    print("- rule: B1 must equal NOW in every monthly table for t < tc − 1, tc = first month whose budget differs; month tc − 1 may differ "
          "because its products step reads budget(t + 1) (ComputeProducts(..., _budget.Next))")
    for sd in seeds:
        tc = min(t for t in (first_budget_diff(runs["B1"][sd], runs["NOW"][sd], a) for a in ACTORS) if t is not None)
        tw = first_world_diff(runs["B1"][sd]["dir"], runs["NOW"][sd]["dir"], MONTHS)
        b1_until[sd] = (tc, tw)
        print(f"- B1 s{sd}: first budget difference t = {tc} ({year_of(tc)}); compared t < {b1_compare_end(tc)}; "
              f"first difference of any monthly table t = {tw}")
        p = b1_problem(sd, tc, tw)
        if p:
            problems.append(p)

    # §7 tables
    def table(title, fields):
        for y in CHECK_YEARS:
            print(f"\n## {title}: {y} (September; window of months {t_sept(y)}…{min(t_sept(y) + 11, MONTHS - 1)}), median (min…max) over seeds")
            print("| величина | actor | " + " | ".join(SERIES) + " |")
            print("|---|---|" + "---|" * len(SERIES))
            for f, label, fmt in fields:
                for a in ACTORS:
                    print(f"| {label} | {a} | " + " | ".join(med_rng([x["yearly"][(y, a)][f] for x in runs[s].values()], fmt) for s in SERIES) + " |")

    table("Places and generations", FIELDS_PLACE)
    table("What the budget changes in volume", FIELDS_VOLUME)
    print("\n## Line output 1945-09…2030-12, sum over all months (machines), median (min…max) over seeds")
    print("| actor | " + " | ".join(SERIES) + " |")
    print("|---|" + "---|" * len(SERIES))
    for a in ACTORS:
        print(f"| {a} | " + " | ".join(med_rng([x["output_total"][a] for x in runs[s].values()], "{:.0f}") for s in SERIES) + " |")
    print("\nDESCRIPTIVE ONLY, not evidence for \"money limits\": Bind = Money is expected under every rule, because the asked line rate "
          "r_AI = ⌊B_proc / unit_cost_floor⌋ is itself a function of the budget; the shares below describe the runs, the §7 criterion uses "
          "places and wins only.")
    table("Descriptive: limiter shares and unused tooling", FIELDS_DESCRIPTIVE)
    print("\n## Catch-up goal (win if rank_1945 − rank_2030 ≥ 1 or rank_2030 = 1): wins of the seeds")
    print("| actor | " + " | ".join(SERIES) + " |")
    print("|---|" + "---|" * len(SERIES))
    for a in ACTORS:
        print(f"| {a} | " + " | ".join(f"{sum(x['win'][a] for x in runs[s].values())}/{len(runs[s])}" for s in SERIES) + " |")

    print("\n## §8 tolerances against NOW (paired seeds)")
    for s_name in ("A", "B2", "B1"):
        v = tolerance_s8(runs[s_name], runs["NOW"])
        print(f"- {s_name}: {'passes' if not v else f'{len(v)} violations'}")
        for line in v[:40]:
            print(f"  - {line}")
    print("\n## §7 criterion \"money limits\": ≥ 2 places or ≥ 3 wins of the seeds for some country (places and wins only)")
    print(f"- definition: {PLACES_DEFINITION}")
    for s_name in ("A", "B1", "B2"):
        h = places_or_wins(runs[s_name], runs["NOW"])
        print(f"- {s_name}: {'yes: ' + '; '.join(h) if h else 'no'}")
    if len(seeds) < MIN_SEEDS:
        print(f"\nNOTE: {len(seeds)} seed(s): the criteria are computed but the decision needs ≥ {MIN_SEEDS} seeds (§7).")
    return report_validity(problems)


def unpaired_seeds(runs, seeds):
    """{series: [NOW seeds without a successful threads-1 run of that series]}; {"NOW": []} when NOW has no run at all."""
    if not seeds:
        return {"NOW": []}
    out = {s: [sd for sd in seeds if sd not in runs.get(s, {})] for s in SERIES}
    return {s: v for s, v in out.items() if v}


def report_validity(problems):
    print("\nVALIDITY: " + ("OK" if not problems else "FAILED"))
    for p in problems:
        print(f"- {p}")
    return 0 if not problems else 1


if __name__ == "__main__":
    sys.exit(main())
