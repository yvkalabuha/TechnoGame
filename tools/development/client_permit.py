#!/usr/bin/env python3
"""Grant or revoke the permit for game client launches (hook guard_shots.py). Orchestrator only, after the owner allows.

Usage: client_permit.py grant [minutes=30] | revoke | status
"""
import sys
import time
from pathlib import Path

PERMIT = Path.home() / ".cache" / "singularity" / "client-launch-ok"


def main() -> None:
    cmd = sys.argv[1] if len(sys.argv) > 1 else "status"
    if cmd == "grant":
        minutes = float(sys.argv[2]) if len(sys.argv) > 2 else 30
        PERMIT.parent.mkdir(parents=True, exist_ok=True)
        PERMIT.write_text(str(time.time() + minutes * 60))
        print(f"client launches allowed for {minutes:g} min")
    elif cmd == "revoke":
        PERMIT.unlink(missing_ok=True)
        print("client launches blocked")
    else:
        try:
            left = float(PERMIT.read_text()) - time.time()
            print(f"permit: {max(0, left) / 60:.0f} min left" if left > 0 else "permit: expired")
        except (OSError, ValueError):
            print("permit: none")


if __name__ == "__main__":
    main()
