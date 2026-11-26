#!/usr/bin/env python3
"""Tests for metrics.py and the landings journal of land.py. Parsers run on fixtures without a database;
database tests use a temporary database and are skipped when PostgreSQL is not available (CI)."""
import contextlib
import io
import json
import os
import sys
import tempfile
import unittest
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import kb_db  # noqa: E402
import land  # noqa: E402
import metrics  # noqa: E402


def line(**kw):
    return json.dumps(kw)


def assistant(mid, ts, out, model="claude-opus-5-5", text="SECRET PROMPT TEXT"):
    return line(type="assistant", timestamp=ts, message=dict(
        id=mid, model=model, content=[dict(type="text", text=text)],
        usage=dict(input_tokens=2, output_tokens=out, cache_read_input_tokens=10, cache_creation_input_tokens=5)))


class ParserTests(unittest.TestCase):
    def test_transcript_counts_a_message_once_and_skips_garbage(self):
        lines = [assistant("m1", "2026-11-20T10:00:00Z", 7), assistant("m1", "2026-11-20T10:00:01Z", 7),
                 "not json", "[1]", line(type="assistant", message="x"),
                 assistant("m2", "2026-11-20T10:05:00Z", 3), assistant("m3", "2026-11-20T10:06:00Z", 99, model="<synthetic>")]
        r = metrics.parse_transcript(lines, "session:s", "session", "s")
        self.assertEqual((r["msgs"], r["output_tokens"], r["input_tokens"], r["cache_read_tokens"], r["cache_write_tokens"]),
                         (2, 10, 4, 20, 10))
        self.assertEqual(r["model"], "claude-opus-5-5")
        self.assertTrue(r["start_t"].startswith("2026-11-20T10:00:00"))
        self.assertTrue(r["end_t"].startswith("2026-11-20T10:06:00"))  # last activity, synthetic line included
        self.assertNotIn("SECRET", json.dumps(r))  # no text reaches a row
        self.assertIsNone(metrics.parse_transcript(["{}"], "k", "session", "s"))

    def test_run_record_review_and_gate(self):
        rev = metrics.parse_run_record("review-TASK-1-abc-1.json", dict(
            kind="review", provider="codex", task="TASK-0001", round=2, created_at="2026-11-26T14:43:56+00:00",
            usage=dict(input_tokens=100, output_tokens=5), result=dict(verdict="changes_requested", findings=[{}, {}])), 0)
        self.assertEqual((rev["task"], rev["round"], rev["findings"], rev["input_tokens"], rev["verdict"]),
                         ("TASK-0001", 2, 2, 100, "changes_requested"))
        gate = metrics.parse_run_record("gate-x.json", dict(
            kind="gate", provider="deepseek-flash", item="docs/x.md", usage=dict(prompt_tokens=9, completion_tokens=4),
            result=dict(STATUS="done", FINDINGS=[{}])), 1000)
        self.assertEqual((gate["task"], gate["input_tokens"], gate["output_tokens"], gate["findings"], gate["verdict"]),
                         ("docs/x.md", 9, 4, 1, "done"))
        self.assertIsNone(metrics.parse_run_record("x.json", [], 0))

    def test_ledger_settle_replaces_reserve(self):
        rows = metrics.parse_ledger([
            line(id="a", model="m", t="reserve", task="TASK-1", ts="2026-11-13T20:46:46+00:00", usd="0.17"),
            line(id="a", t="settle", ts="2026-11-13T20:46:58+00:00", usd="0.03"),
            line(id="b", model="m", t="reserve", task="TASK-2", ts="2026-11-13T21:00:00+00:00", usd="0.5"),
            "junk", line(id="zz", t="settle", usd="1")])
        self.assertEqual({r["key"]: r["usd"] for r in rows}, {"ledger:a": 0.03, "ledger:b": 0.5})

    def test_task_flow_from_git_log(self):
        log = "\n".join(["@@abcdef1234567890 2026-11-20T10:00:00+00:00", "--- a/development/tasks/TASK-0001-x.md",
                         "+++ b/development/tasks/TASK-0001-x.md", "@@ -3 +3 @@", "-status: APPROVED", "+status: IN_PROGRESS",
                         "@@1111111111111111 2026-11-21T10:00:00+00:00", "+++ b/development/tasks/TASK-0001-x.md",
                         "-status: IN_PROGRESS", "+status: REVIEW", "+++ b/development/tasks/TASK-0002-y.md",
                         "+status: PROPOSED"])
        rows = metrics.parse_task_flow(log)
        self.assertEqual([(r["task"], r["from_status"], r["to_status"]) for r in rows],
                         [("TASK-0001", "APPROVED", "IN_PROGRESS"), ("TASK-0001", "IN_PROGRESS", "REVIEW"),
                          ("TASK-0002", None, "PROPOSED")])

    def test_landings_and_owner_waits(self):
        rows = metrics.parse_landings([line(t=1000, branch="b", task="TASK-1", attempt=2, code=1, total_s=30.5,
                                            steps=dict(verify=20.0, review=9)), "junk"])
        self.assertEqual((len(rows), rows[0]["attempt"], rows[0]["verify_s"]), (1, 2, 20.0))
        text = ("| # | В | Кто | Когда |\n|---|---|---|---|\n| 1 | q one | a | 2026-11-10; Закрыт 2026-11-12 |\n"
                "| 2 | q two | a | 2026-11-10 |\n")
        w = metrics.parse_owner_waits(text)
        self.assertEqual([(r["question"], r["asked"], r["answered"]) for r in w], [("Q1", "2026-11-10", "2026-11-12")])

    def test_period_validation(self):
        self.assertEqual(metrics.parse_period("2026-11-19..2026-11-26"), ("2026-11-19", "2026-11-26"))
        for bad in ("2026-11-26..2026-11-19", "2026-13-01..2026-13-02", "x"):
            with self.assertRaises(ValueError):
                metrics.parse_period(bad)

    def test_compare_rendering(self):
        out = metrics.render_compare(("a", "b"), ("c", "d"), {"errors": 4.0, "x": 0.0}, {"errors": 2.0, "x": 3.0})
        self.assertIn("-50%", out)
        self.assertIn("n/a", out)


class LandingJournalTests(unittest.TestCase):
    def test_record_landing_appends_with_attempt_number(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            old = land.main_worktree
            land.main_worktree = lambda: root
            land.LAND_INFO.update(branch="br", task="TASK-9")
            land.STEPS.clear()
            land.STEPS["verify"] = 12.34
            try:
                land.record_landing(1, 20.0)
                land.record_landing(0, 21.0)
            finally:
                land.main_worktree = old
                land.STEPS.clear()
            recs = [json.loads(l) for l in (root / ".claude/.state/landings.jsonl").read_text().splitlines()]
            self.assertEqual([(r["attempt"], r["code"], r["task"]) for r in recs], [(1, 1, "TASK-9"), (2, 0, "TASK-9")])
            self.assertEqual(recs[0]["steps"], {"verify": 12.3})
            self.assertEqual(metrics.parse_landings([json.dumps(recs[1])])[0]["verify_s"], 12.3)


@unittest.skipUnless(kb_db.available(), "PostgreSQL is not available")
class JournalFailureTests(unittest.TestCase):
    def test_record_landing_swallows_write_error(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            (root / ".claude").mkdir()
            (root / ".claude/.state").write_text("a file, so mkdir and open fail")
            old = land.main_worktree
            land.main_worktree = lambda: root
            land.LAND_INFO.update(branch="br", task="")
            try:
                land.record_landing(0, 1.0)  # must not raise
            finally:
                land.main_worktree = old

    def test_record_landing_skips_without_branch(self):
        with tempfile.TemporaryDirectory() as d:
            old = land.main_worktree
            land.main_worktree = lambda: Path(d)
            land.LAND_INFO.clear()
            try:
                land.record_landing(4, 1.0)
            finally:
                land.main_worktree = old
            self.assertFalse((Path(d) / ".claude").exists())


@unittest.skipUnless(kb_db.available(), "PostgreSQL is not available")
class DatabaseTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.db = "mtest_" + uuid.uuid4().hex[:10]
        cls.tmp = tempfile.TemporaryDirectory()
        t = Path(cls.tmp.name)
        for sub in ("state", "claude/sess1/subagents", "runs"):
            (t / sub).mkdir(parents=True)
        (t / "state/agents.jsonl").write_text("\n".join([
            line(t=1795000000, event="launched", agent_id="ag1", agent_type="developer", description="d", tasks=["TASK-0206"]),
            line(t=1795000600, event="stop", agent_id="ag1", agent_type="developer")]) + "\n")
        (t / "state/landings.jsonl").write_text(line(t=1795000700, branch="b", task="TASK-0206", attempt=1, code=0,
                                                       total_s=50, steps=dict(verify=40)) + "\n")
        (t / "claude/sess1.jsonl").write_text(assistant("m1", "2026-11-20T10:00:00Z", 7) + "\n")
        (t / "claude/sess1/subagents/agent-ag1.jsonl").write_text(
            assistant("m9", "2026-11-20T10:00:00Z", 50) + "\n" + assistant("m9", "2026-11-20T10:00:00Z", 50) + "\n")
        (t / "runs/review-TASK-0206-abc-1.json").write_text(json.dumps(dict(
            kind="review", provider="codex", task="TASK-0206", round=1, created_at="2026-11-20T11:00:00+00:00",
            usage=dict(input_tokens=1, output_tokens=2), result=dict(verdict="accept", findings=[]))))
        (t / "ledger.jsonl").write_text(line(id="a", model="m", t="reserve", task="TASK-0206", ts="2026-11-20T11:00:00+00:00", usd="0.1") + "\n")
        cls.env = {"SINGULARITY_KB_DB": cls.db, "SINGULARITY_STATE": str(t / "state"),
                   "SINGULARITY_CLAUDE_PROJECTS": str(t / "claude"), "SINGULARITY_LLM_RUNS": str(t / "runs"),
                   "SINGULARITY_LEDGER": str(t / "ledger.jsonl")}
        cls.old = {k: os.environ.get(k) for k in cls.env}
        os.environ.update(cls.env)

    @classmethod
    def tearDownClass(cls):
        for k, v in cls.old.items():
            os.environ.pop(k, None) if v is None else os.environ.__setitem__(k, v)
        try:
            kb_db.psql("postgres", f"drop database if exists {cls.db}")
        finally:
            cls.tmp.cleanup()

    def count(self, table):
        return int(kb_db.psql(self.db, f"select count(*) from {table}", "-At", schema=True).strip())

    def test_collect_is_idempotent_and_keeps_soft_deletes(self):
        self.assertEqual(metrics.collect(quiet=True), 0)
        first = {t: self.count(t) for t in metrics.COLS}
        self.assertEqual((first["sessions"], first["llm_calls"], first["landings"]), (2, 2, 1))
        self.assertEqual(metrics.collect(quiet=True), 0)
        self.assertEqual({t: self.count(t) for t in metrics.COLS}, first)
        led = Path(self.env["SINGULARITY_LEDGER"])
        led.write_text(led.read_text() + line(id="a", t="settle", ts="2026-11-20T11:01:00+00:00", usd="0.03") + "\n")
        metrics.collect(quiet=True)  # settle after reserve corrects the amount, no new row
        self.assertEqual(self.count("llm_calls"), 2)
        self.assertEqual(kb_db.psql(self.db, "select usd from llm_calls where key = 'ledger:a'", "-At", schema=True).strip(), "0.03")
        kb_db.psql(self.db, "update sessions set deleted_at = now() where key = 'session:sess1'", schema=True)
        metrics.collect(quiet=True)
        self.assertEqual(self.count("sessions where deleted_at is not null"), 1)

    def test_report_and_compare(self):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            kb_db.build(quiet=True)
            self.assertEqual(metrics.main(["report", "--since", "2026-11-01", "--until", "2026-12-31"]), 0)
            self.assertEqual(metrics.main(["report", "--compare", "2026-11-01..2026-11-15", "2026-11-15..2026-12-31"]), 0)
        text = out.getvalue()
        self.assertIn("developer", text)
        self.assertIn("TASK-0206", text)
        self.assertIn("review records", text)
        self.assertRegex(text, r"developer\s+1\s+10\s+\d+\s+\d+\s+50")  # one run, 10 min, 50 output tokens (not 100)


if __name__ == "__main__":
    unittest.main()
