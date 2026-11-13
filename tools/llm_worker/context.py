"""Context package (§5, §7): only the contract's context files plus matching lessons.

Files are read from git objects of `head`, never from the working tree: an untracked or locally edited
file (lab/results, a stray secret) cannot reach the package (ADR-0015 §3a)."""

import hashlib
import subprocess

from . import paths
from .config import CHARS_PER_TOKEN, CONTEXT_EXCLUDE, LESSONS_DIR, REPO_ROOT


class ContextError(ValueError):
    pass


def excluded(rel_path):
    return paths.matches(rel_path, CONTEXT_EXCLUDE)


def estimate_tokens(text):
    return int(len(text) / CHARS_PER_TOKEN) + 1


def select_lessons(task_type, lessons_dir=LESSONS_DIR):
    """Lessons are Markdown files whose first line is 'applies_to: A1, B1'. Sorted by name."""
    selected = []
    if not lessons_dir.is_dir():
        return selected
    for f in sorted(lessons_dir.glob("*.md")):
        lines = f.read_text(encoding="utf-8").splitlines()
        if not lines or not lines[0].startswith("applies_to:"):
            continue
        types = [t.strip() for t in lines[0].split(":", 1)[1].split(",")]
        if task_type in types:
            selected.append((f.name, "\n".join(lines[1:]).strip()))
    return selected


def read_blob(root, head, rel):
    """Bytes of a regular file `rel` in commit `head`. Symlinks, submodules and missing paths are errors."""
    def git(*args):
        return subprocess.run(["git", *args], cwd=root, capture_output=True)
    ls = git("ls-tree", "-z", head, "--", rel)
    entry = ls.stdout.split(b"\0")[0]
    if ls.returncode != 0 or not entry:
        raise ContextError(f"context file is not in git {head}: {rel}")
    meta, _, name = entry.partition(b"\t")
    mode, kind, oid = meta.decode().split()
    if kind != "blob" or mode not in ("100644", "100755") or name.decode() != rel:
        raise ContextError(f"context file is not a regular tracked file: {rel}")
    blob = git("cat-file", "blob", oid)
    if blob.returncode != 0:
        raise ContextError(f"cannot read {rel} from git {head}")
    return blob.stdout


def build_package(contract, repo_root=REPO_ROOT, lessons_dir=LESSONS_DIR, head="HEAD"):
    """Returns (text, manifest). Raises ContextError on excluded, changed or untracked files."""
    root = repo_root.resolve()
    parts, manifest = [], []
    for item in contract["context_files"]:
        rel = paths.normalize(item["path"])
        if rel is None or excluded(rel):
            raise ContextError(f"context file not allowed: {item['path']!r}")
        data = read_blob(root, head, rel)
        digest = hashlib.sha256(data).hexdigest()
        if digest != item["sha256"]:
            raise ContextError(f"context file changed since contract was written: {rel}")
        parts.append(f"=== FILE {rel} (sha256 {digest[:12]}) ===\n{data.decode('utf-8')}")
        manifest.append({"path": rel, "sha256": digest})
    lessons = select_lessons(contract["type"], lessons_dir)
    for name, body in lessons:
        parts.append(f"=== LESSON {name} ===\n{body}")
        manifest.append({"lesson": name})
    text = "\n\n".join(parts)
    tokens = estimate_tokens(text)
    if tokens > contract["budget"]["max_context_tokens"]:
        raise ContextError(f"context package ~{tokens} tokens exceeds max_context_tokens")
    return text, manifest
