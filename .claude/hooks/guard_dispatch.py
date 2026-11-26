#!/usr/bin/env python3
"""PreToolUse hook for Agent/Task (main session): an agent starts only on a committed task in a working status.

Owner #591, platform errors ERR-0002 and ERR-0006 (development/errors.yaml):
- The task the dispatch is about must be committed on main with status APPROVED, IN_PROGRESS or REVIEW, and its
  file in the main checkout must not differ from HEAD: an agent worktree is cut from the last commit, so an
  uncommitted task is not there.
- A task that already has a running agent (agents.jsonl: launched, no stop, younger than STALE_H) gets no
  second agent. Older records are treated as dead: watch.py redispatches after its --stop-hours (3).
  A deliberate redispatch puts "[redispatch]" in the description.
The task is the one named in the description, else the ones named as development/tasks/TASK-NNNN paths in the
prompt, else every TASK id in the prompt (same rule as tools/development/watch.py agent_task).
Read-only agent types are not checked. Any git error lets the dispatch through: only evidence blocks.
"""
import json
import re
import subprocess
import time

from _common import block, project_dir, read_input

OK_STATUS = {"APPROVED", "IN_PROGRESS", "REVIEW"}
READ_ONLY = {"reviewer", "critic", "advocate", "claude-code-guide", "Explore", "web-fetch"}
TASK_RE = re.compile(r"TASK-\d{4}")
STALE_H = 3.0


def ordered(ids: list[str]) -> list[str]:
    return list(dict.fromkeys(ids))


def target_tasks(ti: dict) -> list[str]:
    named = ordered(TASK_RE.findall(ti.get("description") or ""))
    if named:
        return named
    prompt = ti.get("prompt") or ""
    paths = ordered(re.findall(r"development/tasks/(TASK-\d{4})", prompt))
    return paths or ordered(TASK_RE.findall(prompt))


def git(root, *args: str) -> tuple[int, str]:
    try:
        r = subprocess.run(["git", "-C", str(root), *args], text=True, capture_output=True, timeout=10)
    except (OSError, subprocess.SubprocessError):
        return -1, ""
    return r.returncode, r.stdout


def check_task(root, task: str) -> str | None:
    """None when the task may get an agent, else the reason."""
    rc, files = git(root, "ls-tree", "-r", "--name-only", "main", "--", "development/tasks")
    if rc != 0:
        return None
    path = next((f for f in files.splitlines() if f.startswith(f"development/tasks/{task}-")), None)
    if path is None:
        rc, local = git(root, "ls-files", "--others", "--", "development/tasks")
        if rc == 0 and any(f"/{task}-" in f for f in local.splitlines()):
            return f"{task} is not committed on main (the agent worktree will not have it): commit it first"
        return f"{task} has no task file on main: create and commit it first"
    rc, text = git(root, "show", f"main:{path}")
    m = re.search(r"^status:\s*(\w+)", text, re.M) if rc == 0 else None
    if m and m.group(1) not in OK_STATUS:
        return f"{task} is {m.group(1)} on main; an agent needs APPROVED, IN_PROGRESS or REVIEW"
    rc, dirty = git(root, "status", "--porcelain", "--", path)
    if rc == 0 and dirty.strip():
        return f"{task} has uncommitted changes ({path}): commit them first, the agent worktree is cut from the last commit"
    return None


def running_agents(root, tasks: set[str], now: float) -> list[str]:
    path = root / ".claude" / ".state" / "agents.jsonl"
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except OSError:
        return []
    launched: dict[str, dict] = {}
    stopped: set[str] = set()
    for line in lines:
        try:
            e = json.loads(line)
        except ValueError:
            continue
        key = e.get("agent_id") or ""
        if not key:
            continue  # a start record (including this very dispatch) or a foreground agent
        if e.get("event") == "stop":
            stopped.add(key)
        elif e.get("event") == "launched":
            launched.setdefault(key, e)
    out = []
    for key, e in launched.items():
        age_h = (now - float(e.get("t") or 0.0)) / 3600.0
        if key in stopped or age_h > STALE_H:
            continue
        theirs = set(target_tasks({"description": e.get("description"), "prompt": " ".join(e.get("tasks") or [])}))
        both = sorted(theirs & tasks)
        if both:
            out.append(f"{key} ({e.get('agent_type', '?')}, {', '.join(both)}, started {age_h:.1f} h ago)")
    return out


def main() -> None:
    data = read_input()
    ti = data.get("tool_input") or {}
    if (ti.get("subagent_type") or "general-purpose") in READ_ONLY:
        return
    tasks = target_tasks(ti)
    if not tasks:
        return
    root = project_dir(data)
    for task in tasks:
        reason = check_task(root, task)
        if reason:
            block(f"Blocked dispatch (ERR-0002): {reason}. Name the task in the description to scope this check.")
    if "[redispatch]" in (ti.get("description") or ""):
        return
    busy = running_agents(root, set(tasks), time.time())
    if busy:
        block("Blocked dispatch (ERR-0006): the task already has a running agent: " + "; ".join(busy)
              + ". Wait for it or message it (SendMessage); if it is really dead, put [redispatch] in the description.")


if __name__ == "__main__":
    main()
