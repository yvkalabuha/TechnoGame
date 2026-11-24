"""Gemini as a second opinion on UI frames (TASK-0135, #441). Free tier only: no money ledger, but a request
counter per minute and per day that refuses when exceeded. The verdict stays with ui-art-director; this
only writes findings. Frames come from shots-out/ only and are sent as data URIs."""

import base64
import fcntl
import hashlib
import json
import os
import re
import struct
import time
from datetime import datetime, timezone
from pathlib import Path

from . import config, gitleaks, review
from .provider import KeyFileError, OpenAICompatProvider

SCHEMAS = {"criteria": config.PACKAGE_DIR / "schemas" / "visual.schema.json",
           "design": config.PACKAGE_DIR / "schemas" / "visual-design.schema.json"}
MODES = tuple(SCHEMAS)
DIRECTION = "docs/ui/VISUAL_DIRECTION_V06.md"
SYSTEM_DESIGN = (
    "Also act as a design reviewer. The visual direction document below is the intended direction. Besides "
    "findings, give your own proposals: each has a title, what to change, why, the frames it concerns, "
    "an effort estimate (S, M or L) and the risk it carries for the clarity and accessibility criteria "
    "(say 'none' when there is none). Proposals must keep the direction and must not weaken C1-C8, A1-A12."
)
TASK_RE = re.compile(r"\ATASK-\d{4}\Z")
DEFAULT_CRITERIA = "docs/ui/UI_DESIGN_SYSTEM.md#12"
MIN_SIZE = (1920, 1080)
PNG = b"\x89PNG\r\n\x1a\n"
SYSTEM = (
    "You are an independent reviewer of game UI screenshots (1920x1080, 100 % scale). Judge each frame against "
    "the criteria below (C1-C8 clarity, A1-A12 accessibility). Do not pass or fail the task: give your opinion "
    "(pass, fail or mixed) and concrete findings. Each finding names the criterion id, the frame name exactly as given, "
    "the place on the frame, a severity and a fix. Report only what you can see; do not invent defects."
)


class VisualError(RuntimeError):
    """code, then detail: bad_task, bad_frame, bad_criteria, secret_detected, sandbox_unavailable,
    schema_invalid, rate_limit."""

    def __init__(self, code, detail=""):
        super().__init__(f"{code}: {detail}" if detail else code)
        self.code = code


TOKEN_KEYS = ("prompt", "completion", "thinking")


def usage_tokens(usage, thinking=None):
    """{prompt, completion, thinking} from an OpenAI-style usage dict; what the provider did not report is 0.
    Completion is as reported (it may already include thinking: not verified)."""
    def num(v):
        return v if type(v) is int and v >= 0 else 0
    u = usage if isinstance(usage, dict) else {}
    details = u.get("completion_tokens_details")
    t = thinking if thinking is not None else (details.get("reasoning_tokens") if isinstance(details, dict) else 0)
    return {"prompt": num(u.get("prompt_tokens")), "completion": num(u.get("completion_tokens")), "thinking": num(t)}


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
                recent = [t for t in st["recent"] if now - t < 60]  # a new day must not clear the minute window
                same = st["day"] == day
                count = int(st["count"]) if same else 0
                tokens = {k: int(st["tokens"][k]) for k in TOKEN_KEYS} if same else dict.fromkeys(TOKEN_KEYS, 0)
            except FileNotFoundError:
                recent, count, tokens = [], 0, dict.fromkeys(TOKEN_KEYS, 0)
            except (OSError, ValueError, KeyError, TypeError):
                # An unreadable counter must not read as "zero requests": the quota could be burnt silently.
                raise RateLimited("usage file unreadable") from None
            if count >= self.rpd:
                raise RateLimited(f"{count} requests today, limit {self.rpd}")
            if len(recent) >= self.rpm:
                raise RateLimited(f"{len(recent)} requests in the last minute, limit {self.rpm}")
            recent.append(now)
            self._save({"day": day, "count": count + 1, "recent": recent, "tokens": tokens})
        return "free"

    def _save(self, st):
        tmp = self.path.with_suffix(".tmp")
        tmp.write_text(json.dumps(st), encoding="utf-8")
        os.replace(tmp, self.path)

    def settle(self, rid, usage):
        """Adds the reported token usage to the daily totals. No price table: the tool cannot see billing."""
        add = usage_tokens(usage)
        fd = os.open(self.path.with_suffix(".lock"), os.O_CREAT | os.O_WRONLY, 0o600)
        with os.fdopen(fd, "w") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            try:
                st = json.loads(self.path.read_text(encoding="utf-8"))
                st["tokens"] = {k: int(st["tokens"][k]) + add[k] for k in TOKEN_KEYS}
            except (OSError, ValueError, KeyError, TypeError):
                return  # the next before_attempt refuses on a broken file
            self._save(st)

    def release(self, rid):
        pass  # a 4xx / 429 attempt stays counted


# Gemini thinking tokens count against max_tokens: 8000 cut the first live answer at 317 output tokens
# (2026-11-24, gemini-flash-latest, 3 frames, ~7.7K thinking tokens). Free tier: no spend.
VISUAL_MAX_TOKENS = 32768

def image_size(data):
    """(width, height) from a PNG IHDR or a JPEG SOF marker; None when the header is unusable."""
    if data.startswith(PNG):
        return struct.unpack(">II", data[16:24]) if len(data) >= 24 and data[12:16] == b"IHDR" else None
    i = 2
    while i + 9 < len(data) and data[i] == 0xFF:
        marker, length = data[i + 1], struct.unpack(">H", data[i + 2:i + 4])[0]
        if marker in range(0xC0, 0xD0) and marker not in (0xC4, 0xC8, 0xCC):
            h, w = struct.unpack(">HH", data[i + 5:i + 9])
            return w, h
        i += 2 + length
    return None


def default_roots():
    """Resolved frame roots. A root that is itself a symlink authorizes nothing; the client root must stay in the repository."""
    roots = []
    for r, inside_repo in ((Path(config.SHOTS_DIR), False), (Path(config.CLIENT_SHOTS_DIR), True)):
        real = r.resolve()
        # a symlinked ancestor must not redirect the client root: it has to resolve to the expected place
        if r.is_symlink() or (inside_repo and real != Path(config.REPO_ROOT).resolve() / config.CLIENT_SHOTS_REL):
            continue
        roots.append(real)
    return roots


def frame_name(p):
    """Repo-relative path, so frames of the two roots with the same file name stay distinct; the file name outside the repo."""
    try:
        return p.relative_to(Path(config.REPO_ROOT).resolve()).as_posix()
    except ValueError:
        return p.name


def load_frames(frames, shots_dir=None, allow_small=False):
    """[(name, mime, bytes)]. Only PNG or JPEG files inside shots_dir (symlinks resolved), size-limited."""
    if not frames:
        raise VisualError("bad_frame", "no frames")
    if len(frames) > config.MAX_FRAMES:
        raise VisualError("bad_frame", f"more than {config.MAX_FRAMES} frames")
    roots = [Path(shots_dir).resolve()] if shots_dir else default_roots()
    out = []
    for f in frames:
        p = Path(f).resolve()
        if not any(r in p.parents for r in roots):
            raise VisualError("bad_frame", f"{f}: only files under shots-out/ may be sent")
        try:
            size = p.stat().st_size
            if size > config.MAX_FRAME_BYTES:
                raise VisualError("bad_frame", f"{f}: larger than {config.MAX_FRAME_BYTES} bytes")
            data = p.read_bytes()
        except OSError as e:
            raise VisualError("bad_frame", f"{f}: {e.strerror}") from None
        mime = "image/png" if data.startswith(PNG) else "image/jpeg" if data.startswith(b"\xff\xd8\xff") else None
        if mime is None:
            raise VisualError("bad_frame", f"{f}: not a PNG or JPEG")
        wh = image_size(data)
        if wh is None:
            raise VisualError("bad_frame", f"{f}: cannot read the image size")
        if not allow_small and (wh[0] < MIN_SIZE[0] or wh[1] < MIN_SIZE[1]):
            raise VisualError("bad_frame", f"{f}: {wh[0]}x{wh[1]} is smaller than {MIN_SIZE[0]}x{MIN_SIZE[1]} "
                                           "(use --allow-small to send it anyway)")
        out.append((frame_name(p), mime, data))
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


def build_messages(task, criteria, frames, direction=None):
    """OpenAI-style messages: one user message with the text and every frame as an image_url data URI."""
    parts = [{"type": "text", "text": f"Task: {task}\nFrames, in order: {', '.join(n for n, _, _ in frames)}\n\n"
                                      f"Criteria:\n{criteria}"}]
    for name, mime, data in frames:
        parts.append({"type": "text", "text": f"Frame {name}:"})
        parts.append({"type": "image_url", "image_url": {"url": f"data:{mime};base64,{base64.b64encode(data).decode()}"}})
    system = SYSTEM
    if direction is not None:
        system += "\n\n" + SYSTEM_DESIGN
        parts[0]["text"] += f"\n\nVisual direction:\n{direction}"
    return [{"role": "system", "content": system}, {"role": "user", "content": parts}]


def run_visual(task, frames, criteria_spec=DEFAULT_CRITERIA, runs_dir=config.RUNS_DIR, shots_dir=None,
               model=None, make_provider=None, dry_run=False, root=config.REPO_ROOT, mode="criteria",
               allow_small=False):
    """One request. Returns (json path or None, markdown path or None, record)."""
    if not TASK_RE.match(task):
        raise VisualError("bad_task", task)
    if mode not in SCHEMAS:
        raise VisualError("bad_mode", mode)
    schema_path = SCHEMAS[mode]
    loaded = load_frames(frames, shots_dir, allow_small)
    criteria = load_criteria(criteria_spec, root)
    direction = load_criteria(DIRECTION, root) if mode == "design" else None
    try:
        gitleaks.scan_text(SYSTEM + "\n" + SYSTEM_DESIGN + "\n" + criteria + "\n" + (direction or ""))
    except gitleaks.SecretDetected as e:
        raise VisualError("secret_detected", str(e)) from None
    except gitleaks.GitleaksUnavailable as e:
        raise VisualError("sandbox_unavailable", str(e)) from None
    profile = config.PROFILES["gemini-flash"]
    model = model or profile.model
    digest = hashlib.sha256(b"\0".join([model.encode(), mode.encode(), criteria.encode(), (direction or "").encode()]
                                    + [n.encode() + b"\0" + d for n, _, d in loaded])).hexdigest()
    record = {"kind": "visual", "task": task, "mode": mode, "provider": profile.name, "model": model, "criteria": criteria_spec,
              "frames": [{"name": n, "bytes": len(d), "size": "%dx%d" % image_size(d)} for n, _, d in loaded],
              "input_hash": digest}
    messages = build_messages(task, criteria, loaded, direction)
    size = len(json.dumps(messages).encode())  # base64 data URIs and text, as sent
    record["request_bytes"] = size
    if size > config.MAX_REQUEST_BYTES:
        raise VisualError("bad_frame", f"encoded request is {size} bytes, limit {config.MAX_REQUEST_BYTES}")
    if dry_run:
        return None, None, record
    if make_provider is None:
        def make_provider():
            guard = RateGuard(config.LEDGER_DIR / config.GEMINI_USAGE_FILE)
            return OpenAICompatProvider(profile=profile, model=model, guard=guard, timeout_s=300)
    provider = make_provider()  # KeyFileError here: nothing has been sent
    schema = json.loads(schema_path.read_text(encoding="utf-8"))
    try:
        comp = provider.complete(messages, schema, "visual", VISUAL_MAX_TOKENS)
    except gitleaks.SecretDetected as e:
        raise VisualError("secret_detected", str(e)) from None
    except gitleaks.GitleaksUnavailable as e:
        raise VisualError("sandbox_unavailable", str(e)) from None
    stem = f"visual-{task}-{digest[:8]}-gemini"
    record["usage"] = comp.usage
    record["tokens"] = usage_tokens(comp.usage, comp.reasoning_tokens)  # also in gemini-usage.json (daily totals)
    record["response_sha256"] = hashlib.sha256(comp.content.encode()).hexdigest()
    try:
        data = review.parse_answer(comp.content, schema_path, "visual")
    except review.ReviewError as e:
        review.write_run(runs_dir, stem + "-invalid.json", {**record, "error": str(e), "raw": comp.content})
        raise VisualError("schema_invalid", str(e)) from None
    mark_unverified(data, criteria, [n for n, _, _ in loaded])
    record["result"] = data
    path, md = write_pair(runs_dir, stem, record)
    return path, md, record


def mark_unverified(data, criteria, frame_names):
    """Flags findings that cite a criterion id absent from the criteria text or a frame that was not sent.
    The model's text is kept: the reader decides."""
    ids = set(re.findall(r"\b[A-Z]\d{1,2}\b", criteria))
    for f in data["findings"]:
        bad = []
        if ids and not (set(re.findall(r"\b[A-Z]\d{1,2}\b", f["criterion"])) & ids):
            bad.append("criterion")
        if f["frame"] not in frame_names:
            bad.append("frame")
        if bad:
            f["unverified"] = bad


def write_pair(runs_dir, stem, record, summary=None):
    """JSON and Markdown from the same response: both go to temp files first, then are renamed together."""
    runs_dir.mkdir(parents=True, exist_ok=True)
    path, md = runs_dir / (stem + ".json"), runs_dir / (stem + ".md")
    # Concurrent runs with the same input: one writer at a time, so the pair always comes from one response.
    with open(runs_dir / ".visual.lock", "a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        tj, tm = path.with_suffix(".json.tmp"), md.with_suffix(".md.tmp")
        tj.write_text(json.dumps(record, indent=2, sort_keys=True, ensure_ascii=False) + "\n", encoding="utf-8")
        tm.write_text((summary or summary_md)(record), encoding="utf-8")
        os.replace(tm, md)
        os.replace(tj, path)
    return path, md


def summary_md(rec):
    r = rec["result"]
    lines = [f"# Gemini visual opinion: {rec['task']}", "",
             f"Model {rec['model']}, frames: {', '.join(f['name'] for f in rec['frames'])}.",
             "Second opinion only: the PASS/FAIL verdict belongs to ui-art-director.", "",
             f"Response sha256: {rec['response_sha256']}", "",
             f"Tokens (prompt / completion / thinking): {rec['tokens']['prompt']} / {rec['tokens']['completion']} / "
             f"{rec['tokens']['thinking']}", "",
             f"Opinion: **{r['opinion']}**", "", r["summary"], ""]
    if r["findings"]:
        lines += ["| Severity | Criterion | Frame | Place | Fix |", "|---|---|---|---|---|"]
        for f in r["findings"]:
            cells = [f[k].replace("|", "/").replace("\n", " ") for k in ("severity", "criterion", "frame", "location", "fix")]
            if f.get("unverified"):
                cells[0] += " (unverified: " + ", ".join(f["unverified"]) + ")"
            lines.append("| " + " | ".join(cells) + " |")
    else:
        lines.append("No findings.")
    if r.get("proposals"):
        lines += ["", "## Proposals", ""]
        for k, p in enumerate(r["proposals"], 1):
            lines += [f"{k}. **{p['title']}** (effort {p['effort']}; frames: {', '.join(p['frames'])})",
                      f"   - Change: {p['change']}", f"   - Why: {p['why']}", f"   - Risk to clarity: {p['clarity_risk']}"]
    return "\n".join(lines) + "\n"
