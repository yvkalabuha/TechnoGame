"""TASK-0045: sandbox failure is not a Codex failure, `related` via PyYAML, report metrics, unittest verifier."""

import json
import os
import subprocess
import tempfile
import time
import unittest
from decimal import Decimal
from pathlib import Path
from unittest import mock

from helpers import load_contract

from tools.llm_worker import codex, report, review
from tools.llm_worker.contract import validate_contract
from tools.llm_worker.verifiers.b1_tests import parse_test_output
from test_review import make_repo


def run_codex_with(proc):
    with mock.patch.object(codex, "find_codex", return_value="/x/bin/codex"), \
            mock.patch.object(codex, "codex_mounts", return_value=(Path("/x/c.js"), [], Path("/x"))), \
            mock.patch.object(codex, "bwrap_argv", return_value=["bwrap"]), \
            mock.patch("subprocess.run", return_value=proc):
        return codex.run_codex("p", "/w", "/s")


class SandboxFailureTest(unittest.TestCase):
    def test_bwrap_diagnostics_are_sandbox_unavailable(self):
        for err in ("bwrap: No permissions to create new namespace, likely because the kernel does not allow "
                    "non-privileged user namespaces",
                    "warn\nbwrap: Can't mount proc on /newroot/proc: Operation not permitted"):
            with self.assertRaises(codex.SandboxUnavailable):
                run_codex_with(mock.Mock(returncode=1, stdout="", stderr=err))

    def test_other_failures_stay_codex_failed(self):
        with self.assertRaises(codex.CodexFailed):
            run_codex_with(mock.Mock(returncode=1, stdout="", stderr="auth error"))
        with self.assertRaises(codex.CodexFailed):  # "bwrap" inside a line is not bwrap's own diagnostic
            run_codex_with(mock.Mock(returncode=1, stdout="", stderr="tool said: bwrap: x"))

    def test_limit_still_wins_over_stderr_text(self):
        out = json.dumps({"type": "error", "message": "usage limit"})
        with self.assertRaises(codex.CodexLimit):
            run_codex_with(mock.Mock(returncode=1, stdout=out, stderr=""))

    def test_review_does_not_fall_back_to_paid_provider(self):
        from helpers import FakeProvider
        from test_review import GOOD
        repo, base, head = make_repo()
        fake = FakeProvider([GOOD])
        proc = mock.Mock(returncode=1, stdout="", stderr="bwrap: Creating new namespace failed")
        real_run = subprocess.run

        def fake_run(argv, *a, **kw):  # only the bwrap call is faked; git runs for real
            return proc if argv[0] == "bwrap" else real_run(argv, *a, **kw)

        with tempfile.TemporaryDirectory() as d, \
                mock.patch.object(codex, "find_codex", return_value="/x/bin/codex"), \
                mock.patch.object(codex, "codex_mounts", return_value=(Path("/x/c.js"), [], Path("/x"))), \
                mock.patch.object(codex, "bwrap_argv", return_value=["bwrap"]), \
                mock.patch("subprocess.run", side_effect=fake_run), \
                mock.patch.object(config_wt(), "WORKTREES_DIR", Path(d) / "wt"):
            with self.assertRaises(review.ReviewError) as cm:
                review.run_review("TASK-0001", base, head, root=repo.root, runs_dir=Path(d) / "runs",
                                  make_provider=lambda: fake, attempts_dir=Path(d) / "att", log=lambda m: None)
        self.assertEqual(cm.exception.code, "sandbox_unavailable")
        self.assertEqual(fake.calls, [])


def config_wt():
    from tools.llm_worker import config
    return config


class RelatedTest(unittest.TestCase):
    def test_forms(self):
        cases = {
            "---\nrelated: [ADR-0001, 'TASK-0002']\n---\n": ["ADR-0001", "TASK-0002"],
            "---\nrelated:\n  - ADR-0001\n  - TASK-0002  # c\n---\n": ["ADR-0001", "TASK-0002"],
            "---\nrelated: ADR-0001, ADR-0002\n---\n": ["ADR-0001", "ADR-0002"],
            "---\nrelated: []\n---\n": [],
            "---\nid: X\n---\n": [],
            "no frontmatter\n": [],
        }
        for text, ids in cases.items():
            self.assertEqual(review._related_ids(text), (ids, None), text)

    def test_problems_are_reported(self):
        for text in ("---\nrelated: [a\n---\n", "---\nrelated: x\n", "---\nrelated:\n  - {a: 1}\n---\n"):
            ids, problem = review._related_ids(text)
            self.assertTrue(problem, text)

    def test_gate_includes_block_list_and_records_parse_failure(self):
        doc = "---\nid: ADR-0100\nrelated:\n  - ADR-0002\n---\n# D\n"
        repo, _, head = make_repo({"docs/decisions/ADR-0100-x.md": doc})
        text, meta = review.build_gate_input("docs/decisions/ADR-0100-x.md", head, repo.root)
        self.assertIn("docs/decisions/ADR-0002-determinism-levels.md", meta["files"])
        self.assertEqual(meta["skipped"], [])
        bad = "---\nid: ADR-0101\nrelated: [ADR-0002\n---\n# D\n"
        repo, _, head = make_repo({"docs/decisions/ADR-0101-x.md": bad})
        _, meta = review.build_gate_input("docs/decisions/ADR-0101-x.md", head, repo.root)
        self.assertEqual(meta["files"], [])
        self.assertEqual(len(meta["skipped"]), 1)
        self.assertTrue(meta["skipped"][0].startswith("related: yaml"))


class FakeLedger:
    def __init__(self, by_task):
        self.by_task = by_task

    def summary(self):
        return {"total": sum(self.by_task.values(), Decimal(0)), "by_task": self.by_task, "by_model": {},
                "calls": 1, "unsettled": 0}


def rec(task, verdict, severities):
    return {"kind": "review", "task": task, "provider": "codex", "result": {
        "verdict": verdict, "summary": "s", "findings": [
            {"severity": s, "path": "a", "line": 1, "claim": "c", "evidence": ""} for s in severities]}}


class MetricsTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.runs = Path(self.tmp.name) / "runs"
        self.runs.mkdir()
        self.tasks = Path(self.tmp.name) / "tasks"
        self.tasks.mkdir()

    def tearDown(self):
        self.tmp.cleanup()

    def put(self, name, data, age):
        p = self.runs / name
        p.write_text(data if isinstance(data, str) else json.dumps(data))
        os.utime(p, (time.time() - age, time.time() - age))

    def section(self, ledger=None):
        return report._metrics_section(self.runs, ledger, self.tasks)

    def test_rounds_first_verdict_top_severity_and_usd(self):
        self.put("review-TASK-0001-aaaaaaaa-codex.json", rec("TASK-0001", "changes_requested", ["high", "low"]), 30)
        self.put("review-TASK-0001-bbbbbbbb-codex.json", rec("TASK-0001", "accept", ["medium", "low"]), 20)
        self.put("review-TASK-0002-cccccccc-codex.json", rec("TASK-0002", "changes_requested", ["high"]), 10)
        self.put("review-TASK-0003-dddddddd-codex-invalid.json", "{}", 5)
        self.put("review-TASK-0004-eeeeeeee-codex.json", "not json", 5)
        out = "\n".join(self.section(FakeLedger({"TASK-0001": Decimal("0.5")})))
        t1 = next(x for x in out.splitlines() if "`TASK-0001`" in x)
        self.assertIn("до accept: 2", t1)
        self.assertIn("вердикт 1-го раунда changes_requested", t1)
        self.assertIn("в принятом раунде medium", t1)
        self.assertIn("high 1, medium 1, low 2", t1)
        self.assertIn("$0.5000", t1)
        t2 = next(x for x in out.splitlines() if "`TASK-0002`" in x)
        self.assertIn("accept не получен", t2)
        self.assertNotIn("TASK-0003", out)
        self.assertNotIn("TASK-0004", out)

    def test_empty_and_no_ledger(self):
        out = "\n".join(self.section())
        self.assertIn("Записей второго ревью", out)
        self.assertIn("ledger не передан", out)

    def test_escaped_high_from_fixes_links(self):
        (self.tasks / "TASK-0050-a.md").write_text("---\nid: TASK-0050\nfixes: TASK-0007\n---\nfixes: TASK-0099\n")
        (self.tasks / "TASK-0051-b.md").write_text("---\nid: TASK-0051\nfixes:\n  - TASK-0008\n  - TASK-0009\n---\n")
        (self.tasks / "TASK-0052-c.md").write_text("---\nid: TASK-0052\nrelated: [TASK-0007]\n---\n")
        line = [x for x in self.section() if "Сбежавшие high" in x][0]
        self.assertIn(": 3:", line)
        for t in ("TASK-0007", "TASK-0008", "TASK-0009"):
            self.assertIn(t, line)
        self.assertNotIn("TASK-0099", line)  # only frontmatter counts

    def test_build_has_metrics_section(self):
        text = report.build(self.tasks / "q", self.runs, "2026-01-01", tasks_dir=self.tasks)
        self.assertIn("## Метрики (ADR-0015 §7)", text)


class UnittestOutputTest(unittest.TestCase):
    def test_unittest(self):
        ok = "..........\n----------------------------------------------------------------------\nRan 10 tests in 0.001s\n\nOK\n"
        self.assertEqual(parse_test_output(ok), (10, 0))
        self.assertEqual(parse_test_output(ok.replace("OK", "OK (skipped=2)")), (8, 0))
        bad = "Ran 10 tests in 0.1s\n\nFAILED (failures=2, errors=1)\n"
        self.assertEqual(parse_test_output(bad), (7, 3))
        self.assertEqual(parse_test_output("Ran 0 tests in 0.000s\n\nOK\n"), (0, 0))
        self.assertEqual(parse_test_output("Ran 4 tests in 0.1s\n"), (0, 0))  # interrupted: no verdict
        self.assertEqual(parse_test_output("Ran 3 tests in 0.1s\n\nFAILED (errors=3)\n"), (0, 3))

    def test_dotnet_english_and_russian(self):
        self.assertEqual(parse_test_output("Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12"), (12, 0))
        self.assertEqual(parse_test_output("Failed!  - Failed:     1, Passed:     2, Skipped:     0, Total:     3"), (2, 1))
        self.assertEqual(parse_test_output("Пройден!  - не пройдено: 0, пройдено: 12, пропущено: 0, всего: 12"), (12, 0))
        self.assertEqual(parse_test_output("Сбой!  - не пройдено 1, пройдено 2, пропущено 0, всего 3"), (2, 1))
        self.assertEqual(parse_test_output("Build succeeded."), (0, 0))

    def test_l1_contract_uses_unittest_directly(self):
        c = load_contract("L1-risk19-yield-tests.json")
        self.assertEqual(validate_contract(c), [])
        self.assertEqual(c["verification"]["test_cmd"][:4], ["python3", "-B", "-m", "unittest"])
        self.assertNotIn("-c", c["verification"]["test_cmd"])


if __name__ == "__main__":
    unittest.main()
