"""Context package (§5, §7): only the contract's context files plus matching lessons."""

from . import paths
from .config import CHARS_PER_TOKEN, CONTEXT_EXCLUDE, LESSONS_DIR, REPO_ROOT
from .contract import sha256_file


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


def build_package(contract, repo_root=REPO_ROOT, lessons_dir=LESSONS_DIR):
    """Returns (text, manifest). Raises ContextError on excluded, changed or escaping files."""
    root = repo_root.resolve()
    parts, manifest = [], []
    for item in contract["context_files"]:
        rel = paths.normalize(item["path"])
        if rel is None or excluded(rel):
            raise ContextError(f"context file not allowed: {item['path']!r}")
        f = (root / rel).resolve()
        if root not in f.parents:
            raise ContextError(f"context file escapes repository: {rel}")
        digest = sha256_file(f)
        if digest != item["sha256"]:
            raise ContextError(f"context file changed since contract was written: {rel}")
        parts.append(f"=== FILE {rel} (sha256 {digest[:12]}) ===\n{f.read_text(encoding='utf-8')}")
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
