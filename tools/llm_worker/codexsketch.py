"""Design sketches through Codex CLI image generation (TASK-0145, ADR-0026).

Codex is the fallback of the Gemini sketch engine (owner 2026-11-25): `auto` runs agy first and comes here only when
Gemini has no quota. Codex shares its limit with the second review (ADR-0015), so the remembered limit of
limitstate is respected and a daily cap bounds the images. `codex exec --sandbox read-only` runs in the bwrap of
codex.py, never with --dangerously-bypass-approvals-and-sandbox; one image per call. Codex saves a generated image
under ~/.codex/generated_images/<thread_id>/ (path also named by the image event); only files from there are copied.
A sketch is a proposal: ADR-0026 needs ui-art-director's approval before use in mods/core/."""

import base64
import fcntl
import hashlib
import json
import os
import re
import shutil
import subprocess
import tempfile
import time
from datetime import datetime, timezone
from pathlib import Path

from . import codex, config, gitleaks, limitstate
from .sandbox import SandboxUnavailable
from .sketch import (SketchError, attribution_line, check_prompt_file, check_refs, image_complete, one_line, record_stem, summary_md,
                     IMAGE_EXT, run_sketch as run_gemini)
from .visual import write_pair, TASK_RE

ENGINES = ("auto", "gemini", "agy", "codex")  # agy: alias of gemini
DEFAULT_DAILY_CAP = 4
DEFAULT_TIMEOUT_S = 600
ATTEMPT_PROVIDER = "codex-sketch"
FALLBACK_CODES = ("agy_quota", "rate_limit")  # Gemini has no quota: the only reasons to spend the Codex limit
SHELL_ITEMS = ("command_execution", "local_shell_call", "mcp_tool_call", "file_change", "web_search")
LAND_LOCK = Path.home() / ".cache" / "singularity" / "land.lock"  # written by tools/development/land.py
THREAD_RE = re.compile(r"\A[0-9a-f-]{36}\Z")


def codex_home(home=None):
    return Path(home or Path.home()) / ".codex"


def parse_run(stdout):
    """(thread_id, image paths, base64 images, model, shell items) from `codex exec --json` JSONL. An item is an
    image when its type names one; it may carry a saved path or the data itself. Unknown lines are skipped."""
    thread, paths, blobs, model, shell = None, [], [], None, []
    for line in stdout.splitlines():
        try:
            ev = json.loads(line)
        except ValueError:
            continue
        if not isinstance(ev, dict):
            continue
        if ev.get("type") == "thread.started" and isinstance(ev.get("thread_id"), str) and THREAD_RE.match(ev["thread_id"]):
            thread = ev["thread_id"]
        if isinstance(ev.get("model"), str):
            model = model or ev["model"]
        item = ev.get("item")
        if ev.get("type") not in ("item.started", "item.completed") or not isinstance(item, dict):
            continue
        kind = str(item.get("type") or "")
        if kind in SHELL_ITEMS:
            shell.append(kind)
        elif "image" in kind and ev["type"] == "item.completed":
            for k in ("path", "saved_path", "file", "file_path"):
                if isinstance(item.get(k), str):
                    paths.append(item[k])
            for k in ("result", "b64_json", "data"):
                if isinstance(item.get(k), str) and len(item[k]) > 100:
                    blobs.append(item[k])
    return thread, paths, blobs, model, list(dict.fromkeys(shell))


def command(codex_exe, refs):
    """-i goes first: it takes several values, and the next flag ends its list."""
    ref_args = [x for r in refs for x in ("-i", r)]
    return [codex_exe, "exec", *ref_args, "--sandbox", "read-only", "--skip-git-repo-check", "--json",
            "--color", "never", "-C", "/work", "-"]


def build_prompt(brief, ref_names):
    parts = [brief.strip(), "",
             "Make exactly ONE image with the image generation tool: one call, no refinements, no retries; on any error "
             "report it and stop. Do not run shell commands or write code. Reply with one short line."]
    if ref_names:
        parts += ["", "Attached reference frames of the current game UI: " + ", ".join(ref_names)]
    return "\n".join(parts)


def default_runner(argv, prompt, timeout, env):
    try:
        p = subprocess.run(argv, input=prompt, capture_output=True, text=True, timeout=timeout, env=env)
    except subprocess.TimeoutExpired:
        raise SketchError("codex_failed", f"codex timed out after {timeout} s") from None
    except OSError as e:
        raise SketchError("sandbox_unavailable", f"bwrap failed to start: {e}") from None
    return p.returncode, p.stdout, p.stderr


def reserve_attempt(directory, day, cap, task, digest):
    """Check the daily cap and log the attempt under one flock: two runs cannot both take the last slot."""
    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=True)
    fd = os.open(directory / "codex_attempts.lock", os.O_CREAT | os.O_RDWR, 0o600)
    try:
        fcntl.flock(fd, fcntl.LOCK_EX)
        if daily_attempts(directory, day) >= cap:
            raise SketchError("codex_daily_cap", f"{cap} Codex sketch attempts today (--codex-daily-cap)")
        codex.log_attempt(task, digest, "started", directory=directory, provider=ATTEMPT_PROVIDER)
    finally:
        os.close(fd)


def daily_attempts(directory, day):
    """Codex sketch attempts logged on the UTC date `day` (a failed run counts: the subscription may have)."""
    try:
        lines = (Path(directory) / codex.ATTEMPTS).read_text(encoding="utf-8").splitlines()
    except OSError:
        return 0
    n = 0
    for line in lines:
        try:
            r = json.loads(line)
        except ValueError:
            continue
        if isinstance(r, dict) and r.get("provider") == ATTEMPT_PROVIDER and str(r.get("ts", "")).startswith(day):
            n += 1
    return n


def land_running(lock=None):
    """True when land.py holds its lock under a live pid (a stale lock of a dead pid does not count)."""
    try:
        pid = int((lock or LAND_LOCK).read_text(encoding="utf-8").strip())
        os.kill(pid, 0)
    except PermissionError:
        return True
    except (OSError, ValueError):
        return False
    return True


def codex_version(exe):
    try:
        p = subprocess.run([exe, "--version"], capture_output=True, text=True, timeout=15)
    except (OSError, subprocess.SubprocessError):
        return None
    return p.stdout.strip().splitlines()[0] if p.returncode == 0 and p.stdout.strip() else None


def decode_blob(b64):
    try:
        return base64.b64decode(b64, validate=False)
    except ValueError:
        return b""


def run_codex_sketch(task, prompt_file, refs=(), count=1, runs_dir=config.RUNS_DIR, shots_dir=None, root=config.REPO_ROOT,
                     runner=None, timeout_s=DEFAULT_TIMEOUT_S, dry_run=False, daily_cap=DEFAULT_DAILY_CAP,
                     state_dir=limitstate.STATE_DIR, ledger_dir=config.LEDGER_DIR, home=None, environ=os.environ,
                     clock=time.time, fallback=None, find=codex.find_codex, bwrap=None, land_lock=None):
    """One Codex image. Returns (json path or None, md path or None, record)."""
    if not TASK_RE.match(task):
        raise SketchError("bad_task", task)
    if count != 1:
        raise SketchError("bad_count", "Codex makes one image per call")
    pf = check_prompt_file(prompt_file, root)
    brief = pf.read_text(encoding="utf-8")
    ref_paths = check_refs(list(refs), shots_dir)
    try:
        gitleaks.scan_text(brief)
    except gitleaks.SecretDetected as e:
        raise SketchError("secret_detected", str(e)) from None
    except gitleaks.GitleaksUnavailable as e:
        raise SketchError("sandbox_unavailable", str(e)) from None
    digest = hashlib.sha256(b"\0".join([brief.encode(), b"codex"] + [p.name.encode() + b"\0" + p.read_bytes() for p in ref_paths])).hexdigest()
    record = {"kind": "sketch", "task": task, "provider": "codex", "engine": "codex", "prompt_file": str(pf.relative_to(Path(root).resolve())),
              "prompt_sha256": hashlib.sha256(brief.encode()).hexdigest(), "prompt_brief": one_line(brief, 600), "prompt": brief,
              "count": 1, "refs": [p.name for p in ref_paths], "input_hash": digest}
    if fallback:
        record["fallback"] = fallback
    if dry_run:
        return None, None, record
    now = datetime.now(timezone.utc).astimezone()
    until = limitstate.limited_until(now, state_dir)
    if until:
        raise SketchError("codex_quota", f"Codex limit is active, reset at {until.isoformat(timespec='minutes')}")
    if land_running(land_lock):
        raise SketchError("land_in_progress", "land.py is running: the second review needs the Codex limit")
    day = datetime.fromtimestamp(clock(), timezone.utc).strftime("%Y-%m-%d")
    try:
        exe = find()
        real, _, node_dir = codex.codex_mounts(exe, home)
    except SandboxUnavailable as e:
        raise SketchError("sandbox_unavailable", str(e)) from None
    started = clock()
    chome = codex_home(home)
    version = codex_version(real) if runner is None else None
    runner = runner or default_runner
    with tempfile.TemporaryDirectory(prefix="codex-sketch-") as ws:
        ws_refs = []
        for p in ref_paths:
            dst = Path(ws) / f"{len(ws_refs) + 1}-{p.name}"
            shutil.copyfile(p, dst)
            ws_refs.append(dst.name)
        env = {k: environ[k] for k in codex.ENV_KEEP if k in environ}
        env["HOME"] = str(home or Path.home())
        env["PATH"] = f"{node_dir}:{env.get('PATH', '/usr/bin:/bin')}"
        try:
            argv = codex.bwrap_argv(ws, None, exe, home=home, bwrap=bwrap) + command(str(real), [f"/work/{n}" for n in ws_refs])
        except SandboxUnavailable as e:
            raise SketchError("sandbox_unavailable", str(e)) from None
        reserve_attempt(ledger_dir, day, daily_cap, task, digest)
        rc, out, err = runner(argv, build_prompt(brief, ws_refs), timeout_s, env)
    thread, paths, blobs, model, shell = parse_run(out)
    record.update(started_at=datetime.fromtimestamp(started, timezone.utc).isoformat(timespec="seconds"), exit_code=rc,
                  conversation_id=thread, codex_version=version)
    record["date"] = record["started_at"][:10]
    if shell:
        record["agent_tool_violation"] = shell
        write_pair(Path(runs_dir), record_stem(runs_dir, task, digest, started), record,
                   lambda r: f"# Codex sketches: {r['task']}\n\nREFUSED: agent tool violation: {', '.join(r['agent_tool_violation'])}\n")
        raise SketchError("agent_tool_violation", ", ".join(shell))
    images = []  # (name, bytes)
    gen = chome / "generated_images"
    for s in paths:  # only files Codex saved itself, never an arbitrary path from an event
        p = Path(os.path.realpath(s))
        if gen in p.parents and p.suffix.lower() in IMAGE_EXT and image_complete(p):
            images.append((p.name, p.read_bytes()))
    if not images and thread and Path(os.path.realpath(gen / thread)).parent == gen and (gen / thread).is_dir():
        for p in sorted((gen / thread).iterdir()):
            if p.is_file() and not p.is_symlink() and p.suffix.lower() in IMAGE_EXT and image_complete(p):
                images.append((p.name, p.read_bytes()))
    for i, b in enumerate(blobs):
        data = decode_blob(b)
        ext = ".png" if data.startswith(b"\x89PNG") else ".jpg" if data.startswith(b"\xff\xd8") else ".webp"
        with tempfile.NamedTemporaryFile(suffix=ext) as t:
            t.write(data)
            t.flush()
            if image_complete(t.name):
                images.append((f"codex_{thread or 'run'}_{i + 1}{ext}"[:80], data))
    images = images[-1:]  # one image per call
    if not images:
        msg = codex.error_text(out) + err
        if codex.LIMIT_RE.search(msg):
            reset = limitstate.record_limit(limitstate.parse_reset(msg, now), now, state_dir)
            raise SketchError("codex_quota", f"Codex usage limit, reset at {reset.isoformat(timespec='minutes')}")
        if rc != 0:
            raise SketchError("codex_failed", f"exit {rc}: {one_line(err[-400:] or msg, 300)}")
        raise SketchError("no_images", "Codex answered without an image")
    record["model"] = model or "codex image_generation"
    record["files"], record["requested"] = [], 1
    out_dir = Path(shots_dir or config.SHOTS_DIR) / "sketches" / task
    out_dir.mkdir(parents=True, exist_ok=True)
    name, data = images[0]
    dst = out_dir / name
    k = 1
    while dst.exists():
        dst = out_dir / f"{Path(name).stem}-{k}{Path(name).suffix}"
        k += 1
    dst.write_bytes(data)
    record["files"].append({"name": dst.name, "path": str(dst), "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest(),
                            "source": "codex generated_images"})
    record["partial"] = False
    record["attribution"] = [attribution_line(record, f) for f in record["files"]]
    path, md = write_pair(Path(runs_dir), record_stem(runs_dir, task, digest, started), record, summary_md)
    return path, md, record


def run_engine(engine, task, prompt_file, refs=(), count=1, codex_kw=None, **kw):
    """engine gemini | codex | auto. auto: Gemini first; Codex only when Gemini is out of quota and one image was asked."""
    if engine not in ENGINES:
        raise SketchError("bad_engine", engine)
    engine = "gemini" if engine == "agy" else engine
    codex_kw = dict(codex_kw or {})
    for k in ("runs_dir", "shots_dir", "root", "dry_run"):
        if k in kw:
            codex_kw.setdefault(k, kw[k])
    if engine == "codex":
        return run_codex_sketch(task, prompt_file, refs, count, **codex_kw)
    try:
        return run_gemini(task, prompt_file, refs, count, **kw)
    except SketchError as e:
        if engine == "gemini" or e.code not in FALLBACK_CODES or count != 1:
            raise
        return run_codex_sketch(task, prompt_file, refs, count, fallback={"from": "gemini", "code": e.code, "reason": one_line(str(e), 200)},
                                **codex_kw)
