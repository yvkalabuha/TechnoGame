import unittest

from helpers import NODES_CSV, FakeProvider, TempRepo, a2_contract

from tools.llm_worker import report
from tools.llm_worker.provider import ProviderUnavailable
from tools.llm_worker.runner import Env, run_queue
from tools.llm_worker.taskqueue import TaskQueue

GOOD = {"status": "done", "assumptions": [], "open_questions": [], "findings": [
    {"check": "dangling_requires", "node_id": "c", "ref": "zz", "explanation": "zz is not an id in the table at all"}]}
FALSE = {"status": "done", "assumptions": [], "open_questions": [], "findings": [
    {"check": "cycle", "node_id": "a", "ref": "", "explanation": "a looks like it is on a cycle somewhere"}]}
EXPLAIN = {"failure_cause": "invented a cycle", "fix_plan": "report only zz"}


class RunnerTest(unittest.TestCase):
    def setUp(self):
        self.repo = TempRepo({"data/nodes.csv": NODES_CSV})
        self.qdir = self.repo.tmp / "queue"
        self.env = Env(repo_root=self.repo.root, runs_dir=self.repo.tmp / "runs",
                       worktrees_dir=self.repo.tmp / "wt", lessons_dir=self.repo.tmp / "lessons")
        self.q = TaskQueue(self.qdir)

    def tearDown(self):
        self.repo.cleanup()

    def submit(self, task_id):
        self.q.submit(a2_contract(repo=self.repo, task_id=task_id), repo_root=self.repo.root)

    def test_accept_after_retry_and_report(self):
        self.submit("T-a")
        done = run_queue(self.qdir, FakeProvider([FALSE, EXPLAIN, GOOD]), self.env, log=lambda m: None)
        self.assertTrue(done)
        self.assertEqual(self.q.state("T-a")["state"], "ACCEPTED")
        text = report.build(self.qdir, self.env.runs_dir, "2026-11-03")
        self.assertIn("## Принято (все жёсткие проверки пройдены) — 1", text)
        self.assertIn("качество", text)

    def test_outage_requeues_and_resumes_without_loss(self):
        self.submit("T-b")
        sleeps = []
        provider = FakeProvider([FALSE, ProviderUnavailable("down"), ProviderUnavailable("down")])
        done = run_queue(self.qdir, provider, self.env, max_outages=2, sleep=sleeps.append, log=lambda m: None)
        self.assertFalse(done)
        self.assertEqual(len(sleeps), 1)
        st = self.q.state("T-b")
        self.assertEqual(st["state"], "QUEUED")
        self.assertIn("provider unavailable", st["history"][-1]["reason"])
        text = report.build(self.qdir, self.env.runs_dir, "2026-11-03")
        self.assertIn("## Не проверено (не дошло до проверки: в очереди или прервано) — 1", text)
        self.assertIn("## Принято (все жёсткие проверки пройдены) — 0", text)
        # Server is back: round 0 is not requested again.
        provider = FakeProvider([EXPLAIN, GOOD])
        self.assertTrue(run_queue(self.qdir, provider, self.env, log=lambda m: None))
        self.assertEqual(self.q.state("T-b")["state"], "ACCEPTED")
        self.assertEqual(len(provider.calls), 2)

    def test_escalated_is_not_accepted(self):
        self.submit("T-c")
        run_queue(self.qdir, FakeProvider([FALSE, EXPLAIN, FALSE]), self.env, log=lambda m: None)
        st = self.q.state("T-c")
        self.assertEqual(st["state"], "ESCALATED")
        self.assertIn("same failure twice", st["history"][-1]["reason"])
        text = report.build(self.qdir, self.env.runs_dir, "2026-11-03")
        self.assertIn("## Эскалировано к Pilot (проверки не пройдены) — 1", text)
        self.assertIn("## Принято (все жёсткие проверки пройдены) — 0", text)


class FailedTaskTest(unittest.TestCase):
    def test_changed_context_fails_unverified(self):
        repo = TempRepo({"data/nodes.csv": NODES_CSV})
        try:
            qdir = repo.tmp / "queue"
            env = Env(repo_root=repo.root, runs_dir=repo.tmp / "runs", worktrees_dir=repo.tmp / "wt",
                      lessons_dir=repo.tmp / "lessons")
            q = TaskQueue(qdir)
            q.submit(a2_contract(repo=repo, task_id="T-e"), repo_root=repo.root)
            (repo.root / "data/nodes.csv").write_text(NODES_CSV + "4,d,FND,G0,,\n")
            run_queue(qdir, FakeProvider([]), env, log=lambda m: None)
            st = q.state("T-e")
            self.assertEqual(st["state"], "FAILED")
            self.assertTrue(st["unverified"])
            text = report.build(qdir, env.runs_dir, "2026-11-03")
            self.assertIn("## Провалено (техническая ошибка; результат не проверен) — 1", text)
        finally:
            repo.cleanup()


if __name__ == "__main__":
    unittest.main()
