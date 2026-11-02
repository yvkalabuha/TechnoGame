#!/usr/bin/env python3
"""SessionStart hook: print key facts from CURRENT_STATE.md (plain stdout is
added to the session context). Keeps output short: the 'Сводка' section and
the generated active-tasks block, at most 15 lines."""
import re

from _common import project_dir, read_input

MAX_LINES = 15


def main() -> None:
    data = read_input()
    path = project_dir(data) / "CURRENT_STATE.md"
    if not path.exists():
        print("Project state: CURRENT_STATE.md not found.")
        return
    text = path.read_text(encoding="utf-8")
    summary = re.search(r"^## Сводка\n(.*?)(?=^## |\Z)", text, re.S | re.M)
    active = re.search(r"<!-- registry:active:start -->\n(.*?)<!-- registry:active:end -->", text, re.S)
    lines = ["Project state (from CURRENT_STATE.md):"]
    if summary:
        lines += [l for l in summary.group(1).splitlines() if l.strip()]
    if active:
        lines += ["Active tasks:"] + [l for l in active.group(1).splitlines() if l.strip()]
    print("\n".join(lines[:MAX_LINES]))


if __name__ == "__main__":
    main()
