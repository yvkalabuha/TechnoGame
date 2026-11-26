#!/usr/bin/env python3
"""PreToolUse hook for Bash (all sessions and agents): the game client starts only with the owner's permission.

Owner rules 2026-11-24/25: launch the game only with permission, in batches; never the full shot set for one fix.
- Any launch of run-globe-spike.sh / run-client.sh / play.sh / the Godot binary (not --headless) needs a live permit:
  ~/.cache/singularity/client-launch-ok holding an expiry time (unix seconds). The Orchestrator writes it after the
  owner allows a batch (tools/development/client_permit.py).
- A shot run (--shots / --map-frames) must also name its frames with --only, unless the command starts with SHOTS_FULL=1.
- A shot run must come from a checkout that contains the new model commit (MODEL_COMMIT) and is not behind main in
  the client sources (owner #591, ERR-0001/ERR-0005; same rule as tools/development/shot_batch.py check_branch).
  The checkout of each launcher in the command is its own directory when the launcher is named with a path, resolved
  against the nearest "cd DIR" before it (shot_batch.py prints "(cd WT && ./run-globe-spike.sh ...)"), else the hook's cwd.
"""
import os
import re
import shlex
import subprocess
import time
from pathlib import Path

from _common import block, read_input

PERMIT = Path.home() / ".cache" / "singularity" / "client-launch-ok"
LAUNCH = re.compile(r"(run-globe-spike\.sh|run-client\.sh|play\.sh|Godot_v[\w.-]+)")
QUOTED = re.compile(r"'[^']*'|\"(?:[^\"\\\\]|\\\\.)*\"", re.S)
SHOT_FLAGS = re.compile(r"(^|\s)--(shots|time-shots|map-frames)(\s|$)")
MODEL_COMMIT = os.environ.get("SINGULARITY_MODEL_COMMIT") or "f12684f"
CLIENT_DIR = "src/Singularity.Client.Spike"
CD = re.compile(r"(?:^|[\s;&|(])cd\s+('[^']*'|\"[^\"]*\"|[^\s;&|()]+)")
LAUNCHER_PATH = re.compile(r"('[^']*'|\"[^\"]*\"|[^\s;&|'\"()]+)?(?<![\w-])(run-globe-spike\.sh|run-client\.sh|play\.sh)\b")


def permit_live() -> bool:
    try:
        return float(PERMIT.read_text().strip()) > time.time()
    except (OSError, ValueError):
        return False


def unquote(s: str) -> str:
    try:
        return shlex.split(s)[0]
    except (ValueError, IndexError):
        return s


def base_dir(before: str, cwd: str) -> str:
    cds = list(CD.finditer(before))
    return os.path.join(cwd, os.path.expanduser(unquote(cds[-1].group(1)))) if cds else cwd


def checkout_dirs(raw: str, cwd: str) -> list[str]:
    """The directories the client is launched from (see the module docstring)."""
    out = []
    for m in LAUNCHER_PATH.finditer(raw):
        base = base_dir(raw[:m.start()], cwd)
        if m.group(1):
            base = os.path.dirname(os.path.join(base, os.path.expanduser(unquote(m.group(1) + m.group(2)))))
        out.append(os.path.normpath(base))
    if not out:  # the Godot binary itself
        out.append(os.path.normpath(base_dir(raw, cwd)))
    return list(dict.fromkeys(out))


def git(where: str, *args: str) -> tuple[int, str]:
    try:
        r = subprocess.run(["git", "-C", where, *args], text=True, capture_output=True, timeout=15)
    except (OSError, subprocess.SubprocessError):
        return -1, ""
    return r.returncode, r.stdout.strip()


def checkout_problem(where: str) -> str | None:
    """None when the checkout may be shot, else the reason."""
    rc, _ = git(where, "merge-base", "--is-ancestor", MODEL_COMMIT, "HEAD")
    if rc == 1:
        return f"{where} lacks {MODEL_COMMIT} (new model): merge main into its branch"
    if rc != 0:
        return f"cannot check {where} for {MODEL_COMMIT} (not a git checkout or git error)"
    rc, behind = git(where, "log", "--format=%h", "HEAD..main", "--", CLIENT_DIR)
    if rc != 0:
        return f"cannot compare {where} with main (git error)"
    if behind:
        return f"{where} is behind main in {CLIENT_DIR} ({len(behind.splitlines())} commit(s)): merge main"
    return None


def main() -> None:
    data = read_input()
    raw = (data.get("tool_input") or {}).get("command") or ""
    cmd = QUOTED.sub("''", raw)
    m = LAUNCH.search(cmd)
    if not m:
        return
    if m.group(1).startswith("Godot_v") and re.search(r"--headless\b", cmd):
        return  # imports and builds without a window
    if not permit_live():
        block("Blocked: the game client starts only with the owner's permission (no live permit). "
              "Finish code and tests, then report the frames you need; the Orchestrator asks the owner and runs one batch.")
    if SHOT_FLAGS.search(cmd) and not re.search(r"(^|\s)--only(\s|=)", cmd) and not re.search(r"(^|[\s;&(])SHOTS_FULL=1\s", cmd):
        block("Blocked: a shot run must name its frames with --only a,b,c* (one batch, no full set for one fix).")
    if SHOT_FLAGS.search(cmd):
        problems = [p for d in checkout_dirs(raw, data.get("cwd") or os.getcwd()) if (p := checkout_problem(d))]
        problem = "; ".join(problems)
        if problem:
            block(f"Blocked (ERR-0001): shots from an old client: {problem}. "
                  "Use tools/development/shot_batch.py, which checks branches before the batch.")


if __name__ == "__main__":
    main()
