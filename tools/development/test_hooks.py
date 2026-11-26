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
    env = dict(os.environ, CLAUDE_PROJECT_DIR=str(project), SINGULARITY_NO_REFUSAL_LOG="1", SINGULARITY_NO_REDIS="1")
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
        self.env = dict(os.environ, AUTOPILOT_STATE=str(self.state), SINGULARITY_NO_REDIS="1")

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

    def call(self, prompt, description="work", agent="developer", facts="\nФакты: нет связанных"):
        payload = {"tool_input": {"subagent_type": agent, "description": description, "prompt": prompt + facts}}
        return run_hook("guard_dispatch.py", payload, project=self.repo)

    def test_code_agents_need_a_facts_block(self):
        # #597: developer, simulation-engineer, test-engineer, 3d-modeler prompts carry kb_check related output
        related = ("\n  Факты:\n  FACT second-review: x  [ADR-0015]\n  DECISION #597 (2026-11-26): y\n\n"
                   "  Verify: tests\n")
        for agent in ["developer", "simulation-engineer", "test-engineer", "3d-modeler"]:
            self.assertEqual(self.call("do TASK-0001", agent=agent, facts=""), 2, agent)
            self.assertEqual(self.call("no task here", agent=agent, facts=""), 2, agent)
            self.assertEqual(self.call("do TASK-0001", agent=agent, facts=related), 0, agent)
        for facts, want in [("\nФакты: нет связанных", 0), ("\n**Факты:** нет связанных", 0),
                            ("\nФакты:\nnothing related found in FACTS.yaml or DECISIONS_LOG.md", 0),
                            ("\nФакты:\n\nFACT a: b", 0), ("\nФакты:\n- FACT a: b", 0),
                            ("\nФакты:\n", 2), ("\nФакты: см. выше", 2), ("\nFACT a: b", 2),
                            ("\nФакты:\nsome text\n\nFACT a: b", 2)]:
            self.assertEqual(self.call("do TASK-0001", facts=facts), want, facts)

    def test_other_agents_need_no_facts_block(self):
        for agent in ["reviewer", "general-purpose", "ui-art-director", "game-designer"]:
            self.assertEqual(self.call("do TASK-0001", agent=agent, facts=""), 0, agent)

    def test_facts_check_fails_open_on_internal_error(self):
        payload = {"tool_input": {"subagent_type": "developer", "description": "work", "prompt": 123}}
        self.assertEqual(run_hook("guard_dispatch.py", payload, project=self.repo), 0)

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

    def test_quoted_or_missing_status_is_read_like_the_registry(self):
        commit_file(self.repo, "development/tasks/TASK-0005-q.md", '---\nid: TASK-0005\nstatus: "DONE"\n---\n')
        commit_file(self.repo, "development/tasks/TASK-0006-q.md", "---\nid: TASK-0006\nstatus: 'IN_PROGRESS'  # x\n---\n")
        commit_file(self.repo, "development/tasks/TASK-0007-q.md", "---\nid: TASK-0007\n---\n")
        self.assertEqual(self.call("do TASK-0005"), 2)
        self.assertEqual(self.call("do TASK-0006"), 0)
        self.assertEqual(self.call("do TASK-0007"), 2)

    def test_task_must_be_in_head_the_worktree_base(self):
        git_in(self.repo, "checkout", "-q", "-b", "behind", "HEAD~1")  # cut before TASK-0004 was added
        self.assertEqual(self.call("do TASK-0004"), 2)
        self.assertEqual(self.call("do TASK-0001"), 0)

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

    def test_running_read_only_agent_does_not_hold_the_task(self):
        now = time.time()
        self.agents(*[{"t": now - 60, "event": "launched", "agent_type": t, "description": "TASK-0001 review",
                       "tasks": ["TASK-0001"], "agent_id": f"r{i}"}
                      for i, t in enumerate(["reviewer", "Explore", "ui-art-director", "research-scientist"])])
        self.assertEqual(self.call("do TASK-0001", description="TASK-0001 fix"), 0)

    def test_stopped_stale_and_start_only_records_do_not_block(self):
        now = time.time()
        self.agents({"t": now - 60, "event": "start", "description": "TASK-0001", "tasks": ["TASK-0001"]},
                    {"t": now - 60, "event": "launched", "description": "TASK-0001", "tasks": ["TASK-0001"], "agent_id": "a1"},
                    {"t": now - 30, "event": "stop", "agent_id": "a1"},
                    {"t": now - 4 * 3600, "event": "launched", "description": "TASK-0001", "tasks": ["TASK-0001"],
                     "agent_id": "a2"},
                    ["not", "an", "object"], {"t": "bad", "event": "launched", "description": "TASK-0001",
                                              "agent_id": "a3"})
        self.assertEqual(self.call("do TASK-0001", description="TASK-0001"), 0)

    def test_damaged_log_fails_open(self):
        self.log.parent.mkdir(parents=True, exist_ok=True)
        self.log.write_bytes(b'{"t": 1, "event": "launched", "agent_id": "a1", "description": "\xff TASK-0001"}\n')
        self.assertEqual(self.call("do TASK-0001", description="TASK-0001"), 0)
        self.agents({"t": time.time(), "event": "launched", "agent_id": "a2", "description": 5, "tasks": "TASK-0001"},
                    {"t": time.time(), "event": "launched", "agent_id": "a3", "description": None, "tasks": [7]})
        self.assertEqual(self.call("do TASK-0001", description="TASK-0001"), 0)


def kb_check_module():
    sys.path.insert(0, str(ROOT / "tools" / "development"))
    try:
        import kb_check
    finally:
        sys.path.remove(str(ROOT / "tools" / "development"))
    return kb_check


class KbCheckFactsTest(unittest.TestCase):
    """#597: drift and superseded sources are errors; freshness by reviewed:; stale list; landing gate."""

    def setUp(self):
        self.kb = kb_check_module()

    def run_facts(self, facts, decisions=None, fresh=True):
        errors, warnings = [], []
        self.kb.check_facts(facts, errors, warnings, decisions or {}, fresh=fresh)
        return errors, warnings

    def test_probe_drift_is_an_error(self):
        probe = {"file": "tools/development/kb_check.py", "regex": r"STALE_DAYS = (\d+)"}
        errors, _ = self.run_facts([{"id": "f", "statement": "s", "probe": dict(probe, expect="99")}])
        self.assertEqual(len(errors), 1)
        self.assertIn("drifted", errors[0])
        errors, _ = self.run_facts([{"id": "f", "statement": "s", "probe": dict(probe, expect="30")}])
        self.assertEqual(errors, [])
        errors, _ = self.run_facts([{"id": "f", "statement": "s", "probe": {"file": "no/such.file", "count": "x",
                                                                           "expect": 1}}])
        self.assertIn("missing", errors[0])

    def test_superseded_source_is_an_error_partial_a_warning(self):
        dec = {422: "**SUPERSEDED by #565, #569 (2026-11-25).** old goal", 450: "**Verdict part SUPERSEDED by #578.** x",
               565: "air power"}
        errors, warnings = self.run_facts([{"id": "a", "statement": "заменена (#422)", "source": "DECISIONS_LOG #565"}], dec)
        self.assertEqual((errors, warnings), ([], []))  # the statement may name an old decision
        errors, warnings = self.run_facts([{"id": "b", "statement": "s", "source": "DECISIONS_LOG #418–#422"}], dec)
        self.assertEqual(len(errors), 1)
        self.assertIn("#422", errors[0])
        errors, warnings = self.run_facts([{"id": "c", "statement": "s", "source": "#450"}], dec)
        self.assertEqual((len(errors), len(warnings)), (0, 1))

    def test_reviewed_must_be_a_date(self):
        import datetime as dt
        for value in ["2026-11-26", dt.date(2026, 11, 26), None]:
            self.assertEqual(self.run_facts([{"id": "a", "statement": "s", "reviewed": value}], fresh=False)[0], [])
        self.assertEqual(len(self.run_facts([{"id": "a", "statement": "s", "reviewed": "вчера"}])[0]), 1)

    def test_source_changed_after_reviewed_is_a_warning(self):
        fact = {"id": "a", "statement": "s", "source": "tools/development/kb_check.py; DECISIONS_LOG #1"}
        _, warnings = self.run_facts([dict(fact, reviewed="2000-01-01")])
        self.assertEqual(len(warnings), 1)
        self.assertIn("re-check", warnings[0])
        self.assertEqual(self.run_facts([dict(fact, reviewed="2999-01-01")])[1], [])
        self.assertEqual(self.run_facts([dict(fact)])[1], [])  # never reviewed: listed by stale, not warned
        paths = self.kb.fact_source_paths({"source": "docs/decisions/DECISIONS_LOG.md; tools/development/kb_*.py",
                                           "probe": {"file": "tools/development/land.py"}})
        self.assertEqual(paths[0], "tools/development/land.py")
        self.assertIn("tools/development/kb_check.py", paths)
        self.assertNotIn("docs/decisions/DECISIONS_LOG.md", paths)

    def test_stale_lists_old_and_unreviewed_facts(self):
        tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, tmp, True)
        facts = tmp / "FACTS.yaml"
        facts.write_text("facts:\n  - {id: old, statement: s, reviewed: 2026-10-01}\n"
                         "  - {id: new, statement: s, reviewed: 2026-11-20}\n  - {id: never, statement: s}\n",
                         encoding="utf-8")
        out = subprocess.run([sys.executable, "-c", "import sys; sys.path.insert(0, sys.argv[1]); import kb_check;"
                              "kb_check.FACTS = __import__('pathlib').Path(sys.argv[2]);"
                              "sys.exit(kb_check.main(['stale', '--today', '2026-11-26']))",
                              str(ROOT / "tools/development"), str(facts)], capture_output=True, text=True)
        self.assertEqual(out.returncode, 0, out.stderr)
        self.assertIn("STALE old: reviewed 2026-10-01 (56 days ago)", out.stdout)
        self.assertIn("STALE never: never reviewed", out.stdout)
        self.assertNotIn("STALE new", out.stdout)

    def test_decision_rows(self):
        text = "| # | Дата | Решение | Ссылка |\n|---|---|---|---|\n| 7 | 2026-11-26 | a \\| b | ref |\n| x | y |\n"
        self.assertEqual(self.kb.decision_rows(text), {7: "a \\| b"})
        wrapped = "| 8 | d | head:\n- one\n- two | ref |\n| 9 | d | x | r |\n"
        self.assertEqual(self.kb.decision_rows(wrapped), {8: "head: - one - two", 9: "x"})
        real = (ROOT / "docs/decisions/DECISIONS_LOG.md").read_text(encoding="utf-8")
        import re
        self.assertEqual(set(self.kb.decision_rows(real)), {int(n) for n in re.findall(r"^\| (\d+) \|", real, re.M)})

    def landing_repo(self):
        repo = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, repo, True)
        git_in(repo, "init", "-b", "main")
        commit_file(repo, "development/FACTS.yaml",
                    "facts:\n  - {id: codex, statement: s, keywords: [Codex]}\n  - {id: map, statement: m, keywords: [карта]}\n")
        commit_file(repo, "docs/decisions/DECISIONS_LOG.md", "| # | Д | Р | С |\n|---|---|---|---|\n| 1 | d | Codex old | r |\n")
        return repo, git_in(repo, "rev-parse", "HEAD")

    def test_landing_needs_facts_for_a_new_decision(self):
        repo, base = self.landing_repo()
        log = "| # | Д | Р | С |\n|---|---|---|---|\n| 1 | d | Codex old, edited | r |\n"
        commit_file(repo, "docs/decisions/DECISIONS_LOG.md", log)  # an edited row is not a new decision
        self.assertEqual(self.kb.landing_problems(repo, base, "HEAD"), [])
        commit_file(repo, "docs/decisions/DECISIONS_LOG.md", log + "| 2 | d | новое про Codex | r |\n")
        problems = self.kb.landing_problems(repo, base, "HEAD")
        self.assertEqual(len(problems), 1)
        self.assertIn("#2", problems[0])
        self.assertIn("codex", problems[0])
        self.assertNotIn("map", problems[0])
        self.assertEqual(self.kb.landing_problems(repo, base, "HEAD", "merge\n\nfacts-ok: #2"), [])
        self.assertEqual(len(self.kb.landing_problems(repo, base, "HEAD", "facts-ok: #3")), 1)
        commit_file(repo, "x.txt", "x", msg="docs: confirm\n\nfacts-ok: #1, #2")
        self.assertEqual(self.kb.landing_problems(repo, base, "HEAD"), [])

    def test_landing_accepts_a_changed_fact(self):
        repo, base = self.landing_repo()
        commit_file(repo, "docs/decisions/DECISIONS_LOG.md",
                    "| # | Д | Р | С |\n|---|---|---|---|\n| 1 | d | Codex old | r |\n| 2 | d | Codex new | r |\n")
        commit_file(repo, "development/FACTS.yaml", "facts:\n  - {id: codex, statement: s, keywords: [Codex], "
                    "reviewed: 2026-11-26}\n  - {id: map, statement: m, keywords: [карта]}\n")
        self.assertEqual(self.kb.landing_problems(repo, base, "HEAD"), [])


class KbGuardDecisionTest(unittest.TestCase):
    def call(self, ti):
        payload = {"tool_name": "Edit", "tool_input": dict(ti, file_path=str(ROOT / "docs/decisions/DECISIONS_LOG.md"))}
        env = dict(os.environ, CLAUDE_PROJECT_DIR=str(ROOT))
        return subprocess.run([sys.executable, str(HOOKS / "kb_guard.py"), "file"], input=json.dumps(payload),
                              text=True, capture_output=True, env=env).stdout

    def test_new_decision_row_prints_its_facts(self):
        out = self.call({"old_string": "| 595 | x |", "new_string": "| 595 | x |\n| 597 | y |"})
        self.assertIn("DECISION #597", out)
        self.assertIn("facts-ok: #597", out)
        self.assertNotIn("DECISION #595", out)

    def test_write_of_known_rows_prints_no_facts(self):
        text = (ROOT / "docs/decisions/DECISIONS_LOG.md").read_text(encoding="utf-8")
        committed = subprocess.run(["git", "show", "HEAD:docs/decisions/DECISIONS_LOG.md"], cwd=ROOT,
                                   capture_output=True, text=True).stdout
        if text != committed:
            self.skipTest("decisions log has uncommitted rows")
        self.assertNotIn("DECISION #", self.call({"content": text}))


class KbCheckDecisionsLogTest(unittest.TestCase):
    def test_decisions_log_has_no_unknown_ids(self):
        r = subprocess.run([sys.executable, str(ROOT / "tools/development/kb_check.py"), "check", "--file",
                            str(ROOT / "docs/decisions/DECISIONS_LOG.md")], capture_output=True, text=True, cwd=ROOT)
        self.assertEqual(r.returncode, 0, r.stdout)


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
                   SINGULARITY_NO_REFUSAL_LOG="1", CLAUDE_PROJECT_DIR=str(self.repo), SINGULARITY_NO_REDIS="1")
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

    def test_quoted_launcher_or_flag_is_still_checked(self):
        name = "run-globe" + "-spike.sh"
        self.assertEqual(self.call(f"'{self.wt['old']}/{name}' --shots --only a"), 2)
        self.assertEqual(self.call(f"./{name} '--shots' --only a", cwd=self.wt["old"]), 2)
        self.assertEqual(self.call(f"\"{self.wt['fresh']}/{name}\" '--shots' --only a"), 0)
        self.assertEqual(self.call(f"grep -n '{name} --shots' notes.txt", cwd=self.wt["old"]), 0)  # no launch

    def test_cd_inside_a_finished_subshell_does_not_apply(self):
        name = "./run-globe" + "-spike.sh"
        self.assertEqual(self.call(f"(cd {self.wt['fresh']} && pwd); {name} --shots --only a", cwd=self.wt["old"]), 2)
        self.assertEqual(self.call(f"cd {self.wt['fresh']} && {name} --shots --only a", cwd=self.wt["old"]), 0)

    def test_newline_separates_commands(self):
        name = "./run-globe" + "-spike.sh"
        cmd = f"cd {self.wt['fresh']} && pwd\ncd {self.wt['old']} && {name} --shots --only a"
        self.assertEqual(self.call(cmd), 2)
        cmd = f"cd {self.wt['old']} && pwd\ncd {self.wt['fresh']} && \\\n{name} --shots --only a"
        self.assertEqual(self.call(cmd), 0)

    def test_headless_import_does_not_exempt_a_later_shot_launch(self):
        godot = "/opt/Godot_v4.7-stable_linux.x86_64"
        name = "run-globe" + "-spike.sh"
        self.assertEqual(self.call(f"{godot} --headless --import && {self.wt['old']}/{name} --shots --only a"), 2)
        self.assertEqual(self.call(f"{godot} --headless --import && {self.wt['fresh']}/{name} --shots --only a"), 0)
        self.assertEqual(self.call(f"{godot} --headless --import", cwd=self.wt["old"]), 0)

    def test_history_is_read_from_the_checkout_root(self):
        name = "./run-globe" + "-spike.sh"  # launched from inside the client folder of a stale checkout
        sub = self.wt["stale"] / "src" / "Singularity.Client.Spike"
        self.assertEqual(self.call(f"{name} --shots --only a", cwd=sub), 2)

    def test_unknown_directory_fails_closed(self):
        name = "./run-globe" + "-spike.sh"
        for cmd in [f"cd $WT && {name} --shots --only a", f"cd {self.wt['old']} || cd {self.wt['fresh']}; {name} --shots --only a",
                    f"bash -c 'cd {self.wt['fresh']} && {name} --shots --only a'", f"cd - && {name} --shots --only a"]:
            self.assertEqual(self.call(cmd, cwd=self.wt["fresh"]), 2, cmd)

    def test_non_shot_launch_and_non_repo_dir(self):
        launcher = "./run-globe" + "-spike.sh"
        self.assertEqual(self.call(launcher, cwd=self.wt["old"]), 0)  # interactive launch: permit only
        self.assertEqual(self.call(f"{launcher} --shots --only a", cwd=self.home), 2)


# --- TASK-0201: Redis heartbeat, lock holders, permit, agents without psql/redis-cli (#590, #597) -----------------

import redis_kv  # noqa: E402
import socketserver  # noqa: E402
import threading  # noqa: E402


class FakeRedis:
    """Enough of Redis for redis_kv: PING AUTH SET(EX,NX) GET MGET DEL TTL and the two EVAL scripts it sends."""

    def __init__(self):
        self.data, self.exp, self.lock = {}, {}, threading.Lock()

    def _get(self, k):
        if k in self.exp and self.exp[k] <= time.time():
            self.data.pop(k, None)
            self.exp.pop(k, None)
        return self.data.get(k)

    def _set(self, k, v, ex):
        self.data[k] = v
        if ex is None:
            self.exp.pop(k, None)
        else:
            self.exp[k] = time.time() + int(ex)

    def call(self, *a):
        with self.lock:
            a = [x.decode() if isinstance(x, bytes) else str(x) for x in a]
            op = a[0].upper()
            if op in ("PING", "AUTH"):
                return "PONG" if op == "PING" else "OK"
            if op == "SET":
                k, v, opts = a[1], a[2], [x.upper() for x in a[3:]]
                ex = a[3 + opts.index("EX") + 1] if "EX" in opts else None
                if "NX" in opts and self._get(k) is not None:
                    return None
                self._set(k, v, ex)
                return "OK"
            if op == "GET":
                return self._get(a[1])
            if op == "MGET":
                return [self._get(k) for k in a[1:]]
            if op == "DEL":
                n = sum(1 for k in a[1:] if self._get(k) is not None)
                for k in a[1:]:
                    self.data.pop(k, None)
                return n
            if op == "TTL":
                return int(self.exp[a[1]] - time.time()) if self._get(a[1]) is not None and a[1] in self.exp else -1
            if op == "EVAL":
                script, key, args = a[1], a[3], a[4:]
                cur = self._get(key)
                if "del" in script:  # redis_kv.RELEASE
                    if cur == args[0]:
                        self.data.pop(key, None)
                        return 1
                    return 0
                if cur == args[0] or cur is None:  # compare-and-set of holder_set
                    self._set(key, args[1], args[2])
                    return "OK"
                return None
            raise ValueError("ERR unknown command " + op)

    def pipeline(self, cmds):
        return [self.call(*c) for c in cmds]

    def close(self):
        pass


class FakeRedisServer:
    """FakeRedis behind a real socket on 127.0.0.1 (RESP2), for hooks that run as subprocesses."""

    def __init__(self):
        store = self.store = FakeRedis()

        class H(socketserver.StreamRequestHandler):
            def handle(self):
                while True:
                    line = self.rfile.readline()
                    if not line:
                        return
                    n = int(line[1:-2])
                    args = []
                    for _ in range(n):
                        size = int(self.rfile.readline()[1:-2])
                        args.append(self.rfile.read(size + 2)[:-2])
                    try:
                        out = self.enc(store.call(*args))
                    except ValueError as e:
                        out = b"-" + str(e).encode() + b"\r\n"
                    self.wfile.write(out)

            def enc(self, v):
                if v is None:
                    return b"$-1\r\n"
                if isinstance(v, int):
                    return b":%d\r\n" % v
                if isinstance(v, list):
                    return b"*%d\r\n" % len(v) + b"".join(self.enc(x) for x in v)
                b = str(v).encode()
                return b"$%d\r\n%s\r\n" % (len(b), b)

        socketserver.ThreadingTCPServer.allow_reuse_address = True
        self.srv = socketserver.ThreadingTCPServer(("127.0.0.1", 0), H)
        self.srv.daemon_threads = True
        self.port = self.srv.server_address[1]
        threading.Thread(target=self.srv.serve_forever, daemon=True).start()

    def env(self):
        env = dict(os.environ, SINGULARITY_REDIS_HOST="127.0.0.1", SINGULARITY_REDIS_PORT=str(self.port))
        env.pop("SINGULARITY_NO_REDIS", None)
        return env

    def stop(self):
        self.srv.shutdown()
        self.srv.server_close()


def use_fake(test, fake=None):
    """Point redis_kv at a fake (None: Redis down) for the duration of the test."""
    fake = fake if fake is not None else None
    old = redis_kv._factory
    redis_kv._factory = (lambda: fake) if fake is not None else (lambda: None)
    test.addCleanup(setattr, redis_kv, "_factory", old)
    return fake


class RedisKvTest(unittest.TestCase):
    def test_real_protocol_against_fake_server(self):
        srv = FakeRedisServer()
        self.addCleanup(srv.stop)
        c = redis_kv.Client("127.0.0.1", srv.port, 1.0, "pw")
        self.addCleanup(c.close)
        self.assertEqual(c.call("SET", "a", "x y", "EX", 5), "OK")
        self.assertEqual(c.pipeline([("GET", "a"), ("GET", "none"), ("MGET", "a", "none"), ("DEL", "a")]),
                         ["x y", None, ["x y", None], 1])
        with self.assertRaises(redis_kv.RedisError):
            c.call("NOPE")

    def test_beat_and_last_beats(self):
        fake = use_fake(self, FakeRedis())
        self.assertTrue(redis_kv.beat("ag1", 1000.0))
        self.assertEqual(fake.call("TTL", "agent:ag1:beat") > 590, True)
        self.assertGreater(fake.call("TTL", "agent:ag1:seen"), 600)
        self.assertEqual(redis_kv.last_beats(["ag1", "ag2", ""]), {"ag1": 1000.0})

    def test_redis_down_is_silent(self):
        use_fake(self, None)
        self.assertFalse(redis_kv.beat("ag1"))
        self.assertIsNone(redis_kv.last_beats(["ag1"]))
        self.assertIsNone(redis_kv.holder_set("land", {}, 60))
        self.assertEqual(redis_kv.permit_get(), (False, None))

    def test_unreachable_port_is_silent_and_fast(self):
        with socket_free_port() as port:
            env = dict(SINGULARITY_REDIS_HOST="127.0.0.1", SINGULARITY_REDIS_PORT=str(port))
            with mock_env(env):
                t = time.monotonic()
                self.assertFalse(redis_kv.beat("ag1"))
                self.assertLess(time.monotonic() - t, 0.5)

    def test_holder_keeps_live_holder_replaces_dead_and_clears_only_own(self):
        fake = use_fake(self, FakeRedis())
        tok = redis_kv.holder_set("land", {"branch": "b1"}, 60)
        self.assertIsNotNone(tok)
        self.assertIsNone(redis_kv.holder_set("land", {"branch": "b2"}, 60))  # our own pid is alive
        redis_kv.holder_clear("land", "not-the-token")
        self.assertEqual(json.loads(fake.call("GET", "lock:land"))["branch"], "b1")
        redis_kv.holder_clear("land", tok)
        self.assertIsNone(fake.call("GET", "lock:land"))
        fake.call("SET", "lock:land", json.dumps({"pid": 2 ** 22 + 12345}), "EX", 60)  # no such process
        tok = redis_kv.holder_set("land", {"branch": "b3"}, 60)
        self.assertEqual(json.loads(fake.call("GET", "lock:land"))["branch"], "b3")
        self.assertEqual(redis_kv.holders(["land", "test"]), {"land": json.loads(tok)})

    def test_lock_is_flock_with_or_without_redis(self):
        d = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, d, True)
        for fake in (FakeRedis(), None):
            use_fake(self, fake)
            with redis_kv.lock("test", 60, 0, d) as ok:
                self.assertTrue(ok)
                if fake is not None:
                    self.assertIsNotNone(fake.call("GET", "lock:test"))
                with redis_kv.lock("test", 60, 0, d) as again:  # the flock refuses a second holder
                    self.assertFalse(again)
            if fake is not None:
                self.assertIsNone(fake.call("GET", "lock:test"))


import contextlib  # noqa: E402
import socket  # noqa: E402


@contextlib.contextmanager
def socket_free_port():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()  # nothing listens there now
    yield port


@contextlib.contextmanager
def mock_env(env):
    old = {k: os.environ.get(k) for k in [*env, "SINGULARITY_NO_REDIS"]}
    os.environ.update(env)
    os.environ.pop("SINGULARITY_NO_REDIS", None)
    try:
        yield
    finally:
        for k, v in old.items():
            if v is None:
                os.environ.pop(k, None)
            else:
                os.environ[k] = v


class AgentBeatHookTest(unittest.TestCase):
    def run_beat(self, payload, env):
        t = time.monotonic()
        rc = subprocess.run([sys.executable, str(HOOKS / "agent_beat.py")], input=json.dumps(payload), text=True,
                            capture_output=True, env=env).returncode
        return rc, time.monotonic() - t

    def test_agent_beats_main_session_does_not(self):
        srv = FakeRedisServer()
        self.addCleanup(srv.stop)
        self.assertEqual(self.run_beat({"tool_name": "Bash", "agent_id": "abc123"}, srv.env())[0], 0)
        self.assertIsNotNone(srv.store.call("GET", "agent:abc123:beat"))
        self.assertIsNotNone(srv.store.call("GET", "agent:abc123:seen"))
        self.run_beat({"tool_name": "Bash"}, srv.env())
        self.assertEqual(sorted(srv.store.data), ["agent:abc123:beat", "agent:abc123:seen"])

    def test_redis_down_exits_zero(self):
        with socket_free_port() as port:
            env = dict(os.environ, SINGULARITY_REDIS_HOST="127.0.0.1", SINGULARITY_REDIS_PORT=str(port))
            rc, took = self.run_beat({"agent_id": "abc"}, env)
        self.assertEqual(rc, 0)
        self.assertLess(took, 2.0)  # interpreter start dominates; the socket wait is 30 ms


class WatchBeatsTest(unittest.TestCase):
    NOW = 100 * 3600.0

    def started(self, key, hours):
        return {"event": "launched", "agent_id": key, "t": self.NOW - hours * 3600, "tasks": ["TASK-0001"]}

    def acts(self, events, beats):
        return {a["agent_id"]: a["action"] for a in watch.agent_actions(events, self.NOW, 2.0, 3.0, {}, {}, beats, 20.0)}

    def test_beat_replaces_the_log_rule(self):
        ev = [self.started("busy", 5), self.started("quiet", 1), self.started("gone", 1), self.started("never", 5)]
        beats = {"busy": self.NOW - 60, "quiet": self.NOW - 25 * 60, "gone": self.NOW - 70 * 60}
        self.assertEqual(self.acts(ev, beats), {"quiet": "ping", "gone": "stop+redispatch", "never": "stop+redispatch"})

    def test_redis_down_keeps_the_log_rule(self):
        ev = [self.started("busy", 5), self.started("young", 1)]
        self.assertEqual(self.acts(ev, None), {"busy": "stop+redispatch"})

    def test_default_silent_min_covers_a_long_tool_call(self):
        # the beat comes after a tool call: a 30-minute build must not read as silent
        ev = [self.started("build", 5), self.started("dead", 5)]
        beats = {"build": self.NOW - 30 * 60, "dead": self.NOW - 50 * 60}
        acts = {a["agent_id"]: a["action"] for a in watch.agent_actions(ev, self.NOW, 2.0, 3.0, {}, {}, beats)}
        self.assertEqual(acts, {"dead": "ping"})

    def test_stopped_agent_ignores_beats(self):
        ev = [self.started("a", 5), {"event": "stop", "agent_id": "a", "t": self.NOW}]
        self.assertEqual(self.acts(ev, {"a": self.NOW - 90 * 60}), {})


class ClientPermitTest(unittest.TestCase):
    def setUp(self):
        d = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, d, True)
        self.file = d / "client-launch-ok"
        import client_permit
        self.cp = client_permit

    def test_file_decides_when_redis_is_down(self):
        use_fake(self, None)
        self.assertFalse(self.cp.live(self.file))
        self.file.write_text(str(time.time() + 600))
        self.assertTrue(self.cp.live(self.file))
        self.file.write_text(str(time.time() - 1))
        self.assertFalse(self.cp.live(self.file))

    def test_redis_can_only_refuse(self):
        fake = use_fake(self, FakeRedis())
        exp = time.time() + 600
        self.file.write_text(str(exp))
        self.assertFalse(self.cp.live(self.file))  # granted while Redis was down: grant again
        self.assertTrue(redis_kv.permit_set(exp))
        self.assertTrue(self.cp.live(self.file))
        self.file.unlink()  # revoked while Redis was down: the key left in Redis must not bring it back
        self.assertFalse(self.cp.live(self.file))
        self.assertTrue(redis_kv.permit_clear())
        self.assertIsNone(fake.call("GET", redis_kv.PERMIT_KEY))

    def test_guard_shots_reads_redis(self):
        srv = FakeRedisServer()
        self.addCleanup(srv.stop)
        home = self.file.parent / "home"
        permit = home / ".cache" / "singularity" / "client-launch-ok"
        permit.parent.mkdir(parents=True)
        permit.write_text(str(time.time() + 600))
        env = dict(srv.env(), HOME=str(home), SINGULARITY_NO_REFUSAL_LOG="1")
        payload = {"tool_input": {"command": "./run-client" + ".sh"}, "cwd": str(home)}

        def rc():
            return subprocess.run([sys.executable, str(HOOKS / "guard_shots.py")], input=json.dumps(payload),
                                  text=True, capture_output=True, env=env).returncode
        self.assertEqual(rc(), 2)  # file permit but no Redis key
        srv.store.call("SET", redis_kv.PERMIT_KEY, str(time.time() + 600), "EX", 600)
        self.assertEqual(rc(), 0)
        env["SINGULARITY_NO_REDIS"] = "1"
        srv.store.call("DEL", redis_kv.PERMIT_KEY)
        self.assertEqual(rc(), 0)  # Redis down: the file alone, as before


class RedisKvHoldCliTest(unittest.TestCase):
    def test_hold_runs_the_command_under_the_lock(self):
        home = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, home, True)
        env = dict(os.environ, HOME=str(home), SINGULARITY_NO_REDIS="1")
        tool = str(ROOT / "tools/development/redis_kv.py")

        def run(*a):
            return subprocess.run([sys.executable, tool, *a], capture_output=True, text=True, env=env).returncode
        self.assertEqual(run("hold", "test", "--", sys.executable, "-c", "raise SystemExit(3)"), 3)
        self.assertEqual(run("hold", "test", "--ttl"), 2)
        self.assertEqual(run("hold", "test"), 2)
        lockf = home / ".cache" / "singularity" / "test.lock"
        fd = os.open(lockf, os.O_RDWR)
        self.addCleanup(os.close, fd)
        fcntl.flock(fd, fcntl.LOCK_EX)
        self.assertEqual(run("hold", "test", "--", "true"), 75)


class GuardBashDenyDbTest(unittest.TestCase):
    def call(self, cmd, agent="a1", env=None):
        payload = {"tool_input": {"command": cmd}}
        if agent:
            payload["agent_id"] = agent
        e = dict(os.environ, SINGULARITY_NO_REFUSAL_LOG="1", **(env or {}))
        e.pop("SINGULARITY_KB_TEST", None) if not env else None
        return subprocess.run([sys.executable, str(HOOKS / "guard_bash.py"), "--deny-db"], input=json.dumps(payload),
                              text=True, capture_output=True, env=e).returncode

    def test_agents_may_not_run_db_clients(self):
        for cmd in ["psql -d kb", "redis-cli KEYS '*'", "nice -n 10 psql", "cd /tmp && /usr/bin/redis-cli ping",
                    "bash -lc 'psql -c 1'", "sudo -u postgres psql", "X=1 redis-cli get a", "ls | xargs psql",
                    "eval redis-cli ping", 'v="$(redis-cli get a)"', 'echo "$(psql -c 1)"', "echo `psql -c 1`",
                    "for k in a b; do redis-cli get $k; done", "if psql -c 1; then :; fi", "{ psql -c 1; }",
                    "! psql -c 1", "SINGULARITY_KB_TEST=1 redis-cli get a",
                    "echo SINGULARITY_KB_TEST=1; psql -c 1", "psql -c 'SINGULARITY_KB_TEST=1'"]:
            self.assertEqual(self.call(cmd), 2, cmd)
        self.assertEqual(self.call("redis-cli ping", env={"SINGULARITY_KB_TEST": "1"}), 2)

    def test_main_session_mentions_and_kb_test_pass(self):
        self.assertEqual(self.call("psql -d kb", agent=None), 0)
        for cmd in ["git commit -m 'psql and redis-cli'", "git grep psql", "python3 tools/development/kb_db.py status",
                    "if grep -q psql f; then echo x; fi",
                    "echo redis-cli", "SINGULARITY_KB_TEST=1 psql -d kbtest_x -c 'select 1'"]:
            self.assertEqual(self.call(cmd), 0, cmd)
        self.assertEqual(self.call("psql -d kbtest_x", env={"SINGULARITY_KB_TEST": "1"}), 0)

    def test_db_word_as_argument_of_a_wrapped_command_passes(self):
        # reviewer finding: wrappers were skipped with their arguments, so the next mention counted as a call
        for cmd in ["nice -n 10 grep psql x", "timeout 60 rg redis-cli docs", "xargs grep psql",
                    "env A=1 git grep psql", "command -v psql", "sudo -u me grep -r redis-cli ."]:
            self.assertEqual(self.call(cmd), 0, cmd)

    def test_wrapped_db_call_is_denied(self):
        for cmd in ["nice psql -c 1", "nice -n 10 psql", "nice -10 psql", "timeout 5 redis-cli ping",
                    "timeout -s KILL 5 psql", "env X=1 psql", "env -S 'psql -c 1'", "xargs psql", "xargs -0 -n1 psql",
                    "sudo -u postgres psql", "stdbuf -oL redis-cli ping", "ionice -c3 psql", "chrt 10 psql",
                    "taskset 0x3 redis-cli", "time psql", "nice timeout 5 env X=1 psql", "watch -d psql -c 1",
                    "ionice -t psql", "watch -n 5 redis-cli ping"]:
            self.assertEqual(self.call(cmd), 2, cmd)

    def test_heredoc_body_is_not_a_command(self):
        for cmd in ["cat <<'EOF' > notes.md\npsql -d kb\nredis-cli ping\nEOF",
                    "git commit -F - <<EOF\npsql is not used\nEOF",
                    "cat <<-EOF\n\tpsql\n\tEOF\necho done",
                    "cat <<'EOF'\n$(psql -c 1)\nEOF"]:
            self.assertEqual(self.call(cmd), 0, cmd)
        for cmd in ["cat <<EOF\n$(psql -c 1)\nEOF", "cat <<EOF\nx\nEOF\npsql -c 1", "cat <<'A' <<B\nx\nA\ny\nB\npsql",
                    "bash <<'EOF'\npsql -d kb\nEOF", "sh -s <<EOF\nredis-cli ping\nEOF",
                    'echo "a <<EOF"\npsql -d kb', "x=$((1<<n))\npsql -d kb"]:
            self.assertEqual(self.call(cmd), 2, cmd)

    def test_kb_test_only_for_kbtest_databases(self):
        for cmd in ["SINGULARITY_KB_TEST=1 psql -d kbtest_ok", "SINGULARITY_KB_TEST=1 psql --dbname=kbtest_ok -c 1",
                    "SINGULARITY_KB_TEST=1 psql kbtest_ok", "env SINGULARITY_KB_TEST=1 psql -dkbtest_ok",
                    "SINGULARITY_KB_TEST=1 psql postgresql://u@localhost/kbtest_ok?sslmode=disable",
                    "SINGULARITY_KB_TEST=1 psql 'host=x dbname=kbtest_ok'"]:
            self.assertEqual(self.call(cmd), 0, cmd)
        for cmd in ["SINGULARITY_KB_TEST=1 psql -d prod", "SINGULARITY_KB_TEST=1 psql",
                    "SINGULARITY_KB_TEST=1 psql -d kbtest", "SINGULARITY_KB_TEST=1 psql postgresql://u@h/singularity",
                    "SINGULARITY_KB_TEST=1 psql -c 'select 1' kb"]:
            self.assertEqual(self.call(cmd), 2, cmd)
        for cmd, want in [("psql -d prod", 2), ("psql", 2), ("psql -d kbtest_ok", 0)]:
            self.assertEqual(self.call(cmd, env={"SINGULARITY_KB_TEST": "1"}), want, cmd)

    def test_allow_list_mode_unchanged(self):
        args = ["--agent", "reviewer", "--allow", "git diff"]
        for cmd, want in [("git diff", 0), ("psql", 2), ("git diff; ls", 2)]:
            rc = subprocess.run([sys.executable, str(HOOKS / "guard_bash.py"), *args],
                                input=json.dumps({"tool_input": {"command": cmd}}), text=True, capture_output=True,
                                env=dict(os.environ, SINGULARITY_NO_REFUSAL_LOG="1")).returncode
            self.assertEqual(rc, want, cmd)


if __name__ == "__main__":
    unittest.main(verbosity=1)
