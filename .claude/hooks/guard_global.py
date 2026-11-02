#!/usr/bin/env python3
"""PreToolUse hook for Edit|Write (all sessions and agents).

Blocks:
  1. writes to secret-like files (.env*, *.key, *secret*);
  2. Write over an ADR whose status is ACCEPTED;
  3. Edit of an ACCEPTED ADR that touches anything except the
     'status:' / 'superseded_by:' frontmatter lines.
This protects against mistakes, not against deliberate bypass via Bash;
registry.py check in pre-commit and CI is the final barrier.
"""
import re
from pathlib import Path

from _common import block, matches, project_dir, read_input, rel_path

SECRET_PATTERNS = ["**/.env", "**/.env.*", "**/*.key", "**/*secret*", "**/*.pem"]
ADR_PATTERN = "docs/decisions/ADR-*.md"
MUTABLE_LINE = re.compile(r"^\s*(status|superseded_by):.*$")


def adr_is_accepted(path: Path) -> bool:
    try:
        head = path.read_text(encoding="utf-8").split("\n---", 1)[0]
    except OSError:
        return False
    return re.search(r"^status:\s*ACCEPTED\b", head, re.M) is not None


def only_mutable_lines(text: str) -> bool:
    lines = [l for l in text.splitlines() if l.strip()]
    return bool(lines) and all(MUTABLE_LINE.match(l) for l in lines)


def main() -> None:
    data = read_input()
    tool = data.get("tool_name")
    ti = data.get("tool_input") or {}
    file_path = ti.get("file_path")
    if not file_path:
        return
    rel = rel_path(data, file_path)
    name_rel = rel if rel is not None else Path(file_path).name
    if matches(name_rel, SECRET_PATTERNS) or matches("x/" + Path(file_path).name, SECRET_PATTERNS):
        block(f"Blocked: '{file_path}' looks like a secret file. Secrets never go into the repo.")
    if rel is None or not matches(rel, [ADR_PATTERN]):
        return
    full = project_dir(data) / rel
    if not full.exists() or not adr_is_accepted(full):
        return
    if tool == "Write":
        block(f"Blocked: {rel} is an ACCEPTED ADR. Do not rewrite it; create a new ADR "
              "with 'supersedes' and change only the status of the old one.")
    if tool == "Edit":
        old, new = ti.get("old_string", ""), ti.get("new_string", "")
        if not (only_mutable_lines(old) and only_mutable_lines(new)):
            block(f"Blocked: {rel} is an ACCEPTED ADR. Only the 'status:' and "
                  "'superseded_by:' lines may change.")


if __name__ == "__main__":
    main()
