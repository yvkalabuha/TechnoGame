"""Tests of release.py on temporary git repositories (no push, no client, no network; the real errors.py is used)."""
import contextlib
import hashlib
import io
import json
import os
import shutil
import stat
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

import yaml

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import release  # noqa: E402


def run(root, *args):
    subprocess.run(["git", *args], cwd=root, check=True, capture_output=True, text=True)


def script(path: Path, body: str) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("#!/bin/sh\n" + body + "\n", encoding="utf-8")
    path.chmod(path.stat().st_mode | stat.S_IXUSR)
    return path


class Repo(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.base = Path(self.tmp.name)
        self.root = self.base / "repo"
        self.root.mkdir()
        run(self.root, "init", "-q", "-b", "main")
        for k, v in (("user.email", "t@example.com"), ("user.name", "t"), ("commit.gpgsign", "false"), ("tag.gpgsign", "false")):
            run(self.root, "config", k, v)
        (self.root / "development" / "tasks").mkdir(parents=True)
        (self.root / "development" / "TECH_DEBT.md").write_text("# D\n", encoding="utf-8")
        (self.root / "development" / "errors.yaml").write_text("errors: []\n", encoding="utf-8")
        (self.root / "tools" / "development").mkdir(parents=True)
        shutil.copy(HERE / "errors.py", self.root / "tools" / "development" / "errors.py")   # the real catalogue tool
        (self.root / "src" / "Singularity.Client.Spike").mkdir(parents=True)
        (self.root / "src" / "Singularity.Client.Spike" / "project.godot").write_text("x", encoding="utf-8")
        (self.root / "run-globe-spike.sh").write_text("#!/bin/sh\n", encoding="utf-8")
        geo = self.root / "mods" / "core" / "map" / "geo"   # generated data: arcs.bin is git-ignored, its manifest is tracked
        geo.mkdir(parents=True)
        (geo / "arcs.bin").write_bytes(b"arcs-data")
        (geo / "manifest.json").write_text(json.dumps({"files": {"arcs.bin": {"sha256": hashlib.sha256(b"arcs-data").hexdigest()}}}), encoding="utf-8")
        (self.root / ".gitignore").write_text("mods/core/map/geo/arcs.bin\n", encoding="utf-8")
        self.commit("init")
        os.environ["SINGULARITY_RELEASES"] = str(self.base / "rel")
        self.godot_ok = script(self.base / "godot-ok", 'case "$*" in *--build-solutions*|*--import*) exit 0;; esac\n'
                               'echo "$*" >> "$(dirname "$0")/smoke.log"\n[ -n "$SMOKE_TEXT" ] && echo "$SMOKE_TEXT"\nexit ${SMOKE_EXIT:-0}')
        self.godot_bad = script(self.base / "godot-bad", "echo boom; exit 1")
        os.environ["GODOT"] = str(self.godot_ok)

    def tearDown(self):
        os.environ.pop("SINGULARITY_RELEASES", None)
        os.environ.pop("GODOT", None)
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

    def catalogue(self):
        return yaml.safe_load((self.root / "development" / "errors.yaml").read_text(encoding="utf-8"))["errors"]

    def report(self, rc):
        return self.root / "docs" / "releases" / f"{rc}.md"

    def checked_rc(self, **override):
        """An RC whose report holds every required row PASS on the RC commit (override: row name -> status)."""
        self.task(len(list(self.root.glob("development/tasks/*.md"))) + 1, "Alpha")
        rc = release.cmd_rc(self.root, day="2026-11-26")
        rows = [(n, override.get(n, "PASS"), "ok") for n in release.REQUIRED_CHECKS]
        sha = release.git(self.root, "rev-parse", f"{rc}^{{commit}}")
        release.write_checks(self.report(rc), [r for r in rows if r[1] != "missing"] + [("Shot batch", "PENDING", "cmd")], sha)
        return rc


class TestPeriod(Repo):
    def test_done_tasks_since_last_release_tag_only(self):
        self.task(1, "Old")
        run(self.root, "tag", "-a", "v0.1", "-m", "r")
        self.task(2, "New")
        self.task(3, "Newer")
        self.assertEqual(release.done_tasks(self.root, "v0.1"), ["TASK-0002", "TASK-0003"])
        self.assertEqual(release.done_tasks(self.root, None), ["TASK-0001", "TASK-0002", "TASK-0003"])

    def test_done_by_status_change_without_registry_commit(self):
        self.commit("t", {"development/tasks/TASK-0009-x.md": "---\nid: TASK-0009\nstatus: REVIEW\n---\n"})
        run(self.root, "tag", "-a", "v0.1", "-m", "r")
        self.commit("flip", {"development/tasks/TASK-0009-x.md": "---\nid: TASK-0009\nstatus: DONE\n---\n"})
        self.assertEqual(release.done_tasks(self.root, "v0.1"), ["TASK-0009"])

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
        self.assertIn("TASK-0001: Alpha", self.report(tag).read_text(encoding="utf-8"))
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

    def test_period_metrics_skipped_with_note_when_tool_absent_and_used_when_present(self):
        rc = release.cmd_rc(self.root, day="2026-11-26")
        self.assertIn("TASK-0206 не влита", self.report(rc).read_text(encoding="utf-8"))
        (self.root / "tools" / "development" / "metrics.py").write_text("print('agent_runs 42')\n", encoding="utf-8")
        self.commit("metrics")
        rc2 = release.cmd_rc(self.root, day="2026-11-26")
        self.assertIn("agent_runs 42", self.report(rc2).read_text(encoding="utf-8"))
        (self.root / "tools" / "development" / "metrics.py").write_text("import sys; sys.exit(3)\n", encoding="utf-8")
        self.commit("metrics broken")
        rc3 = release.cmd_rc(self.root, day="2026-11-26")
        self.assertIn("не выполнен", self.report(rc3).read_text(encoding="utf-8"))

    def test_main_prints_commit_reminder(self):
        self.task(1, "Alpha")
        old = release.ROOT
        release.ROOT = self.root
        try:
            buf = io.StringIO()
            with contextlib.redirect_stdout(buf):
                self.assertEqual(release.main(["rc"]), 0)
        finally:
            release.ROOT = old
        self.assertIn("commit docs/releases/rc-", buf.getvalue())
        self.assertIn("development/errors.yaml", buf.getvalue())


class TestApprove(Repo):
    def test_approve_builds_before_tag_then_tags_same_commit_and_writes_notes(self):
        rc = self.checked_rc()
        tag = release.cmd_approve(self.root, rc, "#600")
        self.assertEqual(tag, "v0.1")
        sha = release.git(self.root, "rev-parse", f"{rc}^{{commit}}")
        self.assertEqual(release.git(self.root, "rev-parse", "v0.1^{commit}"), sha)
        built = self.base / "rel" / "v0.1"
        self.assertEqual((built / "RELEASE").read_text(encoding="utf-8").strip(), f"v0.1 {sha}")
        self.assertTrue((built / "src" / "run-globe-spike.sh").exists())
        notes = (self.root / "docs" / "releases" / "v0.1.md").read_text(encoding="utf-8")
        self.assertIn("release.py run v0.1", notes)
        self.assertIn("ОДОБРЕН", self.report(rc).read_text(encoding="utf-8"))

    def test_failed_build_leaves_no_tag_notes_status_or_directory(self):
        rc = self.checked_rc()
        os.environ["GODOT"] = str(self.godot_bad)
        with self.assertRaises(release.ReleaseError):
            release.cmd_approve(self.root, rc, "#600")
        self.assertEqual(release.release_tags(self.root), [])
        self.assertFalse((self.root / "docs" / "releases" / "v0.1.md").exists())
        self.assertIn("НА ПРОВЕРКЕ", self.report(rc).read_text(encoding="utf-8"))
        self.assertEqual(os.listdir(self.base / "rel"), [])   # no release directory, no staging leftovers
        errs = self.catalogue()
        self.assertEqual(len(errs), 1)
        self.assertIn("ERR-0001", self.report(rc).read_text(encoding="utf-8"))

    def assert_no_release(self, rc):
        self.assertEqual(release.release_tags(self.root), [])
        self.assertFalse((self.root / "docs" / "releases" / "v0.1.md").exists())
        self.assertIn("НА ПРОВЕРКЕ", self.report(rc).read_text(encoding="utf-8"))
        self.assertEqual(os.listdir(self.base / "rel"), [])

    def test_deployed_snapshot_holds_the_generated_map_data(self):
        rc = self.checked_rc()
        release.cmd_approve(self.root, rc, "#600")
        arcs = self.base / "rel" / "v0.1" / "src" / "mods" / "core" / "map" / "geo" / "arcs.bin"
        self.assertEqual(arcs.read_bytes(), b"arcs-data")

    def test_missing_or_stale_map_data_blocks_the_release(self):
        rc = self.checked_rc()
        arcs = self.root / "mods" / "core" / "map" / "geo" / "arcs.bin"
        arcs.write_bytes(b"stale")
        with self.assertRaisesRegex(release.ReleaseError, "does not match the manifest"):
            release.cmd_approve(self.root, rc, "#600")
        self.assert_no_release(rc)
        arcs.unlink()
        with self.assertRaisesRegex(release.ReleaseError, "ensure_geo"):
            release.cmd_approve(self.root, rc, "#600")
        self.assert_no_release(rc)

    def test_smoke_start_runs_headless_and_its_failure_means_no_tag(self):
        rc = self.checked_rc()
        os.environ["SMOKE_EXIT"] = "7"
        self.addCleanup(os.environ.pop, "SMOKE_EXIT", None)
        with self.assertRaisesRegex(release.ReleaseError, "smoke start: exit 7"):
            release.cmd_approve(self.root, rc, "#600")
        self.assert_no_release(rc)
        self.assertIn("--headless", (self.base / "smoke.log").read_text(encoding="utf-8"))
        self.assertIn("--quit", (self.base / "smoke.log").read_text(encoding="utf-8"))
        os.environ["SMOKE_EXIT"] = "0"
        os.environ["SMOKE_TEXT"] = "SCRIPT ERROR: Parse Error"
        self.addCleanup(os.environ.pop, "SMOKE_TEXT", None)
        with self.assertRaisesRegex(release.ReleaseError, "smoke start"):
            release.cmd_approve(self.root, rc, "#600")
        self.assert_no_release(rc)
        os.environ.pop("SMOKE_TEXT")
        self.assertEqual(release.cmd_approve(self.root, rc, "#600"), "v0.1")

    def test_missing_godot_is_a_failed_deploy_not_a_tag(self):
        rc = self.checked_rc()
        os.environ["GODOT"] = str(self.base / "no-such-godot")
        with self.assertRaises(release.ReleaseError):
            release.cmd_approve(self.root, rc, "#600")
        self.assertEqual(release.release_tags(self.root), [])

    def test_every_required_row_must_be_pass(self):
        for name in release.REQUIRED_CHECKS:
            for status in ("SKIP", "WARN", "FAIL", "PENDING", "missing"):
                rc = self.checked_rc(**{name: status})
                with self.assertRaises(release.ReleaseError, msg=f"{name} {status}"):
                    release.cmd_approve(self.root, rc, "#600")
        self.assertEqual(release.release_tags(self.root), [])

    def test_pace_warn_blocks_approve(self):
        rc = self.checked_rc(**{"Lab pace_check": "WARN"})
        with self.assertRaisesRegex(release.ReleaseError, "pace_check: WARN"):
            release.cmd_approve(self.root, rc, "#600")

    def test_refuses_checks_made_on_another_commit(self):
        rc = self.checked_rc()
        release.write_checks(self.report(rc), [(n, "PASS", "ok") for n in release.REQUIRED_CHECKS], "0" * 40)
        with self.assertRaisesRegex(release.ReleaseError, "checks were made on"):
            release.cmd_approve(self.root, rc, "#600")

    def test_approve_only_once_and_not_after_reject(self):
        rc = self.checked_rc()
        release.cmd_approve(self.root, rc, "#600")
        with self.assertRaises(release.ReleaseError):
            release.cmd_approve(self.root, rc, "#601")
        rc2 = self.checked_rc()
        release.cmd_reject(self.root, rc2, "bad")
        with self.assertRaises(release.ReleaseError):
            release.cmd_approve(self.root, rc2, "#602")

    def test_refuses_an_rc_older_than_the_latest_release(self):
        old_rc = self.checked_rc()
        self.commit("later work")
        run(self.root, "tag", "-a", "v0.1", "-m", "r")
        with self.assertRaisesRegex(release.ReleaseError, "not newer than v0.1"):
            release.cmd_approve(self.root, old_rc, "#600")
        self.assertEqual(release.release_tags(self.root), ["v0.1"])
        new_rc = self.checked_rc()
        self.assertEqual(release.cmd_approve(self.root, new_rc, "#601"), "v0.2")

    @unittest.skipIf(hasattr(os, "geteuid") and os.geteuid() == 0, "permissions are not enforced for root")
    def test_report_write_failure_changes_nothing_and_approve_can_be_retried(self):
        rc = self.checked_rc()
        docs = self.root / "docs" / "releases"
        before = self.report(rc).read_text(encoding="utf-8")
        docs.chmod(0o555)
        try:
            with self.assertRaisesRegex(release.ReleaseError, "nothing changed"):
                release.cmd_approve(self.root, rc, "#600")
        finally:
            docs.chmod(0o755)
        self.assertEqual(release.release_tags(self.root), [])
        self.assertEqual(self.report(rc).read_text(encoding="utf-8"), before)
        self.assertFalse((self.base / "rel" / "v0.1").exists())
        self.assertEqual([p.name for p in docs.glob("*.tmp")], [])
        self.assertEqual(release.cmd_approve(self.root, rc, "#600"), "v0.1")

    def test_needs_owner_ref_and_check_results(self):
        rc = self.checked_rc()
        with self.assertRaises(release.ReleaseError):
            release.cmd_approve(self.root, rc, "")
        self.task(50, "x")
        bare = release.cmd_rc(self.root, day="2026-11-26")
        with self.assertRaises(release.ReleaseError):
            release.cmd_approve(self.root, bare, "#600")


class TestDeploySafety(Repo):
    def test_release_dir_accepts_only_versions(self):
        for bad in ("../x", "v1", "v0.1/../..", "/etc", "v0.1.2", ".", "", "v0.1\n../x"):
            with self.assertRaises(release.ReleaseError, msg=bad):
                release.release_dir(bad)
        self.assertEqual(release.release_dir("v0.7").name, "v0.7")

    def test_existing_directory_is_never_deleted(self):
        run(self.root, "tag", "-a", "v0.1", "-m", "r")
        keep = self.base / "rel" / "v0.1" / "mine.txt"
        keep.parent.mkdir(parents=True)
        keep.write_text("precious", encoding="utf-8")
        with self.assertRaises(release.ReleaseError):
            release.cmd_release(self.root, "v0.1")
        self.assertTrue(keep.exists())

    def test_symlink_destination_is_refused_and_target_untouched(self):
        run(self.root, "tag", "-a", "v0.1", "-m", "r")
        outside = self.base / "outside"
        outside.mkdir()
        (outside / "mine.txt").write_text("precious", encoding="utf-8")
        (self.base / "rel").mkdir()
        os.symlink(outside, self.base / "rel" / "v0.1")
        with self.assertRaises(release.ReleaseError):
            release.cmd_release(self.root, "v0.1")
        self.assertTrue((outside / "mine.txt").exists())

    def test_release_needs_an_existing_tag_and_builds_it(self):
        with self.assertRaises(release.ReleaseError):
            release.cmd_release(self.root, "v0.9")
        run(self.root, "tag", "-a", "v0.9", "-m", "r")
        release.cmd_release(self.root, "v0.9")
        self.assertTrue((self.base / "rel" / "v0.9" / "RELEASE").exists())

    def test_run_command_of_an_exported_client_uses_its_packaged_data(self):
        dest = Path(os.path.realpath(self.base / "rel")) / "v0.3"
        (dest / "bin").mkdir(parents=True)
        (dest / "bin" / "singularity").write_text("x", encoding="utf-8")
        self.assertEqual(release.cmd_run("v0.3", ["--lang", "en"]), [str(dest / "bin" / "singularity"), "--lang", "en"])
        (dest / "bin" / "singularity.pck").write_text("x", encoding="utf-8")
        self.assertEqual(release.cmd_run("v0.3", []), [str(dest / "bin" / "singularity"), "--main-pack", str(dest / "bin" / "singularity.pck")])

    def test_run_prints_launcher_of_the_built_snapshot_and_validates(self):
        run(self.root, "tag", "-a", "v0.1", "-m", "r")
        with self.assertRaises(release.ReleaseError):
            release.cmd_run("v0.1", [])   # not built yet
        release.cmd_release(self.root, "v0.1")
        cmd = release.cmd_run("v0.1", ["--lang", "en"])
        self.assertEqual(cmd, [str(Path(os.path.realpath(self.base / "rel")) / "v0.1" / "src" / "run-globe-spike.sh"), "--lang", "en"])
        with self.assertRaises(release.ReleaseError):
            release.cmd_run("../v0.1", [])


class TestExecPermit(Repo):
    def test_permit_live_reads_the_guard_file(self):
        old = release.client_permit.PERMIT
        release.client_permit.PERMIT = self.base / "permit"
        self.addCleanup(setattr, release.client_permit, "PERMIT", old)
        self.assertFalse(release.permit_live())
        release.client_permit.PERMIT.write_text(str(release.time.time() - 5), encoding="utf-8")
        self.assertFalse(release.permit_live())
        release.client_permit.PERMIT.write_text("junk", encoding="utf-8")
        self.assertFalse(release.permit_live())
        release.client_permit.PERMIT.write_text(str(release.time.time() + 60), encoding="utf-8")
        self.assertTrue(release.permit_live())

    def test_run_exec_without_permit_is_refused_before_exec(self):
        run(self.root, "tag", "-a", "v0.1", "-m", "r")
        release.cmd_release(self.root, "v0.1")
        old, oldx, oldroot = release.client_permit.PERMIT, release.os.execv, release.ROOT
        release.client_permit.PERMIT = self.base / "no-permit"
        calls = []
        release.os.execv = lambda *a: calls.append(a)
        try:
            err = io.StringIO()
            with contextlib.redirect_stderr(err):
                self.assertEqual(release.main(["run", "v0.1", "--exec"]), 1)
            self.assertEqual(calls, [])
            self.assertIn("permit", err.getvalue())
            release.client_permit.PERMIT.write_text(str(release.time.time() + 60), encoding="utf-8")
            release.main(["run", "v0.1", "--exec"])
            self.assertEqual(len(calls), 1)
        finally:
            release.client_permit.PERMIT, release.os.execv, release.ROOT = old, oldx, oldroot


class TestRollbackAndErrors(Repo):
    def test_rollback_lists_revert_commands_and_records_error(self):
        self.merge_task(1)
        self.commit("chore(registry): TASK-0001 DONE")
        run(self.root, "tag", "-a", "v0.1", "-m", "r")
        self.merge_task(2)
        self.commit("chore(registry): TASK-0002 DONE")
        run(self.root, "tag", "-a", "v0.2", "-m", "r")
        head = release.git(self.root, "rev-parse", "HEAD")
        text = "\n".join(release.cmd_rollback(self.root, "crash on start"))
        self.assertIn("release.py run v0.1 --exec", text)
        self.assertIn("git revert -m 1", text)
        self.assertIn("TASK-0002", text)
        self.assertNotIn("TASK-0001 work", text)
        self.assertEqual(release.git(self.root, "rev-parse", "HEAD"), head)  # nothing executed
        e = self.catalogue()[0]
        self.assertIn("crash on start", e["symptom"])
        self.assertEqual(e["cause"], "v0.2")

    def test_rollback_needs_two_releases(self):
        run(self.root, "tag", "-a", "v0.1", "-m", "r")
        with self.assertRaises(release.ReleaseError):
            release.cmd_rollback(self.root, None)

    def test_reject_records_reason_in_symptom_rc_in_cause_and_defect_in_report(self):
        rc = self.checked_rc()
        out = release.cmd_reject(self.root, rc, "determinism hash drift")
        self.assertIn("ERR-0001", out)
        e = self.catalogue()[0]
        self.assertIn("determinism hash drift", e["symptom"])
        self.assertEqual(e["cause"], rc)
        text = self.report(rc).read_text(encoding="utf-8")
        self.assertIn("ОТКЛОНЁН", text)
        self.assertIn("ERR-0001", text)

    def test_two_reasons_two_entries_same_reason_is_a_repeat(self):
        rc = self.checked_rc()
        run(self.root, "checkout", "-q", "--", "development/errors.yaml")
        self.assertIn("ERR-0001 added", release.record_error(self.root, "process", "release", "RC rejected: a", rc))
        self.assertIn("ERR-0002 added", release.record_error(self.root, "process", "release", "RC rejected: b", rc))
        self.assertIn("ERR-0001 repeat recorded", release.record_error(self.root, "process", "release", "RC rejected: a", "rc-other"))
        self.assertEqual(len(self.catalogue()), 2)

    def test_failing_or_missing_errors_tool_is_an_error(self):
        tool = self.root / "tools" / "development" / "errors.py"
        tool.write_text("import sys; sys.exit(3)\n", encoding="utf-8")
        with self.assertRaises(release.ReleaseError):
            release.record_error(self.root, "process", "release", "s", "c")
        tool.unlink()
        with self.assertRaises(release.ReleaseError):
            release.record_error(self.root, "process", "release", "s", "c")


class TestLabChecks(Repo):
    SHA = "a" * 40

    def tree(self, slot_body):
        t = self.base / "tree"
        (t / "lab" / "manifests").mkdir(parents=True, exist_ok=True)
        core = {"series": [{"name": "SLOTS"}, {"name": "SHARES"}], "threads": [1], "seeds": {"start": 1, "count": 10}}
        (t / release.LAB_CORE).write_text(json.dumps(core), encoding="utf-8")
        (t / release.LAB_PLAYER).write_text("{}", encoding="utf-8")
        (t / "lab" / "analysis").mkdir(parents=True, exist_ok=True)
        (t / "lab" / "analysis" / "slot_core.py").write_text(slot_body, encoding="utf-8")
        return t

    def runner(self, commit, dirty=False, ok=True):
        def run_matrix(tree, manifest, out):
            (out / "runs" / "r1").mkdir(parents=True)
            (out / "runs" / "r1" / "run.json").write_text(json.dumps({"core_commit": commit, "dirty": dirty}), encoding="utf-8")
            return ok, "done"
        return run_matrix

    GOOD = ("for g in '" + " ".join(release.REQUIRED_GATES) + "'.split(): print('  PASS %s USA ok' % g)\n"
            "print('  PASS DET threads ok')\n")

    def test_good_series_pass_and_record_the_commit(self):
        rows = release.lab_checks(self.tree(self.GOOD), self.base / "w", self.SHA, self.runner(self.SHA))
        self.assertEqual([r[1] for r in rows], ["PASS", "PASS"])
        self.assertIn(self.SHA[:12], rows[0][2])

    def test_results_of_another_commit_or_dirty_tree_are_refused(self):
        rows = release.lab_checks(self.tree(self.GOOD), self.base / "w", self.SHA, self.runner("b" * 40))
        self.assertEqual([r[1] for r in rows], ["FAIL", "FAIL"])
        self.assertIn("is not the RC commit", rows[0][2])
        rows = release.lab_checks(self.tree(self.GOOD), self.base / "w2", self.SHA, self.runner(self.SHA, dirty=True))
        self.assertIn("dirty", rows[0][2])

    def test_failed_matrix_run_fails_both_rows(self):
        rows = release.lab_checks(self.tree(self.GOOD), self.base / "w", self.SHA, self.runner(self.SHA, ok=False))
        self.assertEqual([r[1] for r in rows], ["FAIL", "FAIL"])

    def test_gate_fail_and_missing_det_are_told_apart(self):
        rows = release.lab_checks(self.tree(self.GOOD + "print('  FAIL GAP36 USA 40')\n"), self.base / "w", self.SHA, self.runner(self.SHA))
        self.assertEqual([r[1] for r in rows], ["FAIL", "PASS"])
        rows = release.lab_checks(self.tree(self.GOOD.replace("print('  PASS DET threads ok')", "pass")), self.base / "w2", self.SHA, self.runner(self.SHA))
        self.assertEqual([r[1] for r in rows], ["PASS", "FAIL"])

    def test_gates_that_did_not_run_fail_the_gate_row(self):
        rows = release.lab_checks(self.tree("print('  PASS GAP36 USA ok')\nprint('  PASS DET threads ok')\n"), self.base / "w", self.SHA, self.runner(self.SHA))
        self.assertEqual([r[1] for r in rows], ["FAIL", "PASS"])
        self.assertIn("gates not run: LIVE0", rows[0][2])
        rows = release.lab_checks(self.tree("print('nothing')\n"), self.base / "w2", self.SHA, self.runner(self.SHA))
        self.assertEqual([r[1] for r in rows], ["FAIL", "FAIL"])
        rows = release.lab_checks(self.tree(self.GOOD + "print('== d: 3 runs; FAIL RUNS (не ok: [1])')\n"), self.base / "w3", self.SHA, self.runner(self.SHA))
        self.assertEqual(rows[0][1], "FAIL")

    def test_l2_manifest_is_generated_with_two_thread_counts(self):
        t = self.tree(self.GOOD)
        release.lab_checks(t, self.base / "w", self.SHA, self.runner(self.SHA))
        l2 = json.loads((t / release.LAB_L2).read_text(encoding="utf-8"))
        self.assertEqual((l2["threads"], l2["seeds"]["count"], len(l2["series"])), ([1, 2], 2, 1))


class TestPure(Repo):
    def job(self, os_name, status="completed", conclusion="success"):
        return {"name": f"dotnet ({os_name})", "status": status, "conclusion": conclusion}

    def test_ci_verdict(self):
        ok = [self.job(o) for o in release.OS_JOBS]
        self.assertEqual(release.ci_os_verdict(ok)[0], "PASS")
        self.assertEqual(release.ci_os_verdict(ok[:2] + [self.job("macos-latest", conclusion="failure")])[0], "FAIL")
        self.assertEqual(release.ci_os_verdict(ok[:2])[0], "FAIL")   # a job missing from a finished run
        self.assertEqual(release.ci_os_verdict(ok[:2], run_completed=False)[0], "PENDING")
        for bad in ("cancelled", "skipped", "neutral", "timed_out", None):
            self.assertEqual(release.ci_os_verdict(ok[:2] + [self.job("macos-latest", conclusion=bad)])[0], "FAIL", bad)
        self.assertEqual(release.ci_os_verdict(ok[:2] + [self.job("macos-latest", "in_progress", None)])[0], "PENDING")

    def test_pace_exit_codes_map_to_pass_warn_fail_and_no_base_is_skip(self):
        t = self.base / "ptree"
        for code, want in ((0, "PASS"), (3, "WARN"), (1, "FAIL")):
            (t / "lab" / "analysis").mkdir(parents=True, exist_ok=True)
            (t / "lab" / "analysis" / "pace_check.py").write_text(f"import sys; print(sys.argv[1:]); sys.exit({code})\n", encoding="utf-8")
            row = release.pace_row(t, "abc")
            self.assertEqual(row[1], want)
            self.assertIn("--strict", row[2])
        self.assertEqual(release.pace_row(t, None)[1], "SKIP")

    def test_first_release_baseline_is_the_rc_itself(self):
        self.task(1, "a")
        rc = release.cmd_rc(self.root, day="2026-11-26")
        sha = release.git(self.root, "rev-parse", f"{rc}^{{commit}}")
        self.assertTrue(release.pace_baseline_is_rc(self.root, "self", sha))
        self.assertTrue(release.pace_baseline_is_rc(self.root, rc, sha))
        self.assertTrue(release.pace_baseline_is_rc(self.root, sha[:10], sha))
        self.assertFalse(release.pace_baseline_is_rc(self.root, "HEAD~1", sha))
        run(self.root, "tag", "-a", "v0.1", "HEAD~1", "-m", "r")   # after the first release the RC cannot be its own baseline
        for base in ("self", rc, sha[:10]):
            with self.assertRaisesRegex(release.ReleaseError, "only for the first release"):
                release.pace_baseline_is_rc(self.root, base, sha)
        self.assertFalse(release.pace_baseline_is_rc(self.root, "v0.1", sha))
        self.assertFalse(release.pace_baseline_is_rc(self.root, None, sha))
        empty = self.base / "no-pace-script"
        empty.mkdir()
        row = release.pace_row(empty, "self", is_rc=True)   # pace_check is not run (the script does not even exist here)
        self.assertEqual(row[1], "PASS")
        self.assertIn("RC itself", row[2])

    def test_checks_table_roundtrip_with_commit(self):
        p = self.base / "rc.md"
        p.write_text(release.render_rc("rc-x", "abc", None, [], []), encoding="utf-8")
        release.write_checks(p, [("verify --full", "PASS", "a|b"), ("CI three OS", "FAIL", "x")], "f" * 40)
        release.write_checks(p, [("verify --full", "PASS", "again")], "e" * 40)
        self.assertEqual(release.parse_checks(p), {"verify --full": "PASS"})
        self.assertEqual(release.checked_commit(p), "e" * 40)

    def test_defects_block_accumulates(self):
        p = self.base / "rc.md"
        p.write_text(release.render_rc("rc-x", "abc", None, [], []), encoding="utf-8")
        release.add_defects(p, ["- ERR-0001: a"])
        release.add_defects(p, ["- ERR-0002: b"])
        text = p.read_text(encoding="utf-8")
        self.assertIn("ERR-0001", text)
        self.assertIn("ERR-0002", text)
        self.assertNotIn("- нет", text)

    def test_shot_command_prints_rc_worktree_and_commit_only(self):
        cmd = release.shot_command("rc-2026-11-26-1", "abcdef" * 7)
        self.assertIn("git worktree add --detach ../shots-rc-2026-11-26-1 rc-2026-11-26-1", cmd)
        self.assertIn("abcdefabcdef", cmd)
        self.assertIn("client_permit.py grant", cmd)


if __name__ == "__main__":
    unittest.main()
