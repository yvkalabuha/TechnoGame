"""B1: unit tests written by the worker, checked in a git worktree: paths, build, tests, mutants."""

import os
import re
import shutil
from pathlib import Path

from .. import paths, worktree
from ..sandbox import Sandbox
from .common import VerifierConfigError, make_report, ratio

_PASSED_RE = re.compile(r"Passed:\s+(\d+)")
_FAILED_RE = re.compile(r"Failed:\s+(\d+)")


def resolve_cmd(cmd):
    """'dotnet' falls back to ~/.dotnet/dotnet when it is not on PATH (CLAUDE.md)."""
    if cmd and cmd[0] == "dotnet" and shutil.which("dotnet") is None:
        home = Path.home() / ".dotnet" / "dotnet"
        if home.is_file():
            return [str(home), *cmd[1:]]
    return list(cmd)


def sandboxed_runner(workdir):
    """Default B1 runner: every command runs in bwrap with only workdir (and NuGet caches) writable.
    Raises VerifierConfigError (task FAILED) when the sandbox is unavailable; never runs unsandboxed."""
    box = Sandbox(workdir)
    box.self_test()

    def run(cmd, cwd, timeout, network=False):
        if Path(cwd).resolve() != box.workdir:
            raise VerifierConfigError(f"sandbox is bound to {box.workdir}, not {cwd}")
        return box.run(resolve_cmd(cmd), timeout, network=network)
    return run


def error_lines(output, root, limit=20):
    seen, out = set(), []
    for line in output.splitlines():
        if re.search(r"\berror\b|\bFailed\b|Assert\.", line):
            line = line.replace(str(root) + os.sep, "").strip()
            if line not in seen:
                seen.add(line)
                out.append(line)
        if len(out) >= limit:
            break
    return "\n".join(out) or output.strip()[-1500:]


def write_files(root, files, allowed, forbidden):
    """Writes only allowed paths inside root. Returns (written, violations)."""
    violations, plan = [], []
    for f in files:
        ok, norm = paths.write_allowed(f["path"], allowed, forbidden)
        if not ok:
            violations.append(norm)
            continue
        target = (root / norm).resolve()
        if root.resolve() not in target.parents:
            violations.append(f"escapes worktree: {norm}")
            continue
        # A committed symlink could point an allowed name at a forbidden file: check the real target.
        real = target.relative_to(root.resolve()).as_posix()
        ok, why = paths.write_allowed(real, allowed, forbidden)
        if not ok:
            violations.append(f"{norm} resolves to {why}")
            continue
        plan.append((norm, target, f["content"]))
    if violations:
        return [], violations
    for norm, target, content in plan:
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")
    return [p[0] for p in plan], []


class B1Verifier:
    def __init__(self, contract, worktree_path, base, branch, runner=None):
        self.contract = contract
        self.root = worktree_path
        self.base = base
        self.branch = branch
        self.run = runner or sandboxed_runner(worktree_path)
        v = contract["verification"]
        self.restore_cmd, self.build_cmd, self.test_cmd = v["restore_cmd"], v["build_cmd"], v["test_cmd"]
        self.timeout = v["command_timeout_s"]
        self.mutants = v["mutants"]
        self._restored = False

    def _restore(self):
        """The only networked step; runs on the pristine worktree, so a failure is not the worker's."""
        if self._restored:
            return
        rc, out = self.run(self.restore_cmd, self.root, self.timeout, network=True)
        if rc != 0:
            raise VerifierConfigError(f"restore failed (rc {rc}): {error_lines(out, self.root)[:500]}")
        self._restored = True

    def verify(self, artifact):
        acceptance = self.contract["acceptance"]
        worktree.reset(self.root, self.base, self.branch)
        self._restore()
        written, violations = write_files(self.root, artifact["files"], self.contract["allowed_paths"],
                                          self.contract["forbidden_paths"])
        if violations:
            return make_report({"paths_allowed": (0.0, "; ".join(violations))}, acceptance, scope_violation=True)
        m = {"paths_allowed": (1.0, f"wrote {', '.join(written)}")}

        rc, out = self.run(self.build_cmd, self.root, self.timeout)
        m["build"] = (1.0 if rc == 0 else 0.0, "ok" if rc == 0 else error_lines(out, self.root))
        if rc != 0:
            return make_report(m, acceptance)

        rc, out = self.run(self.test_cmd, self.root, self.timeout)
        passed = int(_PASSED_RE.findall(out)[-1]) if _PASSED_RE.findall(out) else 0
        failed = int(_FAILED_RE.findall(out)[-1]) if _FAILED_RE.findall(out) else 0
        ok = rc == 0 and passed > 0
        m["tests_pass"] = (1.0 if ok else 0.0,
                           f"{passed} passed" if ok else
                           (error_lines(out, self.root) if failed or rc else "no tests matched the filter"))
        m["test_count"] = (float(passed), f"{passed} passing tests")
        if ok:
            m["mutation_score"] = self._mutants()
        return make_report(m, acceptance)

    def _mutants(self):
        killed, survived = 0, []
        for i, mut in enumerate(self.mutants):
            target = self.root / mut["file"]
            original = target.read_text(encoding="utf-8")
            if original.count(mut["find"]) != 1:
                raise VerifierConfigError(f"mutant {i}: '{mut['find']}' must occur exactly once in {mut['file']}")
            try:
                target.write_text(original.replace(mut["find"], mut["replace"]), encoding="utf-8")
                rc, _ = self.run(self.test_cmd, self.root, self.timeout)
            finally:
                target.write_text(original, encoding="utf-8")
            if rc != 0:
                killed += 1
            else:
                survived.append(f"'{mut['find']}' -> '{mut['replace']}' in {mut['file']}")
        detail = "all mutants killed" if not survived else "survived (tests did not fail): " + "; ".join(survived)
        return ratio(killed, len(self.mutants)), detail

    def finalize(self, artifact):
        """Leaves the best artifact committed on the llm branch. Returns the changed files."""
        worktree.reset(self.root, self.base, self.branch)
        written, violations = write_files(self.root, artifact["files"], self.contract["allowed_paths"],
                                          self.contract["forbidden_paths"])
        if violations:
            return []
        worktree.commit_all(self.root, f"test(llm): {self.contract['task_id']} worker candidate (unreviewed)",
                            self.branch)
        return written
