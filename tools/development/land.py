#!/usr/bin/env python3
"""Land a branch on main only after the merged result passes verify.py.

  python3 tools/development/land.py BRANCH -m "merge: TASK-0001 ..." [--full] [--map]

Steps:
  1. a temporary worktree at main;
  2. `git merge --no-ff BRANCH` there (a conflict stops the landing; main is untouched);
  3. tools/development/verify.py on the merged tree;
  4. `git merge --ff-only` of that merge commit into main.

So main never holds a commit that was not built and tested as merged.
Exit codes: 0 landed, 1 verify failed, 3 merge conflict, 4 main moved or is dirty,
5 the branch changes registry status fields.
The commit message is used as given: put the attribution trailer into -m.
"""
from __future__ import annotations

import argparse
import subprocess
import sys
import time
from pathlib import Path


# Paths whose content verify.py builds or tests.
CODE_PATHS = ("src/", "tests/", "mods/", "tools/", "lab/", ".github/")


def git(*args: str, cwd: Path, check: bool = True) -> subprocess.CompletedProcess:
    return subprocess.run(["git", *args], cwd=cwd, text=True,
                          capture_output=True, check=check)


def main_worktree() -> Path:
    out = subprocess.run(["git", "worktree", "list", "--porcelain"], text=True,
                         capture_output=True, check=True).stdout
    return Path(out.splitlines()[0].split(" ", 1)[1])


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("branch")
    ap.add_argument("-m", "--message", required=True)
    ap.add_argument("--full", action="store_true")
    ap.add_argument("--map", action="store_true")
    ap.add_argument("--target", default="main")
    ap.add_argument("--allow-status", action="store_true",
                    help="accept status changes made on the branch (new items only)")
    args = ap.parse_args()

    root = main_worktree()
    head = git("rev-parse", "--abbrev-ref", "HEAD", cwd=root).stdout.strip()
    if head != args.target:
        print(f"land: the main worktree is on '{head}', not '{args.target}'")
        return 4
    base = git("rev-parse", args.target, cwd=root).stdout.strip()
    if git("rev-parse", "--verify", "--quiet", args.branch, cwd=root, check=False).returncode != 0:
        print(f"land: unknown branch {args.branch}")
        return 4

    tmp = root / ".claude" / "worktrees" / f"_land-{int(time.time())}"
    git("worktree", "add", "--detach", str(tmp), base, cwd=root)
    try:
        m = git("merge", "--no-ff", "-m", args.message, args.branch, cwd=tmp, check=False)
        if m.returncode != 0:
            files = git("diff", "--name-only", "--diff-filter=U", cwd=tmp, check=False).stdout.split()
            print("land: merge conflict, main untouched. Conflicting files:")
            for f in files:
                print("  -", f)
            if not files:
                print(m.stdout + m.stderr)
            return 3
        # Only the Orchestrator changes status fields (set-status on main), never a branch.
        d = git("diff", "-U0", base, "HEAD", "--", "development", "docs/decisions",
                "docs/research", cwd=tmp, check=False).stdout
        changed = [l for l in d.splitlines()
                   if l.startswith(("+status:", "-status:")) and not l.startswith(("+++", "---"))]
        if changed and not args.allow_status:
            print("land: the branch changes registry status fields; main untouched.")
            for l in changed:
                print("  ", l)
            return 5
        cmd = [sys.executable, "tools/development/verify.py"]
        if args.full:
            cmd.append("--full")
        if args.map:
            cmd.append("--map")
        if subprocess.run(cmd, cwd=tmp).returncode != 0:
            print("land: verify failed on the merged tree, main untouched")
            return 1
        sha = git("rev-parse", "HEAD", cwd=tmp).stdout.strip()
        now = git("rev-parse", args.target, cwd=root).stdout.strip()
        if now != base:
            # main moved while verify ran. If only non-code files moved, merge again
            # on the new main and accept it when the code tree equals the verified one.
            moved = git("diff", "--name-only", base, now, cwd=root).stdout.split()
            code = [f for f in moved if f.startswith(CODE_PATHS) or f == "Singularity.sln"]
            if code:
                print("land: main moved in code paths while verify ran; nothing landed. Run again.")
                for f in code[:10]:
                    print("  -", f)
                return 4
            git("reset", "--hard", now, cwd=tmp)
            m = git("merge", "--no-ff", "-m", args.message, args.branch, cwd=tmp, check=False)
            same = m.returncode == 0 and git("diff", "--quiet", sha, "HEAD", "--", *CODE_PATHS,
                                             "Singularity.sln", cwd=tmp, check=False).returncode == 0
            if not same:
                print("land: could not re-merge on the moved main; nothing landed. Run again.")
                return 4
            sha = git("rev-parse", "HEAD", cwd=tmp).stdout.strip()
        ff = git("merge", "--ff-only", sha, cwd=root, check=False)
        if ff.returncode != 0:
            print("land: main has conflicting local changes; nothing landed.")
            print(ff.stdout + ff.stderr)
            return 4
        print(f"land: {args.branch} landed on {args.target} as {sha[:7]}")
        return 0
    finally:
        git("worktree", "remove", "--force", str(tmp), cwd=root, check=False)


if __name__ == "__main__":
    sys.exit(main())
