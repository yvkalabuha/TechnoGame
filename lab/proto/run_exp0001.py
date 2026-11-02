#!/usr/bin/env python3
"""EXP-0001 prototype driver: full run matrix -> CSV in the TASK-0002 format.

Usage (from repo root):
    python3 lab/proto/run_exp0001.py [--out lab/results/exp0001-proto] [--jobs N] [--analyze]
    python3 lab/proto/run_exp0001.py --write-manifest     # (re)write lab/manifests/EXP-0001.json

Steps: calibrate control B per nu (rule in the model doc, §2.7), then run
main (A, B, C x 2 nu x 200 seeds) and grid (A x beta 3 x gamma 3 x 2 nu x 50 seeds).
Every run is a pure function of its key, so --jobs does not change the output.
The run matrix and parameters must equal the manifest; a mismatch is an error.
Output: runs.csv, yearly.csv, countries.csv, milestones.csv, calibration.json,
thresholds.json (analysis config from the manifest) and, with --analyze, report.md.
"""
import argparse
import concurrent.futures as cf
import hashlib
import json
import math
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(ROOT, "lab", "analysis"))
import model as M  # noqa: E402

MANIFEST = os.path.join(ROOT, "lab", "manifests", "EXP-0001.json")

MATRIX = {
    "main": {"models": ["A", "B", "C"], "nu": [0.25, 1.0], "beta": [0.625], "gamma": [1.0],
             "seeds": [1, 200]},
    "grid": {"models": ["A"], "nu": [0.25, 1.0], "beta": [0.5, 0.625, 0.75],
             "gamma": [0.5, 0.75, 1.0], "seeds": [1, 50]},
}
CALIBRATION = {
    "target_model": "A", "target_country": "A5", "seeds": [10001, 10050],
    "point": "main beta/gamma, per nu",
    "params": {"B_c_tau0": [0.05, 2.0], "B_delta_tau": [0.0, 0.5]},
    "match": {"B_c_tau0": 1910, "B_delta_tau": 1950},
    "rounds": 3, "bisection_steps": 30, "overflow": "treated as +inf",
}


def fmt_raw(raw):
    if raw < 0:
        return "-" + fmt_raw(-raw)
    return "%d.%06d" % (raw // M.SCALE, raw % M.SCALE)


def run_id(series, model, nu, beta, gamma, seed):
    return "%s_%s_nu%s_b%s_g%s_s%04d" % (series, model, nu, beta, gamma, seed)


def params_for(beta, gamma, calib=None, nu=None):
    p = dict(M.PARAMS)
    p["beta"], p["gamma"] = beta, gamma
    if calib is not None and nu is not None:
        p.update(calib[str(nu)]["values"])
    return p


# Lab schema 1 (docs/simulation/lab-cli.md). Series names are "<group>/<sub>"; the group
# ("main", "grid") is what runs.csv calls `series` (analysis takes the part before "/").
LAB_MODEL_ID = "toy-funnel"
# Lab model inputs that are not in M.PARAMS; "model" and "nu" select the variant and the point.
NON_SIM_PARAMS = ("archetypes", "levels", "model", "nu")
# Sweep axes: must not be in the base params (Lab rejects a key that is both a parameter and an axis).
SERIES_PARAMS = ("beta", "gamma")
# Not a model parameter in Lab: the run length is the manifest's top-level "ticks".
HOST_PARAMS = ("n_ticks",)
# B_c_tau0 / B_delta_tau stay in the base params with the M.PARAMS defaults (Core reads every key
# for every variant; A and C ignore them, as the prototype does); main/B/nu* override them with
# the calibrated values (series params override base params in Lab and in manifest_points).


def lab_series(calib):
    out = []
    mx = MATRIX["main"]
    seeds = {"start": mx["seeds"][0], "count": mx["seeds"][1] - mx["seeds"][0] + 1}
    for model in mx["models"]:
        fixed = {"model": model, "beta": mx["beta"][0], "gamma": mx["gamma"][0]}
        if model == "B":  # calibrated per nu (§2.7) -> one series per nu
            for nu in mx["nu"]:
                out.append({"name": "main/B/nu%s" % nu, "seeds": seeds,
                            "params": dict(fixed, nu=nu, **calib[str(nu)]["values"])})
        else:
            out.append({"name": "main/%s" % model, "seeds": seeds, "params": fixed,
                        "sweep": {"nu": mx["nu"]}})
    gx = MATRIX["grid"]
    out.append({"name": "grid", "params": {"model": gx["models"][0]},
                "seeds": {"start": gx["seeds"][0], "count": gx["seeds"][1] - gx["seeds"][0] + 1},
                "sweep": {"beta": gx["beta"], "gamma": gx["gamma"], "nu": gx["nu"]}})
    assert len(gx["models"]) == 1
    return out


def _seed_list(s):
    if isinstance(s, dict):
        return list(range(s["start"], s["start"] + s["count"]))
    return list(s)


def manifest_points(man):
    """Lab expansion of a schema-1 manifest: (series, params, seeds) per point, in Lab order."""
    base = man.get("params", {})
    pts = []
    for s in man["series"]:
        merged = dict(base, **s.get("params", {}))
        seeds = _seed_list(s.get("seeds", man["seeds"]))
        axes = sorted(s.get("sweep", {}).items())
        combos = [{}]
        for key, values in axes:  # last axis varies fastest
            assert key not in merged, "sweep axis %s is also a parameter" % key
            combos = [dict(c, **{key: v}) for c in combos for v in values]
        for c in combos:
            pts.append((s["name"], dict(merged, **c), seeds))
    return pts


def run_keys(man):
    """Run keys of the prototype, read from the manifest (same manifest as Lab)."""
    keys = []
    assert man["ticks"] == M.PARAMS["n_ticks"], "manifest ticks != prototype n_ticks"
    for name, p, seeds in manifest_points(man):
        sim = dict(M.PARAMS)
        for k, v in p.items():
            if k not in NON_SIM_PARAMS:
                sim[k] = v
        assert p["archetypes"] == M.ARCHETYPES and p["levels"] == M.LEVELS
        group = name.split("/", 1)[0]
        for seed in seeds:
            keys.append((group, p["model"], p["nu"], p["beta"], p["gamma"], seed, sim))
    return keys


# ---------------------------------------------------------------- workers

def _job(key):
    series, model, nu, beta, gamma, seed, p = key
    r = M.simulate(model, seed, nu, p)
    rid = run_id(series, model, nu, beta, gamma, seed)
    runs = "%s,%s,%s,%r,%r,%r,%r,%d,%s\n" % (rid, series, model, nu, beta, gamma, p["t_ref"],
                                             seed, r["state_hash"])
    yearly = "".join(
        "%s,%d,%s,%s,%s,%s,%s,%s,%s,%s,%s,%d,%d,%r\n" % (
            rid, y, c, fmt_raw(K), fmt_raw(A), fmt_raw(T), fmt_raw(P), fmt_raw(R), fmt_raw(E),
            fmt_raw(N), fmt_raw(Y), B, bind, shock)
        for (y, c, K, A, T, P, R, E, N, Y, B, bind, shock) in r["yearly"])
    countries = "".join("%s,%s,%r,%r,%r,%r\n" % ((rid, a["id"]) + tuple(m))
                        for a, m in zip(M.ARCHETYPES, r["mults"]))
    ms = "".join("%s,%s,%d,%s,%s,%s,%s\n" % (
        rid, c, j, fmt_raw(q), "" if mk is None else mk, "" if ma is None else ma,
        "" if mt is None else mt) for (c, j, q, mk, ma, mt) in r["milestones"])
    return runs, yearly, countries, ms


def _mean_power(args):
    model, seed, nu, p, country = args
    try:
        r = M.simulate(model, seed, nu, p, keep_milestones=False)
    except (AssertionError, ArithmeticError):
        return None
    return {row[0]: row[9] for row in r["yearly"] if row[1] == country}


def mean_traj(pool, model, nu, p, seeds, country):
    res = list(pool.map(_mean_power, [(model, s, nu, p, country) for s in seeds]))
    if any(r is None for r in res):
        return None  # overflow -> +inf
    years = sorted(res[0])
    return {y: sum(r[y] for r in res) / len(res) / M.SCALE for y in years}


# ---------------------------------------------------------------- calibration of B

def calibrate(pool, log):
    seeds = list(range(CALIBRATION["seeds"][0], CALIBRATION["seeds"][1] + 1))
    ctry = CALIBRATION["target_country"]
    beta, gamma = MATRIX["main"]["beta"][0], MATRIX["main"]["gamma"][0]
    out = {}
    for nu in MATRIX["main"]["nu"]:
        base = params_for(beta, gamma)
        target = mean_traj(pool, "A", nu, base, seeds, ctry)
        vals = {"B_c_tau0": 1.0, "B_delta_tau": base["delta_K"]}
        for _round in range(CALIBRATION["rounds"]):
            for name in ("B_c_tau0", "B_delta_tau"):
                lo, hi = CALIBRATION["params"][name]
                year = CALIBRATION["match"][name]
                for _ in range(CALIBRATION["bisection_steps"]):
                    mid = (lo + hi) / 2.0
                    p = dict(base, **vals)
                    p[name] = mid
                    tr = mean_traj(pool, "B", nu, p, seeds, ctry)
                    if tr is None or tr[year] > target[year]:
                        hi = mid
                    else:
                        lo = mid
                vals[name] = (lo + hi) / 2.0
        tr = mean_traj(pool, "B", nu, dict(base, **vals), seeds, ctry)
        dev = [math.log(tr[y] / target[y]) for y in sorted(target)]
        out[str(nu)] = {"values": vals,
                        "max_abs_ln_dev": max(abs(d) for d in dev),
                        "rms_ln_dev": math.sqrt(sum(d * d for d in dev) / len(dev)),
                        "ln_dev_1930": dev[1930 - 1910],
                        "target_A5_mean_power": {str(y): target[y] for y in (1910, 1930, 1950)}}
        log("calibrated B nu=%s: %s, max|ln dev|=%.4f" % (nu, vals, out[str(nu)]["max_abs_ln_dev"]))
    return out


# ---------------------------------------------------------------- manifest

def build_manifest(calib):
    """EXP-0001 manifest in Lab schema 1: params/series/seeds for Lab, the rest in meta."""
    import exp0001
    base = {k: v for k, v in M.PARAMS.items() if k not in SERIES_PARAMS + HOST_PARAMS}
    base["archetypes"] = M.ARCHETYPES
    base["levels"] = M.LEVELS
    return {
        "schema": 1,
        "experiment": "EXP-0001",
        "description": "HYP-0001 pilot: science-industry funnel, models A/B/C, 1910-1950",
        "model": LAB_MODEL_ID,
        "ticks": M.PARAMS["n_ticks"],
        "seeds": {"start": MATRIX["main"]["seeds"][0],
                  "count": MATRIX["main"]["seeds"][1] - MATRIX["main"]["seeds"][0] + 1},
        "threads": [1],
        "params": base,
        "series": lab_series(calib),
        "meta": {
            "freeze": "TASK-0008, DECISIONS_LOG #16-#17; manifest_hash = Singularity.Lab hash",
            "model_doc": "docs/simulation/toy-science-industry-model.md",
            "csv_format": "docs/simulation/exp0001-csv-format.md",
            "reference_implementation": "lab/proto/model.py",
            "series_naming": "<group>/<sub>; runs.csv series = group (main, grid)",
            "years": [1910, 1950], "tick": "month",
            "rng": {"scheme": "Kernel CounterRng (SplitMix64 chain, tools/reference/kernel_vectors.py)",
                    "keys": {"SETUP": "system 0, tick 0, entity i, n 0..3 = mult_S, mult_E, mult_Pl, mult_Nl",
                             "SCIENCE": "system 1, tick t, entity i, n 0 = breakthrough u2",
                             "PRODUCTION": "system 2, tick t, entity i, n 0 = output shock u1"}},
            "calibration_B": {"rule": CALIBRATION, "result": calib},
            "analysis": {"script": "lab/analysis/exp0001.py", "config": exp0001.DEFAULTS},
            "decision_rule": "HYP-0001 rev 2 «Критерии успеха»; implemented by meta.analysis.config.decision",
        },
    }


def canonical(obj):
    return json.dumps(obj, indent=2, sort_keys=True, ensure_ascii=False) + "\n"


# ---------------------------------------------------------------- main

def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--out", default=os.path.join(ROOT, "lab", "results", "exp0001-proto"))
    ap.add_argument("--jobs", type=int, default=os.cpu_count() or 1)
    ap.add_argument("--analyze", action="store_true", help="run lab/analysis/exp0001.py after")
    ap.add_argument("--write-manifest", action="store_true")
    a = ap.parse_args(argv)
    t0 = time.time()
    log = lambda s: sys.stderr.write("[%7.1fs] %s\n" % (time.time() - t0, s))

    with cf.ProcessPoolExecutor(max_workers=a.jobs) as pool:
        calib = calibrate(pool, log)
        man = build_manifest(calib)
        text = canonical(man)
        if a.write_manifest:
            os.makedirs(os.path.dirname(MANIFEST), exist_ok=True)
            with open(MANIFEST, "w", encoding="utf-8", newline="\n") as f:
                f.write(text)
            log("manifest written: %s sha256=%s" % (MANIFEST, hashlib.sha256(text.encode()).hexdigest()))
            return 0
        with open(MANIFEST, encoding="utf-8") as f:
            if f.read() != text:
                sys.stderr.write("error: code/calibration differ from %s (run --write-manifest "
                                 "only before the freeze)\n" % MANIFEST)
                return 2

        with open(MANIFEST, encoding="utf-8") as f:
            keys = run_keys(json.load(f))
        log("running %d runs on %d jobs" % (len(keys), a.jobs))
        res = list(pool.map(_job, keys, chunksize=8))
    log("simulation done")

    os.makedirs(a.out, exist_ok=True)
    heads = ["run,series,model,nu,beta,gamma,t_ref,seed,state_hash\n",
             "run,year,country,K,A,T,P,R,E,N,Y_year,B,eng_binding_months,shock\n",
             "run,country,mult_S,mult_E,mult_Pl,mult_Nl\n",
             "run,country,j,q_j,m_K,m_A,m_T\n"]
    for k, name in enumerate(("runs.csv", "yearly.csv", "countries.csv", "milestones.csv")):
        with open(os.path.join(a.out, name), "w", encoding="utf-8", newline="\n") as f:
            f.write(heads[k])
            for r in res:
                f.write(r[k])
    with open(os.path.join(a.out, "calibration.json"), "w", encoding="utf-8", newline="\n") as f:
        f.write(canonical(calib))
    cfg_path = os.path.join(a.out, "thresholds.json")
    with open(cfg_path, "w", encoding="utf-8", newline="\n") as f:
        f.write(canonical(man["meta"]["analysis"]["config"]))
    log("csv written to %s" % a.out)
    if a.analyze:
        import exp0001
        rc = exp0001.main([a.out, "--config", cfg_path, "--out", os.path.join(a.out, "report.md")])
        log("analysis done rc=%d -> %s" % (rc, os.path.join(a.out, "report.md")))
        return rc
    return 0


if __name__ == "__main__":
    sys.exit(main())
