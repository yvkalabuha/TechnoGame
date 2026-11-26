#!/usr/bin/env python3
"""Minimal Redis client for platform hooks and tools (owner #590), Python stdlib only.

Redis holds short-lived platform state: agent heartbeats, lock holders and the client launch permit.
Files and flock stay the fallback: every helper here fails silently (returns None/False) when Redis is down,
so callers behave exactly as before Redis existed. Agents never talk to Redis directly (owner #597).

  python3 tools/development/redis_kv.py status                  Redis reachable? beats, lock holders, permit
  python3 tools/development/redis_kv.py hold NAME [--ttl S] [--wait S] -- CMD...
                                                                run CMD under lock NAME (test, godot, ...)

Connection: 127.0.0.1:6379, or SINGULARITY_REDIS_HOST / SINGULARITY_REDIS_PORT; password from
SINGULARITY_REDIS_PASSWORD or ~/.config/singularity/redis.pass (none if absent). SINGULARITY_NO_REDIS=1 turns
Redis off (tests and the fallback path).
"""
from __future__ import annotations

import contextlib
import fcntl
import json
import os
import socket
import subprocess
import sys
import time
from pathlib import Path

BEAT_TTL = 600            # agent:<id>:beat, as decided in #590
SEEN_TTL = 2 * 86400      # agent:<id>:seen keeps the last beat time after the beat key expired
LOCK_DIR = Path.home() / ".cache" / "singularity"
RELEASE = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end"

_factory = None  # tests put a fake client here: a callable returning an object with call(*args) / pipeline(cmds)


class RedisError(Exception):
    pass


def _password() -> str | None:
    pw = os.environ.get("SINGULARITY_REDIS_PASSWORD")
    if pw:
        return pw
    try:
        return (Path.home() / ".config" / "singularity" / "redis.pass").read_text().strip() or None
    except OSError:
        return None


def _encode(args) -> bytes:
    out = [b"*%d\r\n" % len(args)]
    for a in args:
        b = a if isinstance(a, bytes) else str(a).encode()
        out += [b"$%d\r\n" % len(b), b, b"\r\n"]
    return b"".join(out)


class Client:
    """One connection, RESP2. Replies: str for simple and bulk strings, int, list, None; '-' raises RedisError."""

    def __init__(self, host: str, port: int, timeout: float, password: str | None):
        self.sock = socket.create_connection((host, port), timeout=timeout)
        self.sock.settimeout(timeout)
        self.f = self.sock.makefile("rb")
        if password:
            self.call("AUTH", password)

    def close(self) -> None:
        with contextlib.suppress(OSError):
            self.f.close()
            self.sock.close()

    def _read(self):
        line = self.f.readline()
        if not line.endswith(b"\r\n"):
            raise OSError("redis: connection closed")
        kind, rest = line[:1], line[1:-2]
        if kind == b"+":
            return rest.decode()
        if kind == b"-":
            raise RedisError(rest.decode())
        if kind == b":":
            return int(rest)
        if kind == b"$":
            n = int(rest)
            if n < 0:
                return None
            data = self.f.read(n + 2)
            return data[:-2].decode()
        if kind == b"*":
            n = int(rest)
            return None if n < 0 else [self._read() for _ in range(n)]
        raise OSError(f"redis: bad reply {line[:20]!r}")

    def call(self, *args):
        self.sock.sendall(_encode(args))
        return self._read()

    def pipeline(self, cmds: list[tuple]) -> list:
        self.sock.sendall(b"".join(_encode(c) for c in cmds))
        return [self._read() for _ in cmds]


def connect(timeout: float = 0.2):
    """A connected client, or None when Redis is off or unreachable. Never raises."""
    if _factory is not None:
        try:
            return _factory()
        except Exception:
            return None
    if os.environ.get("SINGULARITY_NO_REDIS"):
        return None
    try:
        return Client(os.environ.get("SINGULARITY_REDIS_HOST", "127.0.0.1"),
                      int(os.environ.get("SINGULARITY_REDIS_PORT", "6379")), timeout, _password())
    except Exception:
        return None


def _close(r) -> None:
    with contextlib.suppress(Exception):
        r.close()


def _do(fn, timeout: float = 0.2):
    """fn(client) or None when Redis is off, unreachable or answers with an error."""
    r = connect(timeout)
    if r is None:
        return None
    try:
        return fn(r)
    except Exception:
        return None
    finally:
        _close(r)


# --- heartbeat -------------------------------------------------------------------------------------------------

def beat(agent_id: str, t: float | None = None, timeout: float = 0.03) -> bool:
    """SET agent:<id>:beat t EX 600 (and the longer-lived :seen). True when written."""
    t = time.time() if t is None else t
    return bool(_do(lambda r: r.pipeline([("SET", f"agent:{agent_id}:beat", f"{t:.3f}", "EX", BEAT_TTL),
                                          ("SET", f"agent:{agent_id}:seen", f"{t:.3f}", "EX", SEEN_TTL)]),
                    timeout))


def last_beats(ids: list[str]) -> dict[str, float] | None:
    """{agent id: time of its last beat} for agents that ever beat (within SEEN_TTL); None when Redis is down."""
    ids = list(dict.fromkeys(i for i in ids if i))

    def get(r):
        r.call("PING")
        vals = r.call("MGET", *[f"agent:{i}:seen" for i in ids]) if ids else []
        out = {}
        for i, v in zip(ids, vals):
            with contextlib.suppress(TypeError, ValueError):
                out[i] = float(v)
        return out
    return _do(get)


# --- locks -----------------------------------------------------------------------------------------------------

def _pid_alive(pid) -> bool:
    try:
        os.kill(int(pid), 0)
    except (ProcessLookupError, ValueError, TypeError):
        return False
    except PermissionError:
        return True
    return True


def holder_set(name: str, info: dict, ttl: int):
    """Record this process as the holder of lock:<name> (SET NX EX). The caller already holds the flock of the
    lock; a key left by a holder whose process is gone is replaced, a live holder's key is never touched.
    Returns the token for holder_clear, or None when Redis is down or the key is not ours."""
    token = json.dumps({**info, "pid": os.getpid(), "since": round(time.time(), 1)}, sort_keys=True)

    def put(r):
        key = f"lock:{name}"
        if r.call("SET", key, token, "NX", "EX", int(ttl)) is not None:
            return token
        old = r.call("GET", key)
        try:
            pid = json.loads(old).get("pid") if old else None
        except (ValueError, AttributeError):
            pid = None
        if old is not None and _pid_alive(pid):
            return None
        # replace only the value we just read (compare-and-set), so a concurrent new holder is not overwritten
        script = ("if redis.call('get', KEYS[1]) == ARGV[1] or not redis.call('get', KEYS[1]) then "
                  "return redis.call('set', KEYS[1], ARGV[2], 'EX', ARGV[3]) else return false end")
        return token if r.call("EVAL", script, 1, key, old or "", token, int(ttl)) == "OK" else None
    return _do(put)


def holder_clear(name: str, token: str | None) -> None:
    """Delete lock:<name> only if it still holds our token (a later holder is never removed)."""
    if token:
        _do(lambda r: r.call("EVAL", RELEASE, 1, f"lock:{name}", token))


def holders(names: list[str]) -> dict[str, dict] | None:
    def get(r):
        vals = r.call("MGET", *[f"lock:{n}" for n in names])
        out = {}
        for n, v in zip(names, vals):
            if v:
                try:
                    out[n] = json.loads(v)
                except ValueError:
                    out[n] = {"raw": v}
        return out
    return _do(get) if names else {}


@contextlib.contextmanager
def lock(name: str, ttl: int = 3600, wait: float = 0.0, lock_dir: Path | None = None, info: dict | None = None):
    """Exclusive lock NAME. The flock on <lock_dir>/<name>.lock is the authority (the kernel drops it when the
    process dies, and it works with Redis down); while held, lock:<name> in Redis (SET NX EX) names the holder for
    status tools. Yields True when acquired, False when the wait ran out."""
    d = lock_dir or LOCK_DIR
    d.mkdir(parents=True, exist_ok=True)
    fd = os.open(d / f"{name}.lock", os.O_CREAT | os.O_RDWR, 0o600)
    deadline = time.monotonic() + wait
    try:
        while True:
            try:
                fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
                break
            except OSError:
                if time.monotonic() >= deadline:
                    yield False
                    return
                time.sleep(0.5)
        token = holder_set(name, info or {}, ttl)
        try:
            yield True
        finally:
            holder_clear(name, token)
    finally:
        os.close(fd)


# --- client launch permit (tools/development/client_permit.py) ----------------------------------------------------

PERMIT_KEY = "permit:client"


def permit_set(expiry: float) -> bool:
    secs = int(expiry - time.time())
    if secs <= 0:
        return bool(_do(lambda r: r.call("DEL", PERMIT_KEY) is not None))
    return _do(lambda r: r.call("SET", PERMIT_KEY, f"{expiry:.3f}", "EX", secs)) == "OK"


def permit_clear() -> bool:
    return _do(lambda r: r.call("DEL", PERMIT_KEY)) is not None


def permit_get():
    """(reachable, expiry or None)."""
    got = _do(lambda r: (True, r.call("GET", PERMIT_KEY)))
    if got is None:
        return False, None
    try:
        return True, float(got[1]) if got[1] is not None else None
    except ValueError:
        return True, None


def main(argv: list[str]) -> int:
    if argv[:1] == ["hold"] and len(argv) >= 2:
        name, rest = argv[1], argv[2:]
        ttl, wait = 3600, 0.0
        while rest and rest[0] != "--":
            flag, val, rest = rest[0], rest[1], rest[2:]
            ttl, wait = (int(val), wait) if flag == "--ttl" else (ttl, float(val))
        cmd = rest[1:]
        if not cmd:
            print("usage: redis_kv.py hold NAME [--ttl S] [--wait S] -- CMD...")
            return 2
        with lock(name, ttl, wait, info={"cmd": " ".join(cmd)[:120]}) as ok:
            if not ok:
                print(f"redis_kv: lock {name} is held by {(holders([name]) or {}).get(name, 'another process')}")
                return 75
            return subprocess.run(cmd).returncode
    r = connect()
    if r is None:
        print("redis: unavailable (files and flock in use)")
        return 0
    _close(r)
    print("redis: up")
    for n, h in (holders(["land", "test", "godot"]) or {}).items():
        print(f"  lock {n}: {h}")
    up, exp = permit_get()
    print(f"  permit: {f'{max(0, exp - time.time()) / 60:.0f} min left' if exp else 'none'}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
