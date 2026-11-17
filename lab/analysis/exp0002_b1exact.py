"""EXP-0002 B1 exact (#268; outside pre-registration, after the result): chg by the old r6 `fielded` rule on main 1..1000.

Reads the calib-print dump with the #268 leader columns (lab/results/exp0002/b1exact/) and the main series of the matrix
(lab/results/exp0002/main/runs.csv, summary.csv). Checks: final hash = golden per seed; lead_l/field_l = summary lead/lead_field.
Counts chg by both rules with the frozen rule of exp0002.py (counted_changes, window_last, hold_months, Wilson z) and the paired
difference chg_r6 - chg_L (X.paired_delta, the frozen paired bootstrap). Compares with the D3-episode approximation (postgate/b1.json,
rebuilt per seed here). Exits 1 if a self-check is not n/n.

#273 (outside pre-registration): with --seeds244 the same print on the calibration seeds 10001-10100 of print #244 (a copy of the frozen
EXP-0002 manifest with other seeds, lab/results/exp0002/b1seeds/ with its matrix in main/), compared per seed with the #244 print
(lab/results/hyp0002a41/main, series v3/SLOT_P2P3, old r6 metric): chg_r6 - chg_244 on the same seeds = code between the two prints
(#247 dynamics, the default I-OC + A41), without seed noise. No D3 approximation there (no D3 dump on these seeds).
Usage: python3 lab/analysis/exp0002_b1exact.py [results_dir] > out.json
       python3 lab/analysis/exp0002_b1exact.py --seeds244 [b1seeds_dir] [hyp0002a41_main_dir] > out.json
"""
import collections
import csv
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import exp0002 as X  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
SEEDS244 = len(sys.argv) > 1 and sys.argv[1] == "--seeds244"
ARGS = sys.argv[2:] if SEEDS244 else sys.argv[1:]
if SEEDS244:
    B = ARGS[0] if ARGS else os.path.join(HERE, "..", "results", "exp0002", "b1seeds")
    MAIN = os.path.join(B, "main")
    OLD = ARGS[1] if len(ARGS) > 1 else os.path.join(HERE, "..", "results", "hyp0002a41", "main")
    OLD_SERIES = "v3/SLOT_P2P3"   # the variant chosen after #244 (#245), chg 0.37 on the old r6 metric
else:
    R = ARGS[0] if ARGS else os.path.join(HERE, "..", "results", "exp0002")
    B = os.path.join(R, "b1exact")
    MAIN = os.path.join(R, "main")
CFG = X.DEFAULTS
GREAT = CFG["great"]
WL, HOLD, Z = CFG["window_last"], CFG["hold_months"], CFG["wilson_z"]
csv.field_size_limit(10**9)


def year(month):
    return 1945 + (month + 8) // 12


def approx_trace(seed, lead, lf, first, spells):
    """The B1 approximation of postgate/b1_r6approx.py, per seed (same assumptions)."""
    g6 = []
    for a in GREAT:
        fl = first[(seed, a)]
        row = [max([0] + [g + 1 for g in range(4) if fl[g] is not None and fl[g] <= t]) for t in range(len(lead))]
        for st, en, gr in spells.get((seed, a), []):
            for t in range(st, min(en, len(lead) - 1) + 1):
                row[t] = min(row[t], gr)
        g6.append(row)
    inc, tr = lead[0], []
    for t in range(len(lead)):
        gs = [g6[i][t] for i in range(3)]
        mx = max(gs)
        top = [i for i in range(3) if gs[i] == mx]
        if gs[inc] < mx:
            inc = top[0] if lead[t] not in top else lead[t]
        elif lead[t] in top and lf[t] != "=":
            inc = lead[t]
        tr.append(inc)
    return tr


def old_chg():
    """Per-seed chg of the #244 print (series v3/SLOT_P2P3), by the frozen rule on its summary lead (r6 metric of that code)."""
    runs, out = {}, {}
    with open(os.path.join(OLD, "runs.csv")) as f:
        for r in csv.DictReader(f):
            if r["series"] == OLD_SERIES and r["status"] == "ok":
                runs[r["run"]] = int(r["seed"])
    with open(os.path.join(OLD, "summary.csv")) as f:
        for r in csv.DictReader(f):
            if r["run"] in runs:
                out[runs[r["run"]]] = X.counted_changes([int(c) for c in r["lead"]], WL, HOLD)
    return out


def main():
    out = {"note": "outside pre-registration (%s); does not change the EXP-0002 outcome" % ("#273" if SEEDS244 else "#268")}
    runs = {}
    with open(os.path.join(MAIN, "runs.csv")) as f:
        for r in csv.DictReader(f):
            if r["series"] == "main":
                runs[r["run"]] = (int(r["seed"]), r["golden_hash"])
    golden = {s: h for s, h in runs.values()}
    summ = {}
    with open(os.path.join(MAIN, "summary.csv")) as f:
        for r in csv.DictReader(f):
            if r["run"] in runs:
                summ[runs[r["run"]][0]] = ([int(c) for c in r["lead"]], r["lead_field"])
    b = B
    with open(os.path.join(b, "fielded_runs.csv")) as f:
        hashes = {int(r["seed"]): r["hash_final"] for r in csv.DictReader(f)}
    out["hash_equal_golden"] = "%d/%d" % (sum(hashes[s] == golden.get(s) for s in hashes), len(hashes))
    lead = {}
    with open(os.path.join(b, "fielded_lead.csv")) as f:
        for r in csv.DictReader(f):
            lead[int(r["seed"])] = ([int(c) for c in r["lead_l"]], [int(c) for c in r["lead_r6"]], r["field_l"], r["field_r6"])
    out["lead_l_equal_summary"] = "%d/%d" % (sum(lead[s][0] == summ[s][0] and lead[s][2] == summ[s][1] for s in lead), len(lead))
    # Review: a self-check below n/n means the print did not run the matrix world; stop, do not print numbers.
    bad = [k for k in ("hash_equal_golden", "lead_l_equal_summary") if out[k].split("/")[0] != out[k].split("/")[1] or out[k] == "0/0"]
    if bad:
        json.dump(out, sys.stdout, indent=1, sort_keys=True)
        print()
        sys.stderr.write("self-check failed: %s\n" % ", ".join("%s %s" % (k, out[k]) for k in bad))
        sys.exit(1)
    # D3 dump for the approximation (same files as postgate/b1_r6approx.py).
    first, spells = {}, collections.defaultdict(list)
    if not SEEDS244:
        with open(os.path.join(R, "d3print", "fielded_actor.csv")) as f:
            for r in csv.DictReader(f):
                first[(int(r["seed"]), r["actor"])] = [X._oi(r["first_l_g%d" % g]) for g in range(1, 5)]
        with open(os.path.join(R, "d3print", "fielded_spells.csv")) as f:
            for r in csv.DictReader(f):
                spells[(int(r["seed"]), r["actor"])].append((int(r["start"]), int(r["end"]) if r["end"] else 383, int(r["g_r6_min"])))

    seeds = sorted(lead)
    n = len(seeds)
    c = collections.Counter()
    first_month, first_field, new_leader, years, n_changes = [], collections.Counter(), collections.Counter(), collections.Counter(), collections.Counter()
    per_seed = []
    for s in seeds:
        ll, l6, fl, f6 = lead[s]
        chl = X.counted_changes(ll, WL, HOLD)
        ch6 = X.counted_changes(l6, WL, HOLD)
        cha = [] if SEEDS244 else X.counted_changes(approx_trace(s, summ[s][0], summ[s][1], first, spells), WL, HOLD)
        per_seed.append((1 if ch6 else 0, 1 if chl else 0))
        c["chg_L"] += bool(chl)
        c["chg_r6"] += bool(ch6)
        c["chg_approx"] += bool(cha)
        c["r6_and_L"] += bool(ch6) and bool(chl)
        c["r6_only"] += bool(ch6) and not chl
        c["L_only"] += bool(chl) and not ch6
        c["r6_and_approx"] += bool(ch6) and bool(cha)
        c["r6_not_approx"] += bool(ch6) and not cha
        c["approx_not_r6"] += bool(cha) and not ch6
        c["same_first_change_r6_approx"] += bool(ch6) and bool(cha) and ch6[0][:2] == cha[0][:2]
        c["months_lead_differs"] += sum(1 for t in range(len(ll)) if ll[t] != l6[t])
        c["months"] += len(ll)
        c["seeds_lead_differs"] += ll != l6
        n_changes[min(len(ch6), 3)] += 1
        if ch6:
            m, v, old = ch6[0]
            first_month.append(m)
            first_field[f6[m]] += 1
            new_leader["%s->%s" % (GREAT[old], GREAT[v])] += 1
            years[year(m)] += 1
    out["n"] = n
    out["counts"] = dict(c)
    out["chg_L"] = X.share(c["chg_L"], n, CFG)
    out["chg_r6"] = X.share(c["chg_r6"], n, CFG)
    if not SEEDS244:
        out["chg_approx"] = X.share(c["chg_approx"], n, CFG)
    # Paired difference chg_r6 - chg_L (the metric effect on the final dynamics): the frozen paired bootstrap of exp0002.py.
    tag = "b1seeds" if SEEDS244 else "b1exact"
    out["metric_effect_r6_minus_L"] = X.paired_delta([a for a, _ in per_seed], [b_ for _, b_ in per_seed], CFG, tag + "|r6-L")
    if SEEDS244:
        old = old_chg()
        miss = [s for s in seeds if s not in old]
        if miss:
            sys.stderr.write("seeds missing in the #244 print: %s\n" % miss[:5])
            sys.exit(1)
        c244 = [1 if old[s] else 0 for s in seeds]
        r6 = [a for a, _ in per_seed]
        lL = [b_ for _, b_ in per_seed]
        out["chg_244"] = X.share(sum(c244), n, CFG)
        # chg_r6 (final code) - chg_244 (code of #244), both by the r6 metric on the same seeds: the code between the prints.
        out["code_effect_r6_minus_244"] = X.paired_delta(r6, c244, CFG, tag + "|r6-244")
        out["total_L_minus_244"] = X.paired_delta(lL, c244, CFG, tag + "|L-244")
        out["seeds_244_vs_r6"] = {"both": sum(a and b_ for a, b_ in zip(c244, r6)), "only_244": sum(a and not b_ for a, b_ in zip(c244, r6)),
                                  "only_r6": sum(b_ and not a for a, b_ in zip(c244, r6)), "neither": sum(not a and not b_ for a, b_ in zip(c244, r6))}
        ch6s = {s: X.counted_changes(lead[s][1], WL, HOLD) for s in seeds}
        out["same_first_change_244_r6"] = sum(1 for s in seeds if old[s] and ch6s[s] and ch6s[s][0][:2] == old[s][0][:2])
        out["first_change_month_244_p5_p50_p95"] = [X.quantile([old[s][0][0] for s in seeds if old[s]], q) for q in (0.05, 0.5, 0.95)]
        out["first_change_pair_244"] = dict(collections.Counter("%s->%s" % (GREAT[old[s][0][2]], GREAT[old[s][0][1]]) for s in seeds if old[s]))
    out["first_change_month_p5_p50_p95"] = [X.quantile(first_month, q) for q in (0.05, 0.5, 0.95)] if first_month else None
    out["first_change_field_r6"] = dict(first_field)
    out["first_change_pair"] = dict(new_leader)
    out["first_change_year"] = dict(sorted(years.items()))
    out["changes_per_seed_r6_0_1_2_3plus"] = [n_changes[k] for k in range(4)]
    # Key file: months where the r6 key of a great power differs from K_L, by field.
    kd = collections.Counter()
    with open(os.path.join(B, "fielded_key.csv")) as f:
        for r in csv.DictReader(f):
            if r["actor"] not in GREAT:
                continue
            kd["months"] += 1
            if r["g_l"] != r["g_r6"]:
                kd["G_differs_" + r["actor"]] += 1
            elif r["p_l"] != r["p_r6"]:
                kd["only_P_differs_" + r["actor"]] += 1
    out["key_diff_great_actor_months"] = dict(sorted(kd.items()))
    json.dump(out, sys.stdout, indent=1, sort_keys=True)
    print()


if __name__ == "__main__":
    main()
