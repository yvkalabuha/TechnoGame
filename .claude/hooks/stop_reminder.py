#!/usr/bin/env python3
"""Stop hook: once per session, remind about /handoff when project files
changed but CURRENT_STATE.md did not. Never blocks."""
import json
import subprocess

from _common import project_dir, read_input

WATCHED = ("src/", "docs/", "development/", "mods/", "tests/", "lab/", ".claude/")


def main() -> None:
    data = read_input()
    root = project_dir(data)
    session = str(data.get("session_id") or "unknown").replace("/", "_")
    flag = root / ".claude" / ".state" / f"reminded-{session}"
    if flag.exists():
        return
    try:
        out = subprocess.run(["git", "status", "--porcelain"], cwd=root,
                             capture_output=True, text=True, check=True).stdout
    except (subprocess.CalledProcessError, FileNotFoundError):
        return
    paths = [line[3:] for line in out.splitlines()]
    if any(p.startswith(WATCHED) for p in paths) and "CURRENT_STATE.md" not in paths:
        flag.parent.mkdir(parents=True, exist_ok=True)
        flag.touch()
        print(json.dumps({"systemMessage": "Project files changed but CURRENT_STATE.md "
                          "was not updated. Run /handoff before ending the session."}))


if __name__ == "__main__":
    main()
