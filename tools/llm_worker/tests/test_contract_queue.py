import json
import unittest

from helpers import NODES_CSV, TempRepo, a2_contract, load_contract

from tools.llm_worker import paths
from tools.llm_worker.contract import ContractError, validate_contract
from tools.llm_worker.taskqueue import QUEUED, RUNNING, TaskQueue


class PilotContractsTest(unittest.TestCase):
    def test_pilot_contracts_are_valid(self):
        for name in ("A1-product-class-inventory.json", "A2-jet-nodes-clean.json", "B1-fx6-tests.json"):
            self.assertEqual(validate_contract(load_contract(name)), [], name)


class ContractValidationTest(unittest.TestCase):
    def setUp(self):
        self.repo = TempRepo({"data/nodes.csv": NODES_CSV})
        self.q = TaskQueue(self.repo.tmp / "queue")

    def tearDown(self):
        self.repo.cleanup()

    def errors(self, **over):
        return validate_contract(a2_contract(repo=self.repo, **over), repo_root=self.repo.root)

    def test_valid(self):
        self.assertEqual(self.errors(), [])

    def test_rules(self):
        self.assertTrue(self.errors(max_rounds=3), "more than 2 retries")
        self.assertTrue(self.errors(max_candidates=2), "2 candidates only for B1")
        self.assertTrue(self.errors(category="C"))
        self.assertTrue(self.errors(category="B"))
        self.assertTrue(self.errors(allowed_paths=["docs/**"]), "category A writes nothing")
        self.assertTrue(self.errors(task_id="bad/id"))
        self.assertTrue(self.errors(acceptance=[{"check": "all_found", "hard": True}]))

    def test_excluded_or_changed_context_rejected(self):
        c = a2_contract(repo=self.repo)
        c["context_files"].append({"path": ".env", "sha256": "0" * 64})
        self.assertTrue(any("excluded" in e for e in validate_contract(c, repo_root=self.repo.root)))
        c = a2_contract(repo=self.repo)
        (self.repo.root / "data/nodes.csv").write_text(NODES_CSV + "4,d,FND,G0,,\n")
        self.assertTrue(any("changed" in e for e in validate_contract(c, repo_root=self.repo.root)))

    def test_invalid_contract_never_enters_queue(self):
        c = a2_contract(repo=self.repo, max_rounds=5)
        with self.assertRaises(ContractError):
            self.q.submit(c, repo_root=self.repo.root)
        self.assertEqual(self.q.task_ids(), [])

    def test_submit_transitions_and_immutability(self):
        c = a2_contract(repo=self.repo)
        self.q.submit(c, repo_root=self.repo.root)
        tid = c["task_id"]
        st = self.q.state(tid)
        self.assertEqual(st["state"], QUEUED)
        self.assertEqual([h["to"] for h in st["history"]], ["CREATED", "QUEUED"])
        self.assertTrue(all(h["reason"] for h in st["history"]))
        with self.assertRaises(ContractError):
            self.q.submit(c, repo_root=self.repo.root)
        f = self.q.root / tid / "contract.json"
        tampered = json.loads(f.read_text())
        tampered["max_rounds"] = 0
        f.write_text(json.dumps(tampered))
        with self.assertRaises(ContractError):
            self.q.contract(tid)

    def test_recover_running_tasks(self):
        c = a2_contract(repo=self.repo)
        self.q.submit(c, repo_root=self.repo.root)
        self.q.transition(c["task_id"], RUNNING, "picked")
        with self.assertRaises(ValueError):
            self.q.transition(c["task_id"], RUNNING, "again")
        self.q.recover()
        st = self.q.state(c["task_id"])
        self.assertEqual(st["state"], QUEUED)
        self.assertEqual(st["history"][-1]["reason"], "resumed after restart")


class PathsTest(unittest.TestCase):
    def test_glob(self):
        self.assertTrue(paths.matches("a/b/c.cs", ["a/**"]))
        self.assertTrue(paths.matches("x/.env", ["**/.env"]))
        self.assertTrue(paths.matches(".env", ["**/.env"]))
        self.assertFalse(paths.matches("a/b/c.cs", ["a/*.cs"]))

    def test_write_allowed(self):
        allowed = ["tests/X/Llm/*.cs"]
        self.assertTrue(paths.write_allowed("tests/X/Llm/A.cs", allowed, [])[0])
        for bad in ("../tests/X/Llm/A.cs", "/etc/passwd", "tests/X/Llm/../../A.cs", "tests/X/B.cs",
                    "tests/X/Llm/sub/A.cs", "~/A.cs"):
            self.assertFalse(paths.write_allowed(bad, allowed, [])[0], bad)
        self.assertFalse(paths.write_allowed("src/Singularity.Core/A.cs", ["src/**"], [])[0])
        self.assertFalse(paths.write_allowed("tests/X/Llm/A.cs", allowed, ["tests/**"])[0])


if __name__ == "__main__":
    unittest.main()
