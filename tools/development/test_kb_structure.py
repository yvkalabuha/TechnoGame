#!/usr/bin/env python3
"""Tests for the knowledge base structure (TASK-0209, #598-#600): journal layout (kblog), tags, header links and
size limits (kbdocs, kb_check), the docs catalogue and chains (kb_db), file reads (metrics), shorter hooks."""
import contextlib
import datetime as dt
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
import uuid
from pathlib import Path
from unittest import mock

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
HOOKS = ROOT / ".claude" / "hooks"
sys.path.insert(0, str(HERE))
try:
    import kb_check
    import kb_db
    import kbdocs
    import kblog
    import metrics
finally:
    sys.path.remove(str(HERE))

HEAD = "| # | Дата | Решение | Ссылка |\n|---|---|---|---|\n"
DEBT_HEAD = "| ID | Долг | Где | Почему допущен | План | Статус |\n|---|---|---|---|---|---|\n"


def write(root: Path, rel: str, text: str) -> None:
    p = root / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(text, encoding="utf-8")


def sample_log() -> str:
    rows = [f"| {n} | 2026-10-{n % 28 + 1:02d} | old decision {n} | ref |" for n in range(398, 402)]
    rows.append("| 402 | 2026-11-03 | multi-line decision:\n\n**Part:**\n- one | two\n\nend | владелец |")
    rows += [f"| {n} | 2026-11-2{n % 9} | decision {n} SUPERSEDED by #1 | ref |" for n in range(403, 409)]
    return "# DECISIONS LOG\n\nintro\n\n" + HEAD + "\n".join(rows) + "\n"


def sample_debt() -> str:
    return ("# TECH DEBT\n\n" + DEBT_HEAD + "| DEBT-0001 | a | x | y | z | закрыт 2026-11-14 |\n"
            "| DEBT-0002 | b | x | y | z | открыт |\n| DEBT-0003 | multi\n- item | Open |\n"
            "| DEBT-0004 | c | x | y | z | ~~снят~~ закрыт 2026-11-20 |\n")


class JournalLayoutTest(unittest.TestCase):
    """Item 4: rows move by month and to the archive; every reader sees the same rows; nothing is lost."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root, True)
        write(self.root, kblog.DECISIONS, sample_log())
        write(self.root, kblog.DEBT, sample_debt())

    def rows(self):
        return (kb_check.decision_rows(kblog.text("decisions", self.root)),
                {r["id"]: r for r in kb_db.parse_debt(kblog.text("debt", self.root))})

    def test_rollover_keeps_every_row_and_places_it(self):
        before = self.rows()
        self.assertEqual(len(before[0]), 11)
        d = kblog.rollover_decisions(self.root, keep=3)
        t = kblog.rollover_debt(self.root)
        self.assertEqual((d, t), ({"moved": 8, "kept": 3}, {"moved": 2, "kept": 2}))
        self.assertEqual(self.rows(), before)  # row sets equal through the reader: nothing lost or changed
        c = kblog.counts(self.root)
        self.assertEqual(c[kblog.DECISIONS_ARCHIVE], 3)  # 398-400
        self.assertEqual(c["docs/decisions/log/2026-10.md"], 1)  # 401
        self.assertEqual(c["docs/decisions/log/2026-11.md"], 4)  # 402 (multi-line) and 403-405
        self.assertEqual(c[kblog.DECISIONS], 3)
        self.assertIn("**Part:**\n- one | two", (self.root / "docs/decisions/log/2026-11.md").read_text(encoding="utf-8"))
        main = (self.root / kblog.DECISIONS).read_text(encoding="utf-8")
        self.assertIn("log/2026-11.md) — #402–#405", main)
        self.assertEqual(kblog.duplicates("decisions", self.root), {})
        debt_main = (self.root / kblog.DEBT).read_text(encoding="utf-8")
        self.assertNotIn("DEBT-0001", debt_main)
        self.assertIn("DEBT-0003 | multi\n- item | Open |", debt_main)
        self.assertIn("DEBT-0004", (self.root / kblog.DEBT_ARCHIVE).read_text(encoding="utf-8"))

    def test_rollover_is_rerunnable_and_main_file_wins(self):
        kblog.rollover_decisions(self.root, keep=3)
        kblog.rollover_debt(self.root)
        snap = {p: (self.root / p).read_text(encoding="utf-8") for p in kblog.counts(self.root)}
        kblog.rollover_decisions(self.root, keep=3)
        kblog.rollover_debt(self.root)
        self.assertEqual({p: (self.root / p).read_text(encoding="utf-8") for p in kblog.counts(self.root)}, snap)
        # a merge brings the old main file back: its rows win over the moved copies, no duplicates stay
        main = self.root / kblog.DECISIONS
        main.write_text(sample_log().replace("decision 404 SUPERSEDED", "decision 404 EDITED"), encoding="utf-8")
        self.assertTrue(kblog.duplicates("decisions", self.root))
        kblog.rollover_decisions(self.root, keep=3)
        self.assertEqual(kblog.duplicates("decisions", self.root), {})
        self.assertIn("decision 404 EDITED", kb_check.decision_rows(kblog.text("decisions", self.root))[404])

    def test_landing_sees_no_new_rows_when_only_the_layout_changes(self):
        git = lambda *a: subprocess.run(["git", *a], cwd=self.root, check=True, capture_output=True, text=True).stdout
        git("init", "-q")
        git("-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "--allow-empty", "-m", "0")
        git("add", "-A")
        git("-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "old layout")
        base = git("rev-parse", "HEAD").strip()
        kblog.rollover_decisions(self.root, keep=3)
        git("add", "-A")
        git("-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "new layout")
        self.assertEqual(kb_check.decision_rows(kblog.text("decisions", self.root, base)),
                         kb_check.decision_rows(kblog.text("decisions", self.root, "HEAD")))
        self.assertEqual(kb_check.landing_problems(self.root, base, "HEAD"), [])

    def test_real_journals_have_no_duplicates_and_no_lost_rows(self):
        self.assertEqual(kblog.duplicates("decisions", ROOT), {})
        self.assertEqual(kblog.duplicates("debt", ROOT), {})
        starts = sum(len(kblog.raw_rows(t, kblog.DECISION_START)[1]) for _, t in kblog.sources("decisions", ROOT))
        self.assertEqual(len(kb_check.decision_rows(kblog.text("decisions", ROOT))), starts)
        self.assertGreaterEqual(starts, 606)  # rows #1-#606 existed when the log was split (TASK-0209)
        self.assertEqual(kb_check.known_ids()["max_decision"], max(kb_check.decision_rows(kblog.text("decisions", ROOT))))


class TagsAndLimitsTest(unittest.TestCase):
    """Items 1, 3, 8: tag rules, header links, size limits by date."""

    def test_vocabulary_of_the_repository_is_valid(self):
        self.assertEqual(kbdocs.vocab_problems(ROOT), [])
        vocab, _ = kbdocs.load_vocab(ROOT)
        self.assertLessEqual(len(vocab), 50)
        self.assertEqual(set(vocab.values()), {"kind", "area", "state"})

    def test_tag_rules(self):
        vocab = {"review": "kind", "ui": "area", "map": "area", "done": "state", "active": "state"}
        self.assertEqual(kbdocs.tag_problems(["review", "ui", "done"], vocab), [])
        self.assertTrue(any("unknown tag 'uix'" in p for p in kbdocs.tag_problems(["review", "uix", "done"], vocab)))
        self.assertTrue(any("2 tags" in p for p in kbdocs.tag_problems(["review", "done"], vocab)))
        self.assertIn("no 'kind' tag", kbdocs.tag_problems(["ui", "map", "done"], vocab))
        self.assertIn("exactly one 'state' tag is needed", kbdocs.tag_problems(["review", "ui", "done", "active"], vocab))

    def test_header_tags_overrides_and_links(self):
        ids = kb_check.known_ids()
        vocab = {"review": "kind", "ui": "area", "done": "state"}
        errors, missing = [], []
        text = ("---\ntags: [review, ui, done]\nrelated: [TASK-0209, '#598', docs/ui/UI_DESIGN_SYSTEM.md, nope.md]\n"
                "supersedes: [TASK-9999]\n---\n# T\n")
        kb_check.check_doc_header("docs/x.md", text, ids, vocab, {}, errors, missing)
        self.assertEqual(missing, [])
        self.assertEqual(len(errors), 2, errors)  # nope.md and TASK-9999
        kb_check.check_doc_header("docs/decisions/ADR-0001-stack.md", "# ADR\n", ids, vocab,
                                  {"docs/decisions/ADR-0001-stack.md": ["review", "ui", "done"]}, errors, missing)
        kb_check.check_doc_header("docs/y.md", "# no header\n", ids, vocab, {}, errors, missing)
        self.assertEqual(missing, ["docs/y.md"])
        self.assertTrue(kb_check.link_target_ok("E1-model", ids, doc="docs/game-design/economy/x.md"))

    def test_size_limit_is_a_warning_before_the_date_and_an_error_after(self):
        tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, tmp, True)
        write(tmp, "CLAUDE.md", "x" * (6 * 1024 + 1))
        write(tmp, "docs/decisions/log/2026-11.md", "y" * (60 * 1024))  # at the limit: fine
        ids = {"items": set(), "max_decision": 1, "debt": set(), "risk": set(), "orch": set()}
        with mock.patch.object(kb_check, "ROOT", tmp):
            for day, sink in ((dt.date(2026, 12, 2), 1), (dt.date(2026, 12, 3), 0)):
                errors, warnings = [], []
                kb_check.check_structure(["CLAUDE.md", "docs/decisions/log/2026-11.md"], ids, errors, warnings, day)
                out = (errors, warnings)[sink]
                self.assertEqual(len(out), 1, (errors, warnings))
                self.assertIn("CLAUDE.md: 6.0 KB, the limit for 'always' files is 6 KB", out[0])
        self.assertEqual(kbdocs.size_class(".claude/agents/developer.md")[0], "always")
        self.assertEqual(kbdocs.size_class(".claude/skills/land/SKILL.md")[0], "living")
        self.assertIsNone(kbdocs.size_class("docs/decisions/log/archive-0001-0400.md"))

    def test_summary_prefers_the_short_conclusion(self):
        body = "# T\n\nfirst paragraph\n\n## Цель\n\n## Краткий вывод\n\n- a\n- b\n\n## Next\nz"
        self.assertEqual(kbdocs.summary(body), "- a - b")
        self.assertEqual(kbdocs.summary("# T\n\n## Цель\n\n## Цель\ngoal\n"), "goal")
        self.assertEqual(kbdocs.summary("# T\n\nfirst paragraph\n"), "first paragraph")


class CatalogueParserTest(unittest.TestCase):
    """Items 3, 9 without a database."""

    def test_parse_doc_row_and_links(self):
        text = "---\ntags: [review, ui, done]\nrelated: [TASK-0209]\nsupersedes: E1-model\n---\n# Title\n\n## Краткий вывод\nshort\n"
        row, links = kb_db.parse_doc("docs/game-design/economy/new.md", text, {"TASK-0209": "development/tasks/t.md"}, {})
        self.assertEqual((row["title"], row["summary"], row["tags"]), ("Title", "short", ["review", "ui", "done"]))
        self.assertEqual(row["bytes"], len(text.encode()))
        self.assertFalse(row["over_limit"])
        self.assertEqual({(l["kind"], l["to_path"]) for l in links},
                         {("related", "development/tasks/t.md"), ("supersedes", "docs/game-design/economy/E1-model.md")})

    def test_tags_filter_bare_boolean_and_array_copy(self):
        sql = kb_db.build_query("docs", ["tags=ui", "over_limit"])
        self.assertIn("tags @> array['ui']::text[]", sql)
        self.assertIn("over_limit = true", sql)
        self.assertEqual(kb_db.copy_value(["a", 'b"c']), '{"a","b\\\\"c"}')


@unittest.skipUnless(kb_db.available(), "PostgreSQL is not available")
class CatalogueDatabaseTest(unittest.TestCase):
    """Items 3, 9: incremental sync, chain, pack, over_limit on a temporary database."""

    @classmethod
    def setUpClass(cls):
        cls.db = "kbtest_" + uuid.uuid4().hex[:10]
        cls.old = {k: os.environ.get(k) for k in ("SINGULARITY_KB_DB",)}
        os.environ["SINGULARITY_KB_DB"] = cls.db
        kb_db.ensure_db()
        with contextlib.redirect_stdout(io.StringIO()):
            kb_db.build(quiet=True)

    @classmethod
    def tearDownClass(cls):
        for k, v in cls.old.items():
            os.environ.pop(k, None) if v is None else os.environ.__setitem__(k, v)
        kb_db.psql("postgres", f"drop database if exists {cls.db}")

    def test_sync_parses_only_changed_files(self):
        self.assertEqual(kb_db.sync_docs(), 0)  # nothing changed since build
        target = ROOT / "development/tasks/TASK-0209-kb-structure.md"
        st = target.stat()
        os.utime(target, ns=(st.st_atime_ns, st.st_mtime_ns + 1000))
        self.addCleanup(os.utime, target, ns=(st.st_atime_ns, st.st_mtime_ns))
        self.assertEqual(kb_db.sync_docs(), 1)

    def test_chain_pack_and_over_limit(self):
        rows = kb_db.chain("TASK-0209")
        paths = [r["path"] for r in rows]
        self.assertIn("development/tasks/TASK-0208-kb-upkeep.md", paths)  # depends_on
        self.assertEqual(paths, [r["path"] for r in sorted(rows, key=lambda r: (r["date"] or "", r["path"]))])
        text = kb_db.pack("TASK-0209", 300)
        self.assertIn("Факты:", text)
        self.assertLessEqual(kbdocs.approx_tokens(text), 300 + 80)  # plus the header lines and the "did not fit" line
        self.assertGreater(len(kb_db.pack("TASK-0209", 3000)), len(text))
        over = json.loads(self._q("q", "docs", "over_limit", "--json"))
        self.assertIn("docs/ui/UI_DESIGN_SYSTEM.md", {r["path"] for r in over})

    def _q(self, *args):
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(kb_db.main(list(args)), 0)
        return out.getvalue()


class FileReadsTest(unittest.TestCase):
    """Item 10: paths of read tools, repository-relative, no other input kept."""

    def test_tool_reads_and_paths(self):
        b = lambda name, **inp: {"type": "tool_use", "id": "t1", "name": name, "input": inp}
        self.assertEqual(metrics.tool_reads(b("Read", file_path="/var/www/gameDev/.claude/worktrees/agent-x/docs/a.md")),
                         [("Read", "docs/a.md")])
        self.assertEqual(metrics.tool_reads(b("Read", file_path="/etc/passwd")), [])
        got = metrics.tool_reads(b("REPL", code="await file(W+'/tools/development/kb_db.py').text(); sh('cat CLAUDE.md')"))
        self.assertEqual(got, [("REPL", "tools/development/kb_db.py"), ("REPL", "CLAUDE.md")])
        self.assertEqual(metrics.tool_reads(b("Write", file_path="/var/www/gameDev/docs/a.md")), [])

    def test_transcript_reads_and_costly_files(self):
        lines = [json.dumps({"type": "assistant", "timestamp": "2026-11-26T10:00:00Z", "message": {
            "id": "m1", "model": "claude", "usage": {"input_tokens": 1, "output_tokens": 2},
            "content": [{"type": "tool_use", "id": "t1", "name": "Read",
                         "input": {"file_path": "/var/www/gameDev/CLAUDE.md"}}]}})]
        r = metrics.parse_transcript(lines, "session:s", "session", "s")
        self.assertEqual([(x["tool"], x["path"]) for x in r["reads"]], [("Read", "CLAUDE.md")])
        self.assertNotIn("input", r["reads"][0])
        tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, tmp, True)
        write(tmp, "a.md", "x" * 700)
        write(tmp, "b.md", "x" * 70)
        got = metrics.costly_files([{"path": "b.md", "reads": 5, "sessions": 1}, {"path": "a.md", "reads": 1, "sessions": 1},
                                    {"path": "gone.md", "reads": 9, "sessions": 1}], root=tmp)
        self.assertEqual([(g["path"], g["approx_tokens"]) for g in got], [("a.md", 200), ("b.md", 100)])


class ShorterHooksTest(unittest.TestCase):
    """Item 5: kb_guard shows at most 5 related items, none twice in a session; SessionStart shows 2 entries."""

    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp, True)

    def guard(self, session):
        lines = [f"FACT f{i}: statement {i}  [src]" for i in range(4)] + [f"DECISION #{i} (2026-11-26): text" for i in range(4)]
        write(self.tmp, "tools/development/kb_check.py", "print(%r)\n" % "\n".join(lines))
        env = dict(os.environ, CLAUDE_PROJECT_DIR=str(self.tmp), SINGULARITY_STATE=str(self.tmp / "state"))
        payload = {"session_id": session, "tool_input": {"questions": []}, "tool_response": "ok"}
        out = subprocess.run([sys.executable, str(HOOKS / "kb_guard.py"), "answers"], input=json.dumps(payload),
                             text=True, capture_output=True, env=env).stdout
        if not out.strip():
            return []
        ctx = json.loads(out)["hookSpecificOutput"]["additionalContext"]
        return [l.split(":")[0] for l in ctx.splitlines() if l.startswith(("FACT", "DECISION"))]

    def test_at_most_five_and_no_repeats_in_a_session(self):
        first = self.guard("s1")
        self.assertEqual(first, ["FACT f0", "FACT f1", "FACT f2", "FACT f3", "DECISION #0 (2026-11-26)"])
        self.assertEqual(self.guard("s1"), ["DECISION #1 (2026-11-26)", "DECISION #2 (2026-11-26)", "DECISION #3 (2026-11-26)"])
        self.assertEqual(self.guard("s1"), [])
        self.assertEqual(self.guard("s2"), first)  # another session starts clean

    def test_new_row_in_a_monthly_file_prints_its_facts(self):
        payload = {"tool_name": "Edit", "tool_input": {"file_path": str(ROOT / "docs/decisions/log/2026-11.md"),
                                                       "old_string": "| 591 | x |", "new_string": "| 591 | x |\n| 592 | y |"}}
        env = dict(os.environ, CLAUDE_PROJECT_DIR=str(ROOT))
        out = subprocess.run([sys.executable, str(HOOKS / "kb_guard.py"), "file"], input=json.dumps(payload),
                             text=True, capture_output=True, env=env).stdout
        self.assertIn("DECISION #592", out)  # #592 names Codex, rounds and tools: facts of tools-codex-rule

    def test_session_start_shows_the_two_latest_entries(self):
        write(self.tmp, "CURRENT_STATE.md", "## Сводка\n- **new:** a\n  - detail of new\n- **older:** b\n- **oldest:** c\n\n## Next\n")
        env = dict(os.environ, CLAUDE_PROJECT_DIR=str(self.tmp))
        out = subprocess.run([sys.executable, str(HOOKS / "session_context.py")], input="{}", text=True,
                             capture_output=True, env=env).stdout
        self.assertIn("detail of new", out)
        self.assertIn("**older:** b", out)
        self.assertNotIn("oldest", out)


if __name__ == "__main__":
    unittest.main()
