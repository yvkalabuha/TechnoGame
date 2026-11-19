#!/usr/bin/env python3
"""List agent worktrees and prune the ones whose branch is merged into main.

  python3 tools/development/worktrees.py            list: merged / unmerged / dirty
  python3 tools/development/worktrees.py --prune    remove merged, clean worktrees and their branches

Safe by construction: only worktrees under .claude/worktrees/ whose branch
is an ancestor of main are touched; `git worktree remove` without --force
refuses a worktree with modified or untracked files (ignored build output
does not count); `git branch -d` refuses an unmerged branch.
"""
from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path


def git(*args: str, cwd: Path | None = None) -> subprocess.CompletedProcess:
    return subprocess.run(["git", *args], cwd=cwd, text=True, capture_output=True)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--prune", action="store_true")
    args = ap.parse_args()

    out = git("worktree", "list", "--porcelain").stdout
    blocks = [b for b in out.strip().split("\n\n") if b.strip()]
    root = Path(blocks[0].splitlines()[0].split(" ", 1)[1])
    merged = unmerged = removed = kept_dirty = 0
    for b in blocks[1:]:
        fields = dict(l.split(" ", 1) for l in b.splitlines() if " " in l)
        path = Path(fields["worktree"])
        branch = fields.get("branch", "").removeprefix("refs/heads/")
        if ".claude/worktrees/" not in path.as_posix() or not branch:
            continue
        is_merged = git("merge-base", "--is-ancestor", branch, "main", cwd=root).returncode == 0
        if not is_merged:
            unmerged += 1
            print(f"  unmerged  {branch}")
            continue
        merged += 1
        if not args.prune:
            continue
        r = git("worktree", "remove", str(path), cwd=root)
        if r.returncode != 0:
            kept_dirty += 1
            print(f"  kept (not clean)  {branch}")
            continue
        git("branch", "-d", branch, cwd=root)
        removed += 1
    git("worktree", "prune", cwd=root)
    print(f"worktrees: merged {merged}, unmerged {unmerged}"
          + (f", removed {removed}, kept dirty {kept_dirty}" if args.prune else " (use --prune)"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
