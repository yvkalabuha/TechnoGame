#!/usr/bin/env python3
"""Release model (owner #594): release candidate -> integral check -> owner approval -> release -> rollback.

  python3 tools/development/release.py rc                      tag rc-YYYY-MM-DD-N on main, write docs/releases/rc-....md
  python3 tools/development/release.py check [RC] [--slot-dir D ...] [--l2-dir D] [--no-ci-dispatch]
  python3 tools/development/release.py approve RC --owner-ref "#NNN" [--no-build]
  python3 tools/development/release.py release vX.N            (re)build the client of a release into the releases folder
  python3 tools/development/release.py reject RC --reason "..." record the rejected RC in the error catalogue
  python3 tools/development/release.py rollback [--reason "..."]  print the previous release launch and the revert list

Rules: history is never rewritten; this tool creates tags locally and never pushes (the Orchestrator pushes);
it never starts the game client (shots are only printed: the owner allows a batch, the Orchestrator runs it).
A release build lives outside git: ~/singularity-releases/vX.N (SINGULARITY_RELEASES overrides); ./run-client.sh --release vX.N starts it.
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
from pathlib import Path

import yaml

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

def render_rc(tag: str, sha: str, base: str | None, tasks: list[tuple[str, str]], debts: list[str]) -> str:
    lines = [f"# Кандидат в релиз {tag}", "",
             f"- Коммит: {sha}", f"- Прошлый релиз: {base or 'нет (первый)'}",
             "- Статус: НА ПРОВЕРКЕ", "",
             f"## Что вошло ({len(tasks)} задач DONE)"]
    lines += [f"- {i}: {t}" if t else f"- {i}" for i, t in tasks] or ["- Задач DONE с прошлого релиза нет."]
    lines += ["", "## Долги, открытые за период"]
    lines += [f"- {d}" for d in debts] or ["- Новых открытых долгов нет."]
    lines += ["", "## Проверки", CHECKS_START, "Проверки ещё не запускались: python3 tools/development/release.py check", CHECKS_END,
              "", "## Найденные дефекты",
              "Каждый дефект записывается в каталог ошибок (errors.py add) и перечисляется здесь.", ""]
    return "\n".join(lines)


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
    report.write_text(render_rc(tag, sha, base, [(i, task_title(root, i)) for i in ids], open_debts(root, base)), encoding="utf-8")
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


def run_step(cmd: list[str], cwd: Path, timeout: int) -> tuple[bool, str]:
    try:
        r = subprocess.run(cmd, cwd=cwd, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=timeout)
    except (subprocess.TimeoutExpired, OSError) as e:
        return False, f"{type(e).__name__}"
    tail = [l for l in r.stdout.splitlines() if l.strip()][-3:]
    return r.returncode == 0, " / ".join(tail)[:200]


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


def shot_command(tag: str) -> str:
    return f"python3 tools/development/shot_batch.py   # then, with the owner's permit, run the printed commands for {tag}"


def render_checks(rows: list[tuple[str, str, str]]) -> str:
    out = ["| Проверка | Итог | Детали |", "|---|---|---|"]
    out += [f"| {n} | {s} | {d.replace('|', '/')} |" for n, s, d in rows]
    return "\n".join(out)


def write_checks(report: Path, rows: list[tuple[str, str, str]]) -> None:
    text = report.read_text(encoding="utf-8")
    block = f"{CHECKS_START}\n{render_checks(rows)}\n{CHECKS_END}"
    if CHECKS_START in text and CHECKS_END in text:
        a, b = text.index(CHECKS_START), text.index(CHECKS_END) + len(CHECKS_END)
        text = text[:a] + block + text[b:]
    else:
        text += "\n## Проверки\n" + block + "\n"
    report.write_text(text, encoding="utf-8")


def parse_checks(report: Path) -> dict[str, str]:
    text = report.read_text(encoding="utf-8")
    if CHECKS_START not in text:
        return {}
    block = text[text.index(CHECKS_START):text.index(CHECKS_END)]
    res = {}
    for line in block.splitlines():
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if line.startswith("|") and len(cells) >= 2 and cells[1] in ("PASS", "FAIL", "SKIP", "PENDING", "WARN"):
            res[cells[0]] = cells[1]
    return res


def cmd_check(root: Path, rc: str | None, slot_dirs: list[str], l2_dir: str | None, dispatch: bool) -> int:
    rcs = sorted(git(root, "tag", "--list", "rc-*").splitlines(), key=lambda t: (t.split("-")[1:4], version_key(t)))
    rc = rc or (rcs[-1] if rcs else None)
    if not rc:
        raise ReleaseError("no RC tag: run release.py rc first")
    sha = git(root, "rev-parse", f"{rc}^{{commit}}")
    report = root / "docs" / "releases" / f"{rc}.md"
    if not report.exists():
        raise ReleaseError(f"{report} missing")
    base = last_release_tag(root)
    rows: list[tuple[str, str, str]] = []
    work = Path(tempfile.mkdtemp(prefix="rc-check-"))
    tree = work / "tree"
    try:
        git(root, "worktree", "add", "--detach", str(tree), sha)
        ok, d = run_step([sys.executable, "tools/development/verify.py", "--full"], tree, 3600)
        rows.append(("verify --full", "PASS" if ok else "FAIL", d))
        rows.append(("CI three OS", *check_ci(root, rc, sha, dispatch)))
        if base:
            ok, d = run_step([sys.executable, "lab/analysis/pace_check.py", "--base", base], tree, 3600)
            rows.append(("Lab pace_check", "PASS" if ok else "FAIL", d))
        else:
            rows.append(("Lab pace_check", "SKIP", "no previous release tag to compare with"))
        if slot_dirs:
            ok, d = run_step([sys.executable, "lab/analysis/slot_core.py", *slot_dirs], tree, 600)
            rows.append(("Lab slot_core gates", "PASS" if ok else "FAIL", d))
        else:
            rows.append(("Lab slot_core gates", "SKIP", "no matrix dir given (--slot-dir after running the TASK-0187 manifests)"))
        if l2_dir:
            ok, d = run_step([sys.executable, "lab/analysis/slot_core.py", "--l2", l2_dir, *slot_dirs], tree, 600) if slot_dirs else (False, "needs --slot-dir too")
            rows.append(("L2 determinism (Lab)", "PASS" if ok else "FAIL", d))
        else:
            rows.append(("L2 determinism (Lab)", "SKIP", "no --l2-dir given; the L1/L2 hash tests run inside verify (test step)"))
        rows.append(("Client build (Godot)", *build_client(tree, None)))
    finally:
        subprocess.run(["git", "worktree", "remove", "--force", str(tree)], cwd=root, capture_output=True)
        shutil.rmtree(work, ignore_errors=True)
    rows.append(("Shot batch", "PENDING", shot_command(rc)))
    write_checks(report, rows)
    print(render_checks(rows))
    failed = [n for n, s, _ in rows if s == "FAIL"]
    for n in failed:
        print(record_error(root, "process", "release", f"RC check '{n}' failed", f"{rc}: see the RC report"))
    return 1 if failed else 0


# ---------------------------------------------------------------- approve, release, reject, rollback

def releases_dir() -> Path:
    return Path(os.environ.get("SINGULARITY_RELEASES") or Path.home() / "singularity-releases")


def render_notes(tag: str, rc: str, tasks: list[tuple[str, str]], owner_ref: str) -> str:
    lines = [f"# Релиз {tag}", "", f"Собран из кандидата {rc}; одобрен владельцем ({owner_ref}).", "",
             "## Что нового"]
    lines += [f"- {t or i}" for i, t in tasks] or ["- Изменений по задачам нет."]
    lines += ["", "## Как запустить", f"    ./run-client.sh --release {tag}", "",
              "## Если что-то сломалось", "Запустите прошлый релиз: python3 tools/development/release.py rollback", ""]
    return "\n".join(lines)


def deploy(root: Path, tag: str) -> tuple[str, str]:
    """Snapshot of the tag (git archive) + client build into the releases folder, outside git."""
    dest = releases_dir() / tag
    try:
        if dest.exists():
            shutil.rmtree(dest)
        src = dest / "src"
        src.mkdir(parents=True)
        arch = subprocess.run(["git", "archive", tag], cwd=root, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        if arch.returncode != 0:
            return "FAIL", "git archive: " + arch.stderr.decode(errors="replace").strip()[-200:]
        tar = subprocess.run(["tar", "-x", "-C", str(src)], input=arch.stdout, stderr=subprocess.PIPE)
        if tar.returncode != 0:
            return "FAIL", "tar: " + tar.stderr.decode(errors="replace").strip()[-200:]
    except OSError as e:
        return "FAIL", f"{dest}: {e}"
    status, detail = build_client(src, dest / "bin")
    (dest / "RELEASE").write_text(f"{tag}\n", encoding="utf-8")
    return status, f"{dest}: {detail}"


def cmd_approve(root: Path, rc: str, owner_ref: str, build: bool = True) -> str:
    if not owner_ref.strip():
        raise ReleaseError("--owner-ref is required: the release is approved by the owner's answer")
    sha = git(root, "rev-parse", f"{rc}^{{commit}}")
    report = root / "docs" / "releases" / f"{rc}.md"
    if not report.exists() or "- Статус: НА ПРОВЕРКЕ" not in report.read_text(encoding="utf-8"):
        raise ReleaseError("the RC report is missing or its status is not НА ПРОВЕРКЕ (already approved or rejected)")
    checks = parse_checks(report)
    if checks.get("verify --full") != "PASS":
        raise ReleaseError("verify --full is not PASS in the RC report: run release.py check")
    bad = [n for n, s in checks.items() if s == "FAIL"]
    if bad:
        raise ReleaseError("failed checks: " + ", ".join(bad))
    if git(root, "tag", "--points-at", sha, "--list", "v*"):
        raise ReleaseError("this commit already has a release tag")
    tag = next_release_tag(root)
    base = last_release_tag(root)
    tasks = [(i, task_title(root, i)) for i in done_tasks(root, base, sha)]
    notes = root / "docs" / "releases" / f"{tag}.md"
    notes.write_text(render_notes(tag, rc, tasks, owner_ref), encoding="utf-8")
    git(root, "tag", "-a", tag, sha, "-m", f"release {tag} from {rc}")
    text = report.read_text(encoding="utf-8").replace("- Статус: НА ПРОВЕРКЕ", f"- Статус: ОДОБРЕН как {tag} ({owner_ref})")
    report.write_text(text, encoding="utf-8")
    if build:
        status, detail = deploy(root, tag)
        print(f"deploy {status}: {detail}")
        if status == "FAIL":
            print(record_error(root, "tooling", "release", "release client build failed", f"{tag}: {detail}"))
    return tag


def cmd_reject(root: Path, rc: str, reason: str) -> str:
    git(root, "rev-parse", f"{rc}^{{commit}}")
    report = root / "docs" / "releases" / f"{rc}.md"
    if report.exists():
        text = report.read_text(encoding="utf-8").replace("- Статус: НА ПРОВЕРКЕ", f"- Статус: ОТКЛОНЁН ({reason})")
        report.write_text(text, encoding="utf-8")
    return record_error(root, "process", "release", "RC rejected", f"{rc}: {reason}")


def cmd_rollback(root: Path, reason: str | None) -> list[str]:
    tags = release_tags(root)
    if len(tags) < 2:
        raise ReleaseError("rollback needs two release tags (current and previous)")
    cur, prev = tags[-1], tags[-2]
    ids = done_tasks(root, prev, cur)
    lines = [f"Current release {cur}; previous {prev}.",
             f"Run the previous release now: ./run-client.sh --release {prev}",
             f"Fix on {MAIN} (history is not rewritten): revert the task merges of {cur}, newest first (printed, not run):"]
    lines += [f"  git revert -m 1 {sha}   # {subject}" for sha, subject in merge_commits(root, prev, cur, ids)]
    if reason:
        lines.append(record_error(root, "process", "release", "rollback to the previous release", f"{cur}: {reason}"))
    return lines


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("rc")
    c = sub.add_parser("check")
    c.add_argument("rc", nargs="?")
    c.add_argument("--slot-dir", action="append", default=[])
    c.add_argument("--l2-dir")
    c.add_argument("--no-ci-dispatch", action="store_true")
    a = sub.add_parser("approve")
    a.add_argument("rc")
    a.add_argument("--owner-ref", default="")
    a.add_argument("--no-build", action="store_true")
    r = sub.add_parser("release")
    r.add_argument("tag")
    j = sub.add_parser("reject")
    j.add_argument("rc")
    j.add_argument("--reason", required=True)
    b = sub.add_parser("rollback")
    b.add_argument("--reason")
    args = ap.parse_args(argv)
    try:
        if args.cmd == "rc":
            tag = cmd_rc(ROOT)
            print(tag)
            print(f"Reminder: commit docs/releases/{tag}.md (the Orchestrator commits it)")
        elif args.cmd == "check":
            return cmd_check(ROOT, args.rc, args.slot_dir, args.l2_dir, not args.no_ci_dispatch)
        elif args.cmd == "approve":
            tag = cmd_approve(ROOT, args.rc, args.owner_ref, not args.no_build)
            print(tag)
            print(f"Reminder: commit docs/releases/{tag}.md and docs/releases/{args.rc}.md (the Orchestrator commits them)")
        elif args.cmd == "release":
            status, detail = deploy(ROOT, args.tag)
            print(f"{status}: {detail}")
            return 0 if status != "FAIL" else 1
        elif args.cmd == "reject":
            print(cmd_reject(ROOT, args.rc, args.reason))
            print(f"Reminder: commit docs/releases/{args.rc}.md (the Orchestrator commits it)")
        else:
            print("\n".join(cmd_rollback(ROOT, args.reason)))
    except ReleaseError as e:
        print(f"release: {e}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
