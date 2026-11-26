#!/usr/bin/env python3
"""Documents of the knowledge base (owner #598-#600, O-112): scope, header fields, tags, size limits.

Shared by kb_check (rules) and kb_db (the docs catalogue). Pure functions over files; no database.

Header fields of a document (YAML frontmatter between '---' lines at the top):
  tags: [kind, area, state]       3-6 tags from development/TAGS.yaml: at least one kind, one area, one state
  related: [id or path, ...]      links of the chain (kb_db.py q chain ID)
  supersedes: [id or path, ...]
  implements: [id or path, ...]
An id is a registry id (TASK-0209, ADR-0001, RR-0005 ...), a decision #NNN or O-NNN; a path is repository-relative.
A document whose header cannot change (an ACCEPTED ADR) gets its tags from 'overrides' in TAGS.yaml.
"""
from __future__ import annotations

import datetime as dt
import re
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[2]
TAGS_FILE = "development/TAGS.yaml"
LINK_KEYS = ("related", "supersedes", "implements")
MIN_TAGS, MAX_TAGS = 3, 6
FRONT_RE = re.compile(r"\A---\n(.*?)\n---[ \t]*\n?", re.S)
FENCE = "\x60" * 3

# Tool-managed journals, the generated backlog and templates carry no tags.
EXEMPT = {"development/BACKLOG.md", "docs/decisions/DECISIONS_LOG.md", "development/TECH_DEBT.md",
          "development/handoff/auto-checkpoint.md"}  # rewritten by the PreCompact hook, never committed
EXEMPT_PREFIX = ("development/templates/", "docs/decisions/log/", "development/archive/")

# Size limits (#600), bytes. Warnings until LIMITS_ERROR_FROM, errors from that day.
KB = 1024
LIMITS_ERROR_FROM = dt.date(2026, 12, 3)
ALWAYS = ["CLAUDE.md", "CURRENT_STATE.md", "development/PLAN.md"]
ALWAYS_GLOBS = [".claude/rules/*.md", ".claude/agents/*.md"]
LIVING = ["development/ROADMAP.md", "docs/architecture/ARCHITECTURE.md", "docs/vision/PROJECT_VISION.md",
          "docs/ui/UI_DESIGN_SYSTEM.md", "docs/game-design/M_PLAY_LOOP.md"]
LIVING_GLOBS = [".claude/skills/*/SKILL.md"]
MONTH_LOG = re.compile(r"^docs/decisions/log/\d{4}-\d{2}\.md$")
LIMITS = {"always": 6 * KB, "living": 30 * KB, "monthly": 150 * KB}  # monthly: #608 (was 60 KB in #600)


def in_scope(rel: str) -> bool:
    """A document that must carry tags."""
    return (rel.endswith(".md") and rel.startswith(("docs/", "development/")) and rel not in EXEMPT
            and not rel.startswith(EXEMPT_PREFIX))


def scope_paths(root: Path = ROOT) -> list[str]:
    out = []
    for top in ("docs", "development"):
        out += [p.relative_to(root).as_posix() for p in (root / top).rglob("*.md")]
    return sorted(p for p in out if in_scope(p))


def _glob(root: Path, pattern: str) -> set[str]:
    return {p.relative_to(root).as_posix() for p in root.glob(pattern)}


def catalogue_paths(root: Path = ROOT) -> list[str]:
    """Every document of the docs catalogue: docs/, development/ and the files loaded into sessions."""
    out = {p.relative_to(root).as_posix() for top in ("docs", "development") for p in (root / top).rglob("*.md")}
    out |= {p for p in ALWAYS + LIVING if (root / p).exists()}
    for g in ALWAYS_GLOBS + LIVING_GLOBS:
        out |= _glob(root, g)
    return sorted(out)


def split_front(text: str) -> tuple[dict | None, str]:
    """(header fields or None, body). A header that is not a YAML mapping reads as None."""
    m = FRONT_RE.match(text)
    if not m:
        return None, text
    try:
        meta = yaml.safe_load(m.group(1))
    except yaml.YAMLError:
        return None, text[m.end():]
    return (meta if isinstance(meta, dict) else None), text[m.end():]


def as_list(v) -> list[str]:
    if v is None:
        return []
    if isinstance(v, (list, tuple)):
        return [str(x).strip() for x in v if str(x).strip()]
    return [s.strip() for s in str(v).split(",") if s.strip()]


# ---------------------------------------------------------------- tags


def _vocab_data(root: Path) -> dict:
    p = root / TAGS_FILE
    return (yaml.safe_load(p.read_text(encoding="utf-8")) or {}) if p.exists() else {}


def load_vocab(root: Path = ROOT) -> tuple[dict[str, str], dict[str, list[str]]]:
    """(tag -> facet, path -> tags of 'overrides'). Missing file: empty vocabulary."""
    data = _vocab_data(root)
    vocab = {str(tag): str(facet) for facet, spec in (data.get("facets") or {}).items()
             for tag in ((spec or {}).get("tags") or {})}
    overrides = {str(k): as_list(v) for k, v in (data.get("overrides") or {}).items()}
    return vocab, overrides


def vocab_problems(root: Path = ROOT) -> list[str]:
    """Rules of the vocabulary itself: <= 50 tags in the facets kind, area, state; a description and examples each."""
    if not (root / TAGS_FILE).exists():
        return [f"{TAGS_FILE} is missing"]
    facets = _vocab_data(root).get("facets") or {}
    out = []
    if set(facets) != {"kind", "area", "state"}:
        out.append(f"{TAGS_FILE}: facets must be kind, area, state (got {', '.join(map(str, facets))})")
    seen: dict[str, str] = {}
    for facet, spec in facets.items():
        for tag, t in ((spec or {}).get("tags") or {}).items():
            if tag in seen:
                out.append(f"{TAGS_FILE}: tag '{tag}' is in two facets ({seen[tag]}, {facet})")
            seen[tag] = facet
            if not re.fullmatch(r"[a-z][a-z0-9-]*", str(tag)):
                out.append(f"{TAGS_FILE}: tag '{tag}' must be lowercase latin with hyphens")
            if not isinstance(t, dict) or not t.get("description") or not t.get("examples"):
                out.append(f"{TAGS_FILE}: tag '{tag}' needs a description and examples")
    if len(seen) > 50:
        out.append(f"{TAGS_FILE}: {len(seen)} tags, the limit is 50 (#599)")
    return out


def doc_tags(rel: str, meta: dict | None, overrides: dict[str, list[str]]) -> list[str] | None:
    """The document's tags: header first, then overrides; None when it has none."""
    if meta and "tags" in meta:
        return as_list(meta.get("tags"))
    return overrides.get(rel)


def tag_problems(tags: list[str], vocab: dict[str, str]) -> list[str]:
    out = [f"unknown tag '{t}' (not in {TAGS_FILE})" for t in tags if t not in vocab]
    if len(set(tags)) != len(tags):
        out.append("a tag is repeated")
    if not MIN_TAGS <= len(tags) <= MAX_TAGS:
        out.append(f"{len(tags)} tags, a document has {MIN_TAGS}-{MAX_TAGS}")
    if not out:
        facets = [vocab[t] for t in tags]
        out += [f"no '{f}' tag" for f in ("kind", "area") if f not in facets]
        if facets.count("state") != 1:
            out.append("exactly one 'state' tag is needed")
    return out


# ---------------------------------------------------------------- title, summary, links


def title(meta: dict | None, body: str, rel: str) -> str:
    if meta and meta.get("title"):
        return str(meta["title"]).strip()
    m = re.search(r"^# +(.+)$", body, re.M)
    return m.group(1).strip() if m else Path(rel).stem


def summary(body: str, limit: int = 400) -> str:
    """'Краткий вывод' (or 'Вывод', or a task's 'Цель') section, else the first paragraph after the title;
    one line, cut to limit."""
    text = ""
    for name in ("Краткий вывод|Вывод", "Цель"):
        pat = r"^##+ +(?:\d+\. +)?(?:" + name + r")\b[^\n]*\n(.*?)(?=^##? |\Z)"
        text = next((m.group(1) for m in re.finditer(pat, body, re.S | re.M) if m.group(1).strip()), "")
        if text:
            break
    if not text.strip():
        paras = [p for p in re.split(r"\n\s*\n", re.sub(r"^# .*$", "", body, count=1, flags=re.M))
                 if p.strip() and not p.lstrip().startswith(("#", "|", "<!--", FENCE))]
        text = paras[0] if paras else ""
    s = " ".join(text.split())
    return s[:limit].rstrip() + ("..." if len(s) > limit else "")


def doc_date(meta: dict | None, rel: str) -> str | None:
    for k in ("updated", "date", "created", "approved_at"):
        m = re.search(r"\d{4}-\d{2}-\d{2}", str((meta or {}).get(k) or ""))
        if m:
            return m.group(0)
    m = re.search(r"(\d{4}-\d{2}-\d{2})", rel)
    return m.group(1) if m else None


def links(meta: dict | None) -> list[tuple[str, str]]:
    return [(k, t) for k in LINK_KEYS for t in as_list((meta or {}).get(k))]


def approx_tokens(text: str) -> int:
    """Rough token count: about 3.5 characters per token for mixed Russian and English text."""
    return round(len(text) / 3.5)


# ---------------------------------------------------------------- size limits


def _match(rel: str, globs: list[str]) -> bool:
    return any(Path(rel).match(g) and rel.count("/") == g.count("/") for g in globs)


def size_class(rel: str) -> tuple[str, int] | None:
    """(class, limit in bytes) of a file under the size rules (#600), or None."""
    if rel in ALWAYS or _match(rel, ALWAYS_GLOBS) or rel.endswith("memory/MEMORY.md"):
        return "always", LIMITS["always"]
    if rel in LIVING or _match(rel, LIVING_GLOBS):
        return "living", LIMITS["living"]
    if MONTH_LOG.match(rel):
        return "monthly", LIMITS["monthly"]
    return None


def memory_file(root: Path = ROOT) -> Path | None:
    """The owner's auto-memory index of this project (outside the repository), when it exists."""
    import kb_db  # the main checkout, also from a worktree
    main = kb_db.git_common_parent() or root
    p = Path.home() / ".claude" / "projects" / ("-" + str(main).strip("/").replace("/", "-")) / "memory" / "MEMORY.md"
    return p if p.exists() else None


def limit_files(root: Path = ROOT, memory: bool = True) -> list[tuple[str, Path]]:
    """(name, file) of every file under the size rules."""
    out = [(rel, root / rel) for rel in catalogue_paths(root) if size_class(rel)]
    mem = memory_file(root) if memory else None
    if mem:
        out.append(("~/.claude/projects/.../memory/MEMORY.md", mem))
    return out


def over_limit(rel: str, size: int) -> str | None:
    c = size_class(rel)
    if c and size > c[1]:
        return f"{size / KB:.1f} KB, the limit for '{c[0]}' files is {c[1] // KB} KB (#600)"
    return None
