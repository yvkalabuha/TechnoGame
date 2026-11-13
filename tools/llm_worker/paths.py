"""Repository-relative path rules: glob matching with '**' and write/read safety."""

import re
from functools import lru_cache
from pathlib import PurePosixPath


@lru_cache(maxsize=512)
def _glob_regex(pattern):
    out, i = [], 0
    while i < len(pattern):
        if pattern.startswith("**/", i):
            out.append("(?:.*/)?")
            i += 3
        elif pattern.startswith("**", i):
            out.append(".*")
            i += 2
        elif pattern[i] == "*":
            out.append("[^/]*")
            i += 1
        elif pattern[i] == "?":
            out.append("[^/]")
            i += 1
        else:
            out.append(re.escape(pattern[i]))
            i += 1
    return re.compile("".join(out) + r"\Z")


def matches(path, patterns):
    return any(_glob_regex(p).match(path) for p in patterns)


def normalize(path):
    """Returns a clean relative POSIX path, or None if it is absolute or escapes the root."""
    if not isinstance(path, str) or not path or "\\" in path or "\x00" in path:
        return None
    p = PurePosixPath(path)
    if p.is_absolute() or path.startswith("~"):
        return None
    parts = [x for x in p.parts if x not in ("", ".")]
    if not parts or any(x == ".." for x in parts):
        return None
    return "/".join(parts)


def write_allowed(path, allowed, forbidden, exists=False):
    """(ok, reason). A path must match allowed and match neither the contract nor the global deny list.
    Case-insensitive, like the risk classifier. A NEW file (exists=False) under an R1_if_added glob
    may be created even where R2 globs forbid writes (decision #179); an existing one may not."""
    from .config import WRITE_FORBIDDEN, WRITE_NEVER, WRITE_NEW_ALLOWED, WRITE_NEW_EXCEPT

    norm = normalize(path)
    if norm is None:
        return False, f"invalid path {path!r}"
    low = norm.lower()
    lows = lambda gs: [g.lower() for g in gs]
    new_ok = (not exists and matches(low, lows(WRITE_NEW_ALLOWED)) and not matches(low, lows(WRITE_NEW_EXCEPT))
              and not matches(low, lows(WRITE_NEVER)))
    if matches(low, lows(forbidden)) or (not new_ok and matches(low, lows(WRITE_FORBIDDEN))):
        return False, f"forbidden path {norm}"
    if not matches(low, lows(allowed)):
        return False, f"outside allowed_paths: {norm}"
    return True, norm
