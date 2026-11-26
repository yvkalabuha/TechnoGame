#!/usr/bin/env python3
"""Platform knowledge base in PostgreSQL: a derived, read-only index (owner #590).

Files in git stay the source of truth. The database is rebuilt from them and
can be dropped at any time, except the two service journals (hook_refusals,
land_failures), which keep soft-deleted rows (deleted_at) between rebuilds.

  kb_db.py build [--quiet]            rebuild the index from files
  kb_db.py q TABLE [filter ...] [--json]  short filter query, one line per row
  kb_db.py sql "SELECT ..."           SQL, read-only by default (or SQL on stdin)
  kb_db.py status                     database, row counts, freshness
  kb_db.py forget TABLE KEY_PREFIX    soft-delete a journal row

Tables: items, links, decisions, orch, debt, questions, errors, agents,
refusals, landfails.  Filters: key=value  key!=value  key~text (full text, russian +
simple, or substring)  key>v  key<v  key>=v  key<=v, plus since=DATE until=DATE
limit=N order=[-]col, and silent>2h for agents.  For text columns "=" is a
case-insensitive match; for debt area, titles and texts it is a substring match.
Examples:
  kb_db.py q debt status=open area=Lab
  kb_db.py q decisions text~наука since=2026-11-20
  kb_db.py q items status=REVIEW agent=developer
  kb_db.py q agents 'silent>2h'     (quote it: the shell reads > as a redirect; silent=2h also works)

"sql" only sets default_transaction_read_only: this is a guard against slips, not a
sandbox (SQL can lift it); backslash commands are refused. Connection: local socket, peer authentication of the current user, database
singularity_kb (env SINGULARITY_KB_DB, created on first use). No passwords, no
driver: the psql client is used. Without PostgreSQL "build --quiet" does nothing.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))

SOCKET_DIR = os.environ.get("SINGULARITY_KB_SOCKET", "/var/run/postgresql")
DEFAULT_DB = "singularity_kb"
PSQL_TIMEOUT = 60
GIT_TIMEOUT = 10
# Bump when a table or column changes: it forces a rebuild of an index made by older code.
SCHEMA = 1
DATE_RE = re.compile(r"\d{4}-\d{2}-\d{2}")

# --------------------------------------------------------------------------
# Schema: column -> type; the order is the COPY order.
# --------------------------------------------------------------------------

TABLES: dict[str, dict] = {
    "items": dict(sql="items", text="title", date="updated", show=["id", "status", "agent", "level", "milestone", "title"],
                  cols={"id": "text", "type": "text", "status": "text", "level": "text", "milestone": "text",
                        "agent": "text", "category": "text", "priority": "text", "needs_owner": "bool",
                        "approved_by": "text", "approved_at": "date", "approval_ref": "text", "path": "text",
                        "title": "text", "updated": "date"},
                  fuzzy={"title"}),
    "links": dict(sql="item_links", text=None, date=None, show=["from_id", "kind", "to_id"],
                  cols={"from_id": "text", "to_id": "text", "kind": "text"}, fuzzy=set()),
    "decisions": dict(sql="decisions", text="text", date="date", show=["num", "date", "text"],
                      cols={"num": "int", "date": "date", "text": "text", "source": "text", "ref": "text",
                            "superseded_by": "int"}, fuzzy={"text", "ref"}),
    "orch": dict(sql="orch_decisions", text="text", date="date", show=["id", "date", "text"],
                 cols={"id": "text", "num": "int", "date": "date", "text": "text", "basis": "text",
                       "applied": "text"}, fuzzy={"text", "basis", "applied"}),
    "debt": dict(sql="debt", text="text", date="closed", show=["id", "status", "area", "text"],
                 cols={"id": "text", "text": "text", "area": "text", "why": "text", "plan": "text",
                       "status": "text", "status_text": "text", "closed": "date"}, fuzzy={"text", "area", "plan"}),
    "questions": dict(sql="questions", text="text", date=None, show=["id", "kind", "open", "text"],
                      cols={"id": "text", "kind": "text", "text": "text", "blocks": "text", "added": "text",
                            "open": "bool"}, fuzzy={"text", "blocks"}),
    "errors": dict(sql="errors", text="symptom", date="date", show=["id", "date", "status", "area", "repeats", "symptom"],
                   cols={"id": "text", "date": "date", "executor": "text", "kind": "text", "area": "text",
                         "symptom": "text", "cause": "text", "found_by": "text", "cost": "text", "fix": "text",
                         "repeats": "int", "last_repeat": "date", "status": "text", "key": "text"},
                   fuzzy={"symptom", "cause", "fix"}),
    "agents": dict(sql="agent_runs", text="description", date="start_t", show=["agent_id", "type", "start_t", "stop_t", "tasks", "description"],
                   cols={"agent_id": "text", "type": "text", "description": "text", "tasks": "text",
                         "start_t": "ts", "stop_t": "ts", "session": "text"}, fuzzy={"description", "tasks"}),
    "refusals": dict(sql="hook_refusals", text="msg", date="t", show=["key", "t", "hook", "msg"],
                     cols={"key": "text", "t": "ts", "hook": "text", "msg": "text"}, fuzzy={"msg"}, journal=True),
    "landfails": dict(sql="land_failures", text="msg", date="t", show=["key", "t", "branch", "msg"],
                      cols={"key": "text", "t": "ts", "branch": "text", "msg": "text"}, fuzzy={"msg"}, journal=True),
}

PG_TYPES = {"text": "text", "int": "integer", "date": "date", "bool": "boolean", "ts": "timestamptz"}

# Derived tables in COPY order (journals are kept, see build_script).
DERIVED = ["items", "links", "decisions", "orch", "debt", "questions", "errors", "agents"]
FTS_TABLES = ["items", "decisions", "orch", "debt", "questions", "errors", "agents"]
INDEXES = {"items": ["status", "agent", "type", "priority"], "links": ["from_id", "to_id"],
           "decisions": ["date"], "orch": ["date"], "debt": ["status", "area"],
           "questions": ["open"], "errors": ["status", "area", "date"], "agents": ["type", "start_t"]}

# --------------------------------------------------------------------------
# Parsers (pure functions, no database)
# --------------------------------------------------------------------------


def split_row(line: str) -> list[str]:
    """Cells of a Markdown table row; an escaped pipe stays inside its cell."""
    s = line.strip()
    if s.startswith("|"):
        s = s[1:]
    if s.endswith("|") and not s.endswith("\\|"):
        s = s[:-1]
    cells = re.split(r"(?<!\\)\|", s)
    return [c.strip().replace("\\|", "|") for c in cells]


def table_rows(text: str) -> list[list[str]]:
    """Body rows of all Markdown tables (header and separator rows removed)."""
    out: list[list[str]] = []
    pending = ""
    for line in text.splitlines():
        if pending:  # a row whose text continues on the next lines
            line = pending + " " + line.strip()
            pending = ""
        elif not line.lstrip().startswith("|"):
            continue
        if not line.rstrip().endswith("|") or line.rstrip().endswith("\\|"):
            pending = line
            continue
        cells = split_row(line)
        if all(re.fullmatch(r":?-{3,}:?", c) for c in cells if c) and any(cells):
            continue
        out.append(cells)
    return out


def valid_date(s: str | None) -> str | None:
    m = DATE_RE.search(s or "")
    return m.group(0) if m else None


def parse_decisions(text: str) -> list[dict]:
    rows = []
    for c in table_rows(text):
        if len(c) < 4 or not c[0].isdigit():
            continue
        body = c[2]
        sup = re.search(r"SUPERSEDED by #(\d+)", body)
        src = "orchestrator" if body.lstrip("*").startswith("(Orchestrator") else "owner"
        rows.append(dict(num=int(c[0]), date=valid_date(c[1]), text=body, source=src, ref=c[3],
                         superseded_by=int(sup.group(1)) if sup else None))
    return rows


def parse_orch(text: str) -> list[dict]:
    rows = []
    for c in table_rows(text):
        m = re.fullmatch(r"O-(\d+)", c[0]) if c else None
        if not m or len(c) < 5:
            continue
        rows.append(dict(id=c[0], num=int(m.group(1)), date=valid_date(c[1]), text=c[2], basis=c[3], applied=c[4]))
    return rows


def debt_status(s: str) -> str:
    low = s.lower().lstrip("~* ")
    if low.startswith(("закрыт", "closed", "снят", "done")):
        return "closed"
    if low.startswith("частично"):
        return "partial"
    if low.startswith(("открыт", "open")):
        return "open"
    return "other"


def parse_debt(text: str) -> list[dict]:
    rows = []
    for c in table_rows(text):
        if len(c) < 2 or not re.fullmatch(r"DEBT-\d{4}", c[0]):
            continue
        if len(c) == 5:  # older rows without the "Где" column
            c = [c[0], c[1], "", c[2], c[3], c[4]]
        elif len(c) == 3:  # multi-line review rows: id, text, status
            c = [c[0], c[1], "", "", "", c[2]]
        c = (c + [""] * 6)[:6]
        st = c[5]
        closed = valid_date(st) if debt_status(st) == "closed" else None
        rows.append(dict(id=c[0], text=c[1], area=c[2], why=c[3], plan=c[4], status=debt_status(st),
                         status_text=st, closed=closed))
    return rows


def parse_questions(text: str) -> list[dict]:
    rows = []
    kind = "other"
    block: list[str] = []

    def flush() -> None:
        for c in table_rows("\n".join(block)):
            if len(c) < 3 or not c[0] or c[0] in ("#", "ID"):
                continue
            qid = c[0] if not c[0].isdigit() else f"Q{c[0]}"
            if kind == "other":
                blocks, added = (c[3] if len(c) > 3 else ""), ""
            else:
                blocks, added = c[2], (c[3] if len(c) > 3 else "")
            rows.append(dict(id=qid, kind=kind, text=c[1], blocks=blocks, added=added,
                             open=not c[1].startswith("~~")))
        block.clear()

    for line in text.splitlines():
        if line.startswith("#"):
            flush()
            low = line.lower()
            kind = "owner" if "владельцу" in low else "autopilot" if "autopilot" in low else "other"
        else:
            block.append(line)
    flush()
    seen: set[str] = set()
    return [r for r in rows if not (r["id"] in seen or seen.add(r["id"]))]


def parse_errors(data: dict | None) -> list[dict]:
    rows = []
    for e in (data or {}).get("errors") or []:
        reps = [str(r) for r in e.get("repeats") or []]
        rows.append(dict(id=e.get("id"), date=valid_date(str(e.get("date", ""))), executor=e.get("executor"),
                         kind=e.get("kind"), area=e.get("area"), symptom=e.get("symptom"), cause=e.get("cause"),
                         found_by=e.get("found_by"), cost=e.get("cost"), fix=e.get("fix"),
                         repeats=len(reps), last_repeat=valid_date(max(reps)) if reps else None,
                         status=e.get("status"), key=e.get("key")))
    return rows


def parse_agent_log(lines: list[str]) -> list[dict]:
    """Agent runs from agents.jsonl: a 'launched' (or stop-only) record per agent id."""
    runs: dict[str, dict] = {}
    for line in lines:
        try:
            r = json.loads(line)
        except ValueError:
            continue
        aid = r.get("agent_id")
        if not aid or not isinstance(r.get("t"), (int, float)):
            continue
        run = runs.setdefault(aid, dict(agent_id=aid, type=None, description=None, tasks="", start_t=None,
                                        stop_t=None, session=r.get("session")))
        if r.get("event") == "stop":
            run["stop_t"] = r["t"]
            run["type"] = run["type"] or r.get("agent_type")
        else:
            run["start_t"] = r["t"]
            run["type"] = r.get("agent_type") or run["type"]
            run["description"] = r.get("description")
            run["tasks"] = ",".join(r.get("tasks") or [])
    out = []
    for run in runs.values():
        for k in ("start_t", "stop_t"):
            run[k] = dt.datetime.fromtimestamp(run[k], dt.timezone.utc).isoformat() if run[k] else None
        out.append(run)
    return sorted(out, key=lambda r: (r["start_t"] or r["stop_t"] or "", r["agent_id"]))


def parse_journal(lines: list[str], kind: str) -> list[dict]:
    """Rows of hook_refusals / land_failures; key is the hash of the raw line."""
    rows = []
    for line in lines:
        line = line.strip()
        try:
            r = json.loads(line)
        except ValueError:
            continue
        if not isinstance(r, dict):
            continue
        t = r.get("t")
        ts = dt.datetime.fromtimestamp(t, dt.timezone.utc).isoformat() if isinstance(t, (int, float)) else None
        who = str(r.get("hook") or r.get("tool") or "") if kind == "hook" else str(r.get("branch") or "")
        rows.append(dict(key=hashlib.sha1(line.encode()).hexdigest()[:16], t=ts, who=who,
                         msg=str(r.get("msg") or r.get("reason") or r.get("error") or "")[:2000], raw=line))
    return rows


# --------------------------------------------------------------------------
# Sources
# --------------------------------------------------------------------------


def git_common_parent() -> Path | None:
    """Parent of the git common dir: the main checkout, also from inside a worktree."""
    try:
        r = subprocess.run(["git", "rev-parse", "--git-common-dir"], cwd=ROOT, text=True,
                           capture_output=True, timeout=GIT_TIMEOUT)
    except (OSError, subprocess.SubprocessError):
        return None
    if r.returncode != 0 or not r.stdout.strip():
        return None
    common = Path(r.stdout.strip())
    return (common if common.is_absolute() else ROOT / common).resolve().parent


def state_dir() -> Path:
    """.claude/.state of this tree, else of the main checkout (worktrees have none)."""
    env = os.environ.get("SINGULARITY_STATE")
    if env:
        return Path(env)
    local = ROOT / ".claude" / ".state"
    if local.is_dir():
        return local
    common = git_common_parent()
    return common / ".claude" / ".state" if common else local


def source_files() -> list[Path]:
    import registry
    files = [ROOT / t.folder / n for t in registry.TYPES.values() if (ROOT / t.folder).is_dir()
             for n in sorted(p.name for p in (ROOT / t.folder).glob(f"{t.prefix}-*.md"))]
    files += [ROOT / "docs/decisions/DECISIONS_LOG.md", ROOT / "docs/decisions/ORCH_DECISIONS.md",
              ROOT / "development/TECH_DEBT.md", ROOT / "development/OPEN_QUESTIONS.md",
              ROOT / "development/errors.yaml", state_dir() / "agents.jsonl"]
    files += sorted(state_dir().glob("hook_refusals*.jsonl")) + sorted(state_dir().glob("land_failures*.jsonl"))
    return [f for f in files if f.exists()]


def fingerprint() -> str:
    h = hashlib.sha1(f"{SCHEMA}\0{ROOT}".encode())
    for f in source_files():
        st = f.stat()
        h.update(f"{f}\0{st.st_mtime_ns}\0{st.st_size}\n".encode())
    return h.hexdigest()


def read_text(p: Path) -> str:
    return p.read_text(encoding="utf-8") if p.exists() else ""


def collect() -> dict[str, list[dict]]:
    import registry
    import yaml
    items = registry.load_items([])
    item_rows, links = [], []
    for it in items:
        m = it.meta
        upd = valid_date(str(m.get("updated") or m.get("created") or m.get("date") or "")) \
            or dt.date.fromtimestamp(it.path.stat().st_mtime).isoformat()
        item_rows.append(dict(id=it.id, type=it.type.prefix, status=m.get("status"), level=m.get("level"),
                              milestone=m.get("milestone"), agent=m.get("agent"), category=m.get("category"),
                              priority=m.get("priority"), needs_owner=bool(m.get("needs_owner")),
                              approved_by=m.get("approved_by"), approved_at=valid_date(str(m.get("approved_at") or "")),
                              approval_ref=m.get("approval_ref"), path=registry.rel(it.path),
                              title=m.get("title"), updated=upd))
        for kind in ("depends_on", "related"):
            for to in registry.as_list(m.get(kind)):
                links.append(dict(from_id=it.id, to_id=to, kind=kind))
    st = state_dir()
    log = (st / "agents.jsonl")
    err_path = ROOT / "development" / "errors.yaml"
    refusals, fails = [], []
    for p in sorted(st.glob("hook_refusals*.jsonl")):
        refusals += parse_journal(read_text(p).splitlines(), "hook")
    for p in sorted(st.glob("land_failures*.jsonl")):
        fails += parse_journal(read_text(p).splitlines(), "land")
    return dict(
        items=item_rows, links=links,
        decisions=parse_decisions(read_text(ROOT / "docs/decisions/DECISIONS_LOG.md")),
        orch=parse_orch(read_text(ROOT / "docs/decisions/ORCH_DECISIONS.md")),
        debt=parse_debt(read_text(ROOT / "development/TECH_DEBT.md")),
        questions=parse_questions(read_text(ROOT / "development/OPEN_QUESTIONS.md")),
        errors=parse_errors(yaml.safe_load(read_text(err_path)) if err_path.exists() else None),
        agents=parse_agent_log(read_text(log).splitlines()),
        refusals=refusals, landfails=fails)


# --------------------------------------------------------------------------
# PostgreSQL through psql
# --------------------------------------------------------------------------


class PgError(RuntimeError):
    pass


def db_name() -> str:
    return os.environ.get("SINGULARITY_KB_DB", DEFAULT_DB)


def psql(db: str, sql: str, *flags: str) -> str:
    # -w: never prompt for a password; the timeout keeps a hung server from hanging callers
    cmd = ["psql", "-X", "-q", "-w", "-h", SOCKET_DIR, "-d", db, "-v", "ON_ERROR_STOP=1", *flags]
    try:
        r = subprocess.run(cmd, input=sql, text=True, capture_output=True, timeout=PSQL_TIMEOUT)
    except subprocess.TimeoutExpired:
        raise PgError(f"psql timed out after {PSQL_TIMEOUT} s")
    if r.returncode != 0:
        raise PgError(r.stderr.strip() or f"psql exit {r.returncode}")
    return r.stdout


def available() -> bool:
    if not shutil.which("psql"):
        return False
    try:
        psql("postgres", "select 1", "-At")
        return True
    except (PgError, OSError):
        return False


def ensure_db() -> None:
    db = db_name()
    if psql("postgres", f"select 1 from pg_database where datname = {lit(db)}", "-At").strip():
        return
    psql("postgres", f"create database {ident(db)}")


def lit(v) -> str:
    return "'" + str(v).replace("'", "''") + "'"


def ident(name: str) -> str:
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", name):
        raise PgError(f"bad identifier {name!r}")
    return '"' + name + '"'


def copy_value(v) -> str:
    if v is None:
        return r"\N"
    if isinstance(v, bool):
        return "t" if v else "f"
    return str(v).replace("\\", "\\\\").replace("\t", "\\t").replace("\n", "\\n").replace("\r", "\\r")


def create_table(name: str, spec: dict, temp: bool = False) -> str:
    cols = ", ".join(f"{c} {PG_TYPES[t]}" for c, t in spec["cols"].items())
    return f"create {'temp ' if temp else ''}table {name} ({cols});\n"


def copy_block(table_sql: str, cols: list[str], rows: list[dict]) -> str:
    body = "".join("\t".join(copy_value(r.get(c)) for c in cols) + "\n" for r in rows)
    return f"copy {table_sql} ({', '.join(cols)}) from stdin;\n{body}\\.\n"


JOURNAL_DDL = """create table if not exists hook_refusals (key text primary key, t timestamptz, hook text, msg text, raw text, deleted_at timestamptz);
create table if not exists land_failures (key text primary key, t timestamptz, branch text, msg text, raw text, deleted_at timestamptz);
create table if not exists kb_meta (key text primary key, value text);
"""


def build_script(data: dict[str, list[dict]], fp: str) -> str:
    out = ["begin;\nselect pg_advisory_xact_lock(590);\n", JOURNAL_DDL]
    for key in DERIVED:
        spec = TABLES[key]
        out.append(f"drop table if exists {spec['sql']};\n")
        out.append(create_table(spec["sql"], spec))
        out.append(copy_block(spec["sql"], list(spec["cols"]), data[key]))
        pk = {"items": "id", "decisions": "num", "orch": "id", "debt": "id", "errors": "id",
              "agents": "agent_id", "questions": "id"}.get(key)
        if pk:
            out.append(f"alter table {spec['sql']} add primary key ({pk});\n")
        for col in INDEXES.get(key, []):
            out.append(f"create index on {spec['sql']} ({col});\n")
        if key in FTS_TABLES:
            tc = spec["text"]
            for cfg in ("russian", "simple"):
                out.append(f"create index on {spec['sql']} using gin (to_tsvector('{cfg}', coalesce({tc}, '')));\n")
    for key, (table, who) in {"refusals": ("hook_refusals", "hook"), "landfails": ("land_failures", "branch")}.items():
        rows = [dict(r, **{who: r["who"]}) for r in data[key]]
        out.append(f"create temp table tmp_{table} (key text, t timestamptz, {who} text, msg text, raw text) on commit drop;\n")
        out.append(copy_block(f"tmp_{table}", ["key", "t", who, "msg", "raw"], rows))
        out.append(f"insert into {table} (key, t, {who}, msg, raw) select key, t, {who}, msg, raw from tmp_{table} "
                   "on conflict (key) do nothing;\n")
    out.append(f"insert into kb_meta values ('fingerprint', {lit(fp)}), ('built_at', now()::text) "
               "on conflict (key) do update set value = excluded.value;\ncommit;\n")
    return "".join(out)


def build(quiet: bool = False) -> int:
    if quiet and git_common_parent() != ROOT.resolve():
        return 0  # a worktree must not overwrite the index of the main checkout
    if not available():
        if not quiet:
            print("kb_db: PostgreSQL is not available (psql or socket)", file=sys.stderr)
        return 0 if quiet else 1
    try:
        ensure_db()
        fp = fingerprint()
        data = collect()
        psql(db_name(), build_script(data, fp))
    except Exception as ex:  # a derived index must never break the caller
        if not quiet:
            print(f"kb_db: build failed: {ex}", file=sys.stderr)
        return 0 if quiet else 1
    if not quiet:
        print("kb_db: built " + ", ".join(f"{k}={len(v)}" for k, v in data.items()))
    return 0


def is_fresh() -> bool:
    try:
        got = psql(db_name(), "select value from kb_meta where key = 'fingerprint'", "-At").strip()
    except PgError:
        return False
    return got == fingerprint()


def ensure_fresh() -> None:
    ensure_db()
    if not is_fresh():
        if build(quiet=False) != 0:
            raise PgError("rebuild failed")


# --------------------------------------------------------------------------
# Query language
# --------------------------------------------------------------------------

FILTER_RE = re.compile(r"^([A-Za-z_]\w*)(>=|<=|!=|~|=|>|<)(.*)$", re.S)
DUR_RE = re.compile(r"^(\d+)\s*([mhd])$")


def cast(ctype: str, value: str) -> str:
    if ctype == "int":
        if not re.fullmatch(r"-?\d+", value):
            raise ValueError(f"not an integer: {value}")
        return value
    if ctype == "bool":
        if value.lower() not in ("true", "false", "1", "0", "yes", "no"):
            raise ValueError(f"not a boolean: {value}")
        return "true" if value.lower() in ("true", "1", "yes") else "false"
    if ctype in ("date", "ts"):
        if not DATE_RE.fullmatch(value[:10]):
            raise ValueError(f"not a date: {value}")
        return f"{lit(value)}::{PG_TYPES[ctype]}"
    return lit(value)


def build_query(table: str, tokens: list[str]) -> str:
    spec = TABLES[table]
    cols = spec["cols"]
    where, order, limit = [], None, 200
    for tok in tokens:
        m = FILTER_RE.match(tok)
        if not m:
            raise ValueError(f"bad filter {tok!r} (expected key=value, key~text, key>value ...)")
        key, op, val = m.groups()
        if key == "limit":
            limit = int(val)
        elif key == "order":
            col = val.lstrip("-")
            if col not in cols:
                raise ValueError(f"unknown column {col!r}; columns: {', '.join(cols)}")
            order = f"{col} {'desc' if val.startswith('-') else 'asc'}"
        elif key in ("since", "until"):
            if not spec["date"]:
                raise ValueError(f"table {table} has no date column")
            where.append(f"{spec['date']} {'>=' if key == 'since' else '<='} {cast('date', val)}")
        elif key == "silent":
            dm = DUR_RE.match(val)
            if table != "agents" or not dm:
                raise ValueError("silent>2h works on 'agents' (units m, h, d)")
            secs = int(dm.group(1)) * {"m": 60, "h": 3600, "d": 86400}[dm.group(2)]
            where.append(f"stop_t is null and start_t < now() - interval '{secs} seconds'")
        elif key == "deleted":
            pass
        else:
            col = spec["text"] if key == "text" else key
            if not col or col not in cols:
                raise ValueError(f"unknown column {key!r}; columns: {', '.join(cols)}")
            ctype = cols[col]
            if op == "~":
                if ctype != "text":
                    raise ValueError(f"'~' works on text columns, not {col}")
                tsq = lambda cfg: f"to_tsvector('{cfg}', coalesce({col}, '')) @@ plainto_tsquery('{cfg}', {lit(val)})"
                where.append(f"({tsq('russian')} or {tsq('simple')} or {col} ilike {lit('%' + val + '%')})")
            elif ctype == "text" and op in ("=", "!="):
                eq = f"{col} ilike {lit('%' + val + '%')}" if col in spec["fuzzy"] else f"lower({col}) = lower({lit(val)})"
                where.append(eq if op == "=" else f"not coalesce({eq}, false)")
            else:
                where.append(f"{col} {'<>' if op == '!=' else op} {cast(ctype, val)}")
    if spec.get("journal") and "deleted=1" not in tokens:
        where.append("deleted_at is null")
    order = order or (f"{spec['date']} desc" if spec["date"] else None)
    if order is None:
        order = "1"
    select = ", ".join(cols)
    sql = f"select {select} from {spec['sql']}"
    if where:
        sql += " where " + " and ".join(where)
    return f"select row_to_json(q_) from ({sql} order by {order} limit {limit}) q_"


def fmt_cell(v) -> str:
    if v is None:
        return "-"
    if isinstance(v, bool):
        return "yes" if v else "no"
    s = " ".join(str(v).split())
    return s[:160] + "..." if len(s) > 160 else s


def fmt_row(table: str, row: dict) -> str:
    return " | ".join(fmt_cell(row.get(c)) for c in TABLES[table]["show"])


def cmd_q(args) -> int:
    table = args.table
    if table not in TABLES:
        print(f"unknown table {table!r}; tables: {', '.join(TABLES)}", file=sys.stderr)
        return 2
    try:
        sql = build_query(table, args.filters)
    except ValueError as ex:
        print(f"kb_db: {ex}", file=sys.stderr)
        return 2
    try:
        if not available():
            print("kb_db: PostgreSQL is not available", file=sys.stderr)
            return 1
        ensure_fresh()
        rows = [json.loads(l) for l in psql(db_name(), sql, "-At").splitlines() if l.strip()]
    except PgError as ex:
        print(f"kb_db: {ex}", file=sys.stderr)
        return 1
    if args.json:
        print(json.dumps(rows, ensure_ascii=False))
    else:
        for r in rows:
            print(fmt_row(table, r))
    return 0


def cmd_sql(args) -> int:
    query = " ".join(args.query) if args.query else sys.stdin.read()
    if any(l.lstrip().startswith("\\") for l in query.splitlines()):
        print("kb_db: psql backslash commands are not allowed", file=sys.stderr)
        return 2
    try:
        if not available():
            print("kb_db: PostgreSQL is not available", file=sys.stderr)
            return 1
        ensure_fresh()
        sys.stdout.write(psql(db_name(), "set default_transaction_read_only = on;\n" + query, "-A", "-F", " | "))
    except PgError as ex:
        print(f"kb_db: {ex}", file=sys.stderr)
        return 1
    return 0


def cmd_status(_args) -> int:
    if not available():
        print("kb_db: PostgreSQL is not available")
        return 1
    try:
        exists = psql("postgres", f"select 1 from pg_database where datname = {lit(db_name())}", "-At").strip()
        if not exists:
            print(f"database {db_name()}: not created (run build)")
            return 0
        meta = dict(l.split("|", 1) for l in psql(db_name(), "select key, value from kb_meta", "-At").splitlines())
        print(f"database {db_name()}: built {meta.get('built_at', 'never')}, "
              f"{'fresh' if is_fresh() else 'STALE (q rebuilds it)'}")
        for key, spec in TABLES.items():
            n = psql(db_name(), f"select count(*) from {spec['sql']}", "-At").strip()
            print(f"  {key:10} {n}")
    except PgError as ex:
        print(f"kb_db: {ex}", file=sys.stderr)
        return 1
    return 0


def cmd_forget(args) -> int:
    spec = TABLES.get(args.table)
    if not spec or not spec.get("journal"):
        print("forget works on refusals and landfails", file=sys.stderr)
        return 2
    try:
        out = psql(db_name(), f"update {spec['sql']} set deleted_at = now() where deleted_at is null "
                              f"and key like {lit(args.key + '%')} returning key", "-At")
    except PgError as ex:
        print(f"kb_db: {ex}", file=sys.stderr)
        return 1
    print(f"soft-deleted {len(out.split())} row(s)")
    return 0


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    b = sub.add_parser("build")
    b.add_argument("--quiet", action="store_true")
    q = sub.add_parser("q")
    q.add_argument("table")
    q.add_argument("filters", nargs="*")
    q.add_argument("--json", action="store_true")
    s = sub.add_parser("sql")
    s.add_argument("query", nargs="*")
    sub.add_parser("status")
    f = sub.add_parser("forget")
    f.add_argument("table")
    f.add_argument("key")
    args = ap.parse_args(argv)
    if args.cmd == "build":
        return build(args.quiet)
    return {"q": cmd_q, "sql": cmd_sql, "status": cmd_status, "forget": cmd_forget}[args.cmd](args)


if __name__ == "__main__":
    sys.exit(main())
