#!/usr/bin/env python3
"""One client shot batch for all waiting UI tasks (owner #583). Read-only: runs nothing, prints the commands.

  python3 tools/development/shot_batch.py

A task joins the batch when its status is REVIEW or IN_PROGRESS, its category is client and its frontmatter has
  shots: {branch: <branch name>, only: "frame_a,frame_b*"}
For each such task the script checks that the branch
  - contains f12684f (the new model: slot science core, golden train 2026-11-26);
  - is not behind main in the client sources (no commit of main touching src/Singularity.Client.Spike is missing).
A task that passes gets its command; a task that fails is listed with the reason and left out. The Orchestrator asks the
owner once, then runs the commands between `client_permit.py grant` and `revoke`.
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
import registry  # noqa: E402

MODEL_COMMIT = "f12684f"
CLIENT_DIR = "src/Singularity.Client.Spike"
LAUNCHER = "./run-globe-spike.sh"
WAITING = {"REVIEW", "IN_PROGRESS"}


def git(*args: str) -> tuple[int, str]:
    r = subprocess.run(["git", *args], cwd=ROOT, text=True, capture_output=True)
    return r.returncode, r.stdout.strip()


def worktree_of(branch: str) -> str | None:
    _, out = git("worktree", "list", "--porcelain")
    path = None
    for line in out.splitlines():
        if line.startswith("worktree "):
            path = line[9:]
        elif line == f"branch refs/heads/{branch}":
            return path
    return None


def check_branch(branch: str) -> str | None:
    """None when the branch may be shot, else the reason."""
    if git("rev-parse", "--verify", "--quiet", f"refs/heads/{branch}")[0] != 0:
        return "branch not found"
    if git("merge-base", "--is-ancestor", MODEL_COMMIT, branch)[0] != 0:
        return f"lacks {MODEL_COMMIT} (new model): merge main"
    _, behind = git("log", "--format=%h", f"{branch}..main", "--", CLIENT_DIR)
    if behind:
        return f"behind main in client sources ({len(behind.splitlines())} commit(s)): merge main"
    return None


def collect(items) -> list[dict]:
    rows = []
    for it in sorted(items, key=lambda i: i.id):
        shots = it.meta.get("shots")
        if (it.meta.get("status") not in WAITING or it.meta.get("category") != "client"
                or not isinstance(shots, dict)):
            continue
        branch, only = str(shots.get("branch") or ""), str(shots.get("only") or "")
        problem = None if branch and only else "shots needs both branch and only"
        problem = problem or check_branch(branch)
        rows.append({"id": it.id, "branch": branch, "only": only, "problem": problem})
    return rows


def command(row: dict) -> str:
    wt = worktree_of(row["branch"]) or "<worktree of " + row["branch"] + ">"
    return f"(cd {wt} && {LAUNCHER} --shots --only {row['only']})"


def main() -> int:
    items = [i for i in registry.load_items([]) if i.type.prefix == "TASK"]
    rows = collect(items)
    ready = [r for r in rows if not r["problem"]]
    for r in rows:
        if r["problem"]:
            print(f"SKIP {r['id']} ({r['branch'] or '-'}): {r['problem']}")
    if not ready:
        print("no shot batch ready")
        return 0
    print(f"Batch of {len(ready)} task(s); one permission covers it:")
    for r in ready:
        print(f"# {r['id']}\n{command(r)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
