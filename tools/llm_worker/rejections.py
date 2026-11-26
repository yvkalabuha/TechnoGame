"""Rejection memory (owner #591): development/review-rejections.yaml.

A review or gate finding that the Orchestrator checked and rejected is written here with its reason, so that
Codex, DeepSeek and Claude agents do not raise it again. Writers: `review-confirm --rejected` (review and gate
records) and `tools/development/rejections.py add`. Readers: the review, gate and research prompts (the text goes
through the same gitleaks scan as the rest of the prompt) and `rejections.py for`.

Entry: id (stable, "provider|area|normalised summary"), provider, area (repo path, or a short topic), summary,
reason, ref (decision: ADR, #NNN, TASK-NNNN), date, source ("<record>.json#N", empty for manual entries).
Matching a new finding to an entry is a heuristic: the same file and a word overlap of the texts (see similar()).
"""
from __future__ import annotations

import datetime as dt
import fcntl
import json
import os
import re
from pathlib import Path

import yaml

JOURNAL_REL = Path("development") / "review-rejections.yaml"
MAX_LINES = 8  # entries added to one prompt
MAX_TEXT = 300
MIN_OVERLAP = 0.4  # word-set overlap (Jaccard) for "the same finding"
TOPIC_WORDS = 2  # shared words that make an entry relevant to a topic
FIELDS = ("id", "provider", "area", "summary", "reason", "ref", "date", "source")
HEADER = ("# Rejected review findings and why (owner #591). Written by llm_worker review-confirm --rejected and\n"
          "# tools/development/rejections.py add; read into review, gate and research prompts. Do not edit ids.\n")
STOP = frozenset("the and for with that this from are not but can has have its into when then than".split())


def journal_path(root) -> Path:
    return Path(root) / JOURNAL_REL


def words(text: str) -> frozenset:
    return frozenset(w for w in re.sub(r"[\W_]+", " ", str(text).casefold()).split() if len(w) > 2 and w not in STOP)


def norm_path(area: str) -> str:
    """The file part of an area: 'a/b.py:12' -> 'a/b.py'."""
    return re.sub(r":\d+(-\d+)?\Z", "", str(area).strip())


def entry_id(provider: str, area: str, summary: str) -> str:
    return f"{provider}|{norm_path(area)}|{' '.join(sorted(words(summary)))}"


def load(path) -> list[dict]:
    """Raises ValueError for a malformed file; a missing file is an empty journal."""
    path = Path(path)
    if not path.exists():
        return []
    try:
        data = yaml.safe_load(path.read_text(encoding="utf-8"))
    except yaml.YAMLError as e:
        raise ValueError(f"{path.name}: invalid YAML: {e}") from None
    if data is None:
        return []
    if not isinstance(data, dict) or not isinstance(data.get("rejections"), list):
        raise ValueError(f"{path.name}: the root must be a mapping with a 'rejections' list")
    out = []
    for n, e in enumerate(data["rejections"], 1):
        if not isinstance(e, dict):
            raise ValueError(f"{path.name}: entry #{n} is not a mapping")
        e = {k: ("" if e.get(k) is None else e[k]) for k in FIELDS if k in e}
        if isinstance(e.get("date"), dt.date):
            e["date"] = e["date"].isoformat()
        out.append(e)
    return out


def validate(entries: list[dict]) -> list[str]:
    errs, seen = [], set()
    for n, e in enumerate(entries, 1):
        for k in ("id", "provider", "area", "summary", "reason", "date"):
            if not isinstance(e.get(k), str) or not e[k].strip():
                errs.append(f"entry #{n}: {k} must be a non-empty string")
        for k in ("ref", "source"):
            if k in e and not isinstance(e[k], str):
                errs.append(f"entry #{n}: {k} must be a string")
        if isinstance(e.get("date"), str) and not re.fullmatch(r"\d{4}-\d{2}-\d{2}", e["date"]):
            errs.append(f"entry #{n}: bad date {e['date']!r}")
        if e.get("id") in seen:
            errs.append(f"entry #{n}: duplicate id")
        seen.add(e.get("id"))
    return errs


def _save(path: Path, entries: list[dict]) -> None:
    body = yaml.safe_dump({"rejections": [{k: e[k] for k in FIELDS if k in e} for e in entries]},
                          sort_keys=False, allow_unicode=True, width=110)
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(f".{path.name}.{os.getpid()}.tmp")
    try:
        with tmp.open("w", encoding="utf-8") as f:
            f.write(HEADER + body)
            f.flush()
            os.fsync(f.fileno())
        os.replace(tmp, path)
    except BaseException:
        tmp.unlink(missing_ok=True)
        raise


def _locked(path: Path, change) -> None:
    """Read-modify-write under an exclusive lock on a sidecar file (the journal itself is replaced atomically)."""
    lock_dir = path.parents[1] / ".claude" / ".state"  # git-ignored: the lock never reaches a commit
    lock_dir.mkdir(parents=True, exist_ok=True)
    with open(lock_dir / f"{path.name}.lock", "w") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        entries = load(path)
        entries = change(entries)
        _save(path, entries)


def add(path, provider, area, summary, reason, ref="", source="", today=None) -> dict:
    """Add or refresh an entry (same id: the reason, ref, source and date are replaced). Returns the entry."""
    path = Path(path)
    entry = {"id": entry_id(provider, area, summary), "provider": provider, "area": norm_path(area)[:MAX_TEXT],
             "summary": " ".join(str(summary).split())[:MAX_TEXT], "reason": " ".join(str(reason).split())[:MAX_TEXT],
             "ref": ref, "date": today or dt.date.today().isoformat(), "source": source}
    if not entry["summary"] or not entry["reason"]:
        raise ValueError("summary and reason are required")

    def change(entries):
        return [e for e in entries if e.get("id") != entry["id"]] + [entry]

    _locked(path, change)
    return entry


def drop_source(path, source) -> int:
    """Remove entries written from a finding that was later confirmed. Returns how many."""
    path = Path(path)
    if not path.exists():
        return 0
    n = [0]

    def change(entries):
        keep = [e for e in entries if e.get("source") != source]
        n[0] = len(entries) - len(keep)
        return keep

    _locked(path, change)
    return n[0]


def similar(a: str, b: str) -> bool:
    wa, wb = words(a), words(b)
    return bool(wa and wb) and len(wa & wb) / len(wa | wb) >= MIN_OVERLAP


def finding_matches(entry: dict, path: str, text: str) -> bool:
    """A new finding repeats a rejected one: same file (or entry area is a topic) and similar words."""
    return norm_path(path) == entry.get("area") and similar(text, entry.get("summary", ""))


def relevant(entries: list[dict], files=(), topic: str = "", limit: int = MAX_LINES) -> list[dict]:
    """Entries whose area is one of the files (or a directory above one), or whose text shares TOPIC_WORDS words
    with the topic. Newest first, at most `limit`."""
    files = [norm_path(f) for f in files]
    tw = words(topic)
    hit = []
    for e in entries:
        area = e.get("area", "")
        in_files = any(f == area or f.startswith(area.rstrip("/") + "/") for f in files if area)
        on_topic = len(tw & words(area + " " + e.get("summary", ""))) >= TOPIC_WORDS if tw else False
        if in_files or on_topic:
            hit.append(e)
    hit.sort(key=lambda e: e.get("date", ""), reverse=True)  # sort is stable: equal dates keep file order
    return hit[:limit]


def as_items(entries: list[dict]) -> list[dict]:
    """The shape the review prompt fence uses: oldest first."""
    return [{"path": e["area"], "claim": e["summary"],
             "reason": e["reason"] + (f" ({e['ref']})" if e.get("ref") else "")} for e in reversed(entries)]


def load_relevant(root, files=(), topic: str = "", log=print) -> list[dict]:
    """Relevant journal entries of the checkout at `root`; an unreadable journal is a warning, never a failed review."""
    try:
        return relevant(load(journal_path(root)), files, topic)
    except (ValueError, OSError) as e:
        log(f"warning: rejection journal skipped ({e})")
        return []


def text_block(entries: list[dict]) -> str:
    """For people and Claude agents (rejections.py for), Russian heading."""
    if not entries:
        return ""
    lines = ["Ранее отклонено и почему:"]
    for e in entries:
        ref = f" [{e['ref']}]" if e.get("ref") else ""
        lines.append(f"- {e['area']} ({e['provider']}, {e['date']}): {e['summary']} | причина: {e['reason']}{ref}")
    return "\n".join(lines)


def repeat_stats(entries: list[dict], records: list[dict], since: str, until: str) -> dict:
    """Share of new review findings (records created in [since, until), dates YYYY-MM-DD) that repeat a finding
    rejected earlier: the entry is older than the record or equal in date, and was not written from this record.
    records: [{"name", "created_at", "findings": [{"path", "claim"}]}]."""
    total = matched = 0
    for r in records:
        day = str(r.get("created_at", ""))[:10]
        if not (since <= day < until):
            continue
        for f in r.get("findings", []):
            total += 1
            if any(e.get("date", "9") <= day and not str(e.get("source", "")).startswith(r["name"] + "#")
                   and finding_matches(e, f.get("path", ""), f.get("claim", "")) for e in entries):
                matched += 1
    return {"findings": total, "repeats": matched, "share_pct": round(100 * matched / total, 1) if total else None}


def load_records(runs_dir) -> list[dict]:
    """Review records of a runs directory as repeat_stats input; unreadable files are skipped."""
    out = []
    for p in sorted(Path(runs_dir).glob("review-*.json")):
        if p.name.endswith(("-invalid.json", ".confirm.json")):
            continue
        try:
            rec = json.loads(p.read_text(encoding="utf-8"))
            fs = rec["result"]["findings"]
            if rec.get("kind") == "review" and isinstance(fs, list):
                out.append({"name": p.name, "created_at": rec.get("created_at", ""),
                            "findings": [{"path": str(f.get("path", "")), "claim": str(f.get("claim", ""))}
                                         for f in fs if isinstance(f, dict)]})
        except (OSError, ValueError, KeyError, TypeError, AttributeError):
            continue
    return out
