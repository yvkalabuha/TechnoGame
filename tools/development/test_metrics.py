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


ENV_KEYS = ("SINGULARITY_KB_DB", "SINGULARITY_KB_SCHEMA", "SINGULARITY_STATE", "SINGULARITY_CLAUDE_PROJECTS",
            "SINGULARITY_LLM_RUNS", "SINGULARITY_LEDGER")


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

    def test_question_is_answered_only_when_closed_and_a_decision_exists(self):
        log = "\n".join([
            "@@aaaaaaaa 2026-11-10T10:00:00+00:00", "@@ -0,0 +1,5 @@", "+| 1 | q one | a | b |", "+| 2 | q two | a | b |",
            "+| 3 | q three | a | b |", "+| 4 | q four | a | b |", "+| 5 | q five | a | b |",
            "@@bbbbbbbb 2026-11-18T10:00:00+00:00", "-| 1 | q one | a | b |",  # removed, no answer anywhere: unresolved
            "-| 2 | q two | a | b |", "+| 2 | q two edited | a | b |",  # edited, still open even with a decision
            "-| 3 | q three | a | b |", "+| ~~3~~ | ~~q three~~ | a | b |",  # struck through, decision exists
            "-| 4 | q four | a | b |",  # removed, decision exists
            "-| 5 | q five | a | b |", "+| 5 | q five | a | Закрыт 2026-11-14 |"])  # closing text carries the answer date
        dec = [dict(date="2026-11-20", text="answers Q2"), dict(date="2026-11-12", text="decides Q3"),
               dict(date="2026-11-15", text="Q4 decided"), dict(date="2026-11-01", text="old Q4 note"),
               dict(date="2026-11-13", text="Q41 is another question")]
        rows = {r["question"]: (r["asked"], r["answered"]) for r in metrics.parse_question_history(log, dec)}
        self.assertEqual(rows, {"Q3": ("2026-11-10", "2026-11-12"), "Q4": ("2026-11-10", "2026-11-15"),
                                "Q5": ("2026-11-10", "2026-11-14")})

    def test_transcript_messages_have_their_own_time_and_key(self):
        lines = [assistant("m1", "2026-11-20T10:00:00Z", 7), assistant("m1", "2026-11-20T10:00:09Z", 7),
                 assistant("m2", "2026-12-05T10:00:00Z", 3)]
        r = metrics.parse_transcript(lines, "session:s", "session", "s")
        got = {m["key"]: (m["t"][:10], m["output_tokens"]) for m in r["messages"]}
        self.assertEqual(got, {"session:s:m1": ("2026-11-20", 7), "session:s:m2": ("2026-12-05", 3)})

    def test_research_record_sums_item_usage_and_counts_verdicts(self):
        rec = dict(kind="research", provider="deepseek-flash", input="docs/research/notes/x.yaml", created="2026-11-26T03:03:53Z",
                   items=[dict(verdict="confirmed", usage=dict(prompt_cache_hit_tokens=10, prompt_cache_miss_tokens=90, completion_tokens=5)),
                          dict(verdict="not_found", usage=dict(prompt_tokens=7, completion_tokens=2)), dict(verdict="not_found")])
        r = metrics.parse_run_record("research-x.json", rec, 0)
        self.assertEqual((r["input_tokens"], r["output_tokens"], r["findings"], r["task"]), (107, 7, 2, "docs/research/notes/x.yaml"))
        self.assertEqual(r["verdict"], "confirmed=1,not_found=2")

    def test_sanitize_skips_bad_rows_and_cleans_nul(self):
        data = {t: [] for t in metrics.COLS}
        data["landings"] = [dict(key="a", t=None, branch="b\x00x", task="", attempt=2**40, code=0, total_s=1, verify_s=1, steps="{}"),
                            dict(key="b", t=None, branch="ok\x00", task="", attempt=1, code=0, total_s=float("inf"), verify_s=1, steps="{}"),
                            dict(key="c", t=None, branch="ok\x00", task="", attempt=1, code=0, total_s=1, verify_s=1, steps="{}"),
                            dict(key="", t=None, branch="", task="", attempt=1, code=0, total_s=1, verify_s=1, steps="{}")]
        out, notes = metrics.sanitize(data)
        self.assertEqual([(r["key"], r["branch"]) for r in out["landings"]], [("c", "ok")])
        self.assertEqual(notes, ["landings: skipped 3 malformed record(s)"])
        self.assertIsNone(metrics.iso(1e30))  # an absurd time is dropped, not an exception

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
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        old = land.main_worktree
        land.main_worktree = lambda: self.root
        self.addCleanup(setattr, land, "main_worktree", old)

    def journal(self):
        return [json.loads(l) for l in (self.root / ".claude/.state/landings.jsonl").read_text().splitlines()]

    def test_record_landing_appends_with_attempt_number_and_step_timings(self):
        info = dict(branch="br", task="TASK-9", steps={"verify": 12.34, "verify:build": 5.55})
        land.record_landing(info, 1, 20.0)
        land.record_landing(info, 0, 21.0)
        recs = self.journal()
        self.assertEqual([(r["attempt"], r["code"], r["task"]) for r in recs], [(1, 1, "TASK-9"), (2, 0, "TASK-9")])
        self.assertEqual(recs[0]["steps"], {"verify": 12.3, "verify:build": 5.5})
        self.assertEqual(metrics.parse_landings([json.dumps(recs[1])])[0]["verify_s"], 12.3)

    def test_concurrent_landings_get_distinct_attempts(self):
        import threading
        threads = [threading.Thread(target=land.record_landing, args=(dict(branch="br", steps={}), 0, 1.0)) for _ in range(12)]
        [x.start() for x in threads]
        [x.join() for x in threads]
        self.assertEqual(sorted(r["attempt"] for r in self.journal()), list(range(1, 13)))

    def test_record_landing_swallows_write_error(self):
        (self.root / ".claude").mkdir()
        (self.root / ".claude/.state").write_text("a file, so mkdir and open fail")
        land.record_landing(dict(branch="br", steps={}), 0, 1.0)  # must not raise

    def test_record_landing_skips_without_branch(self):
        land.record_landing({"steps": {}}, 4, 1.0)
        self.assertFalse((self.root / ".claude").exists())

    def test_lock_timeout_is_journaled(self):
        import fcntl
        lock = self.root / "land.lock"
        fd = os.open(lock, os.O_CREAT | os.O_RDWR)
        fcntl.flock(fd, fcntl.LOCK_EX)
        self.addCleanup(os.close, fd)
        old = (land.LAND_LOCK, land.LAND_WAIT_S, sys.argv)
        land.LAND_LOCK, land.LAND_WAIT_S, sys.argv = lock, 0, ["land.py", "br", "-m", "msg", "--task", "TASK-1"]
        try:
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(land.main(), 4)
        finally:
            land.LAND_LOCK, land.LAND_WAIT_S, sys.argv = old
        self.assertEqual([(r["branch"], r["task"], r["code"]) for r in self.journal()], [("br", "TASK-1", 4)])

    def test_run_verify_keeps_each_step_time(self):
        steps = {}
        prog = "print('verify: x'); print('  OK   build            12.5 s'); print('  FAIL test-core         3.0 s')"
        with contextlib.redirect_stdout(io.StringIO()):
            rc = land.run_verify([sys.executable, "-c", prog], self.root, steps)
        self.assertEqual(rc, 0)
        self.assertEqual((steps["verify:build"], steps["verify:test-core"]), (12.5, 3.0))
        self.assertIn("verify", steps)

    def test_run_verify_survives_a_broken_console_and_bad_timing_lines(self):
        class Broken:
            def write(self, _s):
                raise OSError("console gone")

            def flush(self):
                raise OSError("console gone")
        steps = {}
        prog = "print('  OK   a                1.2.3 s'); print('  OK   b                 4.0 s'); import sys; sys.exit(3)"
        with contextlib.redirect_stdout(Broken()):
            rc = land.run_verify([sys.executable, "-c", prog], self.root, steps)
        self.assertEqual(rc, 3)  # the exit code is the verify result
        self.assertEqual(steps["verify:b"], 4.0)
        self.assertNotIn("verify:a", steps)


@unittest.skipUnless(kb_db.available(), "PostgreSQL is not available")
class DatabaseTests(unittest.TestCase):
    """Every test gets its own schema, fixture files and tables (setUp), so the tests do not depend on each other."""

    @classmethod
    def setUpClass(cls):
        cls.db = "mtest_" + uuid.uuid4().hex[:10]
        cls.old = {k: os.environ.get(k) for k in ENV_KEYS}
        os.environ["SINGULARITY_KB_DB"] = cls.db

    @classmethod
    def tearDownClass(cls):
        cls.restore_env()
        kb_db.psql("postgres", f"drop database if exists {cls.db}")

    @classmethod
    def restore_env(cls):
        for k, v in cls.old.items():
            os.environ.pop(k, None) if v is None else os.environ.__setitem__(k, v)

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        t = Path(self.tmp.name)
        for sub in ("state", "claude/sess1/subagents", "runs"):
            (t / sub).mkdir(parents=True)
        (t / "state/agents.jsonl").write_text("\n".join([
            line(t=1795000000, event="launched", agent_id="ag1", agent_type="developer", description="d", tasks=["TASK-0206"]),
            line(t=1795000600, event="stop", agent_id="ag1", agent_type="developer")]) + "\n")
        (t / "state/landings.jsonl").write_text(line(t=1795000700, branch="b", task="TASK-0206", attempt=1, code=0,
                                                       total_s=50, steps=dict(verify=40)) + "\n")
        (t / "claude/sess1.jsonl").write_text(assistant("m1", "2026-11-20T10:00:00Z", 7) + "\n"
                                              + assistant("m2", "2026-12-05T10:00:00Z", 1000) + "\n")
        (t / "claude/sess1/subagents/agent-ag1.jsonl").write_text(
            assistant("m9", "2026-11-20T10:00:00Z", 50) + "\n" + assistant("m9", "2026-11-20T10:00:00Z", 50) + "\n")
        (t / "runs/review-TASK-0206-abc-1.json").write_text(json.dumps(dict(
            kind="review", provider="codex", task="TASK-0206", round=1, created_at="2026-11-20T11:00:00+00:00",
            usage=dict(input_tokens=1, output_tokens=2), result=dict(verdict="accept", findings=[]))))
        (t / "ledger.jsonl").write_text(line(id="a", model="m", t="reserve", task="TASK-0206", ts="2026-11-20T11:00:00+00:00", usd="0.1") + "\n")
        self.schema = "t_" + uuid.uuid4().hex[:10]
        self.env = {"SINGULARITY_KB_SCHEMA": self.schema, "SINGULARITY_STATE": str(t / "state"),
                    "SINGULARITY_CLAUDE_PROJECTS": str(t / "claude"), "SINGULARITY_LLM_RUNS": str(t / "runs"),
                    "SINGULARITY_LEDGER": str(t / "ledger.jsonl")}
        os.environ.update(self.env)
        self.addCleanup(self.drop_schema)
        kb_db.build(quiet=True)  # agent_runs and errors come from the kb index
        self.assertEqual(metrics.collect(quiet=True), 0)  # this test's own tables

    def drop_schema(self):
        kb_db.psql(self.db, f'drop schema if exists "{self.schema}" cascade')

    def count(self, table):
        return int(kb_db.psql(self.db, f"select count(*) from {table}", "-At", schema=True).strip())

    def test_collect_is_idempotent_and_keeps_soft_deletes(self):
        first = {t: self.count(t) for t in metrics.COLS}
        self.assertEqual((first["sessions"], first["messages"], first["llm_calls"], first["landings"]), (2, 3, 2, 1))
        self.assertEqual(self.count("sessions where tasks is not null"), 2)
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

    def test_token_totals_of_a_period_are_exact_per_message(self):
        inside = metrics.report_data("2026-11-20", "2026-11-21")["tokens"]
        self.assertEqual(sum(r["out_tokens"] for r in inside), 57)  # m1 (7) + the agent's m9 (50); m2 is in December
        later = metrics.report_data("2026-12-01", "2026-12-31")["tokens"]
        self.assertEqual(sum(r["out_tokens"] for r in later), 1000)
        s = metrics.summary(metrics.report_data("2026-11-20", "2026-11-21", limit=None))
        self.assertEqual(s["claude output tokens"], 57)

    def test_llm_calls_are_counted_once_not_per_ledger_entry(self):
        s = metrics.summary(metrics.report_data("2026-11-20", "2026-11-21", limit=None))
        self.assertEqual((s["llm calls (run records)"], s["ledger entries"]), (1, 1))  # one call, one ledger entry
        self.assertGreater(s["llm usd (ledger)"], 0)

    def test_stalls_include_ongoing_and_are_clipped_to_the_period(self):
        ins = ("insert into task_flow (key, task, t, from_status, to_status, commit_id) values "
               "('k1', 'TASK-9001', '2026-10-01', null, 'APPROVED', 'c1'), ('k2', 'TASK-9001', '2026-10-10', 'APPROVED', 'IN_PROGRESS', 'c2'),"
               "('k3', 'TASK-9002', '2026-10-01', null, 'DONE', 'c3') on conflict do nothing")
        kb_db.psql(self.db, ins, schema=True)
        st = {r["status"]: r for r in metrics.report_data("2026-10-05", "2026-10-08")["stalls"]}
        self.assertEqual(st["APPROVED"]["hours"], 72)  # 3 days inside the period, not 9
        self.assertNotIn("IN_PROGRESS", st)  # starts on 10-10, after the period
        self.assertNotIn("DONE", st)  # a finished task is not waiting
        st = {r["status"]: r for r in metrics.report_data("2026-10-20", "2026-10-21")["stalls"]}
        self.assertEqual((st["IN_PROGRESS"]["hours"], st["IN_PROGRESS"]["ongoing"]), (24, 1))  # still open

    def test_comparison_uses_all_rows_not_the_display_limit(self):
        values = ", ".join(f"('rv{i}', 'review', 'codex', 'm', 'TASK-8{i:03d}', '2026-11-22', 1, 1, 'accept', 0, 1)" for i in range(20))
        kb_db.psql(self.db, "insert into llm_calls (key, kind, provider, model, task, t, input_tokens, output_tokens, verdict, findings, round) "
                            f"values {values} on conflict do nothing", schema=True)
        shown = metrics.report_data("2026-11-21", "2026-11-23")
        self.assertEqual(len(shown["rounds"]), 15)
        self.assertEqual(metrics.summary(metrics.report_data("2026-11-21", "2026-11-23", limit=None))["review records"], 20)

    def test_report_fails_when_refresh_fails(self):
        old = metrics.refresh
        metrics.refresh = lambda: ("collect failed: boom", "")
        try:
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(metrics.main(["report", "--since", "2026-11-01", "--until", "2026-12-31"]), 1)
        finally:
            metrics.refresh = old

    def test_collect_waits_for_the_writer_lock(self):
        import fcntl
        import threading
        import time
        state = Path(self.env["SINGULARITY_STATE"])
        done = threading.Event()
        with (state / "metrics.lock").open("a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            th = threading.Thread(target=lambda: (metrics.refresh(), done.set()))
            th.start()
            time.sleep(0.7)
            self.assertFalse(done.is_set())  # blocked while another collection holds the lock
        th.join(30)
        self.assertTrue(done.is_set())


if __name__ == "__main__":
    unittest.main()
