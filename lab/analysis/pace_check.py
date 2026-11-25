#!/usr/bin/env python3
"""pace-check (owner decision #555): knowledge pace of the slice against the C4 windows and a base build.

Runs the calibration world (lab/manifests/PACE-check.json = EXP-0002-calib: JetSliceCalib, 11688 ticks, horizon 1977-12, levers at
their defaults, calibration seeds 10001-10100) on the current tree and on a base commit (git archive into lab/results/pace/<sha>/tree),
then prints per generation G2..G4:
  - APP known month: node_times.t_known of the APP node of gen g, USA and any actor, median over seeds;
  - first unit: gen_times.t_first_unit of any actor, median and the share of seeds inside the C4 window (months from 1945-09);
  - deltas current - base and a verdict: WARN if a median moves by more than 24 months or an in-window share drops by more than
    5 percentage points (O-085; meta.pace.warn_window_share_drop_pp), else PASS.
Medians are lower medians with never-known / never-built counted as +inf ("inf" = more than half censored at the horizon).
APP node numbers are read from each tree's mods/core/nodes.yaml (type APP, gen g).

Usage:
  python3 lab/analysis/pace_check.py --base SHA [--manifest lab/manifests/PACE-check.json] [--seeds N] [--jobs 8] [--reuse-base]
  python3 lab/analysis/pace_check.py --analyze BASE_RUN_DIR BASE_TREE CUR_RUN_DIR CUR_TREE [--manifest M]
  python3 lab/analysis/pace_check.py --selftest
Exit codes: 0 PASS, 3 WARN (only with --strict; else 0), 1 a build or a run failed, 2 usage error.
Doc: docs/simulation/lab-cli.md, section "pace-check".
"""
import argparse
import csv
import hashlib
import json
import math
import os
import re
import shutil
import statistics
import subprocess
import sys
import tempfile
import time
from fractions import Fraction
from collections import defaultdict

INF = math.inf
DEFAULT_PACE = {"gens": [2, 3, 4], "windows": {"2": [16, 87], "3": [100, 195], "4": [256, 363]}, "usa_actor": "USA",
                "warn_median_shift_months": 24, "warn_window_share_drop_pp": 5}
REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


# ------------------------------------------------------------------ analysis (pure, tested)
def read_text(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def write_text(path, text):
    with open(path, "w", encoding="utf-8") as f:
        f.write(text)


def read_json(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def pace_config(manifest):
    cfg = dict(DEFAULT_PACE)
    cfg.update(((manifest or {}).get("meta") or {}).get("pace") or {})
    cfg["windows"] = {int(k): tuple(v) for k, v in cfg["windows"].items()}
    cfg["gens"] = [int(g) for g in cfg["gens"]]
    return cfg


def app_nodes(tree, gens):
    """node number -> gen for the APP nodes of the given gens in TREE/mods/core/nodes.yaml (regex: no yaml dependency)."""
    text = read_text(os.path.join(tree, "mods", "core", "nodes.yaml"))
    out = {}
    for block in re.split(r"\n  - id:", text)[1:]:
        num = re.search(r"\n    number: (\d+)", block)
        typ = re.search(r"\n    type: (\w+)", block)
        gen = re.search(r"\n    gen: (\d+)", block)
        if num and typ and gen and typ.group(1) == "APP" and int(gen.group(1)) in gens:
            out[int(num.group(1))] = int(gen.group(1))
    missing = sorted(set(gens) - set(out.values()))
    if missing:
        raise ValueError("%s: no APP node for gen %s" % (tree, missing))
    if len(out) != len(set(out.values())):
        raise ValueError("%s: several APP nodes per gen: %s" % (tree, out))
    return out


def lower_median(xs):
    return statistics.median_low(sorted(xs)) if xs else None


def summarize(run_dir, tree, cfg):
    """Per gen: APP known (USA, any), first unit any actor (median, in-window count) over the ok runs of a matrix directory."""
    gens = cfg["gens"]
    nodes = app_nodes(tree, gens)
    with open(os.path.join(run_dir, "runs.csv"), newline="") as f:
        rows = list(csv.DictReader(f))
    bad = [r["run"] for r in rows if r.get("status") != "ok"]
    if bad:
        raise ValueError("%s: runs not ok: %s" % (run_dir, bad[:5]))
    runs = [r["run"] for r in rows]
    usa = defaultdict(lambda: INF)
    anyk = defaultdict(lambda: INF)
    with open(os.path.join(run_dir, "node_times.csv"), newline="") as f:
        for r in csv.DictReader(f):
            g = nodes.get(int(r["node"]))
            t = r["t_known"]
            if g is None or t in ("", None):
                continue
            t = max(0, int(t))   # -1 = known at the start
            k = (r["run"], g)
            anyk[k] = min(anyk[k], t)
            if r["actor"] == cfg["usa_actor"]:
                usa[k] = min(usa[k], t)
    unit = defaultdict(lambda: INF)
    with open(os.path.join(run_dir, "gen_times.csv"), newline="") as f:
        for r in csv.DictReader(f):
            t = r["t_first_unit"]
            if t in ("", None) or int(t) < 0:
                continue
            k = (r["run"], int(r["gen"]))
            unit[k] = min(unit[k], int(t))
    out = {"n": len(runs), "app_nodes": {g: n for n, g in nodes.items()}, "gens": {}}
    for g in gens:
        lo, hi = cfg["windows"][g]
        u = [unit[(r, g)] for r in runs]
        au = [usa[(r, g)] for r in runs]
        aa = [anyk[(r, g)] for r in runs]
        out["gens"][g] = {"app_usa": lower_median(au), "app_any": lower_median(aa), "unit": lower_median(u),
                          "in_window": sum(1 for t in u if lo <= t <= hi),
                          "censored": {"app_usa": au.count(INF), "app_any": aa.count(INF), "unit": u.count(INF)}}
    return out


def _delta(cur, base):
    if cur is None or base is None:
        return None
    if cur == INF and base == INF:
        return 0
    if cur == INF or base == INF:
        return INF if cur == INF else -INF
    return cur - base


def compare(base, cur, cfg):
    """Rows per gen with deltas and the verdict; overall verdict WARN if any row warns."""
    lim = cfg["warn_median_shift_months"]
    rows = []
    for g in cfg["gens"]:
        b, c = base["gens"][g], cur["gens"][g]
        row = {"gen": g, "base": b, "cur": c, "reasons": []}
        for key in ("app_usa", "app_any", "unit"):
            d = _delta(c[key], b[key])
            row["d_" + key] = d
            if d is not None and abs(d) > lim:
                row["reasons"].append("%s moved %s months" % (key, fmt_delta(d)))
        fb = Fraction(b["in_window"], base["n"]) if base["n"] else Fraction(0)
        fc = Fraction(c["in_window"], cur["n"]) if cur["n"] else Fraction(0)
        sb, sc = float(fb), float(fc)
        row["share_base"], row["share_cur"] = sb, sc
        if (fb - fc) * 100 > Fraction(str(cfg["warn_window_share_drop_pp"])):   # exact: a 5 pp drop of 100 seeds is not > 5
            row["reasons"].append("in-window share %.2f -> %.2f" % (sb, sc))
        row["verdict"] = "WARN" if row["reasons"] else "PASS"
        rows.append(row)
    return {"rows": rows, "verdict": "WARN" if any(r["verdict"] == "WARN" for r in rows) else "PASS"}


def month_label(m, censored=0, n=0):
    if m is None:
        return "-"
    if m == INF:
        return "inf (%d/%d never)" % (censored, n) if n else "inf"
    y, mo = divmod(8 + int(m), 12)   # month 0 = 1945-09
    return "%d (%d-%02d)" % (m, 1945 + y, mo + 1)


def fmt_delta(d):
    if d is None:
        return "-"
    if d in (INF, -INF):
        return "+inf" if d > 0 else "-inf"
    return "%+d" % d


def render(res, cfg, base_label, cur_label, base, cur, extra=None):
    w = cfg["windows"]
    lines = ["# pace-check (#555): %s vs base %s" % (cur_label, base_label), ""]
    for k, v in (extra or {}).items():
        lines.append("- %s: %s" % (k, v))
    lines.append("- seeds: base %d, current %d; APP nodes base %s, current %s" % (
        base["n"], cur["n"], base["app_nodes"], cur["app_nodes"]))
    lines.append("- C4 windows (months from 1945-09): " + ", ".join("G%d %d-%d" % (g, *w[g]) for g in cfg["gens"]))
    lines += ["", "| G | measure | base | current | delta |", "|---|---|---|---|---|"]
    for r in res["rows"]:
        g = r["gen"]
        for key, name in (("app_usa", "APP known, USA"), ("app_any", "APP known, any actor"), ("unit", "first unit, any actor")):
            lines.append("| G%d | %s | %s | %s | %s |" % (
                g, name, month_label(r["base"][key], r["base"]["censored"][key], base["n"]),
                month_label(r["cur"][key], r["cur"]["censored"][key], cur["n"]), fmt_delta(r["d_" + key])))
        lines.append("| G%d | first unit in window %d-%d | %d/%d (%.2f) | %d/%d (%.2f) | %+.2f |" % (
            g, w[g][0], w[g][1], r["base"]["in_window"], base["n"], r["share_base"], r["cur"]["in_window"], cur["n"],
            r["share_cur"], r["share_cur"] - r["share_base"]))
    lines += ["", "| G | verdict | reasons |", "|---|---|---|"]
    for r in res["rows"]:
        lines.append("| G%d | %s | %s |" % (r["gen"], r["verdict"], "; ".join(r["reasons"]) or "-"))
    lines += ["", "VERDICT: %s (WARN: a median moved > %d months or an in-window share dropped by > %s pp)" % (
        res["verdict"], cfg["warn_median_shift_months"], cfg["warn_window_share_drop_pp"])]
    return "\n".join(lines) + "\n"


def jsonable(x):
    if isinstance(x, float) and math.isinf(x):
        return "inf" if x > 0 else "-inf"
    if isinstance(x, dict):
        return {str(k): jsonable(v) for k, v in x.items()}
    if isinstance(x, (list, tuple)):
        return [jsonable(v) for v in x]
    return x


# ------------------------------------------------------------------ builds and runs
def sh(cmd, cwd=None, env=None, quiet=False):
    t0 = time.time()
    p = subprocess.run(cmd, cwd=cwd, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    if p.returncode != 0:
        sys.stderr.write(p.stdout[-6000:])
        raise RuntimeError("command failed (%d): %s" % (p.returncode, " ".join(cmd)))
    if not quiet:
        print("  [%.0f s] %s" % (time.time() - t0, " ".join(cmd)), flush=True)
    return p.stdout


def dotnet():
    env = os.environ.get("DOTNET")
    if env:
        return env
    found = shutil.which("dotnet")
    return found or os.path.expanduser("~/.dotnet/dotnet")


def build_lab(tree, env=None, revision=None):
    """Build the Lab of TREE. For a base archive: its own env (GIT_CEILING_DIRECTORIES) and SourceRevisionId = the base sha, so
    the binary's build_commit names the base and not the HEAD of an enclosing checkout."""
    cmd = ["nice", "-n", "10", dotnet(), "build", os.path.join(tree, "src", "Singularity.Lab"), "-c", "Release", "--nologo", "-v", "q"]
    if revision:
        cmd.append("-p:SourceRevisionId=" + revision)
    sh(cmd, env=env)
    rel = os.path.join(tree, "src", "Singularity.Lab", "bin", "Release")
    for fw in sorted(os.listdir(rel), reverse=True):
        dll = os.path.join(rel, fw, "Singularity.Lab.dll")
        if os.path.exists(dll):
            return dll
    raise RuntimeError("no Singularity.Lab.dll under " + rel)


def run_matrix(dll, manifest_path, out, jobs, seeds, env=None):
    if os.path.exists(out):
        shutil.rmtree(out)
    cmd = ["nice", "-n", "10", dotnet(), dll, "matrix", "--manifest", manifest_path, "--out", out, "--jobs", str(jobs)]
    if seeds:
        cmd += ["--seeds", str(seeds)]
    sh(cmd, env=env)
    m = read_json(os.path.join(out, "matrix.json"))
    if m.get("problems"):
        raise RuntimeError("%s: matrix problems: %s" % (out, m["problems"]))
    return m


def side_info(out, matrix):
    first = sorted(os.listdir(os.path.join(out, "runs")))[0]
    rj = read_json(os.path.join(out, "runs", first, "run.json"))
    return {"data_hash": rj.get("data_hash"), "core_commit": rj.get("core_commit"), "dirty": rj.get("dirty"),
            "build_commit": rj.get("build_commit"), "golden_set_hash": matrix.get("golden_set_hash")}


def is_dirty(porcelain):
    """Any change in src/, mods/ or lab/manifests counts, untracked files included (the SDK compiles every *.cs; mods are data)."""
    return any(line.strip() for line in porcelain.splitlines())


def git(*args):
    return subprocess.run(["git", "-C", REPO] + list(args), stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                          check=True).stdout.strip()


def prepare_base(sha, root):
    """git archive SHA into root/<sha>/tree (once); a marker file says the extraction finished."""
    tree = os.path.join(root, sha, "tree")
    marker = os.path.join(root, sha, "tree.ok")
    if not os.path.exists(marker):
        if os.path.exists(tree):
            shutil.rmtree(tree)
        os.makedirs(tree)
        arch = subprocess.Popen(["git", "-C", REPO, "archive", sha], stdout=subprocess.PIPE)
        subprocess.run(["tar", "-x", "-C", tree], stdin=arch.stdout, check=True)
        if arch.wait() != 0:
            raise RuntimeError("git archive failed: " + sha)
        write_text(marker, sha + "\n")
    return tree


def _sha(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def sha256_file(path):
    return _sha(path)


def main_run(a):
    t_start = time.time()
    manifest_path = os.path.abspath(a.manifest)
    manifest = read_json(manifest_path)
    cfg = pace_config(manifest)
    mhash = sha256_file(manifest_path)
    root = os.path.abspath(a.out)
    sha = git("rev-parse", "--verify", a.base + "^{commit}")
    head = git("rev-parse", "HEAD")
    dirty = is_dirty(git("status", "--porcelain", "--untracked-files=all", "--", "src", "mods", "lab/manifests"))
    print("pace-check: base %s, current tree %s (HEAD %s%s), seeds %s, jobs %d" % (
        sha[:10], REPO, head[:10], ", dirty" if dirty else "", a.seeds or "all", a.jobs), flush=True)

    # base: archive, same manifest copied into the base tree (data come from the tree of the manifest), isolated from git
    btree = prepare_base(sha, root)
    bman = os.path.join(btree, "lab", "manifests", "PACE-check.json")
    os.makedirs(os.path.dirname(bman), exist_ok=True)
    shutil.copyfile(manifest_path, bman)
    bout = os.path.join(root, sha, "run")
    stamp = os.path.join(root, sha, "run.stamp.json")
    want = {"manifest_sha256": mhash, "seeds": a.seeds}
    benv = dict(os.environ, GIT_CEILING_DIRECTORIES=os.path.dirname(btree))
    if a.reuse_base and os.path.exists(stamp) and read_json(stamp) == want and os.path.exists(os.path.join(bout, "matrix.json")):
        print("base: reusing " + bout, flush=True)
        bmatrix = read_json(os.path.join(bout, "matrix.json"))
    else:
        print("base: build and run", flush=True)
        bdll = build_lab(btree, benv, sha)
        if os.path.exists(stamp):
            os.remove(stamp)
        bmatrix = run_matrix(bdll, bman, bout, a.jobs, a.seeds, benv)
        write_text(stamp, json.dumps(want))

    print("current: build and run", flush=True)
    cdll = build_lab(REPO)
    bi = side_info(bout, bmatrix)
    if bi["build_commit"] != sha:
        raise RuntimeError("base build_commit %s != base %s (stale build? run without --reuse-base)" % (bi["build_commit"], sha))
    cout = os.path.join(root, "current", "run")
    cmatrix = run_matrix(cdll, manifest_path, cout, a.jobs, a.seeds)

    cfg_base = summarize(bout, btree, cfg)
    cfg_cur = summarize(cout, REPO, cfg)
    res = compare(cfg_base, cfg_cur, cfg)
    ci = side_info(cout, cmatrix)
    extra = {"manifest": "%s (sha256 %s)" % (os.path.relpath(manifest_path, REPO), mhash[:16]),
             "base": "%s (build_commit %s), data_hash %s, golden_set_hash %s" % (sha[:10], (bi["build_commit"] or "-")[:10],
                                                                         (bi["data_hash"] or "-")[:16], (bi["golden_set_hash"] or "-")[:16]),
             "current": "HEAD %s%s (build_commit %s), data_hash %s, golden_set_hash %s" % (
                 head[:10], " + uncommitted changes" if dirty else "", (ci["build_commit"] or "-")[:10],
                 (ci["data_hash"] or "-")[:16], (ci["golden_set_hash"] or "-")[:16]),
             "elapsed": "%.0f s" % (time.time() - t_start)}
    text = render(res, cfg, sha[:10], "current tree", cfg_base, cfg_cur, extra)
    print(text)
    with open(os.path.join(root, "current", "pace.md"), "w") as f:
        f.write(text)
    with open(os.path.join(root, "current", "pace.json"), "w") as f:
        json.dump(jsonable({"base": sha, "head": head, "dirty": dirty, "manifest_sha256": mhash, "seeds": a.seeds,
                            "base_info": bi, "current_info": ci, "base_summary": cfg_base, "current_summary": cfg_cur,
                            "rows": res["rows"], "verdict": res["verdict"]}), f, indent=1, sort_keys=True)
    return res["verdict"]


# ------------------------------------------------------------------ selftest (synthetic directories)
def _write_dir(d, rows_nt, rows_gt, n_runs, status="ok"):
    os.makedirs(d, exist_ok=True)
    with open(os.path.join(d, "runs.csv"), "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["run", "series", "point", "seed", "threads", "status", "golden_hash"])
        for i in range(n_runs):
            w.writerow(["p0-s%d-t1" % i, "base", 0, i, 1, status, "x"])
    with open(os.path.join(d, "node_times.csv"), "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["run", "actor", "node", "t_known", "t_first", "t_07", "t_mastered", "lic"])
        w.writerows(rows_nt)
    with open(os.path.join(d, "gen_times.csv"), "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["run", "actor", "gen", "t_req", "t_first_unit", "t_fielded", "cost_u100"])
        w.writerows(rows_gt)


NODES_YAML = """nodes:
  - id: a.g2
    number: 59
    type: APP
    gen: 2
  - id: a.g3
    number: 60
    type: APP
    gen: 3
  - id: e.g3
    number: 40
    type: ENG
    gen: 3
  - id: a.g4
    number: 61
    type: APP
    gen: 4
"""


def synthetic(root, shift=0, unit_shift=0):
    """5 runs; APP g known by USA at 100*(g-1)+i+shift (GBR 5 months earlier, except run 4: never); first unit of g at
    window centre + unit_shift (run 4: never)."""
    tree = os.path.join(root, "tree")
    os.makedirs(os.path.join(tree, "mods", "core"), exist_ok=True)
    write_text(os.path.join(tree, "mods", "core", "nodes.yaml"), NODES_YAML)
    nt, gt = [], []
    for i in range(5):
        rid = "p0-s%d-t1" % i
        for g, node in ((2, 59), (3, 60), (4, 61)):
            t = 100 * (g - 1) + i + shift
            nt.append([rid, "USA", node, t, "", "", "", 0])
            nt.append([rid, "GBR", node, "" if i == 4 else t - 5, "", "", "", 0])
            lo, hi = DEFAULT_PACE["windows"][str(g)]
            gt.append([rid, "USA", g, 0, "" if i == 4 else (lo + hi) // 2 + unit_shift, "", ""])
        nt.append([rid, "USA", 40, -1, "", "", "", 0])
    _write_dir(os.path.join(root, "run"), nt, gt, 5)
    return os.path.join(root, "run"), tree


def selftest():
    cfg = pace_config({})
    with tempfile.TemporaryDirectory() as tmp:
        bd, bt = synthetic(os.path.join(tmp, "b"))
        base = summarize(bd, bt, cfg)
        assert base["n"] == 5 and base["app_nodes"] == {2: 59, 3: 60, 4: 61}
        assert base["gens"][2]["app_usa"] == 102 and base["gens"][2]["app_any"] == 97, base["gens"][2]
        assert base["gens"][4]["in_window"] == 4 and base["gens"][4]["unit"] == 309, base["gens"][4]
        same = compare(base, base, cfg)
        assert same["verdict"] == "PASS"
        cd, ct = synthetic(os.path.join(tmp, "c"), shift=30)
        res = compare(base, summarize(cd, ct, cfg), cfg)
        assert res["verdict"] == "WARN" and all("app_usa moved +30" in " ".join(r["reasons"]) for r in res["rows"]), res
        cd, ct = synthetic(os.path.join(tmp, "d"), shift=24)
        assert compare(base, summarize(cd, ct, cfg), cfg)["verdict"] == "PASS"
        cd, ct = synthetic(os.path.join(tmp, "e"), unit_shift=100)
        res = compare(base, summarize(cd, ct, cfg), cfg)
        assert res["verdict"] == "WARN" and any("in-window share" in x for r in res["rows"] for x in r["reasons"]), res
    assert month_label(0) == "0 (1945-09)" and month_label(4) == "4 (1946-01)" and month_label(INF) == "inf"
    assert fmt_delta(_delta(INF, 300)) == "+inf" and _delta(INF, INF) == 0
    print("pace_check selftest ok")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--base", help="base commit (sha, tag or branch)")
    ap.add_argument("--manifest", default=os.path.join(REPO, "lab", "manifests", "PACE-check.json"))
    ap.add_argument("--seeds", type=int, default=0, help="first N seeds of the manifest (default: all)")
    ap.add_argument("--jobs", type=int, default=8)
    ap.add_argument("--out", default=os.path.join(REPO, "lab", "results", "pace"))
    ap.add_argument("--reuse-base", action="store_true", help="reuse the base run if manifest and seeds are the same")
    ap.add_argument("--strict", action="store_true", help="exit 3 on WARN")
    ap.add_argument("--analyze", nargs=4, metavar=("BASE_RUN", "BASE_TREE", "CUR_RUN", "CUR_TREE"))
    ap.add_argument("--selftest", action="store_true")
    a = ap.parse_args()
    if a.selftest:
        selftest()
        return 0
    try:
        if a.analyze:
            cfg = pace_config(read_json(a.manifest))
            b = summarize(a.analyze[0], a.analyze[1], cfg)
            c = summarize(a.analyze[2], a.analyze[3], cfg)
            res = compare(b, c, cfg)
            print(render(res, cfg, a.analyze[0], a.analyze[2], b, c))
            verdict = res["verdict"]
        elif a.base:
            verdict = main_run(a)
        else:
            ap.error("--base, --analyze or --selftest")
    except (RuntimeError, ValueError, OSError, subprocess.CalledProcessError) as e:
        sys.stderr.write("pace-check failed: %s\n" % e)
        return 1
    return 3 if a.strict and verdict == "WARN" else 0


if __name__ == "__main__":
    sys.exit(main())
