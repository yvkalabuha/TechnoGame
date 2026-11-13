"""gitleaks wrapper (ADR-0015 §8): text bound for an external provider is scanned first.

A finding refuses the call (SecretDetected); a missing or broken binary refuses it too
(GitleaksUnavailable, a sandbox_unavailable fault). Findings are reported by rule id and line only:
the secret itself is never put into an exception, a log or a run record."""

import json
import os
import shutil
import subprocess
import tempfile
from pathlib import Path

from .sandbox import SandboxUnavailable

LEAK_EXIT = 2  # chosen with --exit-code so a crash (1) is not mistaken for a finding


class SecretDetected(RuntimeError):
    def __init__(self, findings):
        self.findings = findings  # [{"rule": str, "line": int}]
        super().__init__("secret_detected: " + ", ".join(f"{f['rule']}@{f['line']}" for f in findings[:10]))


class GitleaksUnavailable(SandboxUnavailable):
    """No usable gitleaks: nothing may be sent out."""


def find_gitleaks():
    local = Path.home() / ".local" / "bin" / "gitleaks"
    return shutil.which("gitleaks") or (str(local) if os.access(local, os.X_OK) else None)


def _run(args, stdin_text=None, binary=None, timeout=120):
    exe = binary or find_gitleaks()
    if not exe:
        raise GitleaksUnavailable("gitleaks not found: nothing is sent to an external provider without a scan")
    with tempfile.TemporaryDirectory() as d:
        report = Path(d) / "report.json"
        # Explicit config: the scanned tree's own .gitleaks.toml or GITLEAKS_CONFIG must not weaken the rules.
        conf = Path(d) / "gitleaks.toml"
        conf.write_text("[extend]\nuseDefault = true\n", encoding="utf-8")
        env = {k: v for k, v in os.environ.items() if not k.startswith("GITLEAKS_")}
        # Neither `gitleaks:allow` comments nor a .gitleaksignore (cwd, scanned dir) may suppress a finding:
        # the process runs in an empty cwd and reads its ignore file from an empty directory.
        empty = Path(d) / "empty"
        empty.mkdir()
        cmd = [exe, *args, "-c", str(conf), "--ignore-gitleaks-allow", "--gitleaks-ignore-path", str(empty),
               "--no-banner", "--no-color", "--redact", "--exit-code", str(LEAK_EXIT),
               "--report-format", "json", "--report-path", str(report)]
        try:
            p = subprocess.run(cmd, input=stdin_text, capture_output=True, text=True, timeout=timeout, env=env, cwd=empty)
        except (OSError, subprocess.TimeoutExpired) as e:
            raise GitleaksUnavailable(f"gitleaks failed to run: {type(e).__name__}") from None
        if p.returncode == 0:
            return
        if p.returncode != LEAK_EXIT:
            raise GitleaksUnavailable(f"gitleaks failed (rc {p.returncode}): {p.stderr.strip()[:200]}")
        try:
            raw = json.loads(report.read_text(encoding="utf-8"))
            findings = [{"rule": str(f.get("RuleID", "?")), "line": int(f.get("StartLine", 0))} for f in raw]
        except (OSError, ValueError, TypeError):
            findings = [{"rule": "unparsed", "line": 0}]
        raise SecretDetected(findings or [{"rule": "unparsed", "line": 0}])


def scan_text(text, binary=None):
    """Raises SecretDetected or GitleaksUnavailable; returns None when the text is clean."""
    _run(["stdin"], stdin_text=text, binary=binary)


def scan_dir(path, binary=None):
    """Scans the files of a directory (a Codex worktree), not git history.
    gitleaks reads `.gitleaksignore` from the scanned directory itself, so the files are scanned in a copy
    without any .gitleaksignore (symlinks are copied as links, not followed)."""
    with tempfile.TemporaryDirectory() as d:
        copy = Path(d) / "tree"
        shutil.copytree(path, copy, symlinks=True, ignore=shutil.ignore_patterns(".gitleaksignore", ".git"))
        _run(["dir", str(copy)], binary=binary)
