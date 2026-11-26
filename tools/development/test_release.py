"""Tests of release.py on temporary git repositories (no push, no client, no network)."""
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import release  # noqa: E402


def run(root, *args):
    subprocess.run(["git", *args], cwd=root, check=True, capture_output=True, text=True)


class Repo(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name) / "repo"
        self.root.mkdir()
        run(self.root, "init", "-q", "-b", "main")
        run(self.root, "config", "user.email", "t@example.com")
        run(self.root, "config", "user.name", "t")
        run(self.root, "config", "commit.gpgsign", "false")
        run(self.root, "config", "tag.gpgsign", "false")
        (self.root / "development" / "tasks").mkdir(parents=True)
        (self.root / "development" / "TECH_DEBT.md").write_text("# D\n", encoding="utf-8")
        self.commit("init")
        self.env = os.environ.copy()
        os.environ["SINGULARITY_RELEASES"] = str(Path(self.tmp.name) / "rel")

    def tearDown(self):
        os.environ.pop("SINGULARITY_RELEASES", None)
        self.tmp.cleanup()

    def commit(self, msg, files=None):
        for name, text in (files or {}).items():
            p = self.root / name
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text(text, encoding="utf-8")
        run(self.root, "add", "-A")
        run(self.root, "commit", "-q", "--allow-empty", "-m", msg)

    def task(self, n, title):
        self.commit(f"feat: task {n}", {f"development/tasks/TASK-{n:04d}-x.md": f'---\nid: TASK-{n:04d}\ntitle: "{title} (#1)"\n---\n'})
        self.commit(f"chore(registry): TASK-{n:04d} DONE")

    def merge_task(self, n):
        run(self.root, "checkout", "-q", "-b", f"b{n}")
        self.commit(f"feat: work {n}", {f"f{n}.txt": "x"})
        run(self.root, "checkout", "-q", "main")
        run(self.root, "merge", "-q", "--no-ff", f"b{n}", "-m", f"merge: TASK-{n:04d} work")


class TestPeriod(Repo):
    def test_done_tasks_since_last_release_tag_only(self):
        self.task(1, "Old")
        run(self.root, "tag", "-a", "v0.1", "-m", "r")
        self.task(2, "New")
        self.task(3, "Newer")
        self.assertEqual(release.done_tasks(self.root, "v0.1"), ["TASK-0002", "TASK-0003"])
        self.assertEqual(release.done_tasks(self.root, None), ["TASK-0001", "TASK-0002", "TASK-0003"])

    def test_release_tag_order_is_numeric(self):
        for t in ("v0.2", "v0.10", "v0.9", "rc-2026-01-01-1"):
            run(self.root, "tag", t)
        self.assertEqual(release.release_tags(self.root), ["v0.2", "v0.9", "v0.10"])
        self.assertEqual(release.next_release_tag(self.root), "v0.11")

    def test_open_debts_added_in_period(self):
        header = "| ID | a | b | c | d | Статус |\n"
        self.commit("debts", {"development/TECH_DEBT.md": header + "| DEBT-0001 | old | x | y | z | открыт |\n"})
        run(self.root, "tag", "v0.1")
        self.commit("more", {"development/TECH_DEBT.md": header + "| DEBT-0001 | old | x | y | z | открыт |\n"
                             "| DEBT-0002 | fresh | x | y | z | открыт |\n| DEBT-0003 | done | x | y | z | закрыт 2026 |\n"})
        self.assertEqual(release.open_debts(self.root, "v0.1"), ["DEBT-0002: fresh"])
        self.assertEqual(len(release.open_debts(self.root, None)), 2)


class TestRc(Repo):
    def test_rc_tags_and_report_without_rewriting_history(self):
        self.task(1, "Alpha")
        before = release.git(self.root, "rev-parse", "HEAD")
        tag = release.cmd_rc(self.root, day="2026-11-26")
        self.assertEqual(tag, "rc-2026-11-26-1")
        self.assertEqual(release.git(self.root, "rev-parse", "HEAD"), before)
        self.assertEqual(release.git(self.root, "rev-parse", f"{tag}^{{commit}}"), before)
        text = (self.root / "docs" / "releases" / f"{tag}.md").read_text(encoding="utf-8")
        self.assertIn("TASK-0001: Alpha", text)
        self.commit("more")
        self.assertEqual(release.cmd_rc(self.root, day="2026-11-26"), "rc-2026-11-26-2")

    def test_rc_refuses_other_branch_and_dirty_tree(self):
        run(self.root, "checkout", "-q", "-b", "feature")
        with self.assertRaises(release.ReleaseError):
            release.cmd_rc(self.root)
        run(self.root, "checkout", "-q", "main")
        (self.root / "development" / "TECH_DEBT.md").write_text("changed", encoding="utf-8")
        with self.assertRaises(release.ReleaseError):
            release.cmd_rc(self.root)


class TestApprove(Repo):
    def rc_with_checks(self, status):
        self.task(1, "Alpha")
        rc = release.cmd_rc(self.root, day="2026-11-26")
        release.write_checks(self.root / "docs" / "releases" / f"{rc}.md",
                             [("verify --full", status, "ok"), ("Shot batch", "PENDING", "cmd")])
        return rc

    def test_approve_tags_same_commit_and_writes_notes(self):
        rc = self.rc_with_checks("PASS")
        tag = release.cmd_approve(self.root, rc, "#600", build=False)
        self.assertEqual(tag, "v0.1")
        self.assertEqual(release.git(self.root, "rev-parse", "v0.1^{commit}"), release.git(self.root, "rev-parse", f"{rc}^{{commit}}"))
        notes = (self.root / "docs" / "releases" / "v0.1.md").read_text(encoding="utf-8")
        self.assertIn("Alpha", notes)
        self.assertIn("./run-client.sh --release v0.1", notes)

    def test_approve_refuses_failed_or_unchecked_rc_and_missing_owner_ref(self):
        rc = self.rc_with_checks("FAIL")
        with self.assertRaises(release.ReleaseError):
            release.cmd_approve(self.root, rc, "#600", build=False)
        rc2 = self.rc_with_checks("PASS")
        with self.assertRaises(release.ReleaseError):
            release.cmd_approve(self.root, rc2, "", build=False)
        self.assertEqual(release.release_tags(self.root), [])

    def test_approve_refuses_without_check_results(self):
        self.task(1, "Alpha")
        rc = release.cmd_rc(self.root, day="2026-11-26")
        with self.assertRaises(release.ReleaseError):
            release.cmd_approve(self.root, rc, "#600", build=False)


class TestRollbackAndErrors(Repo):
    def test_rollback_lists_revert_commands_and_records_error(self):
        self.merge_task(1)
        self.commit("chore(registry): TASK-0001 DONE")
        run(self.root, "tag", "-a", "v0.1", "-m", "r")
        self.merge_task(2)
        self.commit("chore(registry): TASK-0002 DONE")
        run(self.root, "tag", "-a", "v0.2", "-m", "r")
        head = release.git(self.root, "rev-parse", "HEAD")
        lines = release.cmd_rollback(self.root, "crash on start")
        text = "\n".join(lines)
        self.assertIn("--release v0.1", text)
        self.assertIn("git revert -m 1", text)
        self.assertIn("TASK-0002", text)
        self.assertNotIn("TASK-0001 work", text)
        self.assertEqual(release.git(self.root, "rev-parse", "HEAD"), head)  # nothing executed
        pending = (self.root / "docs" / "releases" / "errors-pending.jsonl").read_text(encoding="utf-8")
        self.assertIn("crash on start", pending)

    def test_rollback_needs_two_releases(self):
        run(self.root, "tag", "-a", "v0.1", "-m", "r")
        with self.assertRaises(release.ReleaseError):
            release.cmd_rollback(self.root, None)

    def test_reject_marks_report_and_records(self):
        self.task(1, "Alpha")
        rc = release.cmd_rc(self.root, day="2026-11-26")
        release.cmd_reject(self.root, rc, "determinism hash drift")
        self.assertIn("ОТКЛОНЁН", (self.root / "docs" / "releases" / f"{rc}.md").read_text(encoding="utf-8"))
        self.assertTrue((self.root / "docs" / "releases" / "errors-pending.jsonl").exists())


class TestPure(unittest.TestCase):
    def job(self, os_name, status="completed", conclusion="success"):
        return {"name": f"dotnet ({os_name})", "status": status, "conclusion": conclusion}

    def test_ci_verdict(self):
        ok = [self.job(o) for o in release.OS_JOBS]
        self.assertEqual(release.ci_os_verdict(ok)[0], "PASS")
        self.assertEqual(release.ci_os_verdict(ok[:2] + [self.job("macos-latest", conclusion="failure")])[0], "FAIL")
        self.assertEqual(release.ci_os_verdict(ok[:2])[0], "PENDING")
        self.assertEqual(release.ci_os_verdict(ok[:2] + [self.job("macos-latest", "in_progress", None)])[0], "PENDING")

    def test_checks_table_roundtrip(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "rc.md"
            p.write_text(release.render_rc("rc-x", "abc", None, [], []), encoding="utf-8")
            release.write_checks(p, [("verify --full", "PASS", "a|b"), ("CI three OS", "FAIL", "x")])
            release.write_checks(p, [("verify --full", "PASS", "again")])
            self.assertEqual(release.parse_checks(p), {"verify --full": "PASS"})

    def test_shot_command_only_prints(self):
        self.assertIn("shot_batch.py", release.shot_command("rc-x"))
        self.assertNotIn("run-globe-spike", release.shot_command("rc-x"))


if __name__ == "__main__":
    unittest.main()
