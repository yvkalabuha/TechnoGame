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
  to-review         it stopped, its task is still IN_PROGRESS and its own branch (worktree-agent-<agent_id>)
                    has commits not in main:
                    start the reviewer at once.
orphan branch     a worktree-agent-* branch with commits not in main, older than --ping-hours, with no running agent
                    and no task in REVIEW (a foreground agent leaves no agent_id in the log): to-review?
A branch without a task id in its commit subjects takes the task of its agent's start record, else the task
named in its first commit body. A newer agent suppresses to-review only while it runs and its record names the same branch; once it
stops, to-review applies to the branch. A running agent that names a branch also keeps it off the orphan list.
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
        first = git("log", f"main..{name}", "--format=%H").splitlines()[-1]
        body = sorted(set(TASK_RE.findall(git("show", "-s", "--format=%B", first))))
        out.append({"name": name, "commits": len(rows), "last": int(rows[0][0]),
                    "tasks": tasks, "body_tasks": body, "subject": rows[0][1][:70]})
    return out


AGENT_BRANCH = "worktree-agent-"


def resolve_tasks(br: list[dict], events: list[dict]) -> None:
    """Fill the tasks of a branch whose commit subjects name none: the task of its agent's start record,
    else the task in the body of its first commit."""
    starts = {e.get("agent_id"): e for e in events if e.get("event") != "stop" and e.get("agent_id")}
    for b in br:
        if b["tasks"] or not b["name"].startswith(AGENT_BRANCH):
            continue
        rec = starts.get(b["name"][len(AGENT_BRANCH):])
        b["tasks"] = (agent_task(rec)[0] if rec else []) or b.get("body_tasks", [])


def orphan_branches(br: list[dict], events: list[dict], status_of: dict[str, str], handled: set[str],
                    now: float, min_hours: float) -> list[dict]:
    """Agent branches with commits not in main, no open review and no running agent: the work of a foreground
    agent (its start record has no agent_id) or of an agent whose stop was missed."""
    done = {e["agent_id"] for e in events if e.get("event") == "stop" and e.get("agent_id")}
    live = [e for e in events if e.get("event") != "stop" and e.get("agent_id") and e["agent_id"] not in done]
    running = {e["agent_id"] for e in live}
    out = []
    for b in br:
        if not b["name"].startswith(AGENT_BRANCH) or b["name"] in handled:
            continue
        if b["name"][len(AGENT_BRANCH):] in running or any(same_branch(e, b["name"]) for e in live) or any(status_of.get(t) == "REVIEW" for t in b["tasks"]):
            continue
        h = age_h(b["last"], now)
        if h is not None and h > min_hours:
            out.append({**b, "hours": h})
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


def agent_task(e: dict) -> tuple[list[str], bool]:
    """The task an agent works on: the one named in its description, else the first id of its prompt
    (candidate=True when the prompt names several, so the guess is shown as such)."""
    named = sorted(set(TASK_RE.findall(e.get("description") or "")))
    if named:
        return named, False
    tasks = e.get("tasks") or []
    return tasks[:1], len(tasks) > 1


def same_branch(o: dict, branch: str) -> bool:
    """A later agent works on this branch when its record names it (record 'branch' or description)."""
    return o.get("branch") == branch or branch in (o.get("description") or "")


def agent_actions(events: list[dict], now: float, ping_h: float, stop_h: float,
                  status_of: dict[str, str], unlanded: dict[str, list]) -> list[dict]:
    """Pure decision: one action per agent id (see the module docstring). Pass the task statuses and the
    unlanded branches by task id."""
    started: dict[str, dict] = {}
    stopped: dict[str, float] = {}
    for e in events:
        key = e.get("agent_id") or ""
        if not key:
            continue
        if e.get("event") == "stop":
            stopped[key] = e.get("t", 0.0)
        else:
            started.setdefault(key, e)
    out = []
    for key, e in started.items():
        tasks, candidate = agent_task(e)
        mine = e.get("branch") or AGENT_BRANCH + key  # a replacement works on the branch it was given
        h = (now - e.get("t", now)) / 3600.0
        action = None
        if key not in stopped:
            action = "stop+redispatch" if h > stop_h else "ping" if h > ping_h else None
        elif any(status_of.get(t) == "IN_PROGRESS"
                 and any(b.get("name") == mine for b in unlanded.get(t, []))
                 for t in tasks) and not any(
                o is not e and ok not in stopped and o.get("t", 0.0) > stopped[key]
                and set(agent_task(o)[0]) & set(tasks) and same_branch(o, mine)
                for ok, o in started.items()):  # a newer agent still running on the same branch is the replacement
            action = "to-review"
        if action:
            out.append({"agent_id": key, "action": action, "hours": h, "tasks": tasks, "candidate": candidate,
                        "agent_type": e.get("agent_type", "?"), "branch": mine})
    seen: set[str] = set()  # one to-review per branch (the original and its replacement name the same one)
    return [a for a in out if a["action"] != "to-review" or not (a["branch"] in seen or seen.add(a["branch"]))]


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
    resolve_tasks(br, agent_events())
    by_task: dict[str, list[dict]] = {}
    for b in br:
        for t in b["tasks"]:
            by_task.setdefault(t, []).append(b)

    if args.actions:
        status = {i.id: str(i.meta.get("status")) for i in items}
        acts = agent_actions(agent_events(), now, args.ping_hours, args.stop_hours, status, by_task)
        for a in acts:
            print(f"{a['action']:<16} {a['agent_id']}  {a['agent_type']:<20} {fmt(a['hours'])}  "
                  f"{','.join(a['tasks']) or '-'}{' (candidate)' if a['candidate'] else ''}  {a['branch']}")
        orphans = orphan_branches(br, agent_events(), status, {a["branch"] for a in acts if a["action"] == "to-review"},
                                  now, args.ping_hours)
        for b in orphans:
            print(f"{'orphan branch':<16} {b['name']}  +{b['commits']}  {fmt(b['hours'])}  "
                  f"{','.join(b['tasks']) or '-'}  to-review?")
        if not acts and not orphans:
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
