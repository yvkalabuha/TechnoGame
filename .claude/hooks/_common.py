"""Shared helpers for Claude Code hooks (Python stdlib only).

Hooks read one JSON object from stdin. Exit code 2 blocks the tool call and
sends stderr back to Claude. Any other non-zero exit is a non-blocking error,
so every hook that must block also sets onFailure: block in its config.
"""
import json
import os
import re
import sys
import time
from pathlib import Path


def read_input() -> dict:
    try:
        return json.load(sys.stdin)
    except (json.JSONDecodeError, ValueError):
        return {}


def project_dir(data: dict) -> Path:
    return Path(os.environ.get("CLAUDE_PROJECT_DIR") or data.get("cwd") or ".").resolve()


def rel_path(data: dict, file_path: str) -> str | None:
    """Path relative to the project root in POSIX form, or None if outside."""
    root = project_dir(data)
    p = Path(file_path)
    if not p.is_absolute():
        p = Path(data.get("cwd") or root) / p
    try:
        rel = p.resolve().relative_to(root).as_posix()
    except ValueError:
        return None
    # Agent worktrees live in .claude/worktrees/<name>/: judge the path inside the worktree.
    prefix = ".claude/worktrees/"
    if rel.startswith(prefix):
        parts = rel[len(prefix):].split("/", 1)
        if len(parts) == 2 and parts[1]:
            return parts[1]
    return rel


def glob_to_regex(pattern: str) -> re.Pattern:
    """'**' matches any number of directories, '*' matches within one."""
    out = ""
    i = 0
    while i < len(pattern):
        if pattern.startswith("**/", i):
            out += "(?:.*/)?"
            i += 3
        elif pattern.startswith("**", i):
            out += ".*"
            i += 2
        elif pattern[i] == "*":
            out += "[^/]*"
            i += 1
        elif pattern[i] == "?":
            out += "[^/]"
            i += 1
        else:
            out += re.escape(pattern[i])
            i += 1
    return re.compile(out + r"\Z")


def matches(path: str, patterns: list[str]) -> bool:
    return any(glob_to_regex(p).match(path) for p in patterns)


def log_refusal(message: str) -> None:
    """Append one refusal line for tools/development/errors.py import (#584). Never fails."""
    if os.environ.get("SINGULARITY_NO_REFUSAL_LOG"):
        return
    try:
        root = Path(os.environ.get("CLAUDE_PROJECT_DIR") or Path(__file__).resolve().parents[2])
        path = root / ".claude" / ".state" / "hook_refusals.jsonl"
        path.parent.mkdir(parents=True, exist_ok=True)
        rec = {"t": time.time(), "hook": Path(sys.argv[0]).stem, "msg": message[:200]}
        with path.open("a", encoding="utf-8") as f:
            f.write(json.dumps(rec, ensure_ascii=False) + "\n")
    except Exception:
        pass


def block(message: str) -> None:
    log_refusal(message)
    print(message, file=sys.stderr)
    sys.exit(2)
