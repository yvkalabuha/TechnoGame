"""bubblewrap sandbox for B1 commands (dotnet restore/build/test on worker-written code).

The whole filesystem is read-only; only the worktree and the NuGet caches are writable; secrets are
hidden; the environment is rebuilt from a short allow-list. Without bwrap B1 refuses to run."""

import os
import shutil
import subprocess
from pathlib import Path

from . import config
from .verifiers.common import VerifierConfigError

# Variables copied from the host; everything else (API keys, tokens, GIT_*) is dropped.
ENV_KEEP = ("PATH", "HOME", "USER", "LOGNAME", "LANG", "LC_ALL", "TERM", "DOTNET_ROOT")
ENV_SET = {
    "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
    "DOTNET_NOLOGO": "1",
    "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
    "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false",
    "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH": "false",
    # English output: test counts are parsed from the summary line.
    "DOTNET_CLI_UI_LANGUAGE": "en",
    # No build servers that outlive the sandboxed command.
    "MSBUILDDISABLENODEREUSE": "1",
    "DOTNET_CLI_USE_MSBUILD_SERVER": "0",
    "UseSharedCompilation": "false",
    "TMPDIR": "/tmp",
}


class SandboxUnavailable(VerifierConfigError):
    """bwrap is missing or broken: B1 must not run (it never falls back to running unsandboxed)."""


def find_bwrap():
    return shutil.which("bwrap") or ("/usr/bin/bwrap" if os.access("/usr/bin/bwrap", os.X_OK) else None)


def default_hidden(home=None, environ=os.environ):
    """Secrets the sandboxed process must not see: ssh keys, the LLM key dir and key file."""
    home = Path(home or Path.home())
    hidden = [home / ".ssh", home / ".config" / "local-llm", Path(config.DEFAULT_API_KEY_FILE)]
    key_file = environ.get(config.API_KEY_FILE_ENV)
    if key_file:
        hidden.append(Path(key_file))
    return hidden


def default_writable(home=None):
    """NuGet package folder and HTTP cache (restore and build write there)."""
    home = Path(home or Path.home())
    return [home / ".nuget" / "packages", home / ".local" / "share" / "NuGet"]


def clean_env(environ=os.environ):
    env = {k: environ[k] for k in ENV_KEEP if k in environ}
    env.update(ENV_SET)
    return env


class Sandbox:
    def __init__(self, workdir, writable=None, hidden=None, bwrap=None, environ=os.environ):
        self.bwrap = bwrap or find_bwrap()
        if not self.bwrap:
            raise SandboxUnavailable("bwrap not found: B1 commands are never run unsandboxed")
        self.workdir = Path(workdir).resolve()
        self.writable = [Path(p) for p in (default_writable() if writable is None else writable)]
        self.hidden = [Path(p) for p in (default_hidden(environ=environ) if hidden is None else hidden)]
        self.env = clean_env(environ)

    def argv(self, cmd, network=False):
        a = [self.bwrap, "--die-with-parent", "--new-session", "--unshare-user", "--unshare-pid",
             "--unshare-ipc", "--unshare-uts", "--unshare-cgroup-try"]
        if not network:
            a.append("--unshare-net")
        a += ["--ro-bind", "/", "/", "--dev", "/dev", "--proc", "/proc", "--tmpfs", "/tmp"]
        for p in self.writable:
            if p.is_dir():
                a += ["--bind", str(p.resolve()), str(p.resolve())]
        a += ["--bind", str(self.workdir), str(self.workdir)]
        # Hidden last so no writable bind can re-expose them.
        dirs = [p.resolve() for p in self.hidden if p.is_dir()]
        for d in dirs:
            a += ["--tmpfs", str(d)]
        for p in self.hidden:
            f = p.resolve()
            if p.exists() and not p.is_dir() and not any(d in f.parents for d in dirs):
                a += ["--ro-bind", "/dev/null", str(f)]
        a += ["--chdir", str(self.workdir), "--", *cmd]
        return a

    def run(self, cmd, timeout, network=False):
        """(returncode, output). Exit code 124 on timeout."""
        try:
            p = subprocess.run(self.argv(cmd, network), capture_output=True, text=True, timeout=timeout,
                               env=self.env, stdin=subprocess.DEVNULL)
        except subprocess.TimeoutExpired:
            return 124, f"timeout after {timeout} s"
        except OSError as e:
            raise SandboxUnavailable(f"bwrap failed to start: {e}") from None
        return p.returncode, p.stdout + p.stderr

    def self_test(self):
        """Fails fast when bwrap exists but cannot create namespaces on this host."""
        rc, out = self.run(["/bin/true"], timeout=30)
        if rc != 0:
            raise SandboxUnavailable(f"bwrap self-test failed: {out.strip()[:300]}")
