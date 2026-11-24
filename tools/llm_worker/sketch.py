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
ALLOWED_COMMANDS = ("ls", "file")
SETTINGS = json.dumps({"permissions": {"allow": [f"command({c})" for c in ALLOWED_COMMANDS]}})
FORBIDDEN_TOOLS = ("read_url_content", "open_browser_url", "call_mcp_tool")
IMAGE_POLL_S = 15
MAX_PROMPT_BYTES = 64 * 1024


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
    self_calls = []

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


def _commands(obj):
    """Every string under a key that looks like a shell command line, recursively."""
    found = []
    if isinstance(obj, dict):
        for k, v in obj.items():
            if isinstance(v, str) and re.search(r"command|cmdline", k, re.I):
                found.append(v)
            else:
                found += _commands(v)
    elif isinstance(obj, list):
        for v in obj:
            found += _commands(v)
    return found


def tool_violations(text):
    """Tool calls outside the allowlist: shell commands other than ls/file, browser_*, URL readers, MCP calls.
    A run_command whose command line cannot be read counts as a violation."""
    bad = []
    for line in text.splitlines():
        try:
            ev = json.loads(line)
        except ValueError:
            continue
        su = ev.get("step_update") if isinstance(ev, dict) and ev.get("event") == "step_update" else None
        name = su.get("tool_name") if isinstance(su, dict) else None
        if not isinstance(name, str):
            continue
        if name.startswith("browser_") or name in FORBIDDEN_TOOLS:
            bad.append(name)
        elif name == "run_command":
            cmds = _commands(su)
            ok = bool(cmds) and all(c.split()[:1] and c.split()[0] in ALLOWED_COMMANDS
                                    and not re.search(r"[;&|`$<>()]", c) for c in cmds)
            if not ok:
                bad.append(f"run_command: {one_line(' '.join(cmds), 80)}".rstrip(": "))
    return list(dict.fromkeys(bad))


def quota_hit(err, result, out):
    """True when stderr, the result (incl. the agent's final response) or the last events mention a quota error.
    The init event is skipped: it lists tool names and settings, not outcomes."""
    texts = [err or "", json.dumps(result or {})]
    tail = []
    for line in out.splitlines():
        try:
            ev = json.loads(line)
        except ValueError:
            continue
        if isinstance(ev, dict) and ev.get("event") == "step_update":
            tail.append(ev)
    for ev in tail[-5:]:
        su = ev.get("step_update") or {}
        texts += [su[k] for k in ("text_delta", "error", "message") if isinstance(su.get(k), str)]
    return any(QUOTA_RE.search(t) for t in texts)


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


def collect_images(conversation_ids, since, brain_dir=BRAIN_DIR):
    """Image files directly inside the conversations' brain dirs, modified at or after `since` (epoch seconds)."""
    out = []
    for cid in conversation_ids:
        if not UUID_RE.match(cid):
            continue
        d = Path(brain_dir) / cid
        if not d.is_dir():
            continue
        for p in sorted(d.iterdir()):
            if p.is_file() and not p.is_symlink() and p.suffix.lower() in IMAGE_EXT and p.stat().st_mtime >= since:
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
    record["started_at"] = datetime.fromtimestamp(started, timezone.utc).isoformat(timespec="seconds")
    record["date"] = record["started_at"][:10]
    ids, result, tools = parse_stream(out)
    record.update(conversation_id=ids[0] if ids else None, conversation_ids=ids, tools=tools, exit_code=rc)
    bad_tools = tool_violations(out)
    if bad_tools:
        record["agent_tool_violation"] = bad_tools
        write_pair(Path(runs_dir), f"sketch-{task}-{digest[:8]}", record,
                   lambda r: f"# Gemini sketches (agy): {r['task']}\n\nREFUSED: agent tool violation: {', '.join(r['agent_tool_violation'])}\n")
        raise SketchError("agent_tool_violation", ", ".join(bad_tools))
    answer = result.get("response") if result and isinstance(result.get("response"), str) else ""
    if quota_hit(err, result, out):
        raise SketchError("agy_quota", one_line(answer or err[-600:] or "quota error", 300))
    if result is None or rc != 0:
        detail = one_line(answer or err or out, 300)
        login = LOGIN_RE.search(err + json.dumps(result or {}))
        raise SketchError("agy_not_logged_in" if login else "agy_failed", f"exit {rc}: {detail}")
    record["status"] = result.get("status")
    record["model"] = find_model(out) or f"agy {record.get('agy_version') or '?'} image-generator"
    usage = result.get("usage") if isinstance(result.get("usage"), dict) else {}
    record["usage"] = {k: v for k, v in usage.items() if type(v) is int}
    guard.settle(None, {"prompt_tokens": usage.get("input_tokens"), "completion_tokens": usage.get("output_tokens"),
                        "completion_tokens_details": {"reasoning_tokens": usage.get("thinking_tokens")}})
    if result.get("status") != "SUCCESS":
        raise SketchError("agy_failed", f"status {result.get('status')}")
    found = collect_images(ids, started - 2, brain_dir)
    for _ in range(IMAGE_POLL_S):  # the file may land a moment after the result event
        if len(found) >= count:
            break
        sleep(1)
        found = collect_images(ids, started - 2, brain_dir)
    if not found:
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
    path, md = write_pair(Path(runs_dir), f"sketch-{task}-{digest[:8]}", record, summary_md)
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
