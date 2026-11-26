#!/usr/bin/env python3
"""PreToolUse hook for Bash (all sessions and agents): the game client starts only with the owner's permission.

Owner rules 2026-11-24/25: launch the game only with permission, in batches; never the full shot set for one fix.
- Any launch of run-globe-spike.sh / run-client.sh / play.sh / the Godot binary (not --headless) needs a live permit:
  ~/.cache/singularity/client-launch-ok holding an expiry time (unix seconds). The Orchestrator writes it after the
  owner allows a batch (tools/development/client_permit.py).
- A shot run (--shots / --map-frames) must also name its frames with --only, unless the command starts with SHOTS_FULL=1.
- A shot run must come from a checkout that contains the new model commit (MODEL_COMMIT) and is not behind main in
  the client sources (owner #591, ERR-0001/ERR-0005; same rule as tools/development/shot_batch.py check_branch).
  The command is tokenised like the shell (quotes removed, cd scoped to its subshell); the checkout of each launcher
  is its own directory, resolved against the cd in force. A launch whose directory cannot be known is refused.
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
LAUNCHER_NAME = re.compile(r"(run-globe-spike\.sh|run-client\.sh|play\.sh|Godot_v[\w.-]+)")
SHOT_WORDS = re.compile(r"--(shots|time-shots|map-frames)(=.*)?")
SEPS = {";", "&&", "||", "|", "&", "|&", ";;", "\n"}
WRAPPERS = {"nice", "env", "exec", "time", "timeout", "nohup", "setsid", "stdbuf", "xvfb-run", "bash", "sh",
            "command", "sudo"}
ASSIGN = re.compile(r"^[A-Za-z_]\w*=")


def permit_live() -> bool:
    try:
        return float(PERMIT.read_text().strip()) > time.time()
    except (OSError, ValueError):
        return False


def analyse(raw: str, cwd: str) -> tuple[list[tuple[str | None, bool, bool]], str | None]:
    """Launches in a shell command: (checkout dir or None when unknown, shot run, headless) for each launcher,
    and the reason when the command cannot be read safely. The command is tokenised like the shell does:
    quotes are removed, "( ... )" keeps a cd inside it, a cd in a pipeline, in the background or after ||
    makes the directory unknown."""
    try:
        # a newline separates commands like ";" (shlex would read it as blank space)
        lex = shlex.shlex(re.sub(r"\\\n", " ", raw).replace("\n", " ; "), posix=True, punctuation_chars=True)
        lex.whitespace_split = True
        lex.commenters = ""
        tokens = list(lex)
    except ValueError:
        return [], "the command cannot be tokenised (unbalanced quotes)"
    out: list[tuple[str | None, bool, bool]] = []
    problem = None
    cur: str | None = cwd
    stack: list[str | None] = []
    words: list[str] = []
    prev = None

    def simple(nxt: str | None) -> None:
        nonlocal cur, problem
        ws = list(words)
        words.clear()
        while ws and ASSIGN.match(ws[0]):
            ws.pop(0)
        if not ws:
            return
        if ws[0] in ("cd", "pushd", "popd"):
            target = ws[1] if len(ws) > 1 else "~"
            if ws[0] != "cd" or prev in ("||", "|", "&", "|&") or nxt in ("|", "&", "|&") \
                    or re.search(r"[$`]", target) or target == "-" or cur is None and not target.startswith(("/", "~")):
                cur = None
            else:
                cur = os.path.normpath(os.path.join(cur or "/", os.path.expanduser(target)))
            return
        for i, w in enumerate(ws):
            if LAUNCHER_NAME.fullmatch(os.path.basename(w)):
                if not all(x in WRAPPERS or x.startswith("-") or x.isdigit() or ASSIGN.match(x) for x in ws[:i]):
                    break
                rest = ws[i + 1:]
                if "/" in w:
                    d = None if cur is None and not w.startswith(("/", "~")) else \
                        os.path.dirname(os.path.normpath(os.path.join(cur or "/", os.path.expanduser(w))))
                else:
                    d = cur
                out.append((d, any(SHOT_WORDS.fullmatch(x) for x in rest), "--headless" in rest))
                return
        if (ws[0] == "eval" or ws[0] in WRAPPERS and "-c" in ws) and any(
                LAUNCHER_NAME.search(w) and SHOT_WORDS.search(w) for w in ws):
            problem = "the launcher runs inside a quoted command string"

    for t in tokens:
        if t in SEPS:
            simple(t)
            prev = t
        elif t == "(":
            simple(None)
            stack.append(cur)
            prev = t
        elif t == ")":
            simple(None)
            if not stack:
                return out, "unbalanced parentheses"
            cur = stack.pop()
            prev = t
        else:
            words.append(t)
    simple(None)
    return out, problem


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
    rc, top = git(where, "rev-parse", "--show-toplevel")
    if rc != 0 or not top:
        return f"cannot find the checkout root of {where} (git error)"
    rc, behind = git(top, "log", "--format=%h", "HEAD..main", "--", f":(top){CLIENT_DIR}")
    if rc != 0:
        return f"cannot compare {where} with main (git error)"
    if behind:
        return f"{where} is behind main in {CLIENT_DIR} ({len(behind.splitlines())} commit(s)): merge main"
    return None


def main() -> None:
    data = read_input()
    raw = (data.get("tool_input") or {}).get("command") or ""
    cmd = QUOTED.sub("''", raw)
    launches, problem = analyse(raw, data.get("cwd") or os.getcwd())
    m = LAUNCH.search(cmd)
    if not m and not launches and not problem:
        return
    # Every launcher counts; only a headless Godot run (import, build) is exempt, and only itself.
    active = [(d, s) for d, s, h in launches if not h]
    if m and not launches and not (all(x.startswith("Godot_v") for x in LAUNCH.findall(cmd))
                                   and re.search(r"--headless\b", cmd)):
        active.append((None, bool(SHOT_FLAGS.search(cmd))))  # launcher text not read as a command
    if not active and not problem:
        return
    if not permit_live():
        block("Blocked: the game client starts only with the owner's permission (no live permit). "
              "Finish code and tests, then report the frames you need; the Orchestrator asks the owner and runs one batch.")
    shot = any(s for _, s in active) or bool(problem) or bool(SHOT_FLAGS.search(cmd)) and not launches
    if shot and not re.search(r"(^|\s)--only(\s|=)", cmd) and not re.search(r"(^|[\s;&(])SHOTS_FULL=1\s", cmd):
        block("Blocked: a shot run must name its frames with --only a,b,c* (one batch, no full set for one fix).")
    if not shot:
        return
    dirs = [d for d, s in active if s] or [d for d, _ in active]
    if problem or not dirs or None in dirs:
        block("Blocked (ERR-0001): cannot tell which checkout the shots run from ("
              + (problem or "the directory of the launch is unknown") + "). Run the launcher from the worktree "
              "directly: cd /path/to/worktree && ./run-globe-spike.sh --shots --only ...")
    problems = [p for d in dict.fromkeys(dirs) if (p := checkout_problem(d))]
    if problems:
        block(f"Blocked (ERR-0001): shots from an old client: {'; '.join(problems)}. "
              "Use tools/development/shot_batch.py, which checks branches before the batch.")


if __name__ == "__main__":
    main()
