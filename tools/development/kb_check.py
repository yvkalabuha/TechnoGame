#!/usr/bin/env python3
"""Knowledge base control: documents and decisions must agree with known facts.

  python3 tools/development/kb_check.py check            all living documents
  python3 tools/development/kb_check.py check --staged   staged Markdown files (pre-commit)
  python3 tools/development/kb_check.py check --file F   one file (PostToolUse hook)
  python3 tools/development/kb_check.py check --brief    one line (SessionStart hook)
  python3 tools/development/kb_check.py related "text"   known facts and earlier decisions on a topic

Errors (exit 1):
  - an id that does not exist: #NNN above the last owner decision, ADR/TASK/EXP/HYP/RR/MS not in
    the registry, DEBT, RISK or O-NNN not in their tables.
Warnings (exit 0):
  - a fact of development/FACTS.yaml whose probe no longer matches the repository (drift);
  - a stale pattern of a fact in a living document;
  - an ADR named with a status that differs from the registry;
  - a repository path in backticks that does not exist.

`related` is a retrieval aid, not a judge: before a decision is recorded or a question is put to
the owner, read what it prints and compare.
"""
from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
import registry  # noqa: E402

FACTS = ROOT / "development" / "FACTS.yaml"
DECISIONS = ROOT / "docs" / "decisions" / "DECISIONS_LOG.md"
ORCH = ROOT / "docs" / "decisions" / "ORCH_DECISIONS.md"
DEBT = ROOT / "development" / "TECH_DEBT.md"
RISKS = ROOT / "development" / "RISKS.md"

# Documents that describe the present state and must not carry outdated claims.
LIVING = ["CLAUDE.md", "CURRENT_STATE.md", "development/PLAN.md", "development/ROADMAP.md",
          "docs/architecture/ARCHITECTURE.md", "docs/vision/PROJECT_VISION.md",
          "docs/ui/UI_DESIGN_SYSTEM.md", "docs/game-design/M_PLAY_LOOP.md"]
LIVING_GLOBS = [".claude/rules/*.md", ".claude/agents/*.md", ".claude/skills/*/SKILL.md"]
PATH_ROOTS = ("src/", "tests/", "tools/", "docs/", "development/", "lab/", "mods/", ".claude/", ".github/")
ADR_STATUS = re.compile(r"\b(PROPOSED|ACCEPTED|SUPERSEDED|DEPRECATED|REJECTED)\b")


def living_files() -> list[Path]:
    out = [ROOT / p for p in LIVING if (ROOT / p).exists()]
    for g in LIVING_GLOBS:
        out += sorted(ROOT.glob(g))
    for it in registry.load_items([]):
        if it.type.prefix == "MS" and it.meta.get("status") != "RELEASED":
            out.append(it.path)
    return out


def load_facts() -> list[dict]:
    if not FACTS.exists():
        return []
    return (yaml.safe_load(FACTS.read_text(encoding="utf-8")) or {}).get("facts", [])


def known_ids() -> dict[str, set[str] | int]:
    items = {i.id for i in registry.load_items([])}
    dec = [int(m) for m in re.findall(r"^\| (\d+) \|", DECISIONS.read_text(encoding="utf-8"), re.M)]
    def table(path: Path, pat: str) -> set[str]:
        return set(re.findall(pat, path.read_text(encoding="utf-8"), re.M)) if path.exists() else set()
    return {"items": items, "max_decision": max(dec) if dec else 0,
            "debt": table(DEBT, r"^\| (DEBT-\d{4}) "), "risk": table(RISKS, r"^\| (RISK-\d{4}) "),
            "orch": table(ORCH, r"^\| (O-\d{3}) ")}


def rel(p: Path) -> str:
    try:
        return p.resolve().relative_to(ROOT).as_posix()
    except ValueError:
        return str(p)


def added_lines(path: Path) -> set[int]:
    """Line numbers added or changed in the staged version of the file."""
    out = subprocess.run(["git", "diff", "--cached", "-U0", "--", rel(path)], cwd=ROOT,
                         capture_output=True, text=True).stdout
    lines: set[int] = set()
    for m in re.finditer(r"^@@ -\S+ \+(\d+)(?:,(\d+))? @@", out, re.M):
        start, count = int(m.group(1)), int(m.group(2) or 1)
        lines.update(range(start, start + count))
    return lines


def check_file(path: Path, ids: dict, facts: list[dict], living: bool,
               errors: list[str], warnings: list[str], only: set[int] | None = None) -> None:
    try:
        text = path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return
    where = rel(path)
    statuses = {i.id: i.meta.get("status") for i in registry.load_items([]) if i.type.prefix == "ADR"}
    for n, line in enumerate(text.splitlines(), 1):
        if only is not None and n not in only:
            continue  # pre-commit judges only the lines this commit adds: old logs are history
        for m in re.finditer(r"(?<![\w/&])#(\d{2,4})\b", line):
            if int(m.group(1)) > ids["max_decision"] and not re.search(r"[0-9A-Fa-f]{6}", line[m.start():m.start() + 8]):
                errors.append(f"{where}:{n}: decision #{m.group(1)} does not exist (last is #{ids['max_decision']})")
        for m in re.finditer(r"\b((?:ADR|TASK|EXP|HYP|RR)-\d{4}|MS-M\d+)\b", line):
            if m.group(1) not in ids["items"]:
                errors.append(f"{where}:{n}: {m.group(1)} is not in the registry")
        for m in re.finditer(r"\b(DEBT-\d{4})\b", line):
            if m.group(1) not in ids["debt"]:
                errors.append(f"{where}:{n}: {m.group(1)} is not in TECH_DEBT.md")
        for m in re.finditer(r"\b(RISK-\d{4})\b", line):
            if m.group(1) not in ids["risk"]:
                errors.append(f"{where}:{n}: {m.group(1)} is not in RISKS.md")
        for m in re.finditer(r"\b(O-\d{3})\b", line):
            if m.group(1) not in ids["orch"]:
                errors.append(f"{where}:{n}: {m.group(1)} is not in ORCH_DECISIONS.md")
        if not living:
            continue
        for f in facts:
            for pat in f.get("stale_patterns", []):
                if re.search(pat, line) and "superseded" not in line.lower() and "отмен" not in line.lower():
                    warnings.append(f"{where}:{n}: contradicts fact '{f['id']}' ({f['statement'][:80]}): /{pat}/")
        st = ADR_STATUS.findall(line)
        if len(set(st)) == 1:
            for adr in re.findall(r"\bADR-(\d{4})\b", line):
                real = statuses.get(f"ADR-{adr}")
                if real and real != st[0]:
                    warnings.append(f"{where}:{n}: ADR-{adr} is {real} in the registry, the line says {st[0]}")
        for m in re.finditer(r"`([\w.\-/]+)`", line):
            p = m.group(1)
            if p.startswith(PATH_ROOTS) and "/" in p.rstrip("/") and not (ROOT / p).exists() \
                    and not re.search(r"NNNN|XXXX|<|\*", p):
                warnings.append(f"{where}:{n}: path `{p}` does not exist")


def check_facts(facts: list[dict], warnings: list[str]) -> None:
    for f in facts:
        pr = f.get("probe")
        if not pr:
            continue
        target = ROOT / pr["file"]
        if not target.exists():
            warnings.append(f"FACTS.yaml: fact '{f['id']}': probe file {pr['file']} is missing")
            continue
        text = target.read_text(encoding="utf-8", errors="replace")
        if "count" in pr:
            got = str(len(re.findall(pr["count"], text, re.M)))
        else:
            m = re.search(pr["regex"], text, re.S)
            got = m.group(1) if m else "no match"
        if got != str(pr["expect"]):
            warnings.append(f"FACTS.yaml: fact '{f['id']}' drifted: {pr['file']} gives {got}, the fact says "
                            f"{pr['expect']}. Update the fact and the documents that state it.")


def cmd_check(args) -> int:
    ids, facts = known_ids(), load_facts()
    errors: list[str] = []
    warnings: list[str] = []
    living = {p.resolve() for p in living_files()}
    if args.file:
        targets = [Path(args.file) if Path(args.file).is_absolute() else ROOT / args.file]
    elif args.staged:
        out = subprocess.run(["git", "diff", "--cached", "--name-only", "--diff-filter=ACM"], cwd=ROOT,
                             capture_output=True, text=True).stdout.split()
        targets = [ROOT / f for f in out if f.endswith((".md", ".yaml")) and not f.startswith("lab/")]
    else:
        targets = sorted(living)
    for t in targets:
        if t.suffix == ".md" and t.exists():
            check_file(t, ids, facts, t.resolve() in living, errors, warnings,
                       added_lines(t) if args.staged else None)
    if not args.file:
        check_facts(facts, warnings)
    if args.brief:
        if errors or warnings:
            print(f"{len(errors)} error(s), {len(warnings)} warning(s) (python3 tools/development/kb_check.py check)")
        return 0
    for e in errors:
        print("ERROR  ", e)
    if not args.errors_only:
        for w in warnings:
            print("WARNING", w)
    print(f"kb_check: {len(errors)} error(s), {len(warnings)} warning(s)")
    return 1 if errors else 0


def cmd_related(args) -> int:
    text = args.text.lower()
    words = {w for w in re.findall(r"[\w-]{4,}", text)}
    nums = set(re.findall(r"#(\d{1,4})", args.text)) | set(re.findall(r"\b(?:19|20)\d\d\b", args.text))
    shown = 0
    for f in load_facts():
        if any(str(k).lower() in text for k in f.get("keywords", [])):
            print(f"FACT {f['id']}: {f['statement']}  [{f.get('source', '')}]")
            shown += 1
    rows = re.findall(r"^\| (\d+) \| (\S+) \| (.*?) \| [^|]*\|\s*$", DECISIONS.read_text(encoding="utf-8"), re.M)
    scored = []
    for num, date, body in rows:
        low = body.lower()
        score = sum(1 for w in words if w in low) + (5 if num in nums else 0) + sum(2 for n in nums if n in body)
        if score >= args.min:
            scored.append((score, int(num), date, body))
    for score, num, date, body in sorted(scored, key=lambda r: (-r[0], -r[1]))[:args.limit]:
        print(f"DECISION #{num} ({date}): {body[:260]}")
        shown += 1
    if not shown:
        print("nothing related found in FACTS.yaml or DECISIONS_LOG.md")
    return 0


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("check")
    c.add_argument("--staged", action="store_true")
    c.add_argument("--file")
    c.add_argument("--brief", action="store_true")
    c.add_argument("--errors-only", action="store_true")
    c.set_defaults(func=cmd_check)
    r = sub.add_parser("related")
    r.add_argument("text")
    r.add_argument("--limit", type=int, default=8)
    r.add_argument("--min", type=int, default=3)
    r.set_defaults(func=cmd_related)
    args = ap.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
