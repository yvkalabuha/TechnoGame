#!/usr/bin/env python3
"""EXP-0008 analysis (TASK-0106): П4 over accepted processes, sensitivity, MAD noise, GC pause share, the outcome row of П7
(ADR-0016 §4 as amended by ADR-0023 п. 1.7), frequency flags, repeats/exclusions by block and the R2(г) interval (П9 п. 4).

Input: the directory written by `lab bench --mode series` (series.json + attempt JSONs). Output: a JSON summary (--out) and a short
text report on stdout. Not part of the harness; it makes the pre-registered rules executable before the run.

Statistics (all host-side, no numpy):
- percentiles are Hyndman-Fan type 7 (the same as BenchStats in Lab);
- the outcome point's median T = median of the accepted processes' daily-run medians; p95 T = over their measured months pooled;
- S, K_d per month pair (П4): k = (T - M)/29, K_d = 30 k, S = M - k; p95 over the pooled pairs;
- noise = (p90 - p10)/median of the accepted medians (main criterion); robust noise MAD/median (record);
- R2(г): g = median of the pooled CoW/full ratios - 1; 95 % CI of the median from order statistics (binomial, distribution-free);
  DEBT-0045 closes only if the upper bound of g <= 2 % at t1 and t8 and the hashes are equal.

Run: python3 lab/analysis/exp0008.py SERIES_DIR [--out summary.json]
Tests: python3 -m unittest discover -s lab/analysis -p 'test_exp0008.py'
"""
import argparse
import json
import math
import os
import sys

BUDGET_P95_MS = 500.0      # ADR-0016 п. 1
BUDGET_MEDIAN_MS = 400.0
S_MAX_MS = 100.0           # ADR-0016 §4 PASS/WARN
KD_MAX_MS = 50.0
KD_FLAG_MS = 150.0         # Kernel-ADR flag (record)
GC_PAUSE_MAX = 0.02        # ADR-0023 п. 2.4
DEBT45_MAX = 0.02          # П9 п. 4


def percentile(values, p):
    """Type 7: h = (n-1)p, linear between order statistics."""
    x = sorted(values)
    if not x:
        raise ValueError("no values")
    h = (len(x) - 1) * p
    lo = int(math.floor(h))
    if lo >= len(x) - 1:
        return x[-1]
    return x[lo] + (h - lo) * (x[lo + 1] - x[lo])


def median(values):
    return percentile(values, 0.5)


def noise(medians):
    """(p90 - p10) / median, type 7 (EXP-0008 П4, ADR-0016 п. 5)."""
    m = median(medians)
    return (percentile(medians, 0.9) - percentile(medians, 0.1)) / m


def mad_noise(medians):
    m = median(medians)
    return median([abs(v - m) for v in medians]) / m


def binom_cdf(k, n, p=0.5):
    """P(B <= k) for B ~ Bin(n, p), exact by summation (n up to a few thousand)."""
    if k < 0:
        return 0.0
    if k >= n:
        return 1.0
    total = 0.0
    log_p, log_q = math.log(p), math.log(1 - p)
    for i in range(0, k + 1):
        total += math.exp(math.lgamma(n + 1) - math.lgamma(i + 1) - math.lgamma(n - i + 1) + i * log_p + (n - i) * log_q)
    return min(1.0, total)


def median_ci(values, level=0.95):
    """Distribution-free CI of the median: [x_(j), x_(n-j+1)] (1-based) with the largest j such that
    P(B <= j-1) <= (1-level)/2, B ~ Bin(n, 1/2); coverage = 1 - 2 P(B <= j-1). None if n is too small for the level."""
    x = sorted(values)
    n = len(x)
    alpha = (1 - level) / 2
    j = 0
    while j + 1 <= n // 2 and binom_cdf(j, n) <= alpha:
        j += 1
    if j == 0:
        return None
    return {"lower": x[j - 1], "upper": x[n - j], "rank_lower": j, "rank_upper": n - j + 1, "n": n,
            "coverage": 1 - 2 * binom_cdf(j - 1, n)}


def load(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def runs_of(doc):
    out = {}
    for r in doc.get("runs", []):
        out[r["mode"]] = r
    return out


def measured(run, warmup):
    return run["month_ms_all"][warmup:]


def process_stats(doc):
    """Per-process values of П4 from one process JSON."""
    warmup = doc["params"]["warmup_months"]
    runs = runs_of(doc)
    d, m = runs.get("daily"), runs.get("monthly")
    st = {"median_T": d["month_ms"]["p50"] if d else None, "T_months": measured(d, warmup) if d else [],
          "median_M": m["month_ms"]["p50"] if m else None}
    if d and m:
        t, mm = measured(d, warmup), measured(m, warmup)
        k = [(ti - mi) / 29.0 for ti, mi in zip(t, mm)]
        st["S_months"] = [mi - ki for mi, ki in zip(mm, k)]
        st["Kd_months"] = [30 * ki for ki in k]
        st["v2"] = doc.get("v2_monthly_daily_hash_equal")
    st["gc_pause_ms"] = sum(r.get("gc_pause_ms", 0) for r in runs.values())
    st["wall_ms"] = sum(r.get("window_wall_ms", 0) for r in runs.values())
    st["gen2"] = sum(r.get("gc_gen2", 0) for r in runs.values())
    st["alloc_net_per_month"] = [r.get("allocated_bytes_net", r.get("allocated_bytes")) / max(1, r["window_months"]) for r in runs.values()]
    fr = doc.get("frequency") or {}
    st["thermal_flag"] = fr.get("possible_thermal_throttle")
    mach = doc.get("machine") or {}
    st["foreign_cpu"] = mach.get("foreign_cpu_window")
    k_thr = mach.get("threads_k")
    cpu, wall = mach.get("self_cpu_s_window"), mach.get("wall_s_window")
    # ADR-0023 gate addition (b): wall * threads / own CPU (> 1 when the own threads did not get the CPU they could use).
    st["wall_threads_over_cpu"] = wall * k_thr / cpu if cpu and wall and k_thr else None
    st["run_delay"] = mach.get("run_delay_share_window")
    st["conditions"] = doc.get("conditions", {})
    st["build_commit"] = (doc.get("provenance") or {}).get("build_commit")
    st["dirty"] = (doc.get("provenance") or {}).get("dirty")
    return st


def point_stats(procs):
    """П4 over a list of per-process stats: median of medians, pooled p95, noise, S/K_d p95, GC pause share."""
    meds = [p["median_T"] for p in procs if p["median_T"] is not None]
    if not meds:
        return None
    t_all = [v for p in procs for v in p["T_months"]]
    s_all = [v for p in procs for v in p.get("S_months", [])]
    kd_all = [v for p in procs for v in p.get("Kd_months", [])]
    wall = sum(p["wall_ms"] for p in procs)
    out = {
        "processes": len(procs),
        "median_T_ms": median(meds),
        "p95_T_ms": percentile(t_all, 0.95),
        "noise": noise(meds),
        "noise_mad": mad_noise(meds),
        "S_p95_ms": percentile(s_all, 0.95) if s_all else None,
        "K_d_p95_ms": percentile(kd_all, 0.95) if kd_all else None,
        "S_p50_ms": median(s_all) if s_all else None,
        "K_d_p50_ms": median(kd_all) if kd_all else None,
        "gc_pause_share_pooled": sum(p["gc_pause_ms"] for p in procs) / wall if wall else None,
        "gc_pause_share_max": max((p["gc_pause_ms"] / p["wall_ms"] for p in procs if p["wall_ms"]), default=None),
        "gen2_total": sum(p["gen2"] for p in procs),
    }
    out["K_d_gt_150_flag"] = out["K_d_p95_ms"] is not None and out["K_d_p95_ms"] > KD_FLAG_MS
    out["gc_pause_le_2pct"] = out["gc_pause_share_pooled"] is not None and out["gc_pause_share_pooled"] <= GC_PAUSE_MAX
    return out


def outcome_row(stats, noise_max, exclusion_inconclusive, invalid_reasons):
    """П7 row. Order: INVALID, INCONCLUSIVE (noise or exclusions; frequency is no condition), FAIL, WARN, PASS."""
    if invalid_reasons:
        return "INVALID"
    if exclusion_inconclusive or stats is None or stats["noise"] > noise_max:
        return "INCONCLUSIVE"
    if stats["p95_T_ms"] > BUDGET_P95_MS or stats["median_T_ms"] > BUDGET_MEDIAN_MS:
        return "FAIL"
    if (stats["S_p95_ms"] or 0) > S_MAX_MS or (stats["K_d_p95_ms"] or 0) > KD_MAX_MS:
        return "WARN"
    return "PASS"


def invalid_reasons(all_stats, validity_exit):
    r = []
    if validity_exit not in (None, 0):
        r.append("validity (V1/V2) failed")
    commits = {s["build_commit"] for s in all_stats}
    if len(commits) > 1:
        r.append(f"different builds: {sorted(commits)}")
    for s in all_stats:
        c = s["conditions"]
        if c.get("configuration") != "Release":
            r.append("not Release")
        if c.get("gc_server"):
            r.append("server GC")
        if c.get("power") == "battery":
            r.append("battery")
        if s.get("v2") is False:
            r.append("V2 monthly != daily hash")
        if s.get("dirty"):
            r.append("dirty tree")
    runtimes = {s["conditions"].get("runtime_version") for s in all_stats}
    if len(runtimes) > 1:
        r.append(f"different .NET versions: {sorted(runtimes)}")
    return sorted(set(r))


def r2d(series_dir, ref):
    """Pool the pairwise ratios of the accepted r2d-* modes by thread count."""
    pools, hashes, evaluated = {}, True, True
    for mode in (ref or {}).get("modes", []):
        if not mode["id"].startswith("r2d"):
            continue
        procs = mode["gate"]["processes"]
        acc = procs[0]["accepted_attempt"] if procs and procs[0]["status"] == "Accepted" else None
        if acc is None:
            evaluated = False
            continue
        doc = load(os.path.join(series_dir, procs[0]["attempts"][acc]["file"]))
        for bt in doc["r2"]["d_month_cow_vs_full"]["by_threads"]:
            pools.setdefault(bt["threads"], []).extend(bt["ratio_cow_over_full_all"])
            hashes &= bool(bt["hashes_equal"])
    out = {"by_threads": {}, "hashes_equal": hashes, "all_evaluated": evaluated}
    closes = evaluated and hashes and bool(pools)
    for t, ratios in sorted(pools.items()):
        ci = median_ci(ratios)
        g = median(ratios) - 1
        row = {"pairs": len(ratios), "g": g, "ci": None if ci is None else {"g_lower": ci["lower"] - 1, "g_upper": ci["upper"] - 1,
                                                                              "coverage": ci["coverage"], "ranks": [ci["rank_lower"], ci["rank_upper"]]}}
        out["by_threads"][str(t)] = row
        if t in (1, 8):
            closes &= ci is not None and ci["upper"] - 1 <= DEBT45_MAX
    closes &= all(str(t) in out["by_threads"] for t in (1, 8))
    out["debt_0045_closes"] = closes
    return out


def analyse(series_dir):
    s = load(os.path.join(series_dir, "series.json"))
    gate = s["gate"]
    outcome_block = gate["outcome_block"]
    procs = s["main"]["processes"]
    by_block = {}
    accepted, sensitivity, all_stats = {}, {}, []
    for p in procs:
        b = by_block.setdefault(p["block"], {"processes": 0, "repeats": 0, "excluded": 0, "accepted_no_evidence": 0,
                                             "thermal_flags": 0, "attempts": 0})
        b["processes"] += 1
        b["repeats"] += len(p["attempts"]) - 1
        b["attempts"] += len(p["attempts"])
        b["excluded"] += p["status"] == "Excluded"
        b["accepted_no_evidence"] += p["accepted_by"] == "no-evidence"
        b["thermal_flags"] += sum(1 for a in p["attempts"] if a.get("possible_thermal_throttle"))
        key = p["group"]   # (block, point, threads, background), the med_point group
        if p["status"] == "Accepted":
            st = process_stats(load(os.path.join(series_dir, p["attempts"][p["accepted_attempt"]]["file"])))
            accepted.setdefault(key, []).append(st)
            sensitivity.setdefault(key, []).append(st)
            all_stats.append(st)
        else:
            # Sensitivity (П4, ADR-0023 п. 1.6): the excluded process's first attempt.
            sensitivity.setdefault(key, []).append(process_stats(load(os.path.join(series_dir, p["attempts"][0]["file"]))))
    for b in by_block.values():
        b["thermal_flag_share"] = b["thermal_flags"] / b["attempts"] if b["attempts"] else None
    points = {k: point_stats(v) for k, v in sorted(accepted.items())}
    sens_points = {k: point_stats(v) for k, v in sorted(sensitivity.items())}
    out_key = next((k for k in points if k.startswith(outcome_block + "|")), None)
    excl = s.get("exclusion_verdict", {})
    inv = invalid_reasons(all_stats, (s.get("validity") or {}).get("exit"))
    row = outcome_row(points.get(out_key), gate["noise_max"], excl.get("inconclusive", False), inv)
    row_sens = outcome_row(sens_points.get(out_key), gate["noise_max"], excl.get("inconclusive", False), inv)
    a = points.get(out_key)
    result = {
        "experiment": s["experiment"], "manifest_hash": s["manifest_hash"], "series_exit_code": s["exit_code"], "smoke": s.get("smoke"),
        "outcome_point": out_key, "outcome": row, "invalid_reasons": inv,
        "exclusion_verdict": excl,
        "noise_inconclusive": a is not None and a["noise"] > gate["noise_max"],
        "sensitivity": {"outcome": row_sens, "differs": row_sens != row, "outcome_point": sens_points.get(out_key),
                        "pass_depends_on_exclusions": row == "PASS" and row_sens != row},
        "partition_rule_note": "S_p95 + K_d,p95 + 19*P_p95 > 500 needs P from the profile (П6); not computed here",
        "points": points,
        "blocks": by_block,
        "r2d": r2d(series_dir, s.get("reference")),
    }
    if a:
        result["wall_threads_over_cpu_outcome"] = [p["wall_threads_over_cpu"] for p in accepted[out_key]]
    return result


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("series_dir")
    ap.add_argument("--out")
    args = ap.parse_args(argv)
    r = analyse(args.series_dir)
    if args.out:
        with open(args.out, "w", encoding="utf-8") as f:
            json.dump(r, f, indent=2, ensure_ascii=False)
    a = r["points"].get(r["outcome_point"]) or {}
    print(f"{r['experiment']} manifest {r['manifest_hash'][:12]}: outcome {r['outcome']}"
          f" (sensitivity {r['sensitivity']['outcome']}{', DIFFERS' if r['sensitivity']['differs'] else ''})")
    if a:
        print(f"  point {r['outcome_point']}: median T {a['median_T_ms']:.1f} ms, p95 {a['p95_T_ms']:.1f} ms, noise {a['noise']:.3f}"
              f" (MAD {a['noise_mad']:.3f}), S_p95 {a['S_p95_ms']}, K_d_p95 {a['K_d_p95_ms']}, GC pause {a['gc_pause_share_pooled']}")
    for b, v in sorted(r["blocks"].items()):
        print(f"  block {b}: {v['processes']} processes, {v['repeats']} repeats, {v['excluded']} excluded,"
              f" {v['accepted_no_evidence']} accepted without evidence, thermal flag share {v['thermal_flag_share']}")
    for t, v in r["r2d"]["by_threads"].items():
        ci = v["ci"]
        print(f"  R2(г) t{t}: {v['pairs']} pairs, g {v['g']:+.4f}, CI {None if ci is None else (round(ci['g_lower'], 4), round(ci['g_upper'], 4))}")
    print(f"  DEBT-0045 closes: {r['r2d']['debt_0045_closes']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
