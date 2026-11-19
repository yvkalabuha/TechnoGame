#!/usr/bin/env python3
"""SessionStart hook: print the facts a new session must start from (plain
stdout is added to the session context).

  1. 'Сводка' from CURRENT_STATE.md;
  2. 'Фокус' from development/PLAN.md: the current plan of work;
  3. live checks: CI on main, unpushed commits, dirty files, worktrees,
     owner question queue;
  4. active tasks (generated block).

Never blocks and never fails: every probe is optional and time-limited.
"""
import json
import re
import subprocess

from _common import project_dir, read_input

MAX_SUMMARY = 12
MAX_FOCUS = 10
MAX_ACTIVE = 12


def section(text: str, title: str) -> list[str]:
    m = re.search(rf"^## {re.escape(title)}\n(.*?)(?=^## |\Z)", text, re.S | re.M)
    return [l for l in m.group(1).splitlines() if l.strip()] if m else []


def run(root, *cmd: str, timeout: float = 6.0) -> str | None:
    try:
        r = subprocess.run(list(cmd), cwd=root, capture_output=True, text=True, timeout=timeout)
    except (OSError, subprocess.SubprocessError):
        return None
    return r.stdout.strip() if r.returncode == 0 else None


def checks(root) -> list[str]:
    out: list[str] = []
    ci = run(root, "gh", "run", "list", "--branch", "main", "--limit", "1",
             "--json", "conclusion,status,displayTitle,createdAt")
    if ci:
        try:
            runs = json.loads(ci)
        except ValueError:
            runs = []
        if runs:
            r = runs[0]
            state = r.get("conclusion") or r.get("status") or "?"
            mark = "OK" if state == "success" else "RED: fix before other work" if state == "failure" else state
            out.append(f"- CI main: {mark} ({r.get('createdAt', '')[:16]}, {r.get('displayTitle', '')[:60]})")
    ahead = run(root, "git", "rev-list", "--count", "origin/main..main")
    if ahead and ahead != "0":
        out.append(f"- Unpushed commits on main: {ahead}")
    dirty = run(root, "git", "status", "--porcelain")
    if dirty:
        out.append(f"- Uncommitted files in the main tree: {len(dirty.splitlines())}")
    wt = run(root, "git", "worktree", "list")
    if wt and len(wt.splitlines()) > 6:
        out.append(f"- Worktrees: {len(wt.splitlines()) - 1} (prune merged ones: tools/development/worktrees.py)")
    w = run(root, "python3", "tools/development/watch.py", "--brief", timeout=10.0)
    if w:
        out.append(f"- Watch: {w}")
    q = root / "development" / "OPEN_QUESTIONS.md"
    if q.exists():
        n = len(re.findall(r"^\| Q\d+ ", q.read_text(encoding="utf-8"), re.M))
        if n:
            out.append(f"- Owner question queue: {n} (ask on request)")
    return out


def main() -> None:
    data = read_input()
    root = project_dir(data)
    path = root / "CURRENT_STATE.md"
    if not path.exists():
        print("Project state: CURRENT_STATE.md not found.")
        return
    text = path.read_text(encoding="utf-8")
    lines = ["Project state (from CURRENT_STATE.md):"]
    lines += section(text, "Сводка")[:MAX_SUMMARY]
    plan = root / "development" / "PLAN.md"
    if plan.exists():
        focus = section(plan.read_text(encoding="utf-8"), "Фокус")[:MAX_FOCUS]
        if focus:
            lines += ["Plan focus (development/PLAN.md; rules: .claude/rules/orchestration.md):"] + focus
    live = checks(root)
    if live:
        lines += ["Checks:"] + live
    active = re.search(r"<!-- registry:active:start -->\n(.*?)<!-- registry:active:end -->", text, re.S)
    if active:
        rows = [l for l in active.group(1).splitlines() if l.strip()]
        lines += ["Active tasks:"] + rows[:MAX_ACTIVE]
    print("\n".join(lines))


if __name__ == "__main__":
    main()
