#!/usr/bin/env python3
"""Release model (owner #594): release candidate -> integral check -> owner approval -> release -> rollback.

  python3 tools/development/release.py rc                        tag rc-YYYY-MM-DD-N on main, write docs/releases/rc-....md
  python3 tools/development/release.py check [RC] [--pace-base SHA] [--no-ci-dispatch]
  python3 tools/development/release.py approve RC --owner-ref "#NNN"
  python3 tools/development/release.py release vX.N              build the client of an existing release tag into the releases folder
  python3 tools/development/release.py run vX.N [--exec] [ARGS]  print the launch command of a built release (--exec starts it)
  python3 tools/development/release.py reject RC --reason "..."  record the rejected RC in the error catalogue
  python3 tools/development/release.py rollback [--reason "..."] print the previous release launch and the revert list

Rules: history is never rewritten; this tool creates tags locally and never pushes (the Orchestrator pushes and commits
docs/releases/*.md and development/errors.yaml). It never starts the game client by itself: shots are only printed (owner permit,
client_permit.py), and run starts the client only with an explicit --exec typed by a person.
check runs everything on the RC commit in a temporary worktree: verify --full, CI of three OSes, pace_check, the Lab series
(TASK-0187 manifests, L2) with slot_core gates, the client build. approve needs every required row PASS on that commit; the client
is built into ~/singularity-releases/vX.N (SINGULARITY_RELEASES overrides; outside git) BEFORE the tag exists, and the tag is
created only after a successful build.
Exit codes: 0 ok, 1 a check failed or a precondition is not met, 2 usage.
"""
from __future__ import annotations

import argparse
import datetime
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

import yaml

sys.path.insert(0, str(Path(__file__).resolve().parent))
import client_permit  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
CLIENT_DIR = "src/Singularity.Client.Spike"
MAIN = "main"
OS_JOBS = ("ubuntu-latest", "windows-latest", "macos-latest")
CHECKS_START, CHECKS_END = "<!-- checks:start -->", "<!-- checks:end -->"
DONE_RE = re.compile(r"chore\(registry\): (TASK-\d{4}) DONE")
TASK_RE = re.compile(r"TASK-\d{4}")


class ReleaseError(Exception):
    pass


def git(root: Path, *args: str, check: bool = True) -> str:
    r = subprocess.run(["git", *args], cwd=root, text=True, capture_output=True)
    if check and r.returncode != 0:
        raise ReleaseError(f"git {' '.join(args)}: {r.stderr.strip() or r.stdout.strip()}")
    return r.stdout.strip()


# ---------------------------------------------------------------- tags and period

def version_key(tag: str) -> tuple[int, ...]:
    return tuple(int(x) for x in re.findall(r"\d+", tag))


def release_tags(root: Path) -> list[str]:
    """v* tags that are plain versions, oldest first."""
    tags = [t for t in git(root, "tag", "--list", "v*").splitlines() if re.fullmatch(r"v\d+(\.\d+)*", t)]
    return sorted(tags, key=version_key)


def last_release_tag(root: Path, before: str | None = None) -> str | None:
    tags = release_tags(root)
    if before in tags:
        tags = tags[: tags.index(before)]
    return tags[-1] if tags else None


def next_release_tag(root: Path) -> str:
    tags = release_tags(root)
    n = max([version_key(t)[1] for t in tags if len(version_key(t)) > 1] + [0]) + 1
    return f"v0.{n}"


def next_rc_tag(root: Path, day: str) -> str:
    taken = [t for t in git(root, "tag", "--list", f"rc-{day}-*").splitlines()]
    n = max([int(t.rsplit("-", 1)[1]) for t in taken if t.rsplit("-", 1)[1].isdigit()] + [0]) + 1
    return f"rc-{day}-{n}"


def task_title(root: Path, task_id: str) -> str:
    for p in sorted((root / "development" / "tasks").glob(f"{task_id}-*.md")):
        m = re.match(r"\A---\n(.*?)\n---", p.read_text(encoding="utf-8"), re.S)
        if m:
            meta = yaml.safe_load(m.group(1)) or {}
            return str(meta.get("title", ""))
    return ""


def status_at(root: Path, ref: str, path: str) -> str:
    text = git(root, "show", f"{ref}:{path}", check=False)
    m = re.search(r"^status:\s*(\w+)", text, re.M)
    return m.group(1) if m else ""


def done_tasks(root: Path, base: str | None, head: str = "HEAD") -> list[str]:
    """Task ids that went DONE in base..head: registry commits in landing order, then task files whose status became DONE."""
    span = f"{base}..{head}" if base else head
    seen: list[str] = []
    for subject in reversed(git(root, "log", "--format=%s", span).splitlines()):
        m = DONE_RE.search(subject)
        if m and m.group(1) not in seen:
            seen.append(m.group(1))
    if base:
        files = git(root, "diff", "--name-only", base, head, "--", "development/tasks").splitlines()
    else:
        files = git(root, "ls-tree", "-r", "--name-only", head, "development/tasks").splitlines()
    for f in sorted(files):
        m = TASK_RE.match(Path(f).name)
        if m and m.group(0) not in seen and status_at(root, head, f) == "DONE" and (not base or status_at(root, base, f) != "DONE"):
            seen.append(m.group(0))
    return seen


def merge_commits(root: Path, base: str | None, head: str, tasks: list[str]) -> list[tuple[str, str]]:
    """(sha, subject) of the merge commits that landed the tasks, newest first (the revert order)."""
    span = f"{base}..{head}" if base else head
    out = []
    for line in git(root, "log", "--merges", "--first-parent", "--format=%h %s", span).splitlines():
        sha, _, subject = line.partition(" ")
        if any(t in subject for t in tasks):
            out.append((sha, subject))
    return out


def open_debts(root: Path, base: str | None, head: str = "HEAD") -> list[str]:
    """Debt rows still open that were added in base..head (all open rows when there is no base)."""
    path = "development/TECH_DEBT.md"
    if base:
        text = git(root, "diff", "--unified=0", f"{base}..{head}", "--", path, check=False)
        rows = [l[1:] for l in text.splitlines() if l.startswith("+| DEBT-")]
    else:
        f = root / path
        rows = [l for l in f.read_text(encoding="utf-8").splitlines() if l.startswith("| DEBT-")] if f.exists() else []
    out = []
    for row in rows:
        cells = [c.strip() for c in row.strip().strip("|").split("|")]
        if len(cells) >= 6 and cells[-1].startswith("открыт"):
            out.append(f"{cells[0]}: {cells[1]}")
    return out


# ---------------------------------------------------------------- error catalogue

def record_error(root: Path, kind: str, area: str, symptom: str, cause: str) -> str:
    """Add an entry to the error catalogue through errors.py; a failure is an error of the release."""
    tool = root / "tools" / "development" / "errors.py"
    if not tool.exists():
        raise ReleaseError("errors.py is missing: the error was not recorded")
    r = subprocess.run([sys.executable, str(tool), "add", "--executor", "tool:release", "--kind", kind,
                        "--area", area, "--symptom", symptom, "--cause", cause, "--found-by", "self"],
                       cwd=root, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    out = r.stdout.strip()
    if r.returncode != 0:
        raise ReleaseError(f"errors.py add failed ({r.returncode}): {out[-300:]}")
    return out or "errors.py add: ok"


# ---------------------------------------------------------------- rc

DEFECTS_START, DEFECTS_END = "<!-- defects:start -->", "<!-- defects:end -->"
ERR_RE = re.compile(r"ERR-\d{4}")
VERSION_RE = re.compile(r"v\d+\.\d+")
STATUS_PENDING = "- Статус: НА ПРОВЕРКЕ"
COMMIT_LINE = "Проверено на коммите: "
# Every row must be PASS on the RC commit before approve (a missing, SKIP, WARN or PENDING row refuses).
REQUIRED_CHECKS = ("verify --full", "CI three OS", "Lab pace_check", "Lab slot_core gates", "L2 determinism (Lab)", "Client build (Godot)")


def period_metrics(root: Path) -> str:
    tool = root / "tools" / "development" / "metrics.py"
    if not tool.exists():
        return "metrics.py не найден (TASK-0206 не влита): метрики периода не собраны."
    try:
        r = subprocess.run([sys.executable, str(tool), "report"], cwd=root, text=True, stdout=subprocess.PIPE,
                           stderr=subprocess.STDOUT, timeout=300)
    except (subprocess.TimeoutExpired, OSError) as e:
        return f"metrics.py report не выполнен: {type(e).__name__}"
    out = r.stdout.strip()
    if r.returncode != 0:
        return f"metrics.py report не выполнен (код {r.returncode}): " + " / ".join(out.splitlines()[-2:])[:200]
    return "\n".join(["~~~", out[:4000], "~~~"])


def render_rc(tag: str, sha: str, base: str | None, tasks: list[tuple[str, str]], debts: list[str], metrics: str = "") -> str:
    lines = [f"# Кандидат в релиз {tag}", "",
             f"- Коммит: {sha}", f"- Прошлый релиз: {base or 'нет (первый)'}",
             STATUS_PENDING, "",
             f"## Что вошло ({len(tasks)} задач DONE)"]
    lines += [f"- {i}: {t}" if t else f"- {i}" for i, t in tasks] or ["- Задач DONE с прошлого релиза нет."]
    lines += ["", "## Долги, открытые за период"]
    lines += [f"- {d}" for d in debts] or ["- Новых открытых долгов нет."]
    lines += ["", "## Метрики периода", metrics or "не собраны.",
              "", "## Проверки", CHECKS_START, "Проверки ещё не запускались: python3 tools/development/release.py check", CHECKS_END,
              "", "## Найденные дефекты (записи каталога ошибок)", DEFECTS_START, "- нет", DEFECTS_END, ""]
    return "\n".join(lines)


def add_defects(report: Path, entries: list[str]) -> None:
    """Append lines to the defects block of an RC report (the block is created when missing)."""
    if not entries:
        return
    text = report.read_text(encoding="utf-8")
    if DEFECTS_START not in text:
        text += f"\n## Найденные дефекты (записи каталога ошибок)\n{DEFECTS_START}\n{DEFECTS_END}\n"
    a, b = text.index(DEFECTS_START) + len(DEFECTS_START), text.index(DEFECTS_END)
    kept = [l for l in text[a:b].strip().splitlines() if l.strip() and l.strip() != "- нет"]
    report.write_text(text[:a] + "\n" + "\n".join(kept + entries) + "\n" + text[b:], encoding="utf-8")


def cmd_rc(root: Path, branch: str = MAIN, day: str | None = None) -> str:
    if git(root, "rev-parse", "--abbrev-ref", "HEAD") != branch:
        raise ReleaseError(f"RC is cut from {branch}; current branch differs")
    if git(root, "status", "--porcelain", "--untracked-files=no"):
        raise ReleaseError("working tree has uncommitted changes")
    tag = next_rc_tag(root, day or datetime.date.today().isoformat())
    base = last_release_tag(root)
    sha = git(root, "rev-parse", "--short", "HEAD")
    ids = done_tasks(root, base)
    report = root / "docs" / "releases" / f"{tag}.md"
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(render_rc(tag, sha, base, [(i, task_title(root, i)) for i in ids], open_debts(root, base),
                                period_metrics(root)), encoding="utf-8")
    git(root, "tag", "-a", tag, "-m", f"release candidate {tag}")
    return tag


# ---------------------------------------------------------------- checks

def ci_os_verdict(jobs: list[dict]) -> tuple[str, str]:
    """Verdict of the three OS dotnet jobs of the newest full run. jobs: [{name, status, conclusion}] (gh run view)."""
    state: dict[str, str] = {}
    for os_name in OS_JOBS:
        job = next((j for j in jobs if j.get("name", "") == f"dotnet ({os_name})"), None)
        if job is None:
            state[os_name] = "missing"
        elif job.get("status") != "completed":
            state[os_name] = "running"
        else:
            state[os_name] = "ok" if job.get("conclusion") == "success" else "failed"
    detail = ", ".join(f"{k.split('-')[0]}: {v}" for k, v in state.items())
    if "failed" in state.values():
        return "FAIL", detail
    if all(v == "ok" for v in state.values()):
        return "PASS", detail
    return "PENDING", detail


def gh_json(root: Path, *args: str):
    r = subprocess.run(["gh", *args], cwd=root, text=True, capture_output=True)
    if r.returncode != 0:
        raise ReleaseError(r.stderr.strip() or "gh failed")
    return json.loads(r.stdout or "null")


def check_ci(root: Path, tag: str, sha: str, dispatch: bool) -> tuple[str, str]:
    if not shutil.which("gh"):
        return "SKIP", "gh not installed"
    try:
        runs = gh_json(root, "run", "list", "--workflow", "ci.yml", "--commit", sha, "--limit", "20",
                       "--json", "databaseId,event,status,conclusion")
        full = sorted((r for r in runs if r["event"] in ("schedule", "workflow_dispatch")), key=lambda r: -r["databaseId"])
        if full:
            jobs = gh_json(root, "run", "view", str(full[0]["databaseId"]), "--json", "jobs")["jobs"]
            return ci_os_verdict(jobs)
        if not dispatch:
            return "PENDING", "no three-OS run on this commit; dispatch skipped"
        r = subprocess.run(["gh", "workflow", "run", "ci.yml", "--ref", tag], cwd=root, text=True, capture_output=True)
        if r.returncode != 0:
            return "PENDING", f"no three-OS run; dispatch failed (push the tag {tag} first): {r.stderr.strip()[:120]}"
        return "PENDING", "three-OS run dispatched; repeat check when it finishes"
    except (ReleaseError, KeyError, ValueError) as e:
        return "SKIP", f"gh query failed: {e}"


def run_cmd(cmd: list[str], cwd: Path, timeout: int) -> tuple[int, str]:
    """(exit code, whole output); -1 on a timeout or a missing program."""
    try:
        r = subprocess.run(cmd, cwd=cwd, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=timeout)
    except (subprocess.TimeoutExpired, OSError) as e:
        return -1, type(e).__name__
    return r.returncode, r.stdout


def tail_of(out: str, n: int = 3) -> str:
    return " / ".join([l for l in out.splitlines() if l.strip()][-n:])[:200]


def godot_path() -> Path:
    return Path(os.environ.get("GODOT") or Path.home() / ".local/opt/godot/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64")


GODOT_TIMEOUT_S = 1800


def run_godot(cmd: list[str], cwd: Path, env: dict) -> tuple[bool, str]:
    try:
        r = subprocess.run(cmd, cwd=cwd, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=GODOT_TIMEOUT_S)
    except subprocess.TimeoutExpired:
        return False, f"timeout after {GODOT_TIMEOUT_S} s"
    except OSError as e:
        return False, str(e)
    return r.returncode == 0, " / ".join(r.stdout.splitlines()[-2:])[:200]


def build_client(tree: Path, out: Path | None) -> tuple[str, str]:
    """Build (and export when presets and templates exist) the Godot client of a tree. Never runs the game."""
    godot = godot_path()
    if not godot.exists():
        return "SKIP", f"Godot not found at {godot}"
    project = tree / CLIENT_DIR
    env = dict(os.environ, PATH=f"{Path.home() / '.dotnet'}:{os.environ.get('PATH', '')}")
    ok, d = run_godot([str(godot), "--headless", "--path", str(project), "--build-solutions", "--quit"], project, env)
    if not ok:
        return "FAIL", "build-solutions: " + d
    templates = Path.home() / ".local/share/godot/export_templates"
    if out is not None and (project / "export_presets.cfg").exists() and templates.is_dir():
        out.mkdir(parents=True, exist_ok=True)
        ok, d = run_godot([str(godot), "--headless", "--path", str(project), "--export-release", "Linux", str(out / "singularity")], project, env)
        return ("PASS", "built and exported") if ok else ("FAIL", "export: " + d)
    return "PASS", "built (no export presets or templates: the release runs from the source snapshot)"


def shot_command(rc: str, sha: str) -> str:
    """Printed only. The check worktree is gone; shots run in a fresh worktree of the RC tag, with the owner's permit."""
    return (f"git worktree add --detach ../shots-{rc} {rc}   # commit {sha[:12]}; then, after client_permit.py grant: "
            f"(cd ../shots-{rc} && ./run-globe-spike.sh --shots --only <frame mask>)")


# Lab series of the check (TASK-0187 manifests, slot-model-pilot.md section 7); L2 is the core manifest with threads 1 and 2.
LAB_CORE = "lab/manifests/TASK-0187-core.json"
LAB_PLAYER = "lab/manifests/TASK-0187-player.json"
LAB_L2 = "lab/manifests/RC-L2.json"   # generated in the check tree (untracked, outside src/: the run stays clean)


def dotnet_bin() -> str:
    return shutil.which("dotnet") or str(Path.home() / ".dotnet" / "dotnet")


def run_lab_matrix(tree: Path, manifest: str, out: Path) -> tuple[bool, str]:
    dll = tree / "src/Singularity.Lab/bin/Release/net10.0/Singularity.Lab.dll"
    if not dll.exists():
        return False, "Lab is not built in the RC tree (verify build step failed?)"
    code, text = run_cmd(["nice", "-n", "10", dotnet_bin(), str(dll), "matrix", "--manifest", manifest, "--out", str(out), "--jobs", "6"], tree, 3600)
    return code == 0, tail_of(text)


def lab_provenance(out: Path, sha: str) -> str | None:
    """None when every run of a matrix directory was made by a clean build of sha, else the reason (results of another commit)."""
    runs = sorted((out / "runs").glob("*/run.json"))
    if not runs:
        return f"{out.name}: no runs"
    for p in runs:
        try:
            j = json.loads(p.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return f"{p.parent.name}: unreadable run.json"
        if j.get("core_commit") != sha:
            return f"{p.parent.name}: core_commit {str(j.get('core_commit'))[:12]} is not the RC commit {sha[:12]}"
        if j.get("dirty") or j.get("dirty_untracked_src"):
            return f"{p.parent.name}: built from a dirty tree"
    return None


def slot_core_rows(tree: Path, dirs: list[Path], l2: Path) -> list[tuple[str, str, str]]:
    code, text = run_cmd([sys.executable, "lab/analysis/slot_core.py", *map(str, dirs), "--l2", str(l2)], tree, 600)
    fails = [l.split() for l in text.splitlines() if l.strip().startswith("FAIL")]
    det = [l.split() for l in text.splitlines() if re.match(r"\s*(PASS|FAIL) DET\b", l)]
    gate_fails = [f"{f[1]} {f[2] if len(f) > 2 else ''}".strip() for f in fails if len(f) > 1 and f[1] != "DET"]
    if code != 0 and not fails:
        return [("Lab slot_core gates", "FAIL", f"slot_core exit {code}: {tail_of(text)}"), ("L2 determinism (Lab)", "FAIL", "slot_core did not finish")]
    gates = ("FAIL", "failed gates: " + ", ".join(gate_fails)[:150]) if gate_fails else ("PASS", "all gates PASS")
    l2s = ("PASS", "DET PASS") if det and det[0][0] == "PASS" else ("FAIL", "DET missing or FAIL")
    return [("Lab slot_core gates", *gates), ("L2 determinism (Lab)", *l2s)]


def lab_checks(tree: Path, work: Path, sha: str, run_matrix=run_lab_matrix) -> list[tuple[str, str, str]]:
    """Run the Lab series from the RC tree and gate them; every run must carry the RC commit. run_matrix is injectable (tests)."""
    manifest = json.loads((tree / LAB_CORE).read_text(encoding="utf-8")) if (tree / LAB_CORE).exists() else None
    if manifest is None or not (tree / LAB_PLAYER).exists():
        return [("Lab slot_core gates", "FAIL", "TASK-0187 manifests missing in the RC tree"), ("L2 determinism (Lab)", "FAIL", "manifests missing")]
    manifest.update(threads=[1, 2], seeds={"start": 1, "count": 2}, series=manifest["series"][:1])
    (tree / LAB_L2).write_text(json.dumps(manifest, indent=1), encoding="utf-8")
    outs = {"core": (LAB_CORE, work / "lab-core"), "player": (LAB_PLAYER, work / "lab-player"), "l2": (LAB_L2, work / "lab-l2")}
    for name, (mf, out) in outs.items():
        ok, d = run_matrix(tree, mf, out)
        why = None if not ok else lab_provenance(out, sha)
        if not ok or why:
            msg = f"matrix {name}: {d if not ok else why}"
            return [("Lab slot_core gates", "FAIL", msg), ("L2 determinism (Lab)", "FAIL", msg)]
    rows = slot_core_rows(tree, [outs["core"][1], outs["player"][1]], outs["l2"][1])
    return [(n, s, f"{d} (commit {sha[:12]})") for n, s, d in rows]


def pace_row(tree: Path, base: str | None) -> tuple[str, str, str]:
    if not base:
        return ("Lab pace_check", "SKIP", "no previous release tag: pass --pace-base SHA")
    code, text = run_cmd([sys.executable, "lab/analysis/pace_check.py", "--base", base, "--strict"], tree, 3600)
    status = {0: "PASS", 3: "WARN"}.get(code, "FAIL")
    return ("Lab pace_check", status, f"base {base}: {tail_of(text)}")


def render_checks(rows: list[tuple[str, str, str]], sha: str = "") -> str:
    out = [COMMIT_LINE + sha, "", "| Проверка | Итог | Детали |", "|---|---|---|"] if sha else ["| Проверка | Итог | Детали |", "|---|---|---|"]
    out += [f"| {n} | {s} | {d.replace('|', '/')} |" for n, s, d in rows]
    return "\n".join(out)


def write_checks(report: Path, rows: list[tuple[str, str, str]], sha: str = "") -> None:
    text = report.read_text(encoding="utf-8")
    block = f"{CHECKS_START}\n{render_checks(rows, sha)}\n{CHECKS_END}"
    if CHECKS_START in text and CHECKS_END in text:
        a, b = text.index(CHECKS_START), text.index(CHECKS_END) + len(CHECKS_END)
        text = text[:a] + block + text[b:]
    else:
        text += "\n## Проверки\n" + block + "\n"
    report.write_text(text, encoding="utf-8")


def checks_block(report: Path) -> str:
    text = report.read_text(encoding="utf-8")
    if CHECKS_START not in text or CHECKS_END not in text:
        return ""
    return text[text.index(CHECKS_START):text.index(CHECKS_END)]


def parse_checks(report: Path) -> dict[str, str]:
    res = {}
    for line in checks_block(report).splitlines():
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if line.startswith("|") and len(cells) >= 2 and cells[1] in ("PASS", "FAIL", "SKIP", "PENDING", "WARN"):
            res[cells[0]] = cells[1]
    return res


def checked_commit(report: Path) -> str:
    for line in checks_block(report).splitlines():
        if line.startswith(COMMIT_LINE):
            return line[len(COMMIT_LINE):].strip()
    return ""


def cmd_check(root: Path, rc: str | None, pace_base: str | None, dispatch: bool) -> int:
    rcs = sorted(git(root, "tag", "--list", "rc-*").splitlines(), key=lambda t: (t.split("-")[1:4], version_key(t)))
    rc = rc or (rcs[-1] if rcs else None)
    if not rc:
        raise ReleaseError("no RC tag: run release.py rc first")
    sha = git(root, "rev-parse", f"{rc}^{{commit}}")
    report = root / "docs" / "releases" / f"{rc}.md"
    if not report.exists():
        raise ReleaseError(f"{report} missing")
    rows: list[tuple[str, str, str]] = []
    work = Path(tempfile.mkdtemp(prefix="rc-check-"))
    tree = work / "tree"
    try:
        git(root, "worktree", "add", "--detach", str(tree), sha)
        code, text = run_cmd([sys.executable, "tools/development/verify.py", "--full"], tree, 3600)
        rows.append(("verify --full", "PASS" if code == 0 else "FAIL", tail_of(text)))
        rows.append(("CI three OS", *check_ci(root, rc, sha, dispatch)))
        rows.append(pace_row(tree, pace_base or last_release_tag(root)))
        if code == 0:
            rows += lab_checks(tree, work, sha)
        else:
            why = "verify failed: the Lab is not built from this commit"
            rows += [("Lab slot_core gates", "FAIL", why), ("L2 determinism (Lab)", "FAIL", why)]
        rows.append(("Client build (Godot)", *build_client(tree, None)))
    finally:
        subprocess.run(["git", "worktree", "remove", "--force", str(tree)], cwd=root, capture_output=True)
        shutil.rmtree(work, ignore_errors=True)
    rows.append(("Shot batch", "PENDING", shot_command(rc, sha)))
    write_checks(report, rows, sha)
    print(render_checks(rows, sha))
    bad = [(n, s) for n, s, _ in rows if s in ("FAIL", "WARN")]
    defects = []
    for n, s in bad:
        out = record_error(root, "process", "release", f"RC check '{n}' {s}", f"{rc} ({sha[:12]})")
        print(out)
        defects.append(f"- {', '.join(ERR_RE.findall(out)) or 'ERR-?'}: {n} {s}")
    add_defects(report, defects)
    return 1 if any(s == "FAIL" for _, s in bad) else 0


# ---------------------------------------------------------------- approve, release, run, reject, rollback

def releases_dir() -> Path:
    return Path(os.environ.get("SINGULARITY_RELEASES") or Path.home() / "singularity-releases")


def release_dir(tag: str) -> Path:
    """The only path a release is ever written to: <releases>/vX.N, validated and resolved. Never built from other input."""
    if not VERSION_RE.fullmatch(tag):
        raise ReleaseError(f"{tag!r} is not a release version (vN.N)")
    base = releases_dir()
    base.mkdir(parents=True, exist_ok=True)
    return Path(os.path.realpath(base)) / tag


def render_notes(tag: str, rc: str, tasks: list[tuple[str, str]], owner_ref: str) -> str:
    lines = [f"# Релиз {tag}", "", f"Собран из кандидата {rc}; одобрен владельцем ({owner_ref}).", "",
             "## Что нового"]
    lines += [f"- {t or i}" for i, t in tasks] or ["- Изменений по задачам нет."]
    lines += ["", "## Как запустить", f"    python3 tools/development/release.py run {tag} --exec", "",
              "## Если что-то сломалось", "Запустите прошлый релиз: python3 tools/development/release.py rollback", ""]
    return "\n".join(lines)


def deploy(root: Path, tag: str, sha: str) -> tuple[str, str]:
    """Snapshot of sha (git archive) + client build into <releases>/<tag>, via a staging directory made here and renamed.
    An existing or symlinked destination is refused, nothing is deleted except the staging directory of this call."""
    dest = release_dir(tag)
    if os.path.lexists(dest):
        return "FAIL", f"{dest} already exists (not deleting it: remove it by hand)"
    stage = Path(tempfile.mkdtemp(prefix=".stage-", dir=dest.parent))
    try:
        src = stage / "src"
        src.mkdir()
        arch = subprocess.run(["git", "archive", sha], cwd=root, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        if arch.returncode != 0:
            return "FAIL", "git archive: " + arch.stderr.decode(errors="replace").strip()[-200:]
        tar = subprocess.run(["tar", "-x", "-C", str(src)], input=arch.stdout, stderr=subprocess.PIPE)
        if tar.returncode != 0:
            return "FAIL", "tar: " + tar.stderr.decode(errors="replace").strip()[-200:]
        status, detail = build_client(src, stage / "bin")
        if status != "PASS":
            return "FAIL", f"client build {status}: {detail}"
        (stage / "RELEASE").write_text(f"{tag} {sha}\n", encoding="utf-8")
        os.rename(stage, dest)
        return "PASS", f"{dest}: {detail}"
    except OSError as e:
        return "FAIL", f"{dest}: {e}"
    finally:
        if stage.exists():
            shutil.rmtree(stage, ignore_errors=True)


def cmd_approve(root: Path, rc: str, owner_ref: str) -> str:
    if not owner_ref.strip():
        raise ReleaseError("--owner-ref is required: the release is approved by the owner's answer")
    sha = git(root, "rev-parse", f"{rc}^{{commit}}")
    report = root / "docs" / "releases" / f"{rc}.md"
    if not report.exists() or STATUS_PENDING not in report.read_text(encoding="utf-8"):
        raise ReleaseError("the RC report is missing or its status is not НА ПРОВЕРКЕ (already approved or rejected)")
    checks = parse_checks(report)
    not_pass = [f"{n}: {checks.get(n, 'missing')}" for n in REQUIRED_CHECKS if checks.get(n) != "PASS"]
    if not_pass:
        raise ReleaseError("required checks are not PASS: " + "; ".join(not_pass))
    if checked_commit(report) != sha:
        raise ReleaseError(f"the checks were made on {checked_commit(report) or 'an unknown commit'}, the RC is {sha}: run release.py check")
    if git(root, "tag", "--points-at", sha, "--list", "v*"):
        raise ReleaseError("this commit already has a release tag")
    tag = next_release_tag(root)
    base = last_release_tag(root)
    tasks = [(i, task_title(root, i)) for i in done_tasks(root, base, sha)]
    status, detail = deploy(root, tag, sha)   # the tag is created only after a successful build
    if status != "PASS":
        out = record_error(root, "tooling", "release", f"release {tag} client build failed", f"{rc}: {detail}"[:300])
        add_defects(report, [f"- {', '.join(ERR_RE.findall(out)) or 'ERR-?'}: build of {tag} failed"])
        raise ReleaseError(f"deploy failed, no tag created: {detail}")
    try:
        git(root, "tag", "-a", tag, sha, "-m", f"release {tag} from {rc}")
    except ReleaseError:
        shutil.rmtree(release_dir(tag), ignore_errors=True)   # our own validated path: the build of a tag that was not made
        raise
    (root / "docs" / "releases" / f"{tag}.md").write_text(render_notes(tag, rc, tasks, owner_ref), encoding="utf-8")
    text = report.read_text(encoding="utf-8").replace(STATUS_PENDING, f"- Статус: ОДОБРЕН как {tag} ({owner_ref})")
    report.write_text(text, encoding="utf-8")
    print(f"deployed: {detail}")
    return tag


def cmd_release(root: Path, tag: str) -> str:
    """Build the client of an existing release tag (a repeat after a failed or lost build)."""
    release_dir(tag)
    if tag not in release_tags(root):
        raise ReleaseError(f"no release tag {tag}")
    status, detail = deploy(root, tag, git(root, "rev-parse", f"{tag}^{{commit}}"))
    if status != "PASS":
        raise ReleaseError(detail)
    return detail


def cmd_run(tag: str, args: list[str]) -> list[str]:
    """Launch command of a built release (printed; started only by main with --exec)."""
    dest = release_dir(tag)
    if dest.is_symlink() or not dest.is_dir():
        raise ReleaseError(f"no build of {tag} in {dest}: python3 tools/development/release.py release {tag}")
    for rel in ("bin/singularity", "src/run-globe-spike.sh"):
        if (dest / rel).is_file():
            return [str(dest / rel), *args]
    raise ReleaseError(f"{dest} holds no launcher")


def permit_live() -> bool:
    """True while the owner's launch permit (client_permit.py grant) is alive: the same file guard_shots reads."""
    try:
        return float(client_permit.PERMIT.read_text()) > time.time()
    except (OSError, ValueError):
        return False


def cmd_reject(root: Path, rc: str, reason: str) -> str:
    git(root, "rev-parse", f"{rc}^{{commit}}")
    report = root / "docs" / "releases" / f"{rc}.md"
    out = record_error(root, "process", "release", f"RC rejected: {reason}"[:200], rc)
    if report.exists():
        text = report.read_text(encoding="utf-8").replace(STATUS_PENDING, f"- Статус: ОТКЛОНЁН ({reason})")
        report.write_text(text, encoding="utf-8")
        add_defects(report, [f"- {', '.join(ERR_RE.findall(out)) or 'ERR-?'}: {reason}"])
    return out


def cmd_rollback(root: Path, reason: str | None) -> list[str]:
    tags = release_tags(root)
    if len(tags) < 2:
        raise ReleaseError("rollback needs two release tags (current and previous)")
    cur, prev = tags[-1], tags[-2]
    ids = done_tasks(root, prev, cur)
    lines = [f"Current release {cur}; previous {prev}.",
             f"Run the previous release now: python3 tools/development/release.py run {prev} --exec",
             f"Fix on {MAIN} (history is not rewritten): revert the task merges of {cur}, newest first (printed, not run):"]
    lines += [f"  git revert -m 1 {sha}   # {subject}" for sha, subject in merge_commits(root, prev, cur, ids)]
    if reason:
        lines.append(record_error(root, "process", "release", f"release rollback: {reason}"[:200], cur))
    return lines


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("rc")
    c = sub.add_parser("check")
    c.add_argument("rc", nargs="?")
    c.add_argument("--pace-base")
    c.add_argument("--no-ci-dispatch", action="store_true")
    a = sub.add_parser("approve")
    a.add_argument("rc")
    a.add_argument("--owner-ref", default="")
    r = sub.add_parser("release")
    r.add_argument("tag")
    u = sub.add_parser("run")
    u.add_argument("tag")
    u.add_argument("--exec", action="store_true", dest="do_exec", help="start the client (otherwise only print the command)")
    u.add_argument("args", nargs="*")
    j = sub.add_parser("reject")
    j.add_argument("rc")
    j.add_argument("--reason", required=True)
    b = sub.add_parser("rollback")
    b.add_argument("--reason")
    args = ap.parse_args(argv)
    commit = "Reminder: commit docs/releases/{} and development/errors.yaml (the Orchestrator commits them)"
    try:
        if args.cmd == "rc":
            tag = cmd_rc(ROOT)
            print(tag)
            print(commit.format(f"{tag}.md"))
        elif args.cmd == "check":
            code = cmd_check(ROOT, args.rc, args.pace_base, not args.no_ci_dispatch)
            print(commit.format("<rc>.md"))
            return code
        elif args.cmd == "approve":
            tag = cmd_approve(ROOT, args.rc, args.owner_ref)
            print(tag)
            print(commit.format(f"{tag}.md and docs/releases/{args.rc}.md"))
        elif args.cmd == "release":
            print(cmd_release(ROOT, args.tag))
        elif args.cmd == "run":
            cmd = cmd_run(args.tag, args.args)
            if args.do_exec:
                if not permit_live():   # exec bypasses the guard_shots hook, so the permit is checked here
                    raise ReleaseError("no live client launch permit (client_permit.py grant, only after the owner allows)")
                os.execv(cmd[0], cmd)
            print(" ".join(cmd))
            print("The owner starts it from his own terminal: run the line above, or add --exec when a permit is granted.")
        elif args.cmd == "reject":
            print(cmd_reject(ROOT, args.rc, args.reason))
            print(commit.format(f"{args.rc}.md"))
        else:
            print("\n".join(cmd_rollback(ROOT, args.reason)))
    except ReleaseError as e:
        print(f"release: {e}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
