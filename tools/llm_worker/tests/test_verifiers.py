import unittest

from helpers import FakeProvider, TempRepo, load_contract, sha

from tools.llm_worker import worktree
from tools.llm_worker.reference.nodes_check import check_rows
from tools.llm_worker.runner import Env, process_task
from tools.llm_worker.taskqueue import TaskQueue
from tools.llm_worker.verifiers import a1_inventory, a2_data
from tools.llm_worker.verifiers.b1_tests import B1Verifier

CS = '''
namespace N;
/// <summary>class Ghost in a comment</summary>
public sealed partial class Table
{
    public int IndexOf(string id) => throw new System.Exception("class Fake");
    private static (int A, string B) ReadPair(Node n) { return (1, ""); }
    public Table(int x) { }
    public static Table operator +(Table a, Table b) => a;
    public int Count => 0;
}
public readonly record struct Pair(int A, int B);
'''


class A1Test(unittest.TestCase):
    def test_scan_ignores_comments_strings_ctors_operators(self):
        types, methods = a1_inventory.scan(CS)
        self.assertEqual(types, {"Table", "Pair"})
        self.assertEqual(methods, {"IndexOf", "ReadPair"})

    def test_measure(self):
        good = {"types": [
            {"name": "Table", "kind": "class", "file": "f.cs", "methods": ["IndexOf", "ReadPair"], "depends_on": ["Node"]},
            {"name": "Pair", "kind": "record struct", "file": "f.cs", "methods": [], "depends_on": []}]}
        m = a1_inventory.measure(good, {"f.cs": CS})
        self.assertTrue(all(v == 1.0 for v, _ in m.values()), m)
        bad = {"types": [
            {"name": "Table", "kind": "class", "file": "f.cs", "methods": ["IndexOf", "Invented"], "depends_on": ["Ghost"]},
            {"name": "Ghost", "kind": "class", "file": "f.cs", "methods": [], "depends_on": []}]}
        m = a1_inventory.measure(bad, {"f.cs": CS})
        self.assertEqual(m["types_exist"][0], 0.5)
        self.assertIn("Ghost", m["types_exist"][1])
        self.assertEqual(m["types_complete"][0], 0.5)
        self.assertEqual(m["methods_exist"][0], 0.5)
        self.assertEqual(m["methods_recall"][0], 0.5)
        self.assertEqual(m["deps_exist"][0], 0.0)


class A2Test(unittest.TestCase):
    ROWS = [
        {"idx": "1", "id": "a", "gen": "G1", "requires": "b", "known_at_start": "USA"},
        {"idx": "2", "id": "b", "gen": "G2", "requires": "a|z", "known_at_start": ""},
        {"idx": "2", "id": "c", "gen": "G0", "requires": "c", "known_at_start": ""},
        {"idx": "4", "id": "c", "gen": "G0", "requires": "", "known_at_start": "ALL"},
    ]

    def test_reference(self):
        self.assertEqual(check_rows(self.ROWS), [
            ("cycle", "a", ""), ("cycle", "b", ""), ("cycle", "c", ""),
            ("dangling_requires", "b", "z"), ("duplicate_id", "c", ""), ("duplicate_idx", "2", ""),
            ("generation_order", "a", "b"), ("start_closure", "a@USA", "b"),
        ])

    def test_clean_pilot_table_has_no_findings(self):
        from tools.llm_worker.config import REPO_ROOT
        from tools.llm_worker.reference.nodes_check import load
        self.assertEqual(check_rows(load(REPO_ROOT / "docs/research/data/jet-slice/nodes.csv")), [])

    def test_false_and_missed_findings(self):
        ref = [("dangling_requires", "b", "z")]
        out = {"findings": [{"check": "cycle", "node_id": "a", "ref": "", "explanation": "short"}]}
        m = a2_data.measure(out, ref)
        self.assertEqual(m["no_false_findings"][0], 0.0)
        self.assertEqual(m["all_found"][0], 0.0)
        self.assertEqual(m["explanations"][0], 0.0)
        self.assertEqual(a2_data.measure({"findings": []}, [])["no_false_findings"][0], 1.0)


SRC = "namespace K;\npublic static class M { public static int Add(int a, int b) => a + b; }\n"
TEST_FILE = "tests/K.Tests/Llm/MTests.cs"


def fake_dotnet():
    """Build always succeeds; tests fail iff the source was mutated."""
    def run(cmd, cwd, timeout, network=False):
        if cmd[1] in ("restore", "build"):
            return 0, "Build succeeded."
        mutated = "a - b" in (cwd / "src/K/M.cs").read_text()
        if mutated:
            return 1, "Failed!  - Failed:     1, Passed:     2, Skipped:     0, Total:     3"
        return 0, "Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12"
    return run


def b1_contract(**over):
    c = load_contract("B1-fx6-tests.json")
    c.update(task_id="T-b1", allowed_paths=["tests/K.Tests/Llm/*.cs"], forbidden_paths=["src/**"])
    c["verification"] = dict(c["verification"], mutants=[
        {"file": "src/K/M.cs", "find": "a + b", "replace": "a - b"},
        {"file": "src/K/M.cs", "find": "int a", "replace": "int a "}])  # equivalent mutant: survives
    c["acceptance"] = [a if a["check"] != "mutation_score" else dict(a, threshold=0.5) for a in c["acceptance"]]
    c.update(over)
    return c


class B1Test(unittest.TestCase):
    def setUp(self):
        self.repo = TempRepo({"src/K/M.cs": SRC, "README.md": "x\n"})
        self.head = self.repo.git("rev-parse", "HEAD").strip()

    def tearDown(self):
        self.repo.cleanup()

    def verifier(self, contract):
        meta = self.repo.tmp / "runs" / "worktree.json"
        path, base, branch = worktree.ensure(self.repo.root, self.repo.tmp / "wt", contract["task_id"], 1, meta)
        return B1Verifier(contract, path, base, branch, runner=fake_dotnet()), path

    def assert_main_untouched(self):
        self.assertEqual(self.repo.git("rev-parse", "HEAD").strip(), self.head)
        self.assertEqual(self.repo.git("status", "--porcelain"), "")
        self.assertEqual(self.repo.git("branch", "--show-current").strip(), "main")

    def test_checks_and_mutants_in_worktree_only(self):
        v, path = self.verifier(b1_contract())
        r = v.verify({"files": [{"path": TEST_FILE, "content": "// tests\n"}]})
        by = {c["check"]: c for c in r["checks"]}
        self.assertTrue(r["hard_passed"], r)
        self.assertEqual(by["mutation_score"]["value"], 0.5)
        self.assertIn("survived", by["mutation_score"]["detail"])
        self.assertEqual(by["test_count"]["value"], 12)
        self.assertEqual((path / "src/K/M.cs").read_text(), SRC)  # mutants restored
        self.assertTrue((path / TEST_FILE).is_file())
        self.assertFalse((self.repo.root / TEST_FILE).exists())
        changed = v.finalize({"files": [{"path": TEST_FILE, "content": "// tests\n"}]})
        self.assertEqual(changed, [TEST_FILE])
        self.assertIn(TEST_FILE, self.repo.git("show", "--name-only", "llm/T-b1"))
        self.assert_main_untouched()

    def test_write_outside_allowed_paths_is_scope_violation(self):
        v, path = self.verifier(b1_contract())
        r = v.verify({"files": [{"path": TEST_FILE, "content": "ok"},
                                {"path": "src/K/M.cs", "content": "hacked"}]})
        self.assertTrue(r["scope_violation"])
        self.assertFalse(r["hard_passed"])
        self.assertEqual((path / "src/K/M.cs").read_text(), SRC)
        self.assertFalse((path / TEST_FILE).exists())  # nothing is written when any path is bad
        self.assert_main_untouched()

    def test_two_candidates_best_wins_and_scope_violation_rejected(self):
        c = b1_contract()
        c["context_files"] = [{"path": "src/K/M.cs", "sha256": sha(self.repo.root / "src/K/M.cs")}]
        q = TaskQueue(self.repo.tmp / "queue")
        q.submit(c, repo_root=self.repo.root)
        env = Env(repo_root=self.repo.root, runs_dir=self.repo.tmp / "runs", worktrees_dir=self.repo.tmp / "wt",
                  lessons_dir=self.repo.tmp / "lessons", b1_runner=fake_dotnet())
        out = {"status": "done", "assumptions": [], "open_questions": []}
        bad = dict(out, files=[{"path": "src/K/M.cs", "content": "x"}])
        good = dict(out, files=[{"path": TEST_FILE, "content": "// t\n"}])
        provider = FakeProvider([bad, good])
        state = process_task(q, "T-b1", provider, env)
        self.assertEqual(state, "ACCEPTED")
        st = q.state("T-b1")
        self.assertEqual(st["chosen_candidate"], 2)
        self.assertEqual([x["outcome"] for x in st["candidates"]], ["rejected", "passed"])
        self.assertEqual([call["seed"] for call in provider.calls], [1, 2])
        self.assertIn(TEST_FILE, self.repo.git("show", "--name-only", "llm/T-b1-c2"))
        self.assert_main_untouched()


if __name__ == "__main__":
    unittest.main()
