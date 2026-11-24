"""Codex second reviewer (ADR-0015 §4): `codex exec --sandbox read-only` inside bwrap.

Visible inside: system directories and the Node install of `codex` (read-only), the clean review worktree
at /work (read-only), ~/.codex (writable: login and session state). $HOME is an empty tmpfs and /var/www is
absent. The network is shared (OpenAI API). Codex is a subscription: no USD is booked, only attempts and tokens."""

import hashlib
import json
import os
import re
import shutil
import stat
import subprocess
from datetime import datetime, timezone
from pathlib import Path

from . import config, limitstate, paths
from .sandbox import SYSTEM_LINKS, SYSTEM_RO, SandboxUnavailable, find_bwrap
from .worktree import git

ENV_KEEP = ("PATH", "USER", "LOGNAME", "LANG", "LC_ALL", "TERM")
LIMIT_RE = re.compile(r"usage limit|rate.?limit|quota|limit (?:reached|exceeded)|too many requests|\b429\b", re.I)
BWRAP_RE = re.compile(r"^bwrap:", re.M)  # bwrap's own diagnostics: no namespace, mount refused, bad argument
ERROR_EVENTS = ("error", "turn.failed")
ATTEMPTS = "codex_attempts.jsonl"


class CodexLimit(RuntimeError):
    """The subscription limit is reached: the review moves to the next provider. reset_at: when the limit
    resets (aware datetime) if the message named it."""

    def __init__(self, msg, reset_at=None):
        super().__init__(msg)
        self.reset_at = reset_at


class CodexFailed(RuntimeError):
    pass


def find_codex():
    exe = shutil.which("codex")
    if not exe:
        raise SandboxUnavailable("codex not found")
    return exe


def make_worktree(repo, head, dest):
    """Clean detached worktree on `head`; files the model may never see are removed, as are bin/obj."""
    git(["worktree", "add", "-q", "--detach", str(dest), head], repo)
    dest = Path(dest)
    for p in sorted(dest.rglob("*")):
        rel = p.relative_to(dest).as_posix()
        if rel == ".git" or rel.startswith(".git/"):
            continue
        if p.is_symlink() or p.is_file():
            if paths.matches(rel, config.CONTEXT_EXCLUDE):
                p.unlink()
    for p in sorted(dest.rglob("*"), reverse=True):
        if p.is_dir() and not p.is_symlink() and p.name in ("bin", "obj") and ".git" not in p.relative_to(dest).parts:
            shutil.rmtree(p)
    return dest


def remove_worktree(repo, path):
    try:
        git(["worktree", "remove", "--force", str(path)], repo)
    except Exception:  # noqa: BLE001 - best effort; fall back to deleting the directory and pruning
        shutil.rmtree(path, ignore_errors=True)
        subprocess.run(["git", "worktree", "prune"], cwd=repo, capture_output=True)


def tree_digest(path):
    """sha256 over every entry except the top-level .git file: relative path, mode, content or link target."""
    h = hashlib.sha256()
    root = Path(path)
    for p in sorted(root.rglob("*")):
        rel = p.relative_to(root).as_posix()
        if rel == ".git":
            continue
        st = p.lstat()
        h.update(f"{rel}\0{stat.S_IMODE(st.st_mode):o}\0".encode())
        if p.is_symlink():
            h.update(os.readlink(p).encode())
        elif p.is_file():
            h.update(hashlib.sha256(p.read_bytes()).digest())
        h.update(b"\n")
    return h.hexdigest()


def codex_mounts(codex_exe, home=None):
    """(real path of the codex entry point, read-only paths to mount, directory of `node`).
    Only the codex package directory and the `node` binary are mounted, not the whole Node install.
    Paths under /usr are already visible, so they are not mounted again. SandboxUnavailable when the layout is
    not a package directory with package.json, `node` is missing, or a mount would cover $HOME."""
    home = Path(home or Path.home()).resolve()
    real = Path(codex_exe).resolve()
    pkg = real.parent.parent
    node = Path(codex_exe).parent / "node"
    node = node if node.is_file() else Path(shutil.which("node") or "")
    if not (pkg / "package.json").is_file() or not node.is_file():
        raise SandboxUnavailable(f"codex package or node not found (package {pkg}, node {node})")
    mounts = []
    for p in (pkg, node):
        p = p.resolve() if p == pkg else p
        if p == home or p in home.parents:
            raise SandboxUnavailable(f"refusing to mount {p}: it covers $HOME")
        if not str(p).startswith("/usr/"):
            mounts.append(p)
    return real, mounts, node.parent


def bwrap_argv(worktree, schema_file, codex_exe, home=None, bwrap=None, codex_home=None):
    bwrap = bwrap or find_bwrap()
    if not bwrap:
        raise SandboxUnavailable("bwrap not found: Codex never runs unsandboxed")
    home = Path(home or Path.home())
    codex_home = Path(codex_home or home / ".codex")
    a = [bwrap, "--die-with-parent", "--new-session", "--unshare-user", "--unshare-pid", "--unshare-ipc",
         "--unshare-uts", "--unshare-cgroup-try",  # the network stays shared
         "--dev", "/dev", "--proc", "/proc", "--tmpfs", "/tmp"]
    for p in SYSTEM_LINKS:
        a += ["--symlink", os.readlink(p), p] if os.path.islink(p) else ["--ro-bind-try", p, p]
    for p in SYSTEM_RO:
        a += ["--ro-bind-try", p, p]
    a += ["--tmpfs", str(home)]
    for p in codex_mounts(codex_exe, home)[1]:
        a += ["--ro-bind", str(p), str(p)]
    if codex_home.is_dir():
        a += ["--bind", str(codex_home), str(codex_home)]
    a += ["--ro-bind", str(worktree), "/work", "--ro-bind", str(schema_file), "/schema.json", "--chdir", "/work"]
    return a


def command(codex_exe):
    return [codex_exe, "exec", "--sandbox", "read-only", "--skip-git-repo-check", "--ephemeral", "--json",
            "--color", "never", "--output-schema", "/schema.json", "-C", "/work", "-"]


def parse_events(stdout):
    """(last agent message text, usage dict) from `codex exec --json` JSONL. Unknown lines are skipped."""
    text, usage = None, {}
    for line in stdout.splitlines():
        try:
            ev = json.loads(line)
        except ValueError:
            continue
        if not isinstance(ev, dict):
            continue
        item = ev.get("item")
        if ev.get("type") == "item.completed" and isinstance(item, dict) and item.get("type") == "agent_message":
            text = item.get("text")
        if ev.get("type") == "turn.completed" and isinstance(ev.get("usage"), dict):
            usage = ev["usage"]
    return text, usage


def error_text(stdout):
    """Text of error events only: model output and tool results must not be able to fake a limit."""
    out = []
    for line in stdout.splitlines():
        try:
            ev = json.loads(line)
        except ValueError:
            continue
        if isinstance(ev, dict) and ev.get("type") in ERROR_EVENTS:
            out.append(json.dumps(ev, ensure_ascii=False))
    return "\n".join(out)


def run_codex(prompt, worktree, schema_file, timeout=1800, home=None, bwrap=None, environ=os.environ):
    """(answer text, usage). CodexLimit on a subscription limit, SandboxUnavailable when bwrap cannot run,
    CodexFailed on any other failure."""
    exe = find_codex()
    env = {k: environ[k] for k in ENV_KEEP if k in environ}
    env["HOME"] = str(home or Path.home())
    real, _, node_dir = codex_mounts(exe, home)
    env["PATH"] = f"{node_dir}:{env.get('PATH', '/usr/bin:/bin')}"
    argv = bwrap_argv(worktree, schema_file, exe, home=home, bwrap=bwrap) + command(str(real))
    try:
        p = subprocess.run(argv, input=prompt, capture_output=True, text=True, timeout=timeout, env=env)
    except subprocess.TimeoutExpired:
        raise CodexFailed(f"codex timed out after {timeout} s") from None
    except OSError as e:
        raise SandboxUnavailable(f"bwrap failed to start: {e}") from None
    text, usage = parse_events(p.stdout)
    if p.returncode != 0 or not text:
        if not text and BWRAP_RE.search(p.stderr):  # the sandbox itself failed: a paid fallback would not help
            raise SandboxUnavailable(f"bwrap failed: {p.stderr.strip()[-300:]}")
        msg = error_text(p.stdout) + p.stderr
        if LIMIT_RE.search(msg):
            raise CodexLimit("codex_limit: subscription limit reached",
                             limitstate.parse_reset(msg, datetime.now(timezone.utc).astimezone()))
        # stderr tail only: it carries no prompt text, and the answer is never echoed into the message.
        raise CodexFailed(f"codex exited {p.returncode}: {p.stderr.strip()[-400:]}")
    return text, usage


def log_attempt(task, head, outcome, usage=None, directory=config.LEDGER_DIR, provider="codex"):
    """Append-only attempt log next to the ledger (Codex costs no USD, so it is not a ledger entry)."""
    directory.mkdir(parents=True, exist_ok=True)
    usage = usage or {}
    rec = {"ts": datetime.now(timezone.utc).isoformat(timespec="seconds"), "task": task, "head": head[:12],
           "outcome": outcome, "input_tokens": usage.get("input_tokens"), "output_tokens": usage.get("output_tokens")}
    if provider != "codex":
        rec["provider"] = provider
    fd = os.open(directory / ATTEMPTS, os.O_CREAT | os.O_WRONLY | os.O_APPEND, 0o600)
    with os.fdopen(fd, "a", encoding="utf-8") as f:
        f.write(json.dumps(rec, sort_keys=True) + "\n")


