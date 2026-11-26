#!/usr/bin/env python3
"""Layout of the two growing journals (owner #598, #599): the decisions log and the tech debt table.

Decisions:
  docs/decisions/DECISIONS_LOG.md          short header, index of the files, the latest rows; new rows are appended here
  docs/decisions/log/YYYY-MM.md            rows by the month of their date
  docs/decisions/log/archive-0001-0400.md  rows #1-#400
Tech debt:
  development/TECH_DEBT.md                 open and partly closed debts; new rows are appended here
  development/archive/TECH_DEBT-closed.md  closed debts

Every tool reads all files of a journal through this module (sources/text): a row is found wherever it is.
The read order is archive, months, then the main file; a number found in two files is a kb_check error.

  python3 tools/development/kblog.py rollover [--keep 12]   move rows out of the main files (re-runnable)
  python3 tools/development/kblog.py count                  rows per file
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

import kb_check  # a sibling: on sys.path when run as a script or imported by the tools here

ROOT = kb_check.ROOT
DECISIONS = kb_check.JOURNAL_DECISIONS
LOG_DIR = kb_check.JOURNAL_LOG_DIR
ARCHIVE_UPTO = 400
DECISIONS_ARCHIVE = f"{LOG_DIR}/archive-0001-{ARCHIVE_UPTO:04d}.md"
DEBT = kb_check.JOURNAL_DEBT
DEBT_ARCHIVE = kb_check.JOURNAL_DEBT_ARCHIVE
KEEP = 12  # rows left in DECISIONS_LOG.md after a rollover (the PreCompact checkpoint shows the last 12)

LOG_FILE = kb_check.LOG_FILE
DECISION_START = re.compile(r"^\| (\d+) \|")
DEBT_START = re.compile(r"^\| (DEBT-\d{4}) \|")
INDEX_START = "<!-- kblog:index:start -->"
INDEX_END = "<!-- kblog:index:end -->"
TABLE_HEAD = "| # | Дата | Решение | Ссылка |\n|---|---|---|---|\n"
DEBT_HEAD = "| ID | Долг | Где | Почему допущен | План | Статус |\n|---|---|---|---|---|---|\n"
Q = "\x60"  # backtick


# ---------------------------------------------------------------- reading (kb_check owns the reader)

from kb_check import (debt_paths, decision_paths, is_decisions_file, journal_sources as sources,  # noqa: E402,F401
                      journal_text as text)


def duplicates(kind: str, root: Path = ROOT) -> dict[str, list[str]]:
    """Row key -> files, for keys found in more than one file (or twice in one)."""
    start = DECISION_START if kind == "decisions" else DEBT_START
    seen: dict[str, list[str]] = {}
    for path, body in sources(kind, root):
        for key, _ in raw_rows(body, start)[1]:
            seen.setdefault(key, []).append(path)
    return {k: v for k, v in seen.items() if len(v) > 1}


# ---------------------------------------------------------------- rows


def raw_rows(body: str, start: re.Pattern) -> tuple[list[str], list[tuple[str, str]], list[str]]:
    """(lines before the first row, [(key, raw row text)], lines after the last row). A row runs from a line
    matching start to the first line that ends with an unescaped pipe (cells with lists and blank lines).
    Text between two rows stays attached to the row before it."""
    lines = body.split("\n")
    head: list[str] = []
    rows: list[tuple[str, str]] = []
    tail: list[str] = []
    i = 0
    while i < len(lines):
        m = start.match(lines[i])
        j = i
        while m and j < len(lines) and not (lines[j].rstrip().endswith("|") and not lines[j].rstrip().endswith("\\|")):
            j += 1
        if not m or j >= len(lines):  # not a row, or a row that never closes
            (tail if rows else head).append(lines[i])
            i += 1
            continue
        if tail:
            k, r = rows[-1]
            rows[-1] = (k, r + "\n" + "\n".join(tail))
            tail = []
        rows.append((m.group(1), "\n".join(lines[i:j + 1])))
        i = j + 1
    if rows and any(l.strip() for l in tail):  # trailing text that is not just blank lines
        while tail and not tail[-1].strip():
            tail.pop()
    else:
        tail = []
    return head, rows, tail


def row_month(raw: str) -> str | None:
    m = re.match(r"^\| \d+ \| *\**(\d{4}-\d{2})-\d{2}", raw)
    return m.group(1) if m else None


def debt_closed(raw: str) -> bool:
    import kb_db  # the debt status rule of the index
    cells = kb_db.split_row(" ".join(l.strip() for l in raw.splitlines()))
    return kb_db.debt_status(cells[-1] if cells else "") == "closed"


# ---------------------------------------------------------------- rollover


def _file_rows(path: Path, start: re.Pattern) -> dict[str, str]:
    if not path.exists():
        return {}
    return dict(raw_rows(path.read_text(encoding="utf-8"), start)[1])


def _write_rows(path: Path, title: str, intro: str, head: str, rows: dict[str, str], key) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    body = "\n".join(rows[k] for k in sorted(rows, key=key))
    path.write_text(f"# {title}\n\n{intro}\n\n{head}{body}\n", encoding="utf-8")


def index_block(root: Path) -> str:
    lines = [INDEX_START]
    for p in decision_paths(root):
        if p == DECISIONS:
            continue
        nums = sorted(int(k) for k in _file_rows(root / p, DECISION_START))
        if nums:
            lines.append(f"- [{Path(p).name}](log/{Path(p).name}) — #{nums[0]}–#{nums[-1]}, строк: {len(nums)}")
    lines.append(INDEX_END)
    return "\n".join(lines)


DECISIONS_INTRO = (
    "Журнал решений владельца (#598, #599). Только добавление записей; старые не меняются. Отменённое решение "
    "помечается новой записью со ссылкой, а в старой строке — пометкой SUPERSEDED (там, где строка лежит сейчас).\n\n"
    f"- **Новые строки добавляются сюда**, в конец таблицы ниже. {Q}python3 tools/development/kblog.py rollover{Q} "
    f"переносит их в месячные файлы {Q}docs/decisions/log/ГГГГ-ММ.md{Q} (строки #1–#{ARCHIVE_UPTO} — в архив) и "
    "оставляет здесь последние {keep}.\n"
    f"- Поиск без чтения файлов: {Q}python3 tools/development/kb_db.py q decisions text~слово{Q}. Все инструменты "
    "(kb_check, kb_guard, kb_db, metrics, land.py) читают все файлы журнала.\n\nФайлы журнала:")
LOG_INTRO = "Строки журнала решений владельца; правила и новые строки — [DECISIONS_LOG.md](../DECISIONS_LOG.md)."


def rollover_decisions(root: Path, keep: int = KEEP) -> dict[str, int]:
    main = root / DECISIONS
    _, rows, tail = raw_rows(main.read_text(encoding="utf-8"), DECISION_START)
    targets = {p: _file_rows(root / p, DECISION_START) for p in decision_paths(root) if p != DECISIONS}
    month = None
    for p in sorted(targets):
        for r in targets[p].values():
            month = row_month(r) or month
    split = max(len(rows) - keep, 0)
    stay = []
    moved = 0
    for idx, (num, raw) in enumerate(rows):
        month = row_month(raw) or month
        dest = DECISIONS_ARCHIVE if int(num) <= ARCHIVE_UPTO else f"{LOG_DIR}/{month}.md" if month else None
        if idx >= split or dest is None:
            stay.append((num, raw))
            continue
        targets.setdefault(dest, {})[num] = raw
        moved += 1
    # the main file is the newest place of a row: drop older copies in other files
    newest = {num: (DECISIONS, raw) for num, raw in stay}
    for p, rows_ in targets.items():
        for num in list(rows_):
            if num in newest and newest[num][0] != p:
                rows_.pop(num)
            else:
                newest[num] = (p, rows_[num])
    for p, rows_ in targets.items():
        stem = Path(p).stem
        title = f"DECISIONS LOG — архив #1–#{ARCHIVE_UPTO}" if stem.startswith("archive-") else f"DECISIONS LOG — {stem}"
        if rows_:
            _write_rows(root / p, title, LOG_INTRO, TABLE_HEAD, rows_, key=int)
        elif (root / p).exists():
            (root / p).unlink()
    body = "\n".join(r for _, r in stay)
    out = "# DECISIONS LOG\n\n" + DECISIONS_INTRO.format(keep=keep) + "\n" + index_block(root) + "\n\n" + TABLE_HEAD
    out += body + ("\n" if body else "") + ("\n" + "\n".join(tail) + "\n" if tail else "")
    main.write_text(out, encoding="utf-8")
    return {"moved": moved, "kept": len(stay)}


DEBT_NOTE = (f"Закрытые долги переносятся в {Q}development/archive/TECH_DEBT-closed.md{Q} "
             f"({Q}python3 tools/development/kblog.py rollover{Q}); поиск по всем: {Q}kb_db.py q debt text~слово{Q}.")


def rollover_debt(root: Path) -> dict[str, int]:
    main = root / DEBT
    head, rows, tail = raw_rows(main.read_text(encoding="utf-8"), DEBT_START)
    arch = _file_rows(root / DEBT_ARCHIVE, DEBT_START)
    stay = []
    moved = 0
    for key, raw in rows:
        if debt_closed(raw):
            arch[key] = raw
            moved += 1
        else:
            stay.append((key, raw))
    if moved:
        _write_rows(root / DEBT_ARCHIVE, "TECH DEBT — архив закрытых долгов",
                    f"Закрытые долги из [TECH_DEBT.md](../TECH_DEBT.md) ({Q}python3 tools/development/kblog.py rollover{Q}).",
                    DEBT_HEAD, arch, key=lambda k: k)
    head_text = "\n".join(head).rstrip("\n")
    if DEBT_NOTE not in head_text and head_text.startswith("# TECH DEBT\n"):
        head_text = head_text.replace("# TECH DEBT\n", "# TECH DEBT\n\n" + DEBT_NOTE + "\n", 1)
    body = "\n".join(r for _, r in stay)
    main.write_text(head_text + "\n" + body + "\n" + ("\n" + "\n".join(tail) + "\n" if tail else ""), encoding="utf-8")
    return {"moved": moved, "kept": len(stay)}


def counts(root: Path = ROOT) -> dict[str, int]:
    out = {p: len(_file_rows(root / p, DECISION_START)) for p in decision_paths(root)}
    out.update({p: len(_file_rows(root / p, DEBT_START)) for p in debt_paths(root)})
    return out


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("rollover")
    r.add_argument("--keep", type=int, default=KEEP)
    sub.add_parser("count")
    args = ap.parse_args(argv)
    if args.cmd == "rollover":
        d = rollover_decisions(ROOT, args.keep)
        t = rollover_debt(ROOT)
        print(f"kblog: decisions moved {d['moved']}, kept {d['kept']}; closed debts moved {t['moved']}, open kept {t['kept']}")
        return 0
    for p, n in counts().items():
        print(f"{n:5}  {p}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
