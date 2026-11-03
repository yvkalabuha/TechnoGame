"""git worktree per candidate on branch llm/<task_id> (candidate 2: llm/<task_id>-c2). main is never touched.

Every destructive command (reset, clean, commit) first checks that the directory is the worktree
itself (not a parent repository found by walking up) and that it is on the expected llm/ branch."""

import json
import os
import subprocess
from pathlib import Path


class GitError(RuntimeError):
    pass


def _env():
    # Inherited GIT_DIR / GIT_WORK_TREE would redirect commands to another repository.
    return {k: v for k, v in os.environ.items() if not k.startswith("GIT_")}


def git(args, cwd):
    try:
        p = subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True, env=_env())
    except OSError as e:
        raise GitError(f"git {' '.join(args)} in {cwd}: {e}") from None
    if p.returncode != 0:
        raise GitError(f"git {' '.join(args)}: {p.stderr.strip()[:500]}")
    return p.stdout.strip()


def branch_name(task_id, candidate):
    return f"llm/{task_id}" if candidate == 1 else f"llm/{task_id}-c{candidate}"


def ensure(repo_root, worktrees_dir, task_id, candidate, meta_path):
    """Creates the worktree, or reuses it after a restart. Returns (path, base_commit, branch).
    meta_path (in runs/) records the base commit so a resumed run resets to the same base."""
    if meta_path.is_file():
        meta = json.loads(meta_path.read_text())
        path = Path(meta.get("path") or worktrees_dir / meta["dir"])
        verify(path, meta["branch"])
        return path, meta["base"], meta["branch"]
    path = Path(worktrees_dir) / f"{task_id}-c{candidate}"
    branch = branch_name(task_id, candidate)
    head = git(["rev-parse", "HEAD"], repo_root)
    exists = subprocess.run(["git", "rev-parse", "--verify", "--quiet", f"refs/heads/{branch}"],
                            cwd=repo_root, capture_output=True, env=_env()).returncode == 0
    if exists or path.exists():
        raise GitError(f"branch {branch} or {path} already exists; remove it or use a new task_id")
    Path(worktrees_dir).mkdir(parents=True, exist_ok=True)
    git(["worktree", "add", "-q", "-b", branch, str(path), head], repo_root)
    verify(path, branch)
    meta_path.parent.mkdir(parents=True, exist_ok=True)
    meta_path.write_text(json.dumps({"dir": path.name, "path": str(path), "branch": branch, "base": head}) + "\n")
    return path, head, branch


def verify(path, branch):
    """GitError unless path is the top of its own work tree and is on branch (an llm/ branch)."""
    if not branch.startswith("llm/"):
        raise GitError(f"refusing to touch non-llm branch {branch!r}")
    path = Path(path)
    if not path.is_dir():
        raise GitError(f"worktree missing: {path}")
    top = git(["rev-parse", "--show-toplevel"], path)
    if Path(top).resolve() != path.resolve():
        raise GitError(f"{path} is not a worktree root (git top-level is {top})")
    current = git(["branch", "--show-current"], path)
    if current != branch:
        raise GitError(f"{path} is on branch {current!r}, expected {branch!r}")


def reset(path, base, branch):
    """Back to the base commit; ignored build outputs (bin/obj) stay for faster rebuilds."""
    verify(path, branch)
    git(["reset", "-q", "--hard", base], path)
    git(["clean", "-fdq"], path)


def commit_all(path, message, branch):
    verify(path, branch)
    git(["add", "-A"], path)
    if subprocess.run(["git", "diff", "--cached", "--quiet"], cwd=path, env=_env()).returncode == 0:
        return None
    # llm/* are service branches: no project hooks (they would run with the worker's output).
    git(["-c", "core.hooksPath=/dev/null", "commit", "-q", "--no-verify", "-m", message], path)
    return git(["rev-parse", "HEAD"], path)
