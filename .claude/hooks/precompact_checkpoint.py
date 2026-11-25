#!/usr/bin/env python3
"""PreCompact hook (owner #529): before the context is compacted, write an automatic checkpoint so that ideas and
work in progress survive. Writes development/handoff/auto-checkpoint.md (not committed by the hook). Never fails."""
import datetime
import re
import subprocess

from _common import project_dir, read_input


def run(root, *cmd):
    try:
        r = subprocess.run(list(cmd), cwd=root, capture_output=True, text=True, timeout=8)
        return r.stdout.strip() if r.returncode == 0 else ""
    except (OSError, subprocess.SubprocessError):
        return ""


def tail_rows(path, n):
    try:
        rows = [l for l in path.read_text(encoding="utf-8").splitlines() if l.startswith("| ")]
    except OSError:
        return []
    return rows[-n:]


def section(path, title):
    try:
        t = path.read_text(encoding="utf-8")
    except OSError:
        return ""
    m = re.search(rf"^## {re.escape(title)}.*?\n(.*?)(?=^## |\Z)", t, re.S | re.M)
    return m.group(1).strip() if m else ""


def main():
    data = read_input()
    root = project_dir(data)
    now = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")
    parts = [f"# Auto-checkpoint {now} (PreCompact, trigger: {data.get('trigger', '?')})", "",
             "Read with the latest handoff in development/handoff/. North star: docs/vision/NORTH_STAR.md.", "",
             "## Active tasks", section(root / "CURRENT_STATE.md", "Активные задачи") or "-", "",
             "## Owner question queue", section(root / "development" / "OPEN_QUESTIONS.md", "Очередь вопросов (autopilot)") or "-", "",
             "## Last owner decisions", *tail_rows(root / "docs" / "decisions" / "DECISIONS_LOG.md", 12), "",
             "## Last Orchestrator decisions", *tail_rows(root / "docs" / "decisions" / "ORCH_DECISIONS.md", 6), "",
             "## Recent commits on main", run(root, "git", "log", "--oneline", "-15", "main"), "",
             "## Unmerged agent branches", run(root, "git", "branch", "--no-merged", "main", "--format=%(refname:short) %(committerdate:relative)"), ""]
    if data.get("custom_instructions"):
        parts += ["## Compaction note", data["custom_instructions"], ""]
    out = root / "development" / "handoff" / "auto-checkpoint.md"
    try:
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text("\n".join(parts) + "\n", encoding="utf-8")
    except OSError:
        pass


if __name__ == "__main__":
    try:
        main()
    except Exception:  # a hook never breaks compaction
        pass
