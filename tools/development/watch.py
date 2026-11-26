#!/usr/bin/env python3
"""Control of stale tasks, forgotten branches and silent agents.

  python3 tools/development/watch.py            full board
  python3 tools/development/watch.py --brief    one line for the SessionStart hook
  python3 tools/development/watch.py --hours 6  another staleness threshold (default 3)
  python3 tools/development/watch.py --actions  what to do with each agent (owner #583);
                                                --ping-hours 2 and --stop-hours 3 set the thresholds

What it reports:
  STALE     an open task (APPROVED, IN_PROGRESS, REVIEW, BLOCKED) whose file and whose
            branches had no commit for more than the threshold;
  UNLANDED  an agent branch with commits that are not in main (work that can be lost),
            with the task ids found in its commit subjects;
  ORPHAN    an unlanded branch that names no open task;
  DONE?     a DONE task that still has an unlanded branch;
  SILENT    an agent started (logged by the agent_log hook) with no stop record for
            more than the threshold.

--actions prints one action per tracked agent (a launched agent has an agent_id in the log):
  ping              no stop record for more than --ping-hours: ask it for a status (SendMessage);
  stop+redispatch   no stop record for more than --stop-hours: TaskStop, then redispatch on its branch;
  to-review         it stopped, its task is still IN_PROGRESS and its branch has commits not in main:
                    start the reviewer at once.
Agents that run within the thresholds, or whose work is accounted for, print nothing.

Read-only. Sources: the registry, git, .claude/.state/agents.jsonl.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
import registry  # noqa: E402

OPEN = {"APPROVED", "IN_PROGRESS", "REVIEW", "BLOCKED"}
TASK_RE = re.compile(r"TASK-\d{4}")
AGENT_LOG = ROOT / ".claude" / ".state" / "agents.jsonl"


def git(*args: str) -> str:
    r = subprocess.run(["git", *args], cwd=ROOT, text=True, capture_output=True)
    return r.stdout.strip() if r.returncode == 0 else ""


def age_h(ts: int | float | None, now: float) -> float | None:
    return None if not ts else (now - float(ts)) / 3600.0


def fmt(h: float | None) -> str:
    if h is None:
        return "  ?  "
    return f"{h:4.1f}h" if h < 48 else f"{h / 24:4.1f}d"


def branches(now: float) -> list[dict]:
    """Local branches with commits that main does not have."""
    out = []
    for name in git("for-each-ref", "--format=%(refname:short)", "refs/heads").splitlines():
        if name == "main" or subprocess.run(["git", "merge-base", "--is-ancestor", name, "main"],
                                             cwd=ROOT).returncode == 0:
            continue
        log = git("log", f"main..{name}", "--format=%ct\t%s")
        if not log:
            continue
        rows = [l.split("\t", 1) for l in log.splitlines()]
        tasks = sorted({t for _, s in rows for t in TASK_RE.findall(s)})
        out.append({"name": name, "commits": len(rows), "last": int(rows[0][0]),
                    "tasks": tasks, "subject": rows[0][1][:70]})
    return out


def silent_agents(now: float, hours: float) -> list[dict]:
    if not AGENT_LOG.exists():
        return []
    started: dict[str, dict] = {}
    stopped: set[str] = set()
    for line in AGENT_LOG.read_text(encoding="utf-8").splitlines():
        try:
            e = json.loads(line)
        except ValueError:
            continue
        key = e.get("agent_id") or e.get("key") or ""
        if e.get("event") == "stop":
            stopped.add(key)
        elif key:
            started.setdefault(key, e)
    return [e for k, e in started.items()
            if k not in stopped and (now - e.get("t", now)) / 3600.0 > hours]


def agent_events() -> list[dict]:
    if not AGENT_LOG.exists():
        return []
    out = []
    for line in AGENT_LOG.read_text(encoding="utf-8").splitlines():
        try:
            out.append(json.loads(line))
        except ValueError:
            continue
    return out


def agent_actions(events: list[dict], now: float, ping_h: float, stop_h: float,
                  status_of: dict[str, str], unlanded: dict[str, list]) -> list[dict]:
    """Pure decision: one action per agent id (see the module docstring). Pass the task statuses and the
    unlanded branches by task id."""
    started: dict[str, dict] = {}
    stopped: set[str] = set()
    for e in events:
        key = e.get("agent_id") or ""
        if not key:
            continue
        if e.get("event") == "stop":
            stopped.add(key)
        else:
            started.setdefault(key, e)
    out = []
    for key, e in started.items():
        tasks = e.get("tasks") or []
        h = (now - e.get("t", now)) / 3600.0
        action = None
        if key not in stopped:
            action = "stop+redispatch" if h > stop_h else "ping" if h > ping_h else None
        elif any(status_of.get(t) == "IN_PROGRESS" and unlanded.get(t) for t in tasks):
            action = "to-review"
        if action:
            out.append({"agent_id": key, "action": action, "hours": h, "tasks": tasks,
                        "agent_type": e.get("agent_type", "?")})
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--brief", action="store_true")
    ap.add_argument("--hours", type=float, default=3.0)
    ap.add_argument("--actions", action="store_true")
    ap.add_argument("--ping-hours", type=float, default=2.0)
    ap.add_argument("--stop-hours", type=float, default=3.0)
    args = ap.parse_args()
    now = time.time()

    items = [i for i in registry.load_items([]) if i.type.prefix == "TASK"]
    by_id = {i.id: i for i in items}
    br = branches(now)
    by_task: dict[str, list[dict]] = {}
    for b in br:
        for t in b["tasks"]:
            by_task.setdefault(t, []).append(b)

    if args.actions:
        status = {i.id: str(i.meta.get("status")) for i in items}
        acts = agent_actions(agent_events(), now, args.ping_hours, args.stop_hours, status, by_task)
        for a in acts:
            print(f"{a['action']:<16} {a['agent_id']}  {a['agent_type']:<20} {fmt(a['hours'])}  "
                  f"{','.join(a['tasks']) or '-'}")
        if not acts:
            print("no agent needs action")
        return 0

    stale, done_unlanded, board = [], [], []
    for it in sorted(items, key=lambda i: i.id):
        status = it.meta.get("status")
        mine = by_task.get(it.id, [])
        if status == "DONE" and mine:
            done_unlanded.append((it, mine))
        if status not in OPEN:
            continue
        file_ts = git("log", "-1", "--format=%ct", "--", registry.rel(it.path))
        last = max([int(file_ts) if file_ts else 0] + [b["last"] for b in mine])
        h = age_h(last, now)
        is_stale = h is not None and h > args.hours
        if is_stale:
            stale.append(it)
        board.append((it, status, h, mine, is_stale))

    open_ids = {it.id for it, *_ in board}
    orphans = [b for b in br if not (set(b["tasks"]) & open_ids)]
    silent = silent_agents(now, args.hours)

    if args.brief:
        parts = []
        if stale:
            parts.append(f"{len(stale)} stale task(s): " + ", ".join(i.id for i in stale[:6]))
        if br:
            parts.append(f"{len(br)} unlanded branch(es), {len(orphans)} without an open task")
        if done_unlanded:
            parts.append(f"{len(done_unlanded)} DONE task(s) with an unlanded branch")
        if silent:
            parts.append(f"{len(silent)} silent agent(s)")
        if parts:
            print("; ".join(parts) + " (python3 tools/development/watch.py)")
        return 0

    print(f"Open tasks (stale after {args.hours:g} h without a commit on the task file or its branches):")
    for it, status, h, mine, is_stale in board:
        mark = "STALE" if is_stale else "     "
        where = ", ".join(f"{b['name'][-12:]}+{b['commits']}" for b in mine) or "no branch"
        print(f"  {mark} {it.id} [{status:<11}] {fmt(h)}  {it.meta.get('agent', ''):<20} {where}  {str(it.meta.get('title', ''))[:50]}")
    if not board:
        print("  none")
    print("\nUnlanded branches (commits not in main):")
    for b in sorted(br, key=lambda b: b["last"]):
        tag = "ORPHAN  " if b in orphans else "UNLANDED"
        print(f"  {tag} {b['name']:<40} +{b['commits']:<3} {fmt(age_h(b['last'], now))}  "
              f"{','.join(b['tasks']) or '-':<20} {b['subject']}")
    if not br:
        print("  none")
    if done_unlanded:
        print("\nDONE tasks with an unlanded branch (land it or drop the branch):")
        for it, mine in done_unlanded:
            print(f"  DONE? {it.id}  " + ", ".join(b["name"] for b in mine))
    if silent:
        print("\nAgents started without a stop record:")
        for e in silent:
            print(f"  SILENT {fmt(age_h(e.get('t'), now))}  {e.get('agent_type', '?'):<20} {str(e.get('description', ''))[:60]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
