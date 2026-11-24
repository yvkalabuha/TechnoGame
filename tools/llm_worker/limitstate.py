"""Remembered reset time of the Codex usage limit (#456): until it, the second review goes straight to agy.

The state is one small JSON file under ~/.cache/singularity/. A missing, corrupt or expired file means "no limit"."""

import json
import os
import re
from datetime import datetime, timedelta, timezone
from pathlib import Path

STATE_DIR = Path.home() / ".cache" / "singularity"
STATE_FILE = "codex-limit.json"
DEFAULT_COOLDOWN = timedelta(minutes=30)  # when the message names no reset time
AT_RE = re.compile(r"try again at (\d{1,2}):(\d{2})\s*([AP]M)", re.I)
IN_RE = re.compile(r"try again in (?:(\d+)\s*h\w*)?\s*(?:(\d+)\s*m\w*)?", re.I)


def parse_reset(text, now):
    """Reset time (aware datetime) named by a Codex limit message, or None. `now` is aware; a clock time with no
    date means its next occurrence in now's time zone."""
    m = AT_RE.search(text or "")
    if m:
        hour = int(m.group(1)) % 12 + (12 if m.group(3).upper() == "PM" else 0)
        if int(m.group(1)) > 12 or int(m.group(2)) > 59:
            return None
        at = now.replace(hour=hour, minute=int(m.group(2)), second=0, microsecond=0)
        return at if at > now else at + timedelta(days=1)
    m = IN_RE.search(text or "")
    if m and (m.group(1) or m.group(2)):
        return now + timedelta(hours=int(m.group(1) or 0), minutes=int(m.group(2) or 0))
    return None


def record_limit(reset_at, now, state_dir=STATE_DIR):
    """Remember the limit; reset_at None falls back to a default cooldown. Returns the stored reset time."""
    reset_at = reset_at or now + DEFAULT_COOLDOWN
    state_dir = Path(state_dir)
    state_dir.mkdir(parents=True, exist_ok=True)
    tmp = state_dir / (STATE_FILE + ".tmp")
    fd = os.open(tmp, os.O_CREAT | os.O_WRONLY | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        json.dump({"reset_at": reset_at.astimezone(timezone.utc).isoformat(timespec="seconds"),
                   "recorded_at": now.astimezone(timezone.utc).isoformat(timespec="seconds")}, f)
    os.replace(tmp, state_dir / STATE_FILE)
    return reset_at


def limited_until(now, state_dir=STATE_DIR):
    """The remembered reset time while it is in the future, else None."""
    try:
        data = json.loads((Path(state_dir) / STATE_FILE).read_text(encoding="utf-8"))
        at = datetime.fromisoformat(data["reset_at"])
    except (OSError, ValueError, KeyError, TypeError):
        return None
    if at.tzinfo is None:
        return None
    return at if at > now else None


def clear(state_dir=STATE_DIR):
    try:
        (Path(state_dir) / STATE_FILE).unlink()
    except OSError:
        pass
