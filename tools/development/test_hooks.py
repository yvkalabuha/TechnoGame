#!/usr/bin/env python3
"""Self-tests for Claude Code hooks and registry.py (stdlib unittest).

Run: python3 tools/development/test_hooks.py
"""
import json
import os
import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HOOKS = ROOT / ".claude" / "hooks"
REGISTRY = ROOT / "tools" / "development" / "registry.py"


def run_hook(script: str, payload: dict, *args: str, project: Path = ROOT) -> int:
    env = dict(os.environ, CLAUDE_PROJECT_DIR=str(project))
    proc = subprocess.run([sys.executable, str(HOOKS / script), *args],
                          input=json.dumps(payload), text=True,
                          capture_output=True, env=env)
    return proc.returncode


class GuardGlobalTest(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
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


if __name__ == "__main__":
    unittest.main(verbosity=1)
