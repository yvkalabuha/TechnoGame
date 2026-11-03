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


def write_allowed(path, allowed, forbidden):
    """(ok, reason). A path must match allowed and match neither the contract nor the global deny list."""
    from .config import WRITE_FORBIDDEN

    norm = normalize(path)
    if norm is None:
        return False, f"invalid path {path!r}"
    if matches(norm, forbidden) or matches(norm, WRITE_FORBIDDEN):
        return False, f"forbidden path {norm}"
    if not matches(norm, allowed):
        return False, f"outside allowed_paths: {norm}"
    return True, norm
