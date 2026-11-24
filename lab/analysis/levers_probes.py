#!/usr/bin/env python3
"""TASK-0148: player lever probes P1-P3 and the second line (docs/simulation/levers-probes.md, pre-registration sections 2-5).

Reads matrix directories of lab/manifests/TASK-0148-levers-{p1,p2,p3,l2}.json (JetSlice FULL, 664 months) and prints Markdown:
  --l2 DIR   validity: plain = LEVER_DIAG golden per seed, threads 1 = 2 per series and seed;
  --p1 DIR   P1 item shifts: paired median effect per actor, arm and key metric against the noise sigma* of the base arm; verdict per item;
             also the second-line observation (L2 of the note) on the base arm;
  --p2 DIR   P2 reserve (needs --p1 for the best no-reserve policy): dominance check; ledger replica;
  --p3 DIR   P3 goal strategies: realised goal share, wins-on-all check, lead over anchor years, forecast tolerance (whynot and lever_eta).
Usage: python3 lab/analysis/levers_probes.py [--l2 D] [--p1 D] [--p2 D] [--p3 D] [--pilot]. Exit 1 = a validity rule failed.
"""
import csv
import json
import os
import re
import statistics
import sys
from collections import defaultdict

ACTORS = ["USA", "GBR", "SUN", "FRA", "SWE"]
LEVER_ACTORS = ["USA", "SUN", "FRA"]
SHIFTS = ["R+10", "R-10", "R0", "C+10", "C-10", "C0", "E+10", "E-10", "E0", "P+10", "P-10", "P0"]
MONTHS = 664
FX = 1_000_000
KEEP = 991_258
CENSOR_YEAR = 2001.0
# key metrics: name, better ("lo" or "hi"), noise floor (absolute; "pct" = 1 % of the base median)
METRICS = [("M1_g3_year", "lo", 0.25), ("M2_output", "hi", "pct"), ("M3a_rank1980", "lo", 0.5), ("M3b_rank2000", "lo", 0.5),
           ("M4a_disc1980", "hi", 0.5), ("M4b_disc2000", "hi", 0.5)]
DIRECTION = {m: d for m, d, _ in METRICS}
P3_METRICS = ["M1_g3_year", "M2_output", "M4a_disc1980", "M4b_disc2000"]


def year_dec(t):
    return 1945 + (8 + t) / 12


def t_sept(year):
    return 12 * (year - 1945)


def read(run_dir, name):
    path = os.path.join(run_dir, name + ".csv")
    if not os.path.exists(path):
        return []
    with open(path, newline="") as f:
        return list(csv.DictReader(f))


def num(v):
    return None if v in ("", None) else int(v)


def comp_rank(keys):
    return {a: 1 + sum(1 for b in keys if keys[b] > keys[a]) for a in keys}


def nodes_meta(repo):
    """node number -> discipline and anchor year from mods/core/nodes.yaml (plain text scan, no yaml dependency)."""
    meta, cur = {}, None
    with open(os.path.join(repo, "mods", "core", "nodes.yaml")) as f:
        for line in f:
            m = re.match(r"\s+number:\s*(\d+)", line)
            if m:
                cur = int(m.group(1))
                meta[cur] = {"disc": None, "anchor": None}
                continue
            m = re.match(r"\s+discipline:\s*(\w+)", line)
            if m and cur is not None:
                meta[cur]["disc"] = m.group(1)
            m = re.match(r"\s+year:\s*(\d+)", line)
            if m and cur is not None and meta[cur]["anchor"] is None:
                meta[cur]["anchor"] = int(m.group(1))
    return meta


def analyse_run(run_dir, meta):
    with open(os.path.join(run_dir, "run.json")) as f:
        info = json.load(f)
    mi = info.get("model_info") or {}
    months = int(mi["months"])
    key, fvf = {}, {}
    for r in read(run_dir, "actor_year"):
        t, a = int(r["t"]), r["actor"]
        key[(t, a)] = (int(r["key_G"]), int(r["key_T"]), int(r["key_N"]), int(r["key_P"]))
        fvf[(t, a)] = int(r["fv_fleet"])
    rank = {t: comp_rank({a: key[(t, a)] for a in ACTORS}) for t in sorted({t for t, _ in key})}
    out = defaultdict(int)
    budget = {a: [None] * months for a in ACTORS}
    bind = defaultdict(lambda: [0, 0, 0, 0, 0])  # actor: months with lines, money, alloy, rate, workforce short
    sm_by = {}
    for r in read(run_dir, "supply_month"):
        t, a = int(r["t"]), r["actor"]
        out[a] += int(r["rate_sum"])
        budget[a][t] = int(r["budget"])
        sm_by[(t, a)] = r
        if int(r["lines"]) > 0:
            b = bind[a]
            b[0] += 1
            b[1] += int(r["bind_money"]) > 0
            b[2] += int(r["bind_alloy"]) > 0
            b[3] += int(r["bind_rate"]) > 0
            b[4] += int(r["eng_granted"]) < int(r["eng_n"]) or int(r["sk_granted"]) < int(r["sk_n"])
    gen = {(r["actor"], int(r["gen"])): num(r["t_first_unit"]) for r in read(run_dir, "gen_times")}
    known = defaultdict(dict)
    for r in read(run_dir, "node_times"):
        tk = num(r["t_known"])
        if tk is not None and tk >= 0 and r["lic"] == "0":
            known[r["actor"]][int(r["node"])] = tk
    per = {}
    for a in ACTORS:
        def gy(g):
            v = gen.get((a, g))
            return CENSOR_YEAR if v is None or v < -1 else (1945 + 8 / 12 if v == -1 else year_dec(v))
        disc = known[a]
        m = {
            "M1_g3_year": gy(3), "M2_output": out[a] / FX,
            "M3a_rank1980": rank[t_sept(1980)][a], "M3b_rank2000": rank[t_sept(2000)][a],
            "M4a_disc1980": sum(1 for t in disc.values() if t <= t_sept(1980)),
            "M4b_disc2000": sum(1 for t in disc.values() if t <= t_sept(2000)),
            "g2_year": gy(2), "g4_year": gy(4),
            "fv_fleet1980": fvf[(t_sept(1980), a)] / FX, "fv_fleet2000": fvf[(t_sept(2000), a)] / FX,
        }
        b = bind[a]
        m["line_months"] = b[0]
        for i, k in enumerate(["bind_money", "bind_alloy", "bind_rate", "wf_short"], start=1):
            m[k] = b[i] / b[0] if b[0] else None
        by_disc = defaultdict(int)
        leads = []
        for n, t in disc.items():
            d = (meta.get(n) or {}).get("disc") or "app"
            if t <= t_sept(1980):
                by_disc[d] += 1
            anc = (meta.get(n) or {}).get("anchor")
            if anc is not None:
                leads.append((anc - year_dec(t), d, n))
        m["disc1980_by"] = dict(by_disc)
        m["leads"] = leads
        per[a] = m
    return dict(seed=int(info["seed"]), status=info["status"], golden=info.get("golden_hash"), months=months, info=mi, per=per,
                budget=budget, dir=run_dir, core=info.get("core_commit"), dirty=info.get("dirty"), data_hash=info.get("data_hash"),
                manifest_hash=info.get("manifest_hash"), sm=sm_by)


def arm_of(r):
    s = r["series"]
    if s == "P1":
        return f"{r['LEVER_ACTOR']}:{r['LEVER_SHIFT']}"
    if s == "P2":
        return f"{r['LEVER_ACTOR']}:r{r['RESERVE']}:{r['RESERVE_SPEND']}"
    return s


def load(root, meta, problems, pilot):
    with open(os.path.join(root, "runs.csv"), newline="") as f:
        rows = list(csv.DictReader(f))
    with open(os.path.join(root, "matrix.json")) as f:
        matrix = json.load(f)
    reasons = matrix.get("invalid_reasons") or []
    # A series manifest has threads [1]: the only accepted invalid reason is the single thread value (L2 is the --l2 manifest);
    # a pilot is also a partial matrix (--seeds 1).
    allowed = ("thread", "partial matrix", "--seeds") if pilot else ("thread",)
    bad_reasons = [x for x in reasons if not any(k in x for k in allowed)]
    if bad_reasons or matrix.get("problems"):
        problems.append(f"{root}: matrix reasons {bad_reasons} problems {matrix.get('problems')}")
    runs = defaultdict(dict)
    for r in rows:
        if r["status"] != "ok":
            problems.append(f"{root}: run {r['run']} status {r['status']}")
            continue
        if r["threads"] != "1":
            continue
        x = analyse_run(os.path.join(root, "runs", r["run"]), meta)
        if x["months"] != MONTHS:
            problems.append(f"{r['run']}: months {x['months']}")
        runs[arm_of(r)][int(r["seed"])] = x
    return runs, matrix


def sd(vals):
    return statistics.stdev(vals) if len(vals) > 1 else 0.0


def noise(base, actor, metric, floor):
    vals = [x["per"][actor][metric] for x in base.values()]
    f = 0.01 * statistics.median(vals) if floor == "pct" else floor
    return max(sd(vals), f), sd(vals)


def paired(arm, base, actor, metric):
    seeds = sorted(set(arm) & set(base))
    return [arm[s]["per"][actor][metric] - base[s]["per"][actor][metric] for s in seeds]


def better(metric, a, b):
    return a < b if DIRECTION.get(metric, "hi") == "lo" else a > b


def fmt(v, nd=2):
    if v is None:
        return "-"
    if isinstance(v, float):
        return f"{v:.{nd}f}"
    return str(v)


def provenance(runs):
    one = next(iter(next(iter(runs.values())).values()))
    print(f"core_commit={one['core']} dirty={one['dirty']} data_hash={one['data_hash'][:12]} manifest_hash={one['manifest_hash'][:12]}")
    print(f"arms={len(runs)} seeds={sorted(next(iter(runs.values())))}")


def effects(runs, base, actor, arm):
    res = {}
    for m, _, floor in METRICS:
        s_star, _ = noise(base, actor, m, floor)
        d = paired(runs[arm], base, actor, m)
        med = statistics.median(d) if d else None
        res[m] = (med, s_star, med is not None and abs(med) > s_star)
    return res


# ---------------------------------------------------------------- L2 manifest

def report_l2(root, problems):
    with open(os.path.join(root, "runs.csv"), newline="") as f:
        rows = list(csv.DictReader(f))
    g = defaultdict(dict)
    for r in rows:
        if r["status"] != "ok":
            problems.append(f"l2 run {r['run']} {r['status']}")
        g[(r["series"], int(r["seed"]))][int(r["threads"])] = r["golden_hash"]
    print("\n## Validity (TASK-0148-levers-l2)")
    for k in sorted(g):
        v = g[k]
        same = len(set(v.values())) == 1 and len(v) == 2
        print(f"- {k[0]} s{k[1]}: threads {sorted(v)} golden {v.get(1, '')[:12]} {'= (L2)' if same else 'differs'}")
        if not same:
            problems.append(f"L2 mismatch {k}")
    for sd_ in sorted({s for _, s in g}):
        p, b = g.get(("plain", sd_), {}).get(1), g.get(("base", sd_), {}).get(1)
        print(f"- seed {sd_}: plain = LEVER_DIAG base: {p == b} ({(p or '')[:12]})")
        if p != b:
            problems.append(f"LEVER_DIAG changed the world at seed {sd_}")
        for arm in ("P1-USA-R+10", "P2-SUN-r40-RES", "ENG80"):
            if g.get((arm, sd_), {}).get(1) == p:
                problems.append(f"arm {arm} did not change the world at seed {sd_}")


# ---------------------------------------------------------------- P1

def report_p1(runs):
    base = runs["base"]
    print("\n## P1. Budget items (paired median effect vs base; * = |effect| > sigma*)")
    for a in LEVER_ACTORS:
        print(f"- sigma* {a}: " + ", ".join(f"{m} {noise(base, a, m, fl)[0]:.2f} (sd {noise(base, a, m, fl)[1]:.2f})" for m, _, fl in METRICS))
    verdict = defaultdict(dict)
    for a in LEVER_ACTORS:
        print(f"\n### {a}")
        print("| arm | " + " | ".join(m for m, _, _ in METRICS) + " |")
        print("|---" * (len(METRICS) + 1) + "|")
        print("| base median | " + " | ".join(fmt(statistics.median(x["per"][a][m] for x in base.values())) for m, _, _ in METRICS) + " |")
        for sh in SHIFTS:
            arm = f"{a}:{sh}"
            if arm not in runs:
                continue
            e = effects(runs, base, a, arm)
            print(f"| {sh} | " + " | ".join(fmt(e[m][0]) + ("*" if e[m][2] else "") for m, _, _ in METRICS) + " |")
        for item in "RCEP":
            works = []
            for op in ("+10", "-10"):
                arm = f"{a}:{item}{op}"
                if arm in runs:
                    works += [m for m, (_, _, ok) in effects(runs, base, a, arm).items() if ok]
            verdict[item][a] = sorted(set(works))
    print("\n### Verdict (item works at an actor if some key metric moves > sigma* under +10 or -10; lever if it works at >= 2 of 3)")
    names = {"R": "Research", "C": "Capability", "E": "Effort", "P": "Procurement"}
    for item in "RCEP":
        n = sum(1 for a in LEVER_ACTORS if verdict[item].get(a))
        print(f"- {names[item]}: works at {n}/3 ({'; '.join(f'{a}: {verdict[item].get(a) or []}' for a in LEVER_ACTORS)}) -> "
              f"{'LEVER' if n >= 2 else 'not a lever by the rule'}")
    print("\n### Secondary: share of line months bound by money / alloy / workforce short (median over seeds); base | +10 | -10 | 0")
    for a in LEVER_ACTORS:
        for item in "RCEP":
            vals = []
            for arm in ["base"] + [f"{a}:{item}{op}" for op in ("+10", "-10", "0")]:
                r = runs.get(arm, {})
                vals.append("/".join(fmt(statistics.median([x["per"][a][k] for x in r.values() if x["per"][a][k] is not None] or [0]))
                                     for k in ("bind_money", "bind_alloy", "wf_short")))
            print(f"- {a} {item}: " + " | ".join(vals))
    print("\n### Secondary: g2 / g4 first-unit year, fv_fleet 1980 / 2000 (median); base | +10 | -10 | 0")
    for a in LEVER_ACTORS:
        for item in "RCEP":
            vals = []
            for arm in ["base"] + [f"{a}:{item}{op}" for op in ("+10", "-10", "0")]:
                r = runs.get(arm, {})
                vals.append("/".join(fmt(statistics.median([x["per"][a][k] for x in r.values()] or [0]), 1)
                                     for k in ("g2_year", "g4_year", "fv_fleet1980", "fv_fleet2000")))
            print(f"- {a} {item}: " + " | ".join(vals))


# ---------------------------------------------------------------- second line (base arm)

def second_line(base):
    print("\n## Second line (base arm, all actors): rate of the existing line 6 months before vs after a second line starts")
    rel, rows_out = [], []
    lim_before, lim_after = defaultdict(list), defaultdict(list)
    for sd_, x in sorted(base.items()):
        by = defaultdict(dict)  # (actor, t) -> {design: rate}
        for r in read(x["dir"], "lines"):
            by[(r["actor"], int(r["t"]))][r["design"] + ":" + r["g_p"]] = int(r["rate"]) / FX
        sm = x["sm"]
        for a in ACTORS:
            for t in range(7, MONTHS - 7):
                prev, cur = by.get((a, t - 1), {}), by.get((a, t), {})
                if len(prev) != 1 or len(cur) != 2:
                    continue
                d = next(iter(prev))
                if d not in cur:
                    continue
                before = [by.get((a, u), {}).get(d) for u in range(t - 6, t)]
                after = [by.get((a, u), {}).get(d) for u in range(t + 1, t + 7)]
                if any(v is None for v in before + after) or any(len(by.get((a, u), {})) != 1 for u in range(t - 6, t)):
                    continue
                mb, ma = statistics.mean(before), statistics.mean(after)
                if mb <= 0:
                    continue
                rel.append(ma / mb - 1)

                def flag(u, k):
                    r = sm[(u, a)]
                    if k == "wf_short":
                        return 1 if int(r["eng_granted"]) < int(r["eng_n"]) or int(r["sk_granted"]) < int(r["sk_n"]) else 0
                    return 1 if int(r[k]) > 0 else 0
                for k in ("bind_money", "bind_alloy", "bind_rate", "wf_short"):
                    lim_before[k].append(statistics.mean(flag(u, k) for u in range(t - 6, t)))
                    lim_after[k].append(statistics.mean(flag(u, k) for u in range(t + 1, t + 7)))
                rows_out.append((sd_, a, t, round(mb, 2), round(ma, 2)))
    if not rel:
        print("- no clean 1 -> 2 transitions (6 months of one line before, the line present 6 months after)")
        return
    q = sorted(rel)
    print(f"- transitions: {len(rel)} (actors {sorted({r[1] for r in rows_out})}); relative change of the old line's rate: "
          f"median {statistics.median(rel):+.1%}, p10 {q[len(q) // 10]:+.1%}, p90 {q[(9 * len(q)) // 10]:+.1%}, min {q[0]:+.1%}, max {q[-1]:+.1%}")
    print(f"- verdict: {'the second line slows the first (median < -10 %)' if statistics.median(rel) < -0.10 else 'no slow-down by the rule (median >= -10 %)'}")
    for k in ("bind_money", "bind_alloy", "bind_rate", "wf_short"):
        print(f"- share of months with {k}: before {statistics.mean(lim_before[k]):.2f}, after {statistics.mean(lim_after[k]):.2f}")
    for r in rows_out[:15]:
        print(f"  - seed {r[0]} {r[1]} t {r[2]} ({year_dec(r[2]):.1f}): {r[3]} -> {r[4]} units/month")


# ---------------------------------------------------------------- P2

def replica(budgets, r_raw, spend):
    bal = acc = leak = spent = 0
    for m, b in enumerate(budgets):
        kept = bal * KEEP // FX
        leak += bal - kept
        add = r_raw * b // FX
        bal, acc = kept + add, acc + add
        if spend != "NONE" and m >= 12 and b > 0 and bal > 0:
            money = min(bal, add)
            share = min(money * FX // b, r_raw)
            paid = share * b // FX
            bal -= paid
            spent += paid
    return dict(acc=acc, leak=leak, spent=spent, bal=bal, last_leak=bal - bal * KEEP // FX)


def report_p2(runs2, runs1, problems):
    base = runs2["base"]
    print("\n## P2. Reserve (paired median vs base; DOM = median better than the best no-reserve median by > sigma*)")
    print("\n### Ledger control: Lab account vs an exact replica of months 0..663 from supply_month.budget (month 664: its leak only)")
    bad = 0
    for arm, rs in sorted(runs2.items()):
        if arm == "base":
            continue
        a, rr, spend = arm.split(":")
        r_raw = round(float(rr[1:]) * FX)
        for sd_, x in rs.items():
            rep = replica(x["budget"][a], r_raw, spend)
            i = {k: int(x["info"][k]) for k in ("reserve_accrued", "reserve_leaked", "reserve_spent", "reserve_end")}
            ok = i["reserve_leaked"] == rep["leak"] + rep["last_leak"] and i["reserve_accrued"] > rep["acc"] \
                and i["reserve_accrued"] - i["reserve_leaked"] - i["reserve_spent"] == i["reserve_end"] \
                and rep["spent"] <= i["reserve_spent"] <= rep["spent"] + (i["reserve_accrued"] - rep["acc"])
            if not ok:
                bad += 1
                problems.append(f"ledger replica mismatch {arm} s{sd_}: {i} vs {rep}")
    print(f"- mismatches: {bad}")
    dom = []
    for a in LEVER_ACTORS:
        print(f"\n### {a}")
        print("| arm | " + " | ".join(m for m, _, _ in METRICS) + " | reserve end / accrued | licence money refusals |")
        print("|---" * (len(METRICS) + 3) + "|")
        nores = {"base": base} | {k: v for k, v in runs1.items() if k.startswith(a + ":")}
        best = {}
        for m, _, _ in METRICS:
            meds = {k: statistics.median(x["per"][a][m] for x in v.values()) for k, v in nores.items()}
            pick = max if DIRECTION[m] == "hi" else min
            best[m] = pick(meds.items(), key=lambda kv: kv[1])
        print("| best no-reserve (arm) | " + " | ".join(f"{fmt(best[m][1])} ({best[m][0].split(':')[-1]})" for m, _, _ in METRICS) + " | | |")
        for arm in sorted(k for k in runs2 if k.startswith(a + ":")):
            rs = runs2[arm]
            e = effects(runs2, base, a, arm)
            cells = []
            for m, _, fl in METRICS:
                med = statistics.median(x["per"][a][m] for x in rs.values())
                s_star = noise(base, a, m, fl)[0]
                beats = better(m, med, best[m][1]) and abs(med - best[m][1]) > s_star
                if beats and float(arm.split(":")[1][1:]) >= 0.4:
                    dom.append((arm, m, med, best[m]))
                cells.append(f"{fmt(e[m][0])}{'*' if e[m][2] else ''}{' DOM' if beats else ''}")
            ends = statistics.median(int(x["info"]["reserve_end"]) / max(1, int(x["info"]["reserve_accrued"])) for x in rs.values())
            refusals = statistics.median(int(x["info"]["lever_licence_money_refusals"]) for x in rs.values())
            print(f"| {arm.split(':', 1)[1]} | " + " | ".join(cells) + f" | {ends:.2f} | {refusals} |")
    print("\n### Verdict (dominance = an arm with r >= 0.4 has a median better than the best no-reserve median by > sigma*)")
    print(f"- {'RESERVE DOMINATES: ' + str(dom) if dom else 'no arm with r >= 0.4 dominates'}")
    base_ref = statistics.median(int(x["info"].get("lever_licence_money_refusals", 0)) for x in base.values()) if base else 0
    print(f"- licence money refusals in base (no lever, not counted): {base_ref}")


# ---------------------------------------------------------------- P3

GOAL = {"ENG": ["prop"], "MAT": ["metal"], "EVEN": ["prop", "metal"]}


def goal_of(arm):
    return GOAL[re.match(r"[A-Z]+", arm).group(0)]


def outside(x, a, g):
    return sum(v for d, v in x["per"][a]["disc1980_by"].items() if d not in g)


def inside(x, a, g):
    return sum(x["per"][a]["disc1980_by"].get(d, 0) for d in g)


def report_p3(runs):
    base = runs["NONE"]
    arms = [k for k in runs if k != "NONE"]
    print("\n## P3. Goal strategies (DISC_WEIGHT for all actors; paired median vs NONE; * = |effect| > sigma*)")
    print("\n### Realised goal share of research money 1945-1980 (alloc trace; median over seeds), target 40 / 80 %")
    for arm in ["NONE"] + arms:
        g = goal_of(arm) if arm != "NONE" else ["prop"]
        shares = []
        for a in ACTORS:
            vals = []
            for x in runs[arm].values():
                tot = sum(int(v) for k, v in x["info"].items() if k.startswith(f"alloc_1980_{a}_"))
                vals.append(sum(int(x["info"][f"alloc_1980_{a}_{d}"]) for d in g) / tot)
            shares.append(f"{a} {statistics.median(vals):.2f}")
        print(f"- {arm} ({'+'.join(g)}): " + ", ".join(shares))
    wins = []
    for a in ACTORS:
        print(f"\n### {a}")
        print("| arm | " + " | ".join(P3_METRICS) + " | g2 / g4 year | disc 1980 in goal (d) / outside (d) |")
        print("|---" * (len(P3_METRICS) + 3) + "|")
        print("| NONE median | " + " | ".join(fmt(statistics.median(x["per"][a][m] for x in base.values())) for m in P3_METRICS) + " | "
              + fmt(statistics.median(x["per"][a]["g2_year"] for x in base.values())) + " / "
              + fmt(statistics.median(x["per"][a]["g4_year"] for x in base.values())) + " | |")
        for arm in arms:
            e = effects(runs, base, a, arm)
            g = goal_of(arm)
            all_better = all(e[m][2] and better(m, e[m][0], 0) for m in P3_METRICS)
            if all_better:
                wins.append((a, arm))
            ing = statistics.median(inside(x, a, g) for x in runs[arm].values())
            outg = statistics.median(outside(x, a, g) for x in runs[arm].values())
            ing0 = statistics.median(inside(x, a, g) for x in base.values())
            outg0 = statistics.median(outside(x, a, g) for x in base.values())
            print(f"| {arm} | " + " | ".join(fmt(e[m][0]) + ("*" if e[m][2] else "") for m in P3_METRICS) + " | "
                  + fmt(statistics.median(x["per"][a]["g2_year"] for x in runs[arm].values())) + " / "
                  + fmt(statistics.median(x["per"][a]["g4_year"] for x in runs[arm].values()))
                  + f" | {ing} ({ing - ing0:+}) / {outg} ({outg - outg0:+}){' WINS-ALL' if all_better else ''} |")
    print("\n### (a) Wins on all of M1, M2, M4a, M4b (better than NONE by > sigma* each)")
    by_arm = defaultdict(list)
    for a, arm in wins:
        by_arm[arm].append(a)
    rebuild = [k for k, v in by_arm.items() if len(v) >= 2]
    print(f"- {dict(by_arm) if by_arm else 'none'}; rebuild of the goal mechanic needed by the rule: {'YES ' + str(rebuild) if rebuild else 'no'}")
    print("\n### (b) Lead: prop nodes discovered >= 15 years before their anchor year (any actor): seeds with such a node")
    for arm in ["NONE"] + arms:
        n = sum(1 for x in runs[arm].values() if any(l >= 15 and d == "prop" for a in ACTORS for l, d, _ in x["per"][a]["leads"]))
        mx = statistics.median(max([l for a in ACTORS for l, d, _ in x["per"][a]["leads"] if d == "prop"] or [-99]) for x in runs[arm].values())
        print(f"- {arm}: {n}/{len(runs[arm])}; median over seeds of the largest prop lead {mx:.1f} years")
    if "ENG80" in runs:
        cells = []
        for a in ACTORS:
            dlt = statistics.median(outside(x, a, ["prop"]) for x in runs["ENG80"].values()) - statistics.median(outside(x, a, ["prop"]) for x in base.values())
            cells.append(f"{a} {dlt:+} (sigma* {noise(base, a, 'M4a_disc1980', 0.5)[0]:.2f})")
        print("- cost (ENG80): own discoveries outside prop by 1980 against NONE: " + ", ".join(cells))
    forecast(base)


def quant(v, q):
    s = sorted(v)
    return s[min(len(s) - 1, int(q * len(s)))]


def forecast(base):
    print("\n### (c) Forecast tolerance: |predicted - actual| discovery month, years (arm NONE, all actors)")
    for src in ("whynot", "lever_eta"):
        buckets, hz = defaultdict(list), defaultdict(list)
        never = defaultdict(int)
        for x in base.values():
            known = {(r["actor"], int(r["node"])): num(r["t_known"]) for r in read(x["dir"], "node_times")}
            for r in read(x["dir"], src):
                if src == "whynot":
                    if r["layer"] != "KNOWLEDGE" or r["eta"] == "":
                        continue
                    node, hazard = int(r["subject"]), False
                else:
                    if r["eta"] == "":
                        continue
                    node, hazard = int(r["node"]), r["hazard"] == "1"
                t, eta = int(r["t"]), int(r["eta"])
                b = "<2y" if eta < 24 else ("2-5y" if eta <= 60 else ">5y")
                act = known.get((r["actor"], node))
                if act is None or act < 0:
                    never[(hazard, b)] += 1
                    continue
                (hz if hazard else buckets)[b].append(abs((t + eta) - act) / 12)
        if not buckets and not hz:
            print(f"- {src}: no rows")
            continue
        for name, bk, hflag in (("deterministic", buckets, False), ("hazard (eta to the threshold)", hz, True)):
            for b in ("<2y", "2-5y", ">5y"):
                v = bk.get(b)
                if not v:
                    continue
                q80 = quant(v, 0.8)
                verdict = "rough estimate (p80 > 3 y)" if q80 > 3 else f"tolerance {q80:.1f} y in 80 %"
                print(f"- {src} {name} eta {b}: n {len(v)}, not discovered by 2000-12 {never[(hflag, b)]}; |err| p50 {quant(v, 0.5):.2f}, "
                      f"p80 {q80:.2f}, p90 {quant(v, 0.9):.2f} y -> {verdict}")


def main():
    args = sys.argv[1:]
    pilot = "--pilot" in args
    repo = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
    meta = nodes_meta(repo)
    opt = {args[i]: args[i + 1] for i in range(len(args) - 1) if args[i] in ("--l2", "--p1", "--p2", "--p3")}
    problems = []
    if "--l2" in opt:
        report_l2(opt["--l2"], problems)
    runs1 = None
    if "--p1" in opt:
        runs1, _ = load(opt["--p1"], meta, problems, pilot)
        print("\n# P1 provenance")
        provenance(runs1)
        report_p1(runs1)
        second_line(runs1["base"])
    if "--p2" in opt:
        runs2, _ = load(opt["--p2"], meta, problems, pilot)
        print("\n# P2 provenance")
        provenance(runs2)
        if runs1 is None:
            problems.append("--p2 needs --p1 (best no-reserve policy)")
        else:
            for sd_ in runs2["base"]:
                if sd_ in runs1["base"] and runs2["base"][sd_]["golden"] != runs1["base"][sd_]["golden"]:
                    problems.append(f"base of p1 and p2 differ at seed {sd_}")
            report_p2(runs2, runs1, problems)
    if "--p3" in opt:
        runs3, _ = load(opt["--p3"], meta, problems, pilot)
        print("\n# P3 provenance")
        provenance(runs3)
        if runs1 is not None:
            for sd_ in runs3["NONE"]:
                if sd_ in runs1["base"] and runs3["NONE"][sd_]["golden"] != runs1["base"][sd_]["golden"]:
                    problems.append(f"P3 NONE differs from the P1 base at seed {sd_} (LEVER_ETA must not change the world)")
        report_p3(runs3)
    print("\n## Validity problems")
    print("\n".join(f"- {p}" for p in problems) if problems else "- none")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
