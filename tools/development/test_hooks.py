#!/usr/bin/env python3
"""Self-tests for Claude Code hooks and registry.py (stdlib unittest).

Run: python3 tools/development/test_hooks.py
"""
import fcntl
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import textwrap
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HOOKS = ROOT / ".claude" / "hooks"
REGISTRY = ROOT / "tools" / "development" / "registry.py"


def run_hook(script: str, payload: dict, *args: str, project: Path = ROOT) -> int:
    env = dict(os.environ, CLAUDE_PROJECT_DIR=str(project), SINGULARITY_NO_REFUSAL_LOG="1")
    proc = subprocess.run([sys.executable, str(HOOKS / script), *args],
                          input=json.dumps(payload), text=True,
                          capture_output=True, env=env)
    return proc.returncode


class GuardGlobalTest(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp, True)
        adr = self.tmp / "docs" / "decisions"
        adr.mkdir(parents=True)
        (adr / "ADR-0001-x.md").write_text(
            "---\nid: ADR-0001\nstatus: ACCEPTED\nsuperseded_by:\n---\n\n# body\n", encoding="utf-8")
        (adr / "ADR-0002-y.md").write_text(
            "---\nid: ADR-0002\nstatus: PROPOSED\n---\n\n# body\n", encoding="utf-8")

    def call(self, tool, name, **extra):
        payload = {"tool_name": tool,
                   "tool_input": {"file_path": str(self.tmp / name), **extra}}
        return run_hook("guard_global.py", payload, project=self.tmp)

    def test_secret_blocked(self):
        self.assertEqual(self.call("Write", ".env"), 2)
        self.assertEqual(self.call("Write", "config/api.key"), 2)

    def test_accepted_adr_write_blocked(self):
        self.assertEqual(self.call("Write", "docs/decisions/ADR-0001-x.md"), 2)

    def test_accepted_adr_status_edit_allowed(self):
        rc = self.call("Edit", "docs/decisions/ADR-0001-x.md",
                       old_string="status: ACCEPTED", new_string="status: SUPERSEDED")
        self.assertEqual(rc, 0)

    def test_accepted_adr_body_edit_blocked(self):
        rc = self.call("Edit", "docs/decisions/ADR-0001-x.md",
                       old_string="# body", new_string="# changed")
        self.assertEqual(rc, 2)

    def test_proposed_adr_free(self):
        self.assertEqual(self.call("Write", "docs/decisions/ADR-0002-y.md"), 0)


class GuardZoneTest(unittest.TestCase):
    ARGS = ["--agent", "developer", "--allow", "src/**", "--allow", "tests/**",
            "--deny", "src/Singularity.Lab/**"]

    def call(self, path):
        return run_hook("guard_zone.py", {"tool_input": {"file_path": path}}, *self.ARGS)

    def test_inside_zone(self):
        self.assertEqual(self.call(str(ROOT / "src/Singularity.Core/A.cs")), 0)
        self.assertEqual(self.call(str(ROOT / "tests/X/B.cs")), 0)

    def test_denied_subfolder(self):
        self.assertEqual(self.call(str(ROOT / "src/Singularity.Lab/A.cs")), 2)

    def test_outside_zone_and_project(self):
        self.assertEqual(self.call(str(ROOT / "docs/a.md")), 2)
        self.assertEqual(self.call("/etc/passwd"), 2)


class GuardBashTest(unittest.TestCase):
    def call(self, cmd):
        return run_hook("guard_bash.py", {"tool_input": {"command": cmd}},
                        "--agent", "reviewer", "--allow", "git diff", "--allow", "dotnet test")

    def test_allowed(self):
        self.assertEqual(self.call("git diff HEAD~1"), 0)
        self.assertEqual(self.call("dotnet test"), 0)

    def test_blocked(self):
        for cmd in ["git diff; rm -rf x", "git diff | sh", "git differ", "rm x",
                    "git diff $(whoami)", "dotnet test > out"]:
            self.assertEqual(self.call(cmd), 2, cmd)


class RegistryTest(unittest.TestCase):
    """Runs registry.py against a temporary copy of the needed layout."""

    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp, True)
        (self.tmp / "tools" / "development").mkdir(parents=True)
        (self.tmp / "tools" / "development" / "registry.py").write_text(
            REGISTRY.read_text(encoding="utf-8"), encoding="utf-8")
        (self.tmp / "development" / "tasks").mkdir(parents=True)
        (self.tmp / "development" / "milestones").mkdir(parents=True)
        (self.tmp / "development" / "milestones" / "MS-M0-a.md").write_text(
            "---\nid: MS-M0\ntitle: M0\nstatus: PLANNED\n---\n", encoding="utf-8")

    def task(self, body: str):
        (self.tmp / "development" / "tasks" / "TASK-0001-a.md").write_text(
            textwrap.dedent(body), encoding="utf-8")

    def check(self) -> int:
        return subprocess.run([sys.executable, str(self.tmp / "tools/development/registry.py"), "check"],
                              capture_output=True, text=True).returncode

    def test_valid_task(self):
        self.task("""\
            ---
            id: TASK-0001
            title: t
            status: PROPOSED
            level: light
            milestone: MS-M0
            agent: developer
            ---
            """)
        self.assertEqual(self.check(), 0)

    def test_approved_without_approval_fields(self):
        self.task("""\
            ---
            id: TASK-0001
            title: t
            status: APPROVED
            level: light
            milestone: MS-M0
            agent: developer
            ---
            """)
        self.assertEqual(self.check(), 1)

    def test_orchestrator_cannot_approve_gate_task(self):
        self.task("""\
            ---
            id: TASK-0001
            title: t
            status: APPROVED
            level: gate
            milestone: MS-M0
            agent: developer
            approved_by: orchestrator
            approved_at: 2026-11-02
            approval_ref: x
            ---
            """)
        self.assertEqual(self.check(), 1)

    def test_unknown_link(self):
        self.task("""\
            ---
            id: TASK-0001
            title: t
            status: PROPOSED
            level: light
            milestone: MS-M9
            agent: developer
            ---
            """)
        self.assertEqual(self.check(), 1)



class RefusalLogTest(unittest.TestCase):
    """#584: a blocking hook records its refusal for errors.py import."""

    def test_block_appends_refusal_line(self):
        tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, tmp, True)
        code = "import sys; sys.path.insert(0, %r); import _common; _common.block('no shots without permit')" % str(HOOKS)
        env = dict(os.environ, CLAUDE_PROJECT_DIR=str(tmp))
        env.pop("SINGULARITY_NO_REFUSAL_LOG", None)
        proc = subprocess.run([sys.executable, "-c", code], env=env, capture_output=True, text=True)
        self.assertEqual(proc.returncode, 2)
        lines = (tmp / ".claude" / ".state" / "hook_refusals.jsonl").read_text(encoding="utf-8").splitlines()
        self.assertEqual(len(lines), 1)
        self.assertIn("no shots without permit", json.loads(lines[0])["msg"])

    def test_env_suppresses_log(self):
        tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, tmp, True)
        code = "import sys; sys.path.insert(0, %r); import _common; _common.block('x')" % str(HOOKS)
        env = dict(os.environ, CLAUDE_PROJECT_DIR=str(tmp), SINGULARITY_NO_REFUSAL_LOG="1")
        subprocess.run([sys.executable, "-c", code], env=env, capture_output=True, text=True)
        self.assertFalse((tmp / ".claude" / ".state" / "hook_refusals.jsonl").exists())


sys.path.insert(0, str(ROOT / "tools" / "development"))
import shot_batch  # noqa: E402
import watch  # noqa: E402


class WatchActionsTest(unittest.TestCase):
    NOW = 100 * 3600.0

    def acts(self, events, status=None, unlanded=None):
        return {a["agent_id"]: a["action"] for a in watch.agent_actions(
            events, self.NOW, 2.0, 3.0, status or {}, unlanded or {})}

    def started(self, key, hours, tasks=("TASK-0001",)):
        return {"event": "launched", "agent_id": key, "t": self.NOW - hours * 3600, "tasks": list(tasks)}

    def test_silent_thresholds(self):
        ev = [self.started("a", 1), self.started("b", 2.5), self.started("c", 3.5)]
        self.assertEqual(self.acts(ev), {"b": "ping", "c": "stop+redispatch"})

    def test_stopped_agent_not_silent(self):
        ev = [self.started("a", 5), {"event": "stop", "agent_id": "a", "t": self.NOW}]
        self.assertEqual(self.acts(ev), {})

    def test_replacement_agent_after_stop_suppresses_to_review(self):
        ev = [self.started("a", 3), {"event": "stop", "agent_id": "a", "t": self.NOW - 2 * 3600},
              dict(self.started("b", 1), branch="worktree-agent-a")]
        self.assertEqual(self.acts(ev, {"TASK-0001": "IN_PROGRESS"}, {"TASK-0001": [{"name": "worktree-agent-a"}]}), {})

    def test_timed_out_replacement_redispatched_on_its_own_branch(self):
        ev = [self.started("a", 6), {"event": "stop", "agent_id": "a", "t": self.NOW - 5 * 3600},
              dict(self.started("b", 4), branch="worktree-agent-a")]
        got = {a["agent_id"]: a for a in watch.agent_actions(ev, self.NOW, 2.0, 3.0, {}, {})}
        self.assertEqual((got["b"]["action"], got["b"]["branch"]), ("stop+redispatch", "worktree-agent-a"))
        ev = [self.started("c", 4)]
        self.assertEqual(watch.agent_actions(ev, self.NOW, 2.0, 3.0, {}, {})[0]["branch"], "worktree-agent-c")

    def test_replacement_finishing_gets_to_review(self):
        ev = [self.started("a", 3), {"event": "stop", "agent_id": "a", "t": self.NOW - 2 * 3600},
              dict(self.started("b", 1), branch="worktree-agent-a"),
              {"event": "stop", "agent_id": "b", "t": self.NOW}]
        got = self.acts(ev, {"TASK-0001": "IN_PROGRESS"}, {"TASK-0001": [{"name": "worktree-agent-a"}]})
        self.assertEqual(got["a"], "to-review")

    def test_orphan_skips_branch_named_by_running_agent(self):
        old = self.NOW - 5 * 3600
        br = [{"name": "worktree-agent-a", "commits": 1, "last": old, "tasks": []}]
        ev = [dict(self.started("b", 1), branch="worktree-agent-a")]
        self.assertEqual(watch.orphan_branches(br, ev, {}, set(), self.NOW, 2.0), [])

    def test_later_agent_on_another_branch_does_not_suppress(self):
        ev = [self.started("a", 3), {"event": "stop", "agent_id": "a", "t": self.NOW - 2 * 3600},
              self.started("b", 1)]
        un = {"TASK-0001": [{"name": "worktree-agent-a"}]}
        self.assertEqual(self.acts(ev, {"TASK-0001": "IN_PROGRESS"}, un), {"a": "to-review"})
        ev[2]["description"] = "continue worktree-agent-a"
        self.assertEqual(self.acts(ev, {"TASK-0001": "IN_PROGRESS"}, un), {})

    def test_orphan_branch_of_foreground_agent(self):
        old = self.NOW - 5 * 3600
        br = [{"name": "worktree-agent-fg", "commits": 2, "last": old, "tasks": ["TASK-0001"]},
              {"name": "worktree-agent-run", "commits": 1, "last": old, "tasks": []},
              {"name": "worktree-agent-rev", "commits": 1, "last": old, "tasks": ["TASK-0002"]},
              {"name": "worktree-agent-new", "commits": 1, "last": self.NOW, "tasks": []},
              {"name": "feature-x", "commits": 1, "last": old, "tasks": []}]
        ev = [{"event": "start", "t": old}, self.started("run", 5)]
        got = watch.orphan_branches(br, ev, {"TASK-0002": "REVIEW"}, set(), self.NOW, 2.0)
        self.assertEqual([b["name"] for b in got], ["worktree-agent-fg"])

    def test_branch_task_from_agent_record_then_first_commit_body(self):
        br = [{"name": "worktree-agent-x1", "tasks": [], "body_tasks": ["TASK-0009"]},
              {"name": "worktree-agent-x2", "tasks": [], "body_tasks": ["TASK-0009"]},
              {"name": "worktree-agent-x3", "tasks": ["TASK-0005"], "body_tasks": ["TASK-0009"]}]
        ev = [self.started("x1", 1, ("TASK-0003",))]
        watch.resolve_tasks(br, ev)
        self.assertEqual([b["tasks"] for b in br], [["TASK-0003"], ["TASK-0009"], ["TASK-0005"]])

    def test_foreign_branch_does_not_trigger_to_review(self):
        ev = [self.started("a", 1), {"event": "stop", "agent_id": "a", "t": self.NOW}]
        self.assertEqual(self.acts(ev, {"TASK-0001": "IN_PROGRESS"},
                                   {"TASK-0001": [{"name": "worktree-agent-zzz"}]}), {})

    def test_stopped_in_progress_with_unlanded_branch_goes_to_review(self):
        ev = [self.started("a", 1), {"event": "stop", "agent_id": "a", "t": self.NOW}]
        self.assertEqual(self.acts(ev, {"TASK-0001": "IN_PROGRESS"}, {"TASK-0001": [{"name": "worktree-agent-a"}]}), {"a": "to-review"})
        self.assertEqual(self.acts(ev, {"TASK-0001": "REVIEW"}, {"TASK-0001": [{"name": "worktree-agent-a"}]}), {})
        self.assertEqual(self.acts(ev, {"TASK-0001": "IN_PROGRESS"}, {}), {})


class AgentLogShapeTest(unittest.TestCase):
    """A record as agent_log.py writes it, fed from a real async tool_response payload."""

    def record(self, prompt_tasks, description):
        with tempfile.TemporaryDirectory() as d:
            payload = {"tool_input": {"subagent_type": "developer", "description": description,
                                      "prompt": " ".join(prompt_tasks)},
                       "tool_response": {"agentId": "abc", "status": "async_launched"}}
            env = dict(os.environ, CLAUDE_PROJECT_DIR=d)
            subprocess.run([sys.executable, str(HOOKS / "agent_log.py"), "launched"], input=json.dumps(payload),
                           text=True, env=env, check=True)
            return json.loads((Path(d) / ".claude/.state/agents.jsonl").read_text().splitlines()[0])

    def test_real_payload_feeds_agent_actions(self):
        rec = self.record(["TASK-0002", "TASK-0001"], "TASK-0002 work")
        self.assertEqual(rec["agent_id"], "abc")
        rec["t"] = 0.0
        got = watch.agent_actions([rec], 4 * 3600.0, 2.0, 3.0, {}, {})
        self.assertEqual([(a["action"], a["tasks"], a["candidate"]) for a in got],
                         [("stop+redispatch", ["TASK-0002"], False)])

    def test_task_choice(self):
        self.assertEqual(watch.agent_task({"description": "x", "tasks": ["TASK-0001", "TASK-0002"]}),
                         (["TASK-0001"], True))
        self.assertEqual(watch.agent_task({"description": "x", "tasks": ["TASK-0001"]}), (["TASK-0001"], False))


class SessionContextTest(unittest.TestCase):
    def test_prints_autopilot_state(self):
        tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, tmp, True)
        (tmp / "CURRENT_STATE.md").write_text("## Сводка\n- x\n", encoding="utf-8")
        (tmp / "tools" / "development").mkdir(parents=True)
        shutil.copy(ROOT / "tools/development/autopilot.py", tmp / "tools/development/autopilot.py")
        state = tmp / "state.json"
        state.write_text(json.dumps({"state": "stop", "since": "2026-11-26 10:00"}))
        env = dict(os.environ, CLAUDE_PROJECT_DIR=str(tmp), AUTOPILOT_STATE=str(state))
        out = subprocess.run([sys.executable, str(HOOKS / "session_context.py")], input="{}", text=True,
                             capture_output=True, env=env).stdout
        self.assertIn("autopilot: stop since 2026-11-26 10:00", out)


class AutopilotTest(unittest.TestCase):
    def setUp(self):
        tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, tmp, True)
        self.state = tmp / "sub" / "autopilot.json"
        self.env = dict(os.environ, AUTOPILOT_STATE=str(self.state))

    def run_tool(self, script, *args):
        return subprocess.run([sys.executable, str(ROOT / "tools/development" / script), *args],
                              capture_output=True, text=True, env=self.env)

    def test_corrupt_state_reads_as_stopped_and_write_is_atomic(self):
        self.state.parent.mkdir(parents=True)
        self.state.write_text("{not json")
        self.assertIn("autopilot stopped", self.run_tool("capacity.py").stdout)
        self.assertIn("unreadable", self.run_tool("autopilot.py", "status").stdout)
        self.run_tool("autopilot.py", "on")
        self.assertEqual(json.loads(self.state.read_text())["state"], "on")
        self.assertEqual([p.name for p in self.state.parent.iterdir()], ["autopilot.json"])

    def test_switch_waits_for_the_lock(self):
        self.state.parent.mkdir(parents=True)
        fd = os.open(self.state.parent, os.O_RDONLY)
        self.addCleanup(os.close, fd)  # closing also drops the lock, so the child can never hang the run
        fcntl.flock(fd, fcntl.LOCK_EX)
        proc = subprocess.Popen([sys.executable, str(ROOT / "tools/development/autopilot.py"), "stop"],
                                env=self.env, stdout=subprocess.DEVNULL)

        def reap():
            if proc.poll() is None:
                proc.kill()
            proc.wait(timeout=10)
        self.addCleanup(reap)
        time.sleep(0.5)
        self.assertIsNone(proc.poll())
        self.assertFalse(self.state.exists())
        fcntl.flock(fd, fcntl.LOCK_UN)
        self.assertEqual(proc.wait(timeout=10), 0)
        self.assertTrue(self.state.exists())

    def test_stop_zeroes_capacity_and_on_restores_default(self):
        self.assertEqual(self.run_tool("autopilot.py", "status", "--brief").stdout, "")
        self.run_tool("autopilot.py", "stop")
        self.assertIn("stop", self.run_tool("autopilot.py", "status").stdout)
        out = self.run_tool("capacity.py").stdout
        self.assertIn("free agent slots 0 (autopilot stopped)", out)
        self.assertEqual(json.loads(self.run_tool("capacity.py", "--json").stdout)["agent_slots_free"], 0)
        self.run_tool("autopilot.py", "on")
        self.assertNotIn("autopilot stopped", self.run_tool("capacity.py").stdout)


class ShotBatchTest(unittest.TestCase):
    def setUp(self):
        self.repo = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.repo, True)
        self.old = (shot_batch.ROOT, shot_batch.MODEL_COMMIT)
        shot_batch.ROOT = self.repo
        self.addCleanup(lambda: (setattr(shot_batch, "ROOT", self.old[0]),
                                 setattr(shot_batch, "MODEL_COMMIT", self.old[1])))
        self.g("init", "-b", "main")
        self.commit("base", "a.txt")
        self.g("branch", "old")
        self.commit("model", "b.txt")
        shot_batch.MODEL_COMMIT = self.g("rev-parse", "HEAD")
        self.g("branch", "fresh")
        wt = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, wt, True)
        self.wt = wt / "fresh"
        self.g("worktree", "add", str(self.wt), "fresh")

    def g(self, *a):
        env = dict(os.environ, GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@t", GIT_COMMITTER_NAME="t",
                   GIT_COMMITTER_EMAIL="t@t")
        return subprocess.run(["git", *a], cwd=self.repo, text=True, capture_output=True, env=env).stdout.strip()

    def commit(self, msg, name):
        p = self.repo / name
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(msg + os.urandom(4).hex())
        self.g("add", "-A")
        self.g("commit", "-m", msg)

    def test_branch_checks(self):
        self.assertIsNone(shot_batch.check_branch("fresh"))
        self.assertIn("lacks", shot_batch.check_branch("old"))
        self.assertIn("not found", shot_batch.check_branch("nope"))
        self.commit("client change", shot_batch.CLIENT_DIR + "/c.cs")
        self.assertIn("behind main", shot_batch.check_branch("fresh"))

    def test_no_worktree_is_not_ready_and_git_error_is_not_ready(self):
        self.g("branch", "bare")
        self.assertIn("no worktree: git worktree add", shot_batch.check_branch("bare"))
        shot_batch.MODEL_COMMIT = "0" * 40  # unknown object: git errors instead of answering
        self.assertIn("git error", shot_batch.check_branch("fresh"))

    def test_collect_selects_waiting_client_tasks_with_shots(self):
        class It:
            def __init__(s, i, **m):
                s.id, s.meta = i, m
        shots = {"branch": "fresh", "only": "x*"}
        items = [It("TASK-1", status="REVIEW", category="client", shots=shots),
                 It("TASK-2", status="DONE", category="client", shots=shots),
                 It("TASK-3", status="REVIEW", category="core", shots=shots),
                 It("TASK-4", status="REVIEW", category="client"),
                 It("TASK-5", status="IN_PROGRESS", category="client", shots={"branch": "old", "only": "y"})]
        rows = shot_batch.collect(items)
        self.assertEqual([(r["id"], r["problem"] is None) for r in rows], [("TASK-1", True), ("TASK-4", False), ("TASK-5", False)])
        self.assertIn("--shots --only 'x*'", shot_batch.command(rows[0]))

    def test_missing_field_listed_and_list_form_with_flags(self):
        class It:
            def __init__(s, i, **m):
                s.id, s.meta = i, m
        items = [It("TASK-1", status="REVIEW", category="client"),
                 It("TASK-2", status="REVIEW", category="client", shots=[
                     {"branch": "fresh", "only": "tech_s1_*"},
                     {"branch": "fresh", "only": "map1945_*", "flags": "--map-frames"}])]
        rows = shot_batch.collect(items)
        self.assertEqual([(r["id"], r["problem"]) for r in rows],
                         [("TASK-1", "missing shots field"), ("TASK-2", None), ("TASK-2", None)])
        self.assertIn("--map-frames --only 'map1945_*'", shot_batch.command(rows[2]))

    def test_only_mask_reaches_every_command(self):
        class It:
            def __init__(s, i, **m):
                s.id, s.meta = i, m
        rows = shot_batch.collect([It("TASK-1", status="REVIEW", category="client", shots=[
            {"branch": "fresh", "only": "m1_*"},
            {"branch": "fresh", "only": "m2_*", "flags": "--map-frames"},
            {"branch": "fresh", "only": "m3", "flags": "--shots --only=other"}])])
        self.assertIn("--only 'm1_*'", shot_batch.command(rows[0]))
        self.assertIn("--only 'm2_*'", shot_batch.command(rows[1]))
        self.assertIn("must not set --only", rows[2]["problem"])

    def test_registry_is_read_from_the_main_checkout(self):
        shot_batch.ROOT = self.wt
        self.assertEqual(shot_batch.main_root().resolve(), self.repo.resolve())

    def test_bare_launch_is_interactive_not_ready(self):
        class It:
            def __init__(s, i, **m):
                s.id, s.meta = i, m
        rows = shot_batch.collect([It("TASK-1", status="REVIEW", category="client",
                                      shots={"branch": "fresh", "only": "a", "flags": "--headless"})])
        self.assertIn("interactive command", rows[0]["problem"])

    def test_malformed_flags_reported_and_others_continue(self):
        class It:
            def __init__(s, i, **m):
                s.id, s.meta = i, m
        rows = shot_batch.collect([It("TASK-1", status="REVIEW", category="client", shots=[
            {"branch": "fresh", "only": "a", "flags": "--map-frames 'oops"},
            {"branch": "fresh", "only": "b"}])])
        self.assertIn("malformed flags", rows[0]["problem"])
        self.assertIsNone(rows[1]["problem"])

    def test_recovery_command_quotes_every_argument(self):
        self.g("branch", "a&b")
        self.assertIn("git worktree add ../shots-a&b 'a&b'".replace("../shots-a&b", "'../shots-a&b'"),
                      shot_batch.check_branch("a&b"))

    def test_recovery_command_quotes_branch(self):
        self.g("branch", "a&b")
        self.assertIn("'a&b'", shot_batch.check_branch("a&b"))


def git_in(repo: Path, *a: str) -> str:
    env = dict(os.environ, GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@t", GIT_COMMITTER_NAME="t",
               GIT_COMMITTER_EMAIL="t@t")
    return subprocess.run(["git", *a], cwd=repo, text=True, capture_output=True, env=env).stdout.strip()


def commit_file(repo: Path, name: str, text: str, msg: str = "c") -> None:
    p = repo / name
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(text, encoding="utf-8")
    git_in(repo, "add", "-A")
    git_in(repo, "commit", "-m", msg)


TASK_STATUS_LINE = "status: {}         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED"


class GuardDispatchTest(unittest.TestCase):
    """guard_dispatch.py: committed task in a working status, one running agent per task (#591, ERR-0002/0006)."""

    def setUp(self):
        self.repo = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.repo, True)
        git_in(self.repo, "init", "-b", "main")
        for n, st in [("0001", "IN_PROGRESS"), ("0002", "DONE"), ("0004", "APPROVED")]:
            commit_file(self.repo, f"development/tasks/TASK-{n}-x.md",
                        f"---\nid: TASK-{n}\n{TASK_STATUS_LINE.format(st)}\n---\n")
        self.log = self.repo / ".claude" / ".state" / "agents.jsonl"

    def call(self, prompt, description="work", agent="developer"):
        payload = {"tool_input": {"subagent_type": agent, "description": description, "prompt": prompt}}
        return run_hook("guard_dispatch.py", payload, project=self.repo)

    def agents(self, *recs):
        self.log.parent.mkdir(parents=True, exist_ok=True)
        self.log.write_text("".join(json.dumps(r) + "\n" for r in recs), encoding="utf-8")

    def test_working_status_passes_and_done_is_blocked_despite_status_comment(self):
        self.assertEqual(self.call("Task: development/tasks/TASK-0001-x.md"), 0)
        self.assertEqual(self.call("do TASK-0004"), 0)
        self.assertEqual(self.call("Task: development/tasks/TASK-0002-x.md"), 2)

    def test_uncommitted_or_modified_task_is_blocked(self):
        (self.repo / "development/tasks/TASK-0003-new.md").write_text("---\nid: TASK-0003\n---\n")
        self.assertEqual(self.call("do TASK-0003"), 2)
        self.assertEqual(self.call("do TASK-0009"), 2)  # no file at all
        (self.repo / "development/tasks/TASK-0001-x.md").write_text("---\nid: TASK-0001\nstatus: IN_PROGRESS\n---\nnew\n")
        self.assertEqual(self.call("do TASK-0001"), 2)

    def test_status_is_read_from_main_not_the_working_tree(self):
        git_in(self.repo, "checkout", "-b", "side")
        commit_file(self.repo, "development/tasks/TASK-0002-x.md", f"---\nid: TASK-0002\n{TASK_STATUS_LINE.format('APPROVED')}\n---\n")
        self.assertEqual(self.call("do TASK-0002"), 2)

    def test_scope_description_then_task_path_then_all_ids(self):
        # context ids in the prompt do not count when the description or a task path names the task
        self.assertEqual(self.call("TASK-0001, see also TASK-0002", description="TASK-0001 work"), 0)
        self.assertEqual(self.call("Task: development/tasks/TASK-0001-x.md; earlier TASK-0002"), 0)
        self.assertEqual(self.call("TASK-0001 and TASK-0002"), 2)

    def test_read_only_agents_and_no_task_pass(self):
        self.assertEqual(self.call("review TASK-0002", agent="reviewer"), 0)
        self.assertEqual(self.call("review TASK-0002", agent="Explore"), 0)
        self.assertEqual(self.call("no task here"), 0)

    def test_second_dispatch_for_running_task_is_blocked(self):
        now = time.time()
        self.agents({"t": now - 60, "event": "start", "agent_type": "developer", "description": "TASK-0001 work",
                     "tasks": ["TASK-0001"]},
                    {"t": now - 60, "event": "launched", "agent_type": "developer", "description": "TASK-0001 work",
                     "tasks": ["TASK-0001"], "agent_id": "a1"})
        self.assertEqual(self.call("do TASK-0001", description="TASK-0001 again"), 2)
        self.assertEqual(self.call("do TASK-0004", description="TASK-0004"), 0)
        self.assertEqual(self.call("do TASK-0001", description="TASK-0001 [redispatch]"), 0)

    def test_stopped_stale_and_start_only_records_do_not_block(self):
        now = time.time()
        self.agents({"t": now - 60, "event": "start", "description": "TASK-0001", "tasks": ["TASK-0001"]},
                    {"t": now - 60, "event": "launched", "description": "TASK-0001", "tasks": ["TASK-0001"], "agent_id": "a1"},
                    {"t": now - 30, "event": "stop", "agent_id": "a1"},
                    {"t": now - 4 * 3600, "event": "launched", "description": "TASK-0001", "tasks": ["TASK-0001"],
                     "agent_id": "a2"})
        self.assertEqual(self.call("do TASK-0001", description="TASK-0001"), 0)


class GuardShotsBranchTest(unittest.TestCase):
    """guard_shots.py: a shot run needs the model commit and current client sources (#591, ERR-0001/0005)."""

    def setUp(self):
        base = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, base, True)
        self.home = base / "home"
        permit = self.home / ".cache" / "singularity" / "client-launch-ok"
        permit.parent.mkdir(parents=True)
        permit.write_text(str(time.time() + 3600))
        self.repo = base / "repo"
        self.repo.mkdir()
        git_in(self.repo, "init", "-b", "main")
        commit_file(self.repo, "src/Singularity.Client.Spike/a.txt", "base")
        git_in(self.repo, "branch", "old")
        commit_file(self.repo, "src/Singularity.Client.Spike/b.txt", "model")
        self.model = git_in(self.repo, "rev-parse", "HEAD")
        git_in(self.repo, "branch", "stale")
        commit_file(self.repo, "src/Singularity.Client.Spike/c.txt", "newer client")
        git_in(self.repo, "branch", "fresh")
        commit_file(self.repo, "docs/x.md", "not client")  # main moves outside the client: fresh stays fresh
        self.wt = {}
        for b in ["old", "stale", "fresh"]:
            self.wt[b] = base / f"wt-{b}"
            git_in(self.repo, "worktree", "add", str(self.wt[b]), b)

    def call(self, cmd, cwd=None):
        env = dict(os.environ, HOME=str(self.home), SINGULARITY_MODEL_COMMIT=self.model,
                   SINGULARITY_NO_REFUSAL_LOG="1", CLAUDE_PROJECT_DIR=str(self.repo))
        payload = {"tool_input": {"command": cmd}, "cwd": str(cwd or self.repo)}
        return subprocess.run([sys.executable, str(HOOKS / "guard_shots.py")], input=json.dumps(payload),
                              text=True, capture_output=True, env=env).returncode

    def test_checkout_from_cd_launcher_path_or_cwd(self):
        launcher = "./run-globe" + "-spike.sh"
        for b, rc in [("fresh", 0), ("old", 2), ("stale", 2)]:
            self.assertEqual(self.call(f"(cd {self.wt[b]} && {launcher} --shots --only a)"), rc, b)
            self.assertEqual(self.call(f"{self.wt[b]}/run-globe" + "-spike.sh --map-frames --only a"), rc, b)
            self.assertEqual(self.call(f"{launcher} --time-shots --only a", cwd=self.wt[b]), rc, b)
        self.assertEqual(self.call(f"{launcher} --shots --only a"), 0)  # main itself

    def test_every_launcher_of_a_batch_is_checked(self):
        launcher = "./run-globe" + "-spike.sh"
        cmd = f"(cd {self.wt['fresh']} && {launcher} --shots --only a); (cd {self.wt['old']} && {launcher} --shots --only b)"
        self.assertEqual(self.call(cmd), 2)

    def test_non_shot_launch_and_non_repo_dir(self):
        launcher = "./run-globe" + "-spike.sh"
        self.assertEqual(self.call(launcher, cwd=self.wt["old"]), 0)  # interactive launch: permit only
        self.assertEqual(self.call(f"{launcher} --shots --only a", cwd=self.home), 2)


if __name__ == "__main__":
    unittest.main(verbosity=1)
