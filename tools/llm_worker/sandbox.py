"""bubblewrap sandbox for B1 commands (dotnet restore/build/test on worker-written code).

Nothing of the host is mounted except system directories (read-only), ~/.dotnet and the NuGet package
folder (read-only; writable only on the networked restore step) and the worktree (writable). $HOME is an
empty tmpfs and /var/www is absent, so credentials, .mcp.json and lab/results are not reachable (ADR-0015 §8).
The environment is rebuilt from a short allow-list. Without bwrap B1 refuses to run."""

import os
import shutil
import subprocess
from pathlib import Path

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


# System paths a toolchain needs. Missing ones are skipped (--ro-bind-try).
SYSTEM_RO = ("/usr", "/etc/ssl", "/etc/ca-certificates", "/etc/resolv.conf", "/etc/hosts", "/etc/nsswitch.conf",
             "/etc/host.conf", "/etc/passwd", "/etc/group", "/etc/localtime", "/etc/ld.so.cache",
             "/etc/ld.so.conf", "/etc/ld.so.conf.d", "/etc/alternatives", "/etc/os-release")
# Usually symlinks into /usr on merged-usr hosts; recreated the same way inside.
SYSTEM_LINKS = ("/bin", "/sbin", "/lib", "/lib32", "/lib64", "/libx32")


def clean_env(environ=os.environ):
    env = {k: environ[k] for k in ENV_KEEP if k in environ}
    env.update(ENV_SET)
    return env


class Sandbox:
    def __init__(self, workdir, home=None, extra_ro=(), bwrap=None, environ=os.environ):
        self.bwrap = bwrap or find_bwrap()
        if not self.bwrap:
            raise SandboxUnavailable("bwrap not found: B1 commands are never run unsandboxed")
        self.workdir = Path(workdir).resolve()
        self.home = Path(home or Path.home())
        self.extra_ro = [Path(p) for p in extra_ro]
        self.env = clean_env(environ)

    def argv(self, cmd, network=False):
        """network=True is the restore step: host network and a writable NuGet package folder.
        Every other step has no network and a read-only package folder."""
        a = [self.bwrap, "--die-with-parent", "--new-session", "--unshare-user", "--unshare-pid",
             "--unshare-ipc", "--unshare-uts", "--unshare-cgroup-try"]
        if not network:
            a.append("--unshare-net")
        a += ["--dev", "/dev", "--proc", "/proc", "--tmpfs", "/tmp"]
        for p in SYSTEM_LINKS:
            if os.path.islink(p):
                a += ["--symlink", os.readlink(p), p]
            else:
                a += ["--ro-bind-try", p, p]
        for p in SYSTEM_RO:
            a += ["--ro-bind-try", p, p]
        # Home first: everything under it that is not mounted below stays invisible.
        a += ["--tmpfs", str(self.home)]
        for p in self.extra_ro:  # after the $HOME tmpfs, or a path under $HOME would be hidden
            a += ["--ro-bind", str(p), str(p)]
        dotnet = self.home / ".dotnet"
        if dotnet.is_dir():
            # The SDK writes ~/.dotnet/corefx (shared-memory mutexes). A read-only ~/.dotnet cannot take
            # a mountpoint, so ~/.dotnet is a tmpfs holding read-only binds of its entries; corefx and any
            # other write land in memory, never on the host.
            a += ["--tmpfs", str(dotnet)]
            for child in sorted(dotnet.iterdir()):
                if child.name != "corefx":
                    a += ["--ro-bind", str(child), str(child)]
            a += ["--tmpfs", str(dotnet / "corefx")]
        packages = self.home / ".nuget" / "packages"
        if packages.is_dir():
            a += ["--bind" if network else "--ro-bind", str(packages), str(packages)]
        a += ["--bind", str(self.workdir), str(self.workdir)]
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
