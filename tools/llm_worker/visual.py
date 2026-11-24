"""Gemini as a second opinion on UI frames (TASK-0135, #441). Free tier only: no money ledger, but a request
counter per minute and per day that refuses when exceeded. The verdict stays with ui-art-director; this
only writes findings. Frames come from shots-out/ only and are sent as data URIs."""

import base64
import fcntl
import hashlib
import json
import os
import re
import time
from datetime import datetime, timezone
from pathlib import Path

from . import config, gitleaks, review
from .provider import KeyFileError, OpenAICompatProvider

SCHEMA = config.PACKAGE_DIR / "schemas" / "visual.schema.json"
TASK_RE = re.compile(r"\ATASK-\d{4}\Z")
DEFAULT_CRITERIA = "docs/ui/UI_DESIGN_SYSTEM.md#12"
PNG = b"\x89PNG\r\n\x1a\n"
SYSTEM = (
    "You are an independent reviewer of game UI screenshots (1920x1080, 100 % scale). Judge each frame against "
    "the criteria below (C1-C8 clarity, A1-A12 accessibility). Do not pass or fail the task: give your opinion "
    "(pass, fail or mixed) and concrete findings. Each finding names the criterion id, the frame file name, "
    "the place on the frame, a severity and a fix. Report only what you can see; do not invent defects."
)


class VisualError(RuntimeError):
    """code, then detail: bad_task, bad_frame, bad_criteria, secret_detected, sandbox_unavailable,
    schema_invalid, rate_limit."""

    def __init__(self, code, detail=""):
        super().__init__(f"{code}: {detail}" if detail else code)
        self.code = code


class RateLimited(VisualError):
    def __init__(self, detail):
        super().__init__("rate_limit", detail)


class RateGuard:
    """Guard of a free provider: counts HTTP attempts per minute and per UTC day in a small file under flock.
    A refused or failed attempt still counts (the provider may have counted it too). No money is booked."""

    def __init__(self, path, rpm=config.GEMINI_RPM, rpd=config.GEMINI_RPD, clock=time.time):
        self.path, self.rpm, self.rpd, self.clock = Path(path), rpm, rpd, clock

    def before_attempt(self, request_bytes, max_tokens):
        self.path.parent.mkdir(parents=True, exist_ok=True)
        fd = os.open(self.path.with_suffix(".lock"), os.O_CREAT | os.O_WRONLY, 0o600)
        with os.fdopen(fd, "w") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            now = self.clock()
            day = datetime.fromtimestamp(now, timezone.utc).strftime("%Y-%m-%d")
            try:
                st = json.loads(self.path.read_text(encoding="utf-8"))
                if st["day"] != day:
                    raise KeyError
                recent = [t for t in st["recent"] if now - t < 60]
                count = int(st["count"])
            except (OSError, ValueError, KeyError, TypeError):
                recent, count = [], 0
            if count >= self.rpd:
                raise RateLimited(f"{count} requests today, limit {self.rpd}")
            if len(recent) >= self.rpm:
                raise RateLimited(f"{len(recent)} requests in the last minute, limit {self.rpm}")
            recent.append(now)
            tmp = self.path.with_suffix(".tmp")
            tmp.write_text(json.dumps({"day": day, "count": count + 1, "recent": recent, "usd": "0"}), encoding="utf-8")
            os.replace(tmp, self.path)
        return "free"

    def settle(self, rid, usage):
        pass

    def release(self, rid):
        pass  # a 4xx / 429 attempt stays counted


def load_frames(frames, shots_dir=None):
    """[(name, mime, bytes)]. Only PNG or JPEG files inside shots_dir (symlinks resolved), size-limited."""
    if not frames:
        raise VisualError("bad_frame", "no frames")
    if len(frames) > config.MAX_FRAMES:
        raise VisualError("bad_frame", f"more than {config.MAX_FRAMES} frames")
    root = Path(shots_dir or config.SHOTS_DIR).resolve()
    out, total = [], 0
    for f in frames:
        p = Path(f).resolve()
        if root not in p.parents:
            raise VisualError("bad_frame", f"{f}: only files under shots-out/ may be sent")
        try:
            data = p.read_bytes()
        except OSError as e:
            raise VisualError("bad_frame", f"{f}: {e.strerror}") from None
        mime = "image/png" if data.startswith(PNG) else "image/jpeg" if data.startswith(b"\xff\xd8\xff") else None
        if mime is None:
            raise VisualError("bad_frame", f"{f}: not a PNG or JPEG")
        if len(data) > config.MAX_FRAME_BYTES:
            raise VisualError("bad_frame", f"{f}: larger than {config.MAX_FRAME_BYTES} bytes")
        total += len(data)
        out.append((p.name, mime, data))
    if total > config.MAX_REQUEST_BYTES:
        raise VisualError("bad_frame", f"frames total {total} bytes, limit {config.MAX_REQUEST_BYTES}")
    return out


def load_criteria(spec, root=config.REPO_ROOT):
    """'path#N': the section '## N.' of the Markdown file; without '#N' the whole file."""
    path, _, frag = spec.partition("#")
    p = (Path(root) / path).resolve()
    if Path(root).resolve() not in p.parents or not p.is_file():
        raise VisualError("bad_criteria", f"{path}: not a file of the repository")
    text = p.read_text(encoding="utf-8")
    if not frag:
        return text
    m = re.search(rf"^## {re.escape(frag)}\. .*?(?=^## |\Z)", text, re.S | re.M)
    if not m:
        raise VisualError("bad_criteria", f"{spec}: no section '## {frag}.'")
    return m.group(0)


def build_messages(task, criteria, frames):
    """OpenAI-style messages: one user message with the text and every frame as an image_url data URI."""
    parts = [{"type": "text", "text": f"Task: {task}\nFrames, in order: {', '.join(n for n, _, _ in frames)}\n\n"
                                      f"Criteria:\n{criteria}"}]
    for name, mime, data in frames:
        parts.append({"type": "text", "text": f"Frame {name}:"})
        parts.append({"type": "image_url", "image_url": {"url": f"data:{mime};base64,{base64.b64encode(data).decode()}"}})
    return [{"role": "system", "content": SYSTEM}, {"role": "user", "content": parts}]


def run_visual(task, frames, criteria_spec=DEFAULT_CRITERIA, runs_dir=config.RUNS_DIR, shots_dir=None,
               model=None, make_provider=None, dry_run=False, root=config.REPO_ROOT):
    """One request. Returns (json path or None, markdown path or None, record)."""
    if not TASK_RE.match(task):
        raise VisualError("bad_task", task)
    loaded = load_frames(frames, shots_dir)
    criteria = load_criteria(criteria_spec, root)
    try:
        gitleaks.scan_text(SYSTEM + "\n" + criteria)
    except gitleaks.SecretDetected as e:
        raise VisualError("secret_detected", str(e)) from None
    except gitleaks.GitleaksUnavailable as e:
        raise VisualError("sandbox_unavailable", str(e)) from None
    profile = config.PROFILES["gemini-flash"]
    model = model or profile.model
    digest = hashlib.sha256(b"\0".join([model.encode(), criteria.encode()] + [d for _, _, d in loaded])).hexdigest()
    record = {"kind": "visual", "task": task, "provider": profile.name, "model": model, "criteria": criteria_spec,
              "frames": [{"name": n, "bytes": len(d)} for n, _, d in loaded], "input_hash": digest}
    if dry_run:
        return None, None, record
    if make_provider is None:
        def make_provider():
            guard = RateGuard(config.LEDGER_DIR / config.GEMINI_USAGE_FILE)
            return OpenAICompatProvider(profile=profile, model=model, guard=guard, timeout_s=300)
    provider = make_provider()  # KeyFileError here: nothing has been sent
    schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
    comp = provider.complete(build_messages(task, criteria, loaded), schema, "visual", 8000)
    stem = f"visual-{task}-{digest[:8]}-gemini"
    record["usage"] = comp.usage
    try:
        data = review.parse_answer(comp.content, SCHEMA, "visual")
    except review.ReviewError as e:
        review.write_run(runs_dir, stem + "-invalid.json", {**record, "error": str(e), "raw": comp.content})
        raise VisualError("schema_invalid", str(e)) from None
    record["result"] = data
    path, _ = review.write_run(runs_dir, stem + ".json", record)
    md = path.with_suffix(".md")
    md.write_text(summary_md(record), encoding="utf-8")
    return path, md, record


def summary_md(rec):
    r = rec["result"]
    lines = [f"# Gemini visual opinion: {rec['task']}", "",
             f"Model {rec['model']}, frames: {', '.join(f['name'] for f in rec['frames'])}.",
             "Second opinion only: the PASS/FAIL verdict belongs to ui-art-director.", "",
             f"Opinion: **{r['opinion']}**", "", r["summary"], ""]
    if r["findings"]:
        lines += ["| Severity | Criterion | Frame | Place | Fix |", "|---|---|---|---|---|"]
        for f in r["findings"]:
            cells = [f[k].replace("|", "/").replace("\n", " ") for k in ("severity", "criterion", "frame", "location", "fix")]
            lines.append("| " + " | ".join(cells) + " |")
    else:
        lines.append("No findings.")
    return "\n".join(lines) + "\n"
