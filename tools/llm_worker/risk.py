"""Risk classes R0-R3 by path (ADR-0015 section 1, decision #179). The class of a diff only goes up.

    python3 -m tools.llm_worker.risk classify --range main...llm/<id> [--max R1] [--paths-file risk_paths.json]

Depends only on paths.py and risk_paths.json, so CI can run copies taken from main.
"""

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

from . import paths

RISK_FILE = Path(__file__).resolve().parent / "risk_paths.json"
CLASSES = ("R0", "R1", "R2", "R3")
APPROVAL_FIELDS = ("status", "approved_by", "approved_at", "approval_ref")
UNKNOWN = "R2"
SPECIAL_MODES = ("120000", "160000")  # symlink, submodule


def load_classes(path=None):
    data = json.loads(Path(path or RISK_FILE).read_text(encoding="utf-8"))
    low = lambda gs: [g.lower() for g in gs]
    out = {c: low(data[c]) for c in CLASSES}
    out["R1_if_added"] = low(data["R1_if_added"])
    out["R1_if_added_except"] = low(data["R1_if_added_except"])
    return out


def combine(*risks):
    """The higher class; a class is never lowered."""
    return max(risks, key=CLASSES.index)


def classify_path(path, classes=None, status=None):
    """Class of one path. Status 'A' (added) lowers R2 to R1 under R1_if_added; any other status or None does not."""
    classes = classes or load_classes()
    norm = paths.normalize(path)
    if norm is None:
        return "R3"  # absolute, '..' or otherwise outside the repository
    norm = norm.lower()
    for cls in reversed(CLASSES):
        if paths.matches(norm, classes[cls]):
            if (cls == "R2" and status == "A" and paths.matches(norm, classes["R1_if_added"])
                    and not paths.matches(norm, classes["R1_if_added_except"])):
                return "R1"
            return cls
    return UNKNOWN


def classify_paths(files, classes=None, status=None):
    classes = classes or load_classes()
    return combine("R0", *(classify_path(f, classes, status) for f in files))


def _literal_prefix(glob):
    m = re.search(r"[*?\[]", glob)
    return glob if m is None else glob[:m.start()]


def classify_glob(glob, classes=None):
    """Highest class a contract glob for NEW files may reach: the glob itself as a sample path, plus any
    R2/R3 glob whose literal directory prefix overlaps it (a broad `tests/**` may cover protected files)."""
    classes = classes or load_classes()
    g = (paths.normalize(glob) or "").lower()
    if not g:
        return "R3"
    cls = classify_path(g, classes, "A")
    for c in ("R2", "R3"):
        if c == "R2" and cls == "R1" and paths.matches(g, classes["R1_if_added"]):
            continue  # new test files under Llm/: exempt from the R2 overlap
        for pat in classes[c]:
            pre = _literal_prefix(pat)
            if pre and (g.startswith(pre) or pre.startswith(_literal_prefix(g))):
                cls = combine(cls, c)
    return cls


_FM_LINE = re.compile(r"""^\s*["']?([A-Za-z_][A-Za-z0-9_]*)["']?\s*:\s*(.*?)\s*$""")


class FrontmatterError(ValueError):
    pass


def _no_alias(value):
    """YAML anchors and aliases could make a value mean something else: not understood, so fail closed."""
    v = value.strip()
    v = v[1:].lstrip() if v.startswith("-") else v
    if v.startswith(("*", "&")):
        raise FrontmatterError("anchor or alias in an approval field")


def _frontmatter(text):
    """Approval fields of a frontmatter block. Raises FrontmatterError if the block cannot be parsed."""
    if text is None:
        return {}
    text = text.lstrip("﻿")
    if not text.startswith("---"):
        return {}
    out, closed, cur = {}, False, None
    for line in text.splitlines()[1:]:
        if line.strip() == "---":
            closed = True
            break
        if cur is not None and (not line.strip() or line[0] in " \t" or line.startswith("-")
                                or line.lstrip().startswith("#")):
            _no_alias(line)
            out[cur] += "\n" + line.rstrip()  # block scalar, list item or multi-line quote: part of the value
            continue
        if not line.strip() or line.lstrip().startswith("#") or line[0] in " \t" or line.startswith("-"):
            continue  # belongs to a field we do not track
        m = _FM_LINE.match(line)
        if not m:
            raise FrontmatterError(f"cannot parse line {line[:40]!r}")
        cur = m.group(1) if m.group(1) in APPROVAL_FIELDS else None
        if cur:
            _no_alias(m.group(2))
            out[cur] = m.group(2)  # raw: a "#" may sit inside quotes, so comments are not cut off
    if not closed:
        raise FrontmatterError("frontmatter is not closed")
    return {k: v.rstrip() for k, v in out.items()}


def approval_changed(old_text, new_text):
    """True if an approval field changed; an unparseable frontmatter counts as changed (fail closed)."""
    try:
        return _frontmatter(old_text) != _frontmatter(new_text)
    except FrontmatterError:
        return True


def _git(repo, *args):
    r = subprocess.run(["git", *args], cwd=repo, capture_output=True)
    if r.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} failed: {r.stderr.decode(errors='replace').strip()}")
    return r.stdout


def diff_entries(rev_range, repo=None):
    """[(status, path, old_mode, new_mode)] from `git diff --raw --no-renames -z`; both sides of a rename count."""
    raw = _git(repo or Path.cwd(), "diff", "--raw", "--no-renames", "-z", rev_range)
    parts = raw.decode("utf-8", "surrogateescape").split("\0")
    out, i = [], 0
    while i < len(parts) and parts[i].startswith(":"):
        old_mode, new_mode, _, _, status = parts[i][1:].split(" ")
        n = 2 if status[0] in "RC" else 1
        out += [(status[0], p, old_mode, new_mode) for p in parts[i + 1:i + 1 + n]]
        i += 1 + n
    return out


def _show(repo, rev, path):
    r = subprocess.run(["git", "show", f"{rev}:{path}"], cwd=repo, capture_output=True)
    return r.stdout.decode("utf-8", "replace") if r.returncode == 0 else None


def classify_range(rev_range, repo=None, classes=None):
    """(overall class, [(path, class, why)]) for `base...head`. Approval fields and symlinks/submodules raise the class."""
    repo = repo or Path.cwd()
    classes = classes or load_classes()
    if "..." not in rev_range:
        raise ValueError("range must be base...head (three dots)")
    base, head = rev_range.split("...", 1)
    merge_base = _git(repo, "merge-base", base, head).decode().strip()
    rows = []
    for status, p, old_mode, new_mode in diff_entries(rev_range, repo):
        cls, why = classify_path(p, classes, status), "path"
        if old_mode in SPECIAL_MODES or new_mode in SPECIAL_MODES:
            cls, why = combine(cls, "R2"), "symlink or submodule"
        if p.lower().endswith(".md") and approval_changed(_show(repo, merge_base, p), _show(repo, head, p)):
            cls, why = "R3", "approval fields in frontmatter"
        rows.append((p, cls, why))
    return combine("R0", *(r[1] for r in rows)), rows


def main(argv=None):
    ap = argparse.ArgumentParser(prog="python3 -m tools.llm_worker.risk")
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("classify")
    p.add_argument("--range", required=True, help="base...head")
    p.add_argument("--max", default="R1", choices=CLASSES, help="highest class allowed to merge")
    p.add_argument("--paths-file", type=Path, default=None,
                   help="risk_paths.json to use (CI passes the copy from main, not the branch's own)")
    a = ap.parse_args(argv)
    try:
        overall, rows = classify_range(a.range, classes=load_classes(a.paths_file))
    except (RuntimeError, ValueError, OSError, KeyError) as e:
        print(f"error: {e}", file=sys.stderr)
        return 2  # unclassifiable: no merge
    for path, cls, why in rows:
        print(f"{cls}\t{path}\t{why}")
    print(f"overall: {overall} (max allowed {a.max})")
    return 0 if CLASSES.index(overall) <= CLASSES.index(a.max) else 1


if __name__ == "__main__":
    sys.exit(main())
