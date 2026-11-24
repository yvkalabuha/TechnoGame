"""Design sketches from Gemini through the Antigravity CLI (TASK-0141, #451, ADR-0026).

`agy -p` runs in an empty temp workspace with --mode accept-edits and never --dangerously-skip-permissions, so the
agent's own shell commands stay denied. The agent saves images under ~/.gemini/antigravity-cli/brain/<conversation_id>/
(the image tool may run in a sub-agent with its own conversation id); this module reads the ids from the NDJSON
events and copies the new images into shots-out/sketches/<task>/. A sketch is a proposal, not an asset: ADR-0026
needs ui-art-director's approval before any of it goes into mods/core/."""

import hashlib
import json
import os
import re
import shutil
import signal
import subprocess
import tempfile
import time
import zlib
from datetime import datetime, timezone
from pathlib import Path

from . import config, gitleaks
from .visual import PNG, RateGuard, TASK_RE, VisualError, write_pair

AGY_RPM = 2
AGY_RPD = 20  # careful guess of the subscription's daily room, not a documented quota
AGY_USAGE_FILE = "agy-usage.json"  # in LEDGER_DIR
MAX_COUNT = 4
DEFAULT_TIMEOUT_S = 400
BRAIN_DIR = Path.home() / ".gemini" / "antigravity-cli" / "brain"
IMAGE_EXT = (".jpg", ".jpeg", ".png", ".webp")
UUID_RE = re.compile(r"\A[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}\Z")
LOGIN_RE = re.compile(r"log ?in|sign ?in|unauthenticated|not authenticated", re.I)
QUOTA_RE = re.compile(r"RESOURCE_EXHAUSTED|ResourceExhausted|quota|\b429\b", re.I)
ENV_NAMES = ("HOME", "PATH", "LANG", "TERM", "USER")
ENV_PREFIXES = ("LC_", "XDG_")
PROMPT_SUFFIXES = (".md", ".txt")
# Evidence 2026-11-24 (agy 1.5.16): --settings takes {"permissions": {"allow"|"deny"|"ask": [rule]}}, rules look like
# command(<prefix>), write_file(<path>), read_url(<domain>), mcp(<server>/<tool>); an unknown top-level key (disabledTools)
# is rejected, and agy has no flag that lists tools to disable. A deny rule beats an allow rule; headless mode
# auto-denies whatever is neither allowed nor denied. So the shell is denied up front here and checked again afterwards.
DENY_RULES = ["command(*)", "write_file(*)", "read_url(*)", "execute_url(*)", "mcp(*)"]
SETTINGS = json.dumps({"permissions": {"allow": [], "deny": DENY_RULES}})
CONTROL_TOOLS = ("generate_image", "invoke_subagent", "finish", "wait", "wait_5_seconds")
PATH_TOOLS = ("view_file", "read_file", "list_dir")
TRANSCRIPT_TOOLS = ("send_message",)  # a sub-agent reporting to its parent; seen only in transcripts
IMAGE_POLL_S = 15
MAX_PROMPT_BYTES = 64 * 1024
MIN_IMAGE_BYTES = 100


class SketchError(VisualError):
    """code: bad_task, bad_prompt, bad_ref, bad_count, agy_missing, agy_not_logged_in, agy_failed, agy_timeout,
    no_images, secret_detected, sandbox_unavailable, rate_limit, agy_quota, agent_tool_violation."""


def find_agy(which=shutil.which):
    found = which("agy") or str(Path.home() / ".local" / "bin" / "agy")
    if not os.access(found, os.X_OK):
        raise SketchError("agy_missing", "agy (Antigravity CLI) is not on PATH or in ~/.local/bin")
    return found


def parse_stream(text):
    """(conversation_ids, result, tools) from NDJSON. conversation_ids: main first, then sub-agents in order.
    result is the final result dict or None. Lines that are not JSON objects are ignored."""
    ids, result, tools = [], None, []

    def add(cid):
        if isinstance(cid, str) and UUID_RE.match(cid) and cid not in ids:
            ids.append(cid)

    for line in text.splitlines():
        try:
            ev = json.loads(line)
        except ValueError:
            continue
        if not isinstance(ev, dict):
            continue
        kind = ev.get("event")
        if kind == "init":
            add(ev.get("conversation_id"))
        elif kind == "step_update" and isinstance(ev.get("step_update"), dict):
            su = ev["step_update"]
            add(su.get("conversation_id"))
            if su.get("tool_name") and su["tool_name"] not in tools:
                tools.append(su["tool_name"])
            info = su.get("subagent_info")
            for sub in (info.get("subagents") or []) if isinstance(info, dict) else []:
                if isinstance(sub, dict):
                    add(sub.get("conversation_id"))
        elif kind == "result" and isinstance(ev.get("result"), dict):
            result = ev["result"]
            add(result.get("conversation_id"))
    return ids, result, tools


def _paths(obj):
    """Every string under a key that looks like a path, recursively."""
    found = []
    if isinstance(obj, dict):
        for k, v in obj.items():
            if isinstance(v, str) and re.search(r"path|file|dir|uri|url", k, re.I):
                found.append(v)
            else:
                found += _paths(v)
    elif isinstance(obj, list):
        for v in obj:
            found += _paths(v)
    return found


def _as_roots(roots):
    return [roots] if isinstance(roots, (str, os.PathLike)) else list(roots)


def run_roots(ws, brain_dir, ids):
    """Folders a PATH_TOOLS call may touch: the work folder and the brain folder of each conversation of this run."""
    return [str(ws)] + [str(Path(brain_dir) / cid) for cid in ids if UUID_RE.match(cid)]


def _inside(path, roots):
    """True when path (relative ones resolve against roots[0]) is, after realpath, inside one of roots."""
    roots = _as_roots(roots)
    path = path.strip().strip('"')  # agy writes string args as JSON-quoted text
    if path.startswith("file://"):
        path = path[7:]
    if path.startswith("~"):
        return False
    real = os.path.realpath(os.path.join(roots[0], path))
    for r in roots:
        root = os.path.realpath(r)
        if real == root or real.startswith(root + os.sep):
            return True
    return False


def transcript_calls(brain_dir, ids):
    """(calls, missing): calls are (tool name, path args) of every tool call in the conversations' transcripts;
    missing lists the ids whose transcript is absent or unreadable. The result stream names a call but may not
    carry its arguments; the transcript does."""
    calls, missing = [], []
    for cid in ids:
        f = Path(brain_dir) / cid / ".system_generated" / "logs" / "transcript.jsonl"
        try:
            lines = f.read_text(encoding="utf-8").splitlines()
        except (OSError, ValueError):
            missing.append(cid)
            continue
        for line in lines:
            try:
                ev = json.loads(line)
            except ValueError:
                continue
            for call in (ev.get("tool_calls") or []) if isinstance(ev, dict) else []:
                if isinstance(call, dict) and isinstance(call.get("name"), str):
                    calls.append((call["name"], _paths(call.get("args"))))
    return calls, missing


def verified_tools(calls):
    """Path tools whose arguments the transcript actually holds."""
    return frozenset(name for name, paths in calls if name in PATH_TOOLS and paths)


def call_violations(calls, roots):
    """Transcript calls outside the allowlist; same rules as tool_violations."""
    bad = []
    for name, paths in calls:
        if name in PATH_TOOLS:
            if not paths or not all(_inside(p, roots) for p in paths):
                bad.append(f"{name}: {one_line(' '.join(paths), 80)}".rstrip(": "))
        elif name not in CONTROL_TOOLS and name not in TRANSCRIPT_TOOLS:
            bad.append(name)
    return list(dict.fromkeys(bad))


def tool_violations(text, roots, verified=frozenset()):
    """Tool calls outside the allowlist (anything not named here is a violation): generate_image, invoke_subagent,
    finish/wait control calls, path tools only inside roots (the work folder and this run's own conversation folders). run_command is never allowed.
    A call whose path cannot be read counts as a violation, unless the transcript holds arguments for a call of that
    tool (verified: then call_violations checks them)."""
    bad = []
    for line in text.splitlines():
        try:
            ev = json.loads(line)
        except ValueError:
            continue
        su = ev.get("step_update") if isinstance(ev, dict) and ev.get("event") == "step_update" else None
        name = su.get("tool_name") if isinstance(su, dict) else None
        if not isinstance(name, str) or name in CONTROL_TOOLS:
            continue
        if name in PATH_TOOLS:
            paths = _paths(su)
            if paths and all(_inside(p, roots) for p in paths):
                continue
            if not paths and name in verified:  # arguments are checked in the transcript
                continue
            bad.append(f"{name}: {one_line(' '.join(paths), 80)}".rstrip(": "))
        else:
            bad.append(name)
    return list(dict.fromkeys(bad))


def _strings(err, result, out):
    """Texts that can describe a failure: stderr, result.response/error, text of the last 5 events (never init/usage)."""
    texts = [err or ""]
    texts += [result[k] for k in ("response", "error") if isinstance(result, dict) and isinstance(result.get(k), str)]
    tail = []
    for line in out.splitlines():
        try:
            ev = json.loads(line)
        except ValueError:
            continue
        if isinstance(ev, dict) and ev.get("event") == "step_update" and isinstance(ev.get("step_update"), dict):
            tail.append(ev["step_update"])
    for su in tail[-5:]:
        texts += [su[k] for k in ("text_delta", "error", "message") if isinstance(su.get(k), str)]
    return texts


def quota_hit(err, result, out):
    """True when a failure text mentions a quota error. Call it on the error path only."""
    return any(QUOTA_RE.search(t) for t in _strings(err, result, out))


def find_model(text):
    """First string 'model' field in any event, or None (agy 1.5.15 stream events carry none)."""
    for line in text.splitlines():
        try:
            ev = json.loads(line)
        except ValueError:
            continue
        for d in (ev, ev.get("step_update"), ev.get("result")) if isinstance(ev, dict) else ():
            if isinstance(d, dict) and isinstance(d.get("model"), str) and d["model"]:
                return d["model"]
    return None


def image_complete(path):
    """True when the file is a whole PNG, JPEG or WebP: magic bytes, internal structure and the closing marker."""
    try:
        data = Path(path).read_bytes()
    except OSError:
        return False
    if len(data) < MIN_IMAGE_BYTES:
        return False
    if data.startswith(PNG):
        pos, seen = len(PNG), []
        while pos + 12 <= len(data):
            n = int.from_bytes(data[pos:pos + 4], "big")
            end = pos + 12 + n
            if end > len(data) or zlib.crc32(data[pos + 4:pos + 8 + n]) != int.from_bytes(data[end - 4:end], "big"):
                return False
            seen.append(data[pos + 4:pos + 8])
            pos = end
        return pos == len(data) and seen[:1] == [b"IHDR"] and b"IDAT" in seen and seen[-1] == b"IEND"
    if data.startswith(b"\xff\xd8\xff"):
        if not data.endswith(b"\xff\xd9"):
            return False
        pos, sof = 2, False
        while pos + 4 <= len(data) and data[pos] == 0xFF:
            marker = data[pos + 1]
            if marker == 0xDA:  # start of scan: entropy data follows, the closing marker was checked above
                return sof
            if 0xC0 <= marker <= 0xC2:
                sof = True
            pos += 2 + int.from_bytes(data[pos + 2:pos + 4], "big")
        return False
    if data[:4] == b"RIFF" and data[8:12] == b"WEBP":
        return int.from_bytes(data[4:8], "little") + 8 == len(data)
    return False


def collect_images(conversation_ids, since, brain_dir=BRAIN_DIR):
    """Complete image files directly inside the conversations' brain dirs, modified at or after `since` (epoch seconds)."""
    out = []
    for cid in conversation_ids:
        if not UUID_RE.match(cid):
            continue
        d = Path(brain_dir) / cid
        if not d.is_dir():
            continue
        for p in sorted(d.iterdir()):
            if p.is_file() and not p.is_symlink() and p.suffix.lower() in IMAGE_EXT and p.stat().st_mtime >= since and image_complete(p):
                out.append(p)
    return out


def check_refs(refs, shots_dir=None):
    """Resolved reference frames: PNG/JPEG files inside shots-out/ only."""
    root = Path(shots_dir or config.SHOTS_DIR).resolve()
    out = []
    if len(refs) > config.MAX_FRAMES:
        raise SketchError("bad_ref", f"more than {config.MAX_FRAMES} references")
    for r in refs:
        p = Path(r).resolve()
        if root not in p.parents:
            raise SketchError("bad_ref", f"{r}: only files under shots-out/ may be used")
        try:
            head = p.read_bytes()[:3]
            size = p.stat().st_size
        except OSError as e:
            raise SketchError("bad_ref", f"{r}: {e.strerror}") from None
        if not (head.startswith(PNG[:3]) or head == b"\xff\xd8\xff"):
            raise SketchError("bad_ref", f"{r}: not a PNG or JPEG")
        if size > config.MAX_FRAME_BYTES:
            raise SketchError("bad_ref", f"{r}: larger than {config.MAX_FRAME_BYTES} bytes")
        out.append(p)
    return out


def build_prompt(text, count, ref_paths):
    """The user's brief plus fixed instructions. Refs are copied to the workspace and named by absolute path."""
    parts = [text.strip(),
             "",
             f"Generate exactly {count} distinct image(s) with the image generation tool, one tool call per image. "
             "Do not write code or run commands. Reply with one short line per image."]
    if ref_paths:
        parts += ["", "Reference frames of the current game UI (read each with the file viewer before drawing):"]
        parts += [f"- {p}" for p in ref_paths]
    return "\n".join(parts)


def safe_env(environ=None):
    """Allowlisted environment for agy: no *_KEY, *_TOKEN or other secrets of the parent process."""
    environ = os.environ if environ is None else environ
    return {k: v for k, v in environ.items() if k in ENV_NAMES or k.startswith(ENV_PREFIXES)}


def default_runner(cmd, cwd, timeout):
    # Own session: on timeout the whole process group (agy and what it started) is killed.
    p = subprocess.Popen(cmd, cwd=cwd, env=safe_env(), stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                         start_new_session=True)
    try:
        out, err = p.communicate(timeout=timeout)
    except subprocess.TimeoutExpired:
        try:
            os.killpg(p.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        p.communicate()
        raise SketchError("agy_timeout", f"no answer in {timeout} s") from None
    return p.returncode, out, err


def record_stem(runs_dir, task, digest, started):
    """A record name unique per run (start time, then a counter): a retry never overwrites an earlier record."""
    base = f"sketch-{task}-{digest[:8]}-{datetime.fromtimestamp(started, timezone.utc):%Y%m%dT%H%M%SZ}"
    stem, k = base, 1
    while (Path(runs_dir) / (stem + ".json")).exists():
        stem = f"{base}-{k}"
        k += 1
    return stem


def check_prompt_file(prompt_file, root):
    """Resolved prompt path: a .md/.txt file of the repository, no dot-path parts, not ignored by git."""
    rootp = Path(root).resolve()
    pf = Path(prompt_file).resolve()
    bad = lambda why: SketchError("bad_prompt", f"{prompt_file}: {why}")
    if rootp not in pf.parents or not pf.is_file() or pf.stat().st_size > MAX_PROMPT_BYTES:
        raise bad(f"must be a file of the repository, up to {MAX_PROMPT_BYTES} bytes")
    rel = pf.relative_to(rootp)
    if any(part.startswith(".") for part in rel.parts):
        raise bad("dot-files and dot-directories are not allowed")
    if pf.suffix.lower() not in PROMPT_SUFFIXES:
        raise bad("only .md and .txt files are allowed")
    try:
        ignored = subprocess.run(["git", "check-ignore", "-q", "--", str(pf)], cwd=rootp, capture_output=True).returncode
    except OSError:
        raise bad("git is not available to check the ignore rules") from None
    if ignored != 1:  # 0 ignored, 128 not a repository: both refuse
        raise bad("ignored by git or not in a git repository")
    return pf


def agy_version(agy, runner):
    try:
        rc, out, _ = runner([agy, "--version"], None, 15)
    except (OSError, SketchError):
        return None
    return out.strip().splitlines()[0] if rc == 0 and out.strip() else None


def one_line(s, n=300):
    s = " ".join(s.split())
    return s if len(s) <= n else s[:n - 1] + "..."


def attribution_line(rec, f):
    """One line for mods/core/ATTRIBUTION.md; written only after ui-art-director approves the picture (ADR-0026 4)."""
    return (f"- {f['name']}: AI-generated sketch, model {rec['model']}, Antigravity CLI {rec.get('agy_version') or 'agy'}, "
            f"{rec['date']}, prompt {rec['prompt_file']} (sha256 {rec['prompt_sha256']}, full text in the run record) "
            f"({rec['task']}): \"{one_line(rec['prompt_brief'])}\"; "
            "approved by: <ui-art-director, review ref>")


def run_sketch(task, prompt_file, refs=(), count=1, runs_dir=config.RUNS_DIR, shots_dir=None, root=config.REPO_ROOT,
               brain_dir=BRAIN_DIR, runner=None, which=shutil.which, timeout_s=DEFAULT_TIMEOUT_S, dry_run=False,
               guard=None, clock=time.time, sleep=time.sleep):
    """One agy run. Returns (json path or None, md path or None, record)."""
    if not TASK_RE.match(task):
        raise SketchError("bad_task", task)
    if not 1 <= count <= MAX_COUNT:
        raise SketchError("bad_count", f"1..{MAX_COUNT}")
    pf = check_prompt_file(prompt_file, root)
    brief = pf.read_text(encoding="utf-8")
    ref_paths = check_refs(list(refs), shots_dir)
    try:
        gitleaks.scan_text(brief)
    except gitleaks.SecretDetected as e:
        raise SketchError("secret_detected", str(e)) from None
    except gitleaks.GitleaksUnavailable as e:
        raise SketchError("sandbox_unavailable", str(e)) from None
    runner = runner or default_runner
    agy = find_agy(which)
    digest = hashlib.sha256(b"\0".join([brief.encode(), str(count).encode()]
                                       + [p.name.encode() + b"\0" + p.read_bytes() for p in ref_paths])).hexdigest()
    record = {"kind": "sketch", "task": task, "provider": "gemini-agy", "prompt_file": str(pf.relative_to(Path(root).resolve())),
              "prompt_sha256": hashlib.sha256(brief.encode()).hexdigest(), "prompt_brief": one_line(brief, 600), "prompt": brief,
              "count": count, "refs": [p.name for p in ref_paths], "input_hash": digest}
    if dry_run:
        return None, None, record
    guard = guard or RateGuard(config.LEDGER_DIR / AGY_USAGE_FILE, rpm=AGY_RPM, rpd=AGY_RPD)
    guard.before_attempt(len(brief), 0)  # a failed run stays counted: the subscription may have counted it
    started = clock()
    with tempfile.TemporaryDirectory(prefix="agy-sketch-") as ws:
        ws_refs = []
        for p in ref_paths:
            dst = Path(ws) / f"{len(ws_refs) + 1}-{p.name}"  # same basenames must not collapse
            shutil.copyfile(p, dst)
            ws_refs.append(dst)
        cmd = [agy, "-p", build_prompt(brief, count, ws_refs), "--print-timeout", f"{timeout_s}s",
               "--mode", "accept-edits", "--output-format", "stream-json",
               "--settings", SETTINGS]
        record["agy_version"] = agy_version(agy, runner)
        rc, out, err = runner(cmd, ws, timeout_s + 60)
        ws_dir = ws
    record["started_at"] = datetime.fromtimestamp(started, timezone.utc).isoformat(timespec="seconds")
    record["date"] = record["started_at"][:10]
    ids, result, tools = parse_stream(out)
    record.update(conversation_id=ids[0] if ids else None, conversation_ids=ids, tools=tools, exit_code=rc)
    calls, missing = transcript_calls(brain_dir, ids)
    roots = run_roots(ws_dir, brain_dir, ids)
    bad_tools = tool_violations(out, roots, verified_tools(calls))  # fail closed: no transcript, no trust
    bad_tools = list(dict.fromkeys(bad_tools + call_violations(calls, roots) + [f"transcript missing: {c}" for c in missing]))
    if bad_tools:
        record["agent_tool_violation"] = bad_tools
        write_pair(Path(runs_dir), record_stem(runs_dir, task, digest, started), record,
                   lambda r: f"# Gemini sketches (agy): {r['task']}\n\nREFUSED: agent tool violation: {', '.join(r['agent_tool_violation'])}\n")
        raise SketchError("agent_tool_violation", ", ".join(bad_tools))
    answer = result.get("response") if result and isinstance(result.get("response"), str) else ""
    if result is None or rc != 0 or result.get("status") != "SUCCESS":
        if quota_hit(err, result, out):
            raise SketchError("agy_quota", one_line(answer or err[-600:] or "quota error", 300))
        detail = one_line(answer or err or out, 300)
        login = any(LOGIN_RE.search(t) for t in _strings(err, result, "")) if result is not None or err else False
        if result is not None:
            record["status"] = result.get("status")
        raise SketchError("agy_not_logged_in" if login else "agy_failed", f"exit {rc}: {detail}")
    record["status"] = result.get("status")
    record["denied_actions"] = result.get("denied_actions") if isinstance(result.get("denied_actions"), list) else []
    record["model"] = find_model(out) or f"agy {record.get('agy_version') or '?'} image-generator"
    usage = result.get("usage") if isinstance(result.get("usage"), dict) else {}
    record["usage"] = {k: v for k, v in usage.items() if type(v) is int}
    guard.settle(None, {"prompt_tokens": usage.get("input_tokens"), "completion_tokens": usage.get("output_tokens"),
                        "completion_tokens_details": {"reasoning_tokens": usage.get("thinking_tokens")}})
    found = collect_images(ids, started - 2, brain_dir)
    sizes, prev = [p.stat().st_size for p in found], None
    for _ in range(IMAGE_POLL_S):  # the file may land a moment after the result event; its size must stop changing
        if len(found) >= count and sizes == prev:
            break
        sleep(1)
        prev = sizes
        found = collect_images(ids, started - 2, brain_dir)
        sizes = [p.stat().st_size for p in found]
    if not found:
        if quota_hit(err, result, out):
            raise SketchError("agy_quota", one_line(answer or err[-600:] or "quota error", 300))
        raise SketchError("no_images", f"no new image in the brain dirs of {', '.join(ids)}")
    out_dir = Path(shots_dir or config.SHOTS_DIR) / "sketches" / task
    out_dir.mkdir(parents=True, exist_ok=True)
    record["files"] = []
    record["requested"] = count
    for src in found:
        dst = out_dir / src.name
        k = 1
        while dst.exists():
            dst = out_dir / f"{src.stem}-{k}{src.suffix}"
            k += 1
        shutil.copyfile(src, dst)
        data = dst.read_bytes()
        record["files"].append({"name": dst.name, "path": str(dst), "bytes": len(data),
                                "sha256": hashlib.sha256(data).hexdigest(), "source": str(src)})
    record["partial"] = len(record["files"]) < count
    record["attribution"] = [attribution_line(record, f) for f in record["files"]]
    path, md = write_pair(Path(runs_dir), record_stem(runs_dir, task, digest, started), record, summary_md)
    return path, md, record


def summary_md(rec):
    lines = [f"# Gemini sketches (agy): {rec['task']}", "",
             f"Model {rec['model']}. Images {len(rec['files'])} of {rec['requested']} requested"
             f"{' (PARTIAL)' if rec['partial'] else ''}. Conversation {rec['conversation_id']}, started {rec['started_at']}, agy {rec.get('agy_version') or 'unknown'}.",
             "Sketches are proposals: ADR-0026 needs ui-art-director's approval before use in the game.", "",
             f"Prompt ({rec['prompt_file']}, sha256 {rec['prompt_sha256']}): {rec['prompt_brief']}", ""]
    if rec["refs"]:
        lines += [f"References: {', '.join(rec['refs'])}", ""]
    lines += [f"- {f['name']} ({f['bytes']} bytes, sha256 {f['sha256'][:12]})" for f in rec["files"]]
    lines += ["", "Attribution lines for mods/core/ATTRIBUTION.md (fill the approver; add only approved files):", ""]
    return "\n".join(lines + rec["attribution"]) + "\n"
