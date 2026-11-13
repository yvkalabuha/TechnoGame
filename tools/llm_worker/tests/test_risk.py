import json
import unittest

from helpers import TempRepo

from tools.llm_worker import config, risk
from tools.llm_worker.report import build
from tools.llm_worker.taskqueue import TaskQueue


class ClassifyPathTest(unittest.TestCase):
    def cls(self, path):
        return risk.classify_path(path)

    def test_basic_classes(self):
        self.assertEqual(self.cls("docs/research/x.md"), "R0")
        self.assertEqual(self.cls("tests/other/x.cs"), "R0")
        self.assertEqual(self.cls("tools/foo/x.py"), "R1")
        self.assertEqual(self.cls("src/Singularity.Lab/Program.cs"), "R1")
        self.assertEqual(self.cls("src/Singularity.Core/Data/X.cs"), "R2")
        self.assertEqual(self.cls("docs/decisions/ADR-0015.md"), "R3")
        self.assertEqual(self.cls("lab/results/x.json"), "R3")
        self.assertEqual(self.cls("lab/runners/a.py"), "R1")

    def test_unknown_path_is_r2(self):
        self.assertEqual(self.cls("README.md"), "R2")
        self.assertEqual(self.cls("src/NewProject/X.cs"), "R2")

    def test_build_files_are_r2(self):
        for p in ("src/Directory.Build.props", "tests/Directory.Build.props", "Directory.Packages.props",
                  "a/b.targets", "nuget.config", "sub/NuGet.Config", "global.json", "x/y.runsettings",
                  "src/Singularity.Lab/Singularity.Lab.csproj", "Singularity.sln"):
            self.assertEqual(self.cls(p), "R2", p)

    def test_real_determinism_files_are_r2(self):
        for p in ("tests/Singularity.Session.Tests/Fixtures/t6c2-baseline.txt",
                  "tests/Singularity.Kernel.Tests/StateHashTests.cs",
                  "tests/Singularity.Core.Tests/Data/DataHashTests.cs",
                  "tests/Singularity.Kernel.Tests/Audit/StateHashSensitivityTests.cs",
                  "tests/Singularity.Architecture.Tests/DeterminismRules.cs",
                  "src/Singularity.Kernel/StateHash.cs", "tests/other/my_golden_file.txt"):
            self.assertEqual(self.cls(p), "R2", p)

    def test_case_and_dot_segments(self):
        self.assertEqual(self.cls("SRC/singularity.core/X.cs"), "R2")
        self.assertEqual(self.cls("Docs/Decisions/A.md"), "R3")
        self.assertEqual(self.cls("./docs/decisions/A.md"), "R3")
        self.assertEqual(self.cls("docs//decisions/A.md"), "R3")
        self.assertEqual(self.cls("TOOLS/LLM_Worker/risk_paths.JSON"), "R3")

    def test_escape_is_r3(self):
        for p in ("../x", "docs/../../x", "/etc/passwd", "~/x", "a\\b", ""):
            self.assertEqual(self.cls(p), "R3", p)

    def test_class_only_goes_up(self):
        self.assertEqual(risk.combine("R1", "R0"), "R1")
        self.assertEqual(risk.classify_paths(["docs/a.md", "tools/x.py", "tests/Singularity.Core.Tests/a.cs"]), "R2")
        self.assertEqual(risk.classify_paths([]), "R0")

    def test_write_forbidden_is_r2_plus_r3(self):
        data = json.loads(config.RISK_PATHS_FILE.read_text())
        self.assertEqual(config.WRITE_FORBIDDEN, data["R2"] + data["R3"])
        self.assertIn("tools/llm_worker/risk_paths.json", config.WRITE_FORBIDDEN)
        self.assertIn("**/*.targets", config.WRITE_FORBIDDEN)


class FrontmatterTest(unittest.TestCase):
    def test_approval_fields(self):
        a = "---\nid: T\nstatus: PROPOSED   # c\napproved_by: owner\n---\nbody\n"
        self.assertFalse(risk.approval_changed(a, a.replace("body", "other body")))
        self.assertFalse(risk.approval_changed(a, a.replace("id: T", "id: U")))
        self.assertTrue(risk.approval_changed(a, a.replace("owner", "orchestrator")))
        self.assertTrue(risk.approval_changed(a, a.replace("PROPOSED", "APPROVED")))
        self.assertTrue(risk.approval_changed(a, a.replace("approved_by: owner\n", "")))
        self.assertTrue(risk.approval_changed(None, a))


class ClassifyRangeTest(unittest.TestCase):
    def setUp(self):
        self.repo = TempRepo({
            "docs/note.md": "---\nid: N\napproved_by: owner\n---\nx\n",
            "tools/a.py": "a\n", "tools/gone.py": "g\n",
            "src/Singularity.Core/K.cs": "k\n",
        })
        self.repo.git("checkout", "-q", "-b", "llm/t")

    def tearDown(self):
        self.repo.cleanup()

    def commit(self, msg="c"):
        self.repo.git("add", "-A")
        self.repo.git("commit", "-q", "-m", msg)

    def write(self, rel, text):
        p = self.repo.root / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text)

    def classify(self):
        return risk.classify_range("main...llm/t", self.repo.root)

    def test_low_risk_diff(self):
        self.write("tools/a.py", "b\n")
        self.write("docs/new.md", "x\n")
        self.commit()
        overall, rows = self.classify()
        self.assertEqual(overall, "R1")
        self.assertEqual(sorted(r[0] for r in rows), ["docs/new.md", "tools/a.py"])

    def test_rename_counts_both_sides(self):
        self.repo.git("mv", "src/Singularity.Core/K.cs", "tools/K.cs")
        self.commit()
        overall, rows = self.classify()
        self.assertEqual(overall, "R2")
        self.assertEqual(sorted(r[0] for r in rows), ["src/Singularity.Core/K.cs", "tools/K.cs"])
        self.repo.git("checkout", "-q", "main")
        self.repo.git("checkout", "-q", "-B", "llm/t")
        self.repo.git("mv", "tools/a.py", "src/Singularity.Core/a.py")
        self.commit()
        self.assertEqual(self.classify()[0], "R2")

    def test_approval_edit_is_r3_even_in_r0_path(self):
        self.write("docs/note.md", "---\nid: N\napproved_by: orchestrator\n---\nx\n")
        self.commit()
        overall, rows = self.classify()
        self.assertEqual(overall, "R3")
        self.assertEqual(rows[0][2], "approval fields in frontmatter")

    def test_body_edit_of_markdown_stays_r0(self):
        self.write("docs/note.md", "---\nid: N\napproved_by: owner\n---\nchanged\n")
        self.commit()
        self.assertEqual(self.classify()[0], "R0")

    def test_deleted_golden_is_r2_and_cli_exit_codes(self):
        self.write("tests/Singularity.Session.Tests/Fixtures/t6c2-baseline.txt", "h\n")
        self.commit()
        self.assertEqual(self.classify()[0], "R2")
        self.assertEqual(risk.main(["classify", "--range", "nope...llm/t"]), 2)

    def test_empty_diff_is_r0(self):
        self.assertEqual(self.classify(), ("R0", []))


class ReportContractErrorTest(unittest.TestCase):
    def test_changed_contract_goes_to_unverified(self):
        from helpers import NODES_CSV, a2_contract
        repo = TempRepo({"data/nodes.csv": NODES_CSV})
        try:
            q = TaskQueue(repo.tmp / "queue")
            q.submit(a2_contract(repo=repo, task_id="T-old"), repo_root=repo.root)
            path = q.root / "T-old" / "contract.json"
            path.write_text(path.read_text().replace('"max_rounds": 2', '"max_rounds": 1'))
            text = build(q.root, repo.tmp / "runs", "2026-11-13")
            self.assertIn("Не проверено (не дошло до проверки: в очереди или прервано) — 1", text)
            self.assertIn("T-old", text)
        finally:
            repo.cleanup()



class RunnerStopReasonsTest(unittest.TestCase):
    """SecretDetected fails one task; key and sandbox errors stop the run and requeue the task."""

    def setUp(self):
        from helpers import NODES_CSV
        from tools.llm_worker.runner import Env
        self.repo = TempRepo({"data/nodes.csv": NODES_CSV})
        self.qdir = self.repo.tmp / "queue"
        self.env = Env(repo_root=self.repo.root, runs_dir=self.repo.tmp / "runs",
                       worktrees_dir=self.repo.tmp / "wt", lessons_dir=self.repo.tmp / "lessons")
        self.q = TaskQueue(self.qdir)

    def tearDown(self):
        self.repo.cleanup()

    def run_with(self, exc, ids=("T-a",)):
        from helpers import FakeProvider, a2_contract
        from tools.llm_worker.runner import run_queue
        for t in ids:
            self.q.submit(a2_contract(repo=self.repo, task_id=t), repo_root=self.repo.root)
        logs = []
        done = run_queue(self.qdir, FakeProvider([exc]), self.env, log=logs.append)
        return done, logs

    def test_secret_detected_fails_task_and_run_continues(self):
        from tools.llm_worker.gitleaks import SecretDetected
        done, _ = self.run_with(SecretDetected([{"rule": "aws-key", "line": 7}]), ids=("T-a", "T-b"))
        st = self.q.state("T-a")
        self.assertEqual(st["state"], "FAILED")
        self.assertTrue(st["history"][-1]["reason"].startswith("secret_detected: aws-key@7"))
        self.assertNotEqual(self.q.state("T-b")["state"], "QUEUED")  # the run went on to the next task
        text = build(self.qdir, self.env.runs_dir, "2026-11-13")
        self.assertIn("secret_detected: aws-key@7", text)

    def test_key_error_stops_and_requeues(self):
        from tools.llm_worker.provider import KeyFileError
        done, logs = self.run_with(KeyFileError("key_error: key file is empty"), ids=("T-a", "T-b"))
        self.assertFalse(done)
        self.assertEqual(self.q.state("T-a")["state"], "QUEUED")
        self.assertEqual(self.q.state("T-b")["state"], "QUEUED")
        self.assertTrue(self.q.state("T-a")["history"][-1]["reason"].startswith("key_error, run stopped"))
        self.assertTrue(any("key_error" in m for m in logs))

    def test_sandbox_and_gitleaks_unavailable_stop_and_requeue(self):
        from tools.llm_worker.gitleaks import GitleaksUnavailable
        from tools.llm_worker.sandbox import SandboxUnavailable
        for i, exc in enumerate((GitleaksUnavailable("no gitleaks"), SandboxUnavailable("no bwrap"))):
            tid = f"T-{i}"
            done, logs = self.run_with(exc, ids=(tid,))
            self.assertFalse(done)
            self.assertEqual(self.q.state(tid)["state"], "QUEUED")
            self.assertTrue(self.q.state(tid)["history"][-1]["reason"].startswith("sandbox_unavailable, run stopped"))
            self.assertTrue(any("sandbox_unavailable" in m for m in logs))
            (self.qdir / ".run.lock").unlink(missing_ok=True)
            self.q.transition(tid, "RUNNING", "x")
            self.q.transition(tid, "FAILED", "cleanup")


class IssueAddedLlmTestsTest(unittest.TestCase):
    NEW = "tests/Singularity.Kernel.Tests/Llm/Fx6LlmTests.cs"

    def test_added_vs_modified(self):
        self.assertEqual(risk.classify_path(self.NEW, status="A"), "R1")
        for st in ("M", "D", "R", None):
            self.assertEqual(risk.classify_path(self.NEW, status=st), "R2", st)
        self.assertEqual(risk.classify_path("tests/Singularity.Kernel.Tests/Fx6Tests.cs", status="A"), "R2")
        self.assertEqual(risk.classify_path("tests/Singularity.Core.Tests/Llm/X.csproj", status="A"), "R2")
        self.assertEqual(risk.classify_path("tests/Singularity.Core.Tests/Llm/golden.cs", status="A"), "R2")
        self.assertEqual(risk.classify_path("TESTS/singularity.kernel.tests/LLM/A.CS", status="A"), "R1")

    def test_write_allowed_creates_but_does_not_overwrite(self):
        from tools.llm_worker import paths
        allowed = ["tests/Singularity.Kernel.Tests/Llm/*.cs"]
        self.assertTrue(paths.write_allowed(self.NEW, allowed, [], exists=False)[0])
        self.assertFalse(paths.write_allowed(self.NEW, allowed, [], exists=True)[0])
        self.assertFalse(paths.write_allowed("tests/Singularity.Kernel.Tests/Fx6Tests.cs", ["tests/**"], [])[0])
        self.assertFalse(paths.write_allowed(self.NEW.upper(), ["TESTS/**"], ["tests/**/llm/**"])[0])  # case-insensitive
        self.assertTrue(paths.write_allowed(self.NEW.replace("Llm", "LLM"), allowed, [])[0])

    def test_range_added_is_r1_modified_is_r2(self):
        repo = TempRepo({"tests/Singularity.Kernel.Tests/Llm/Old.cs": "a\n", "tools/a.py": "a\n"})
        try:
            repo.git("checkout", "-q", "-b", "llm/t")
            (repo.root / "tests/Singularity.Kernel.Tests/Llm/New.cs").write_text("n\n")
            repo.git("add", "-A"); repo.git("commit", "-q", "-m", "c")
            self.assertEqual(risk.classify_range("main...llm/t", repo.root)[0], "R1")
            (repo.root / "tests/Singularity.Kernel.Tests/Llm/Old.cs").write_text("b\n")
            repo.git("add", "-A"); repo.git("commit", "-q", "-m", "c2")
            self.assertEqual(risk.classify_range("main...llm/t", repo.root)[0], "R2")
        finally:
            repo.cleanup()

    def test_b1_writer_refuses_existing_llm_file(self):
        import tempfile
        from pathlib import Path
        from tools.llm_worker.verifiers.b1_tests import write_files
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            (root / "tests/Singularity.Kernel.Tests/Llm").mkdir(parents=True)
            (root / self.NEW).write_text("old")
            allowed = ["tests/Singularity.Kernel.Tests/Llm/*.cs"]
            w, v = write_files(root, [{"path": self.NEW, "content": "x"}], allowed, [])
            self.assertEqual((w, len(v)), ([], 1))
            self.assertEqual((root / self.NEW).read_text(), "old")
            new = self.NEW.replace("Fx6Llm", "Other")
            w, v = write_files(root, [{"path": new, "content": "x"}], allowed, [])
            self.assertEqual((w, v), ([new], []))


class IssueContractAllowedPathsTest(unittest.TestCase):
    def test_classify_glob(self):
        g = risk.classify_glob
        self.assertEqual(g("tests/Singularity.Kernel.Tests/Llm/*.cs"), "R1")
        self.assertEqual(g("tests/**"), "R2")  # may cover Kernel.Tests
        self.assertEqual(g("tools/**"), "R3")  # may cover tools/development
        self.assertEqual(g("tools/foo/**"), "R1")
        self.assertEqual(g("docs/**"), "R3")  # may cover decisions
        self.assertEqual(g("docs/research/**"), "R0")
        self.assertEqual(g("src/Singularity.Core/**"), "R2")
        self.assertEqual(g("**/*.cs"), "R3")  # no directory prefix: may reach anything
        self.assertEqual(g("../x"), "R3")

    def test_validation_rejects_allowed_above_risk(self):
        from tools.llm_worker.contract import validate_contract
        from helpers import load_contract
        c = load_contract("B1-fx6-tests.json")
        self.assertEqual([e for e in validate_contract(c) if "allowed_paths" in e], [])
        for bad in (["tests/**"], ["src/Singularity.Core/*.cs"], ["tools/llm_worker/*.py"], ["docs/decisions/*.md"]):
            errs = validate_contract(dict(c, allowed_paths=bad))
            self.assertTrue(any("allowed_paths" in e for e in errs), bad)

    def test_pilot_b1_is_valid(self):
        from tools.llm_worker.contract import validate_contract
        from helpers import load_contract
        self.assertEqual(validate_contract(load_contract("B1-fx6-tests.json")), [])


class IssueFrontmatterAndModesTest(unittest.TestCase):
    A = "---\nid: T\nstatus: PROPOSED\n---\nbody\n"

    def test_quoted_keys_bom_and_unparseable(self):
        self.assertTrue(risk.approval_changed(self.A, self.A.replace("status: PROPOSED", '"status": APPROVED')))
        self.assertTrue(risk.approval_changed(self.A, "\ufeff" + self.A.replace("PROPOSED", "APPROVED")))
        self.assertFalse(risk.approval_changed(self.A, "\ufeff" + self.A))
        self.assertTrue(risk.approval_changed(self.A, "---\nid: T\nstatus PROPOSED\n---\n"))  # no colon
        self.assertTrue(risk.approval_changed(self.A, "---\nid: T\nstatus: PROPOSED\nbody\n"))  # not closed

    def test_symlink_in_range_is_r2(self):
        repo = TempRepo({"docs/a.md": "x\n"})
        try:
            repo.git("checkout", "-q", "-b", "llm/t")
            (repo.root / "docs/link.md").symlink_to("../src/Singularity.Core/X.cs")
            repo.git("add", "-A"); repo.git("commit", "-q", "-m", "c")
            overall, rows = risk.classify_range("main...llm/t", repo.root)
            self.assertEqual(overall, "R2")
            self.assertEqual(rows[0][2], "symlink or submodule")
        finally:
            repo.cleanup()

    def test_paths_file_option(self):
        import tempfile
        from pathlib import Path
        data = json.loads(config.RISK_PATHS_FILE.read_text())
        data["R3"] = data["R3"] + ["docs/**"]  # the branch's own file would not be trusted; the given one is
        with tempfile.TemporaryDirectory() as d:
            f = Path(d) / "r.json"
            f.write_text(json.dumps(data))
            self.assertEqual(risk.classify_path("docs/a.md", risk.load_classes(f)), "R3")
        self.assertEqual(risk.classify_path("docs/a.md"), "R0")


class IssueCliKeyErrorTest(unittest.TestCase):
    def test_key_file_error_exits_4(self):
        from unittest import mock
        from tools.llm_worker import __main__ as cli
        from tools.llm_worker.provider import KeyFileError
        prices = {"ready": True, "cap": 1, "task_cap": 1, "models": {}}
        ledger = mock.Mock()
        ledger.summary.return_value = {}
        with mock.patch.object(cli, "load_prices", return_value=prices), \
                mock.patch.object(cli, "Ledger", return_value=ledger), \
                mock.patch.object(cli, "OpenAICompatProvider", side_effect=KeyFileError("key_error: empty")):
            self.assertEqual(cli.main(["run", "--profile", "deepseek-flash"]), 4)


if __name__ == "__main__":
    unittest.main()
