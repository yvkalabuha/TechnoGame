"""Remembered reset time of the Codex usage limit (#456): until it, the second review goes straight to agy.

The state is one small JSON file under ~/.cache/singularity/. A missing, corrupt or expired file means "no limit"."""

import contextlib
import fcntl
import json
import os
import re
import tempfile
from datetime import datetime, timedelta, timezone
from pathlib import Path

STATE_DIR = Path.home() / ".cache" / "singularity"
STATE_FILE = "codex-limit.json"
LOCK_FILE = "codex-limit.lock"
DEFAULT_COOLDOWN = timedelta(minutes=30)  # when the message names no reset time
MAX_COOLDOWN = timedelta(hours=24)  # a longer wait in a message is not trusted
AT_RE = re.compile(r"try again at (\d{1,2}):(\d{2})\s*([AP]M)", re.I)
IN_RE = re.compile(r"try again in (?:(\d+)\s*h\w*)?\s*(?:(\d+)\s*m\w*)?", re.I)


def parse_reset(text, now):
    """Reset time (aware datetime) named by a Codex limit message, or None. `now` is aware; a clock time with no
    date means its next occurrence in now's time zone."""
    at = _parse(text, now)
    return min(at, now + MAX_COOLDOWN) if at else None


def _parse(text, now):
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


@contextlib.contextmanager
def _locked(state_dir):
    """Exclusive lock for every read-modify-write of the state: clear can never delete a record a concurrent run
    wrote between its read and its unlink."""
    state_dir = Path(state_dir)
    state_dir.mkdir(parents=True, exist_ok=True)
    fd = os.open(state_dir / LOCK_FILE, os.O_CREAT | os.O_RDWR, 0o600)
    try:
        fcntl.flock(fd, fcntl.LOCK_EX)
        yield
    finally:
        os.close(fd)  # closing releases the lock


def record_limit(reset_at, now, state_dir=STATE_DIR):
    """Remember the limit; reset_at None falls back to a default cooldown. Returns the stored reset time."""
    reset_at = min(reset_at or now + DEFAULT_COOLDOWN, now + MAX_COOLDOWN)
    state_dir = Path(state_dir)
    with _locked(state_dir):
        _write(reset_at, now, state_dir)
    return reset_at


def _write(reset_at, now, state_dir):
    fd, tmp = tempfile.mkstemp(dir=state_dir, prefix=STATE_FILE + ".")  # own file per writer, mode 0600
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            json.dump({"reset_at": reset_at.astimezone(timezone.utc).isoformat(timespec="seconds"),
                       "recorded_at": now.astimezone(timezone.utc).isoformat(timespec="seconds")}, f)
        os.replace(tmp, state_dir / STATE_FILE)  # atomic: a reader sees the old or the new file, never a mix
    except BaseException:
        Path(tmp).unlink(missing_ok=True)
        raise


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


def clear(before, state_dir=STATE_DIR):
    """Remove the state only if it was recorded before `before` (the start of the run that succeeded): a limit that a
    concurrent run recorded later must stay."""
    p = Path(state_dir) / STATE_FILE
    with _locked(state_dir):
        try:
            recorded = datetime.fromisoformat(json.loads(p.read_text(encoding="utf-8"))["recorded_at"])
            if recorded.tzinfo is not None and recorded >= before:
                return
        except (OSError, ValueError, KeyError, TypeError):
            pass  # missing or unreadable: nothing worth keeping
        try:
            p.unlink()
        except OSError:
            pass
