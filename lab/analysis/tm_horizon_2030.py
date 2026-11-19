#!/usr/bin/env python3
"""T-M probe of docs/architecture/notes/slice-horizon-2030.md §5: what happens 1978-2030 on the JetSlice main run.

Reads a `matrix` directory of lab/manifests/TM-horizon-2030.json (FULL, 1024 months) and prints Markdown tables for
docs/simulation/m-play-horizon-2030.md, section "После 1977". Unlike exp0002.py it does not censor at 384 months: the
horizon is `model_info.months` of every run. Only threads = 1 runs are read (threads 2 must have the same golden hash;
the matrix checks that).

Usage: python3 lab/analysis/tm_horizon_2030.py lab/results/tm-horizon-2030
"""
import csv
import json
import os
import statistics
import sys
from collections import defaultdict

ACTORS = ["USA", "GBR", "SUN", "FRA", "SWE"]
CHECK_YEARS = [1945, 1960, 1977, 1990, 2010, 2030]
T_1978 = 388  # 1978-01: t = 0 is 1945-09
FX = 1_000_000
LONG_MAX = 2**63 - 1


def year_of(t):
    return 1945 + (8 + t) // 12


def t_sept(year):
    return 12 * (year - 1945)  # actor_year rows are t mod 12 = 0, i.e. September


def read(run_dir, name):
    with open(os.path.join(run_dir, name + ".csv"), newline="") as f:
        return list(csv.DictReader(f))


def num(v):
    return None if v in ("", None) else int(v)


def comp_rank(keys):
    """Competition rank (1 = best, ties share the better rank) by TechKey (G, T, N, P), as ActorView.Key / tech_lead."""
    return {a: 1 + sum(1 for b in keys if keys[b] > keys[a]) for a in keys}


def analyse_run(run_dir):
    info = json.load(open(os.path.join(run_dir, "run.json")))
    months = int(info["model_info"]["months"])
    ay = read(run_dir, "actor_year")
    key = {}  # (t, actor) -> TechKey tuple
    budget = {}
    for r in ay:
        t, a = int(r["t"]), r["actor"]
        key[(t, a)] = (int(r["key_G"]), int(r["key_T"]), int(r["key_N"]), int(r["key_P"]))
        budget[(t, a)] = int(r["budget"])
    years = sorted({t for t, _ in key})
    rank = {t: comp_rank({a: key[(t, a)] for a in ACTORS}) for t in years}

    gen = read(run_dir, "gen_times")
    nodes = read(run_dir, "node_times")
    reqs = read(run_dir, "requirements")
    lines = read(run_dir, "lines")
    events = read(run_dir, "events")
    fleet = read(run_dir, "fleet_cmd")
    lic = read(run_dir, "licences")
    a40 = read(run_dir, "a40")

    # activity months per actor: narrow = new node known, new design (compose), line start or close
    narrow = defaultdict(set)
    wide = defaultdict(set)  # + mastery, first unit, fielded, any events row, fleet command, licence, A40
    for r in nodes:
        tk = num(r["t_known"])
        if tk is not None and tk >= 0:
            narrow[r["actor"]].add(tk)
        for c in ("t_first", "t_07", "t_mastered"):
            v = num(r[c])
            if v is not None and v >= 0:
                wide[r["actor"]].add(v)
    for r in reqs:
        narrow[r["actor"]].add(int(r["t"]))
    prev = {}
    by_t = defaultdict(list)
    for r in lines:
        by_t[int(r["t"])].append(r)
    for t in range(months):
        cur = {(r["actor"], r["design"], r["g_p"]) for r in by_t.get(t, [])}
        for k in cur - prev.get("set", set()):
            if t > 0:
                narrow[k[0]].add(t)
        for k in prev.get("set", set()) - cur:
            narrow[k[0]].add(t)
        for r in by_t.get(t, []):
            if r["close"] == "1" or r["start"] == "1":
                narrow[r["actor"]].add(t)
        prev["set"] = cur
    for r in gen:
        for c in ("t_first_unit", "t_fielded"):
            v = num(r[c])
            if v is not None and v >= 0:
                wide[r["actor"]].add(v)
    for r in events:
        wide[r["actor"]].add(int(r["t"]))
    for r in fleet:
        wide[r["actor"]].add(int(r["t"]))
    for r in lic:
        wide[r["buyer"]].add(int(r["t"]))
    for r in a40:
        wide[r["actor"]].add(int(r["t"]))
    for a in ACTORS:
        wide[a] |= narrow[a]
    window = range(T_1978, months)
    dead = {a: 1 - len([t for t in narrow[a] if t in window]) / len(window) for a in ACTORS}
    dead_wide = {a: 1 - len([t for t in wide[a] if t in window]) / len(window) for a in ACTORS}

    last_node = {a: max([num(r["t_known"]) for r in nodes if r["actor"] == a and (num(r["t_known"]) or -1) >= 0], default=None) for a in ACTORS}
    last_design = {a: max([int(r["t"]) for r in reqs if r["actor"] == a], default=None) for a in ACTORS}
    last_first_unit = {a: max([num(r["t_first_unit"]) for r in gen if r["actor"] == a and (num(r["t_first_unit"]) or -1) >= 0], default=None) for a in ACTORS}
    known_at = {(y, a): sum(1 for r in nodes if r["actor"] == a and num(r["t_known"]) is not None and num(r["t_known"]) <= t_sept(y)) for y in CHECK_YEARS for a in ACTORS}
    fielded_at = {(y, a): sum(1 for r in gen if r["actor"] == a and num(r["t_fielded"]) is not None and num(r["t_fielded"]) <= t_sept(y)) for y in CHECK_YEARS for a in ACTORS}
    first_known = {}  # node -> first month any actor knows it (-1 = known at start by someone)
    for r in nodes:
        tk = num(r["t_known"])
        if tk is not None:
            first_known[r["node"]] = min(first_known.get(r["node"], tk), tk)
    world_last_node = max(first_known.values())  # last month when a node became known to the world for the first time

    # rank dynamics after 1977 (annual rows, September)
    after = [t for t in years if t >= t_sept(1977)]
    final_t = years[-1]
    rank_changes = {a: sum(1 for i in range(1, len(after)) if rank[after[i]][a] != rank[after[i - 1]][a]) for a in ACTORS}
    fixed_all = next(y for y in reversed(years) if any(rank[y][a] != rank[final_t][a] for a in ACTORS)) if any(
        rank[y] != rank[final_t] for y in years) else -12
    fixed_actor = {}
    for a in ACTORS:
        diff = [y for y in years if rank[y][a] != rank[final_t][a]]
        fixed_actor[a] = year_of(diff[-1] + 12) if diff else 1945
    summary = read(run_dir, "summary")[0]
    lead_all = summary["lead_all"]
    lead_changes_after = sum(1 for t in range(max(T_1978, 1), months) if lead_all[t] != lead_all[t - 1])
    lead_changes_before = sum(1 for t in range(1, T_1978) if lead_all[t] != lead_all[t - 1])
    lead_share = {a: sum(1 for t in window if lead_all[t] == str(i)) / len(window) for i, a in enumerate(ACTORS)}

    # degenerations: largest raw Fx6/int value per column of every table vs long range
    maxima = {}
    for name in ("actor_year", "lines", "tooling_ledger", "prc_year", "fleet_cmd", "gen_times", "licences", "events"):
        for r in read(run_dir, name):
            for c, v in r.items():
                if c in ("run", "t", "actor", "design", "node") or v in ("", None):
                    continue
                try:
                    x = abs(int(v))
                except ValueError:
                    continue
                k = f"{name}.{c}"
                if x > maxima.get(k, 0):
                    maxima[k] = x
    # money: tooling rest unspent share and line spend vs budget by decade
    led = read(run_dir, "tooling_ledger")
    unspent = defaultdict(lambda: [0, 0])
    for r in led:
        d = (year_of(int(r["t"])) // 10) * 10
        unspent[(d, r["actor"])][0] += int(r["rest_unspent"])
        unspent[(d, r["actor"])][1] += int(r["rest"])
    spend = defaultdict(int)  # Σ rate·unit_cost per (decade, actor), raw Fx6·Fx6/1e6
    for r in lines:
        if r["rate"] and r["unit_cost"]:
            spend[((year_of(int(r["t"])) // 10) * 10, r["actor"])] += int(r["rate"]) * int(r["unit_cost"]) // FX
    bud_dec = defaultdict(int)
    for (t, a), b in budget.items():
        bud_dec[((year_of(t) // 10) * 10, a)] += b * 12
    fleet_after = defaultdict(int)
    for r in fleet:
        if int(r["t"]) >= T_1978:
            fleet_after[(r["actor"], r["kind"])] += 1
    coexist = defaultdict(int)  # actor-months with two lines (W and N) after 1977
    for t in window:
        c = defaultdict(int)
        for r in by_t.get(t, []):
            c[r["actor"]] += 1
        for a, n in c.items():
            if n >= 2:
                coexist[a] += 1
    return dict(seed=int(info["seed"]), months=months, golden=info["golden_hash"], core=info["core_commit"],
                build=info.get("build_commit"), data_hash=info["data_hash"], manifest_hash=info["manifest_hash"],
                key=key, budget=budget, rank=rank, years=years, dead=dead, dead_wide=dead_wide, last_node=last_node,
                last_design=last_design, last_first_unit=last_first_unit, known_at=known_at, fielded_at=fielded_at,
                world_last_node=world_last_node, rank_changes=rank_changes, fixed_all=year_of(fixed_all + 12),
                fixed_actor=fixed_actor, lead_changes_after=lead_changes_after, lead_changes_before=lead_changes_before,
                lead_share=lead_share, lead_final=ACTORS[int(lead_all[-1])], maxima=maxima, unspent=unspent,
                spend=spend, bud_dec=bud_dec, fleet_after=fleet_after, coexist=coexist)


def med_rng(vals, fmt="{:.2f}"):
    vals = [v for v in vals if v is not None]
    if not vals:
        return "—"
    m = statistics.median(vals)
    lo, hi = min(vals), max(vals)
    if lo == hi:
        return fmt.format(m)
    return f"{fmt.format(m)} ({fmt.format(lo)}…{fmt.format(hi)})"


def yr(t):
    return None if t is None else year_of(t)


def main():
    root = sys.argv[1]
    runs_csv = list(csv.DictReader(open(os.path.join(root, "runs.csv"))))
    dirs = [os.path.join(root, "runs", r["run"]) for r in runs_csv if r["threads"] == "1" and r["status"] == "ok"]
    R = [analyse_run(d) for d in dirs]
    m = json.load(open(os.path.join(root, "matrix.json")))
    print(f"runs={len(R)} seeds={[r['seed'] for r in R]} months={set(r['months'] for r in R)} valid={m.get('valid')} "
          f"thread_mismatch={m.get('thread_mismatch')} golden_set_hash={m.get('golden_set_hash')}")
    print(f"core_commit={R[0]['core']} build_commit={R[0]['build']} data_hash={R[0]['data_hash']} manifest_hash={R[0]['manifest_hash']}")
    print("golden:", {r["seed"]: r["golden"][:12] for r in R})

    print("\n## Budget index (USA 1945 = 1), median (min…max) over seeds; September of the year")
    print("| actor | " + " | ".join(str(y) for y in CHECK_YEARS) + " |")
    for a in ACTORS:
        print(f"| {a} | " + " | ".join(med_rng([r["budget"][(t_sept(y), a)] / FX for r in R]) for y in CHECK_YEARS) + " |")

    print("\n## Rank by TechKey (G, T, N, P), competition rank 1..5; per seed s1..s5")
    print("| actor | " + " | ".join(str(y) for y in CHECK_YEARS) + " |")
    for a in ACTORS:
        print(f"| {a} | " + " | ".join("/".join(str(r["rank"][t_sept(y)][a]) for r in R) for y in CHECK_YEARS) + " |")

    print("\n## Fielded generation key_G per seed")
    print("| actor | " + " | ".join(str(y) for y in CHECK_YEARS) + " |")
    for a in ACTORS:
        print(f"| {a} | " + " | ".join("/".join(str(r["key"][(t_sept(y), a)][0]) for r in R) for y in CHECK_YEARS) + " |")

    print("\n## Generations ever fielded (count of gen_times.t_fielded <= t) per seed")
    print("| actor | " + " | ".join(str(y) for y in CHECK_YEARS) + " |")
    for a in ACTORS:
        print(f"| {a} | " + " | ".join("/".join(str(r["fielded_at"][(y, a)]) for r in R) for y in CHECK_YEARS) + " |")

    print("\n## Nodes known (of 61) per seed")
    print("| actor | " + " | ".join(str(y) for y in CHECK_YEARS) + " |")
    for a in ACTORS:
        print(f"| {a} | " + " | ".join("/".join(str(r["known_at"][(y, a)]) for r in R) for y in CHECK_YEARS) + " |")

    print("\n## Last events and dead time (1978-01…2030-12, 636 months)")
    print("| actor | last new node (year) | last new design (compose) | last first unit of a gen | dead share narrow | dead share wide | rank changes after 1977-09 (annual) | own rank fixed from |")
    for a in ACTORS:
        print(f"| {a} | {med_rng([yr(r['last_node'][a]) for r in R], '{:.0f}')} | {med_rng([yr(r['last_design'][a]) for r in R], '{:.0f}')} | "
              f"{med_rng([yr(r['last_first_unit'][a]) for r in R], '{:.0f}')} | {med_rng([r['dead'][a] for r in R])} | "
              f"{med_rng([r['dead_wide'][a] for r in R])} | {'/'.join(str(r['rank_changes'][a]) for r in R)} | "
              f"{'/'.join(str(r['fixed_actor'][a]) for r in R)} |")
    print("world last new node year per seed:", [yr(r["world_last_node"]) for r in R])
    print("whole ranking fixed from (year) per seed:", [r["fixed_all"] for r in R])
    print("leader changes (lead_all, monthly) before 1978 / after:", [(r["lead_changes_before"], r["lead_changes_after"]) for r in R])
    print("leader in 2030-12 per seed:", [r["lead_final"] for r in R])
    print("leader share of months 1978-2030 per seed:", [{a: round(v, 2) for a, v in r["lead_share"].items() if v} for r in R])

    print("\n## Catch-up goal (M_PLAY_LOOP C: win if rank_1945 - rank_end >= 1 or rank_end = 1), AI play, per seed")
    for a in ACTORS:
        res = []
        for r in R:
            r0, r1 = r["rank"][0][a], r["rank"][t_sept(2030)][a]
            res.append(f"{r0}->{r1}{'W' if (r0 - r1 >= 1 or r1 == 1) else ('L' if r1 > r0 else '=')}")
        print(f"{a}: {' '.join(res)}")

    print("\n## Fleet commands after 1977 (RETIRE/SELL), actor-months with two lines after 1977, per seed")
    for a in ACTORS:
        print(a, [(r["fleet_after"][(a, 'RETIRE')], r["fleet_after"][(a, 'SELL')], r["coexist"][a]) for r in R])

    print("\n## Money by decade, median over seeds: tooling rest_unspent/rest; line spend Σ rate·unit_cost / Σ 12·budget(Sept)")
    for a in ACTORS:
        row = []
        for d in range(1940, 2040, 10):
            u = [r["unspent"][(d, a)][0] / r["unspent"][(d, a)][1] for r in R if r["unspent"][(d, a)][1]]
            s = [r["spend"][(d, a)] / r["bud_dec"][(d, a)] for r in R if r["bud_dec"][(d, a)]]
            row.append(f"{d}s u={statistics.median(u):.2f} s={statistics.median(s):.3f}" if u and s else f"{d}s —")
        print(a, "; ".join(row))

    print("\n## Largest raw values seen (all seeds) vs long range 9.22e18, top 12")
    allmax = defaultdict(int)
    for r in R:
        for k, v in r["maxima"].items():
            allmax[k] = max(allmax[k], v)
    for k, v in sorted(allmax.items(), key=lambda kv: -kv[1])[:12]:
        print(f"{k}: {v:.3e} raw = {v / FX:.3e} (share of range {v / LONG_MAX:.1e})")


if __name__ == "__main__":
    main()
