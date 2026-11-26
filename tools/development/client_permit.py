#!/usr/bin/env python3
"""Grant or revoke the permit for game client launches (hook guard_shots.py). Orchestrator only, after the owner allows.

Usage: client_permit.py grant [minutes=30] | revoke | status

The permit is the file ~/.cache/singularity/client-launch-ok (expiry, unix seconds) and, when Redis is up, the key
permit:client with the same expiry as its EX (owner #590). live() needs the file; when Redis answers it needs the key
too, so a revoke made while Redis was down is never undone by Redis, and Redis can only refuse, never allow.
Without Redis everything works on the file alone, as before.
"""
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import redis_kv  # noqa: E402

PERMIT = Path.home() / ".cache" / "singularity" / "client-launch-ok"


def file_expiry(path: Path | None = None) -> float | None:
    try:
        return float((path or PERMIT).read_text().strip())
    except (OSError, ValueError):
        return None


def live(path: Path | None = None, now: float | None = None) -> bool:
    now = time.time() if now is None else now
    exp = file_expiry(path)
    if exp is None or exp <= now:
        return False
    reachable, rexp = redis_kv.permit_get()
    return not reachable or (rexp is not None and rexp > now)


def main() -> None:
    cmd = sys.argv[1] if len(sys.argv) > 1 else "status"
    if cmd == "grant":
        minutes = float(sys.argv[2]) if len(sys.argv) > 2 else 30
        expiry = time.time() + minutes * 60
        PERMIT.parent.mkdir(parents=True, exist_ok=True)
        PERMIT.write_text(str(expiry))
        where = "file + redis" if redis_kv.permit_set(expiry) else "file"
        print(f"client launches allowed for {minutes:g} min ({where})")
    elif cmd == "revoke":
        PERMIT.unlink(missing_ok=True)
        redis_kv.permit_clear()
        print("client launches blocked")
    else:
        exp = file_expiry()
        if exp is None:
            print("permit: none")
        elif not live():
            print("permit: expired" if exp <= time.time() else "permit: refused (file live, no Redis key: grant again)")
        else:
            print(f"permit: {max(0, exp - time.time()) / 60:.0f} min left")


if __name__ == "__main__":
    main()
