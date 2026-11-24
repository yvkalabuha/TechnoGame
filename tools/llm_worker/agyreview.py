"""Second reviewer through the Antigravity CLI: Claude Sonnet 5.5 when the Codex limit is exhausted (#456).

`agy -p` runs read-only in the clean review worktree: the shell, file writes, URLs and MCP are denied up front
(sketch.DENY_RULES plus a per-run read scope), --dangerously-skip-permissions is never passed, the environment is the sketch allowlist, and
the stream plus the transcripts are checked afterwards: any tool outside the allowlist, or a file path outside the
worktree and this run's own brain folders, refuses the review. Keys are never read here."""

import functools
import json
import shutil

MODEL = "claude-sonnet-5-5-medium"
DEFAULT_TIMEOUT_S = 900
REVIEW_CONTROL = ("finish", "wait", "wait_5_seconds")  # no generate_image, no sub-agents
REVIEW_PATH_TOOLS = ("view_file", "read_file", "list_dir", "grep_search", "find_by_name")


class AgyLimit(RuntimeError):
    """The agy quota of the model group is exhausted."""


class AgyFailed(RuntimeError):
    pass


class AgyViolation(RuntimeError):
    """The agent used a tool or path outside the allowlist."""


def command(agy, schema_file, model, timeout_s, settings):
    """The prompt goes through stdin (one stream-json user message): a review prompt can be up to 1 MB, far more
    than one command-line argument may hold (E2BIG at about 128 KB). Event format checked on agy 1.5.16."""
    return [agy, "--input-format", "stream-json", "--model", model, "--print-timeout", f"{timeout_s}s", "--mode", "plan",
            "--output-format", "stream-json", "--json-schema", str(schema_file), "--settings", settings]


def user_message(prompt):
    return json.dumps({"event": "user", "message": {"role": "user", "content": prompt}}, ensure_ascii=False) + "\n"


def init_model(out):
    """Model name from the init event of the stream, or None."""
    for line in out.splitlines():
        try:
            ev = json.loads(line)
        except ValueError:
            continue
        init = ev.get("init") if isinstance(ev, dict) and ev.get("event") == "init" else None
        if isinstance(init, dict) and isinstance(init.get("model"), str):
            return init["model"]
    return None


def extract_answer(result):
    """Answer text from the final result: the structured output when agy validated one, else the response text."""
    if isinstance(result.get("structured_output"), dict):
        return json.dumps(result["structured_output"], ensure_ascii=False)
    resp = result.get("response")
    return resp if isinstance(resp, str) else ""


def run_agy(prompt, worktree, schema_file, model=MODEL, timeout_s=DEFAULT_TIMEOUT_S, brain_dir=None,
            runner=None, which=shutil.which):
    """(answer text, usage dict, model name). AgyLimit on a quota error, AgyViolation on a forbidden tool or path,
    AgyFailed on anything else (also when another model than `model` answered). Runs inside a Box (bwrap) with a
    private brain folder unless a runner is injected (tests)."""
    from . import codex, sketch  # sketch -> visual -> review -> agyreview: imported late to avoid a cycle

    try:
        if runner is not None:
            return _run_agy(prompt, worktree, schema_file, model, timeout_s, brain_dir or sketch.BRAIN_DIR, None, runner, which)
        with sketch.private_box(cwd_writable=False) as (box, private_brain):
            return _run_agy(prompt, worktree, schema_file, model, timeout_s, private_brain, sketch.BRAIN_DIR,
                            functools.partial(sketch.default_runner, box=box), which)
    except sketch.SketchError as e:
        if e.code == "sandbox_unavailable":  # never a paid fallback for a missing sandbox, like Codex
            raise codex.SandboxUnavailable(str(e)) from None
        raise AgyFailed(f"{e.code}: {e}") from None
    except OSError as e:  # the process could not start at all
        raise AgyFailed(f"agy could not start: {e}") from None


def _run_agy(prompt, worktree, schema_file, model, timeout_s, brain_dir, inside_brain, runner, which):
    """brain_dir: where this process finds the brain folders; inside_brain: how agy names that folder."""
    from . import sketch

    inside_brain = inside_brain or brain_dir
    agy = sketch.find_agy(which)
    # Reads are limited by rules as a second layer; the first one is the Box. The diff is untrusted data.
    with sketch.settings_file([worktree, inside_brain], [inside_brain]) as settings:
        rc, out, err = runner(command(agy, schema_file, model, timeout_s, settings), str(worktree), timeout_s + 60,
                              user_message(prompt))
    ids, result, _ = sketch.parse_stream(out)
    if not ids and result is not None and result.get("status") == "SUCCESS":
        raise AgyViolation("no conversation id in the stream: the transcript cannot be checked")  # fail closed
    calls, missing = sketch.transcript_calls(brain_dir, ids)
    roots = sketch.run_roots(worktree, inside_brain, ids)
    bad = sketch.tool_violations(out, roots, sketch.verified_tools(calls, REVIEW_PATH_TOOLS), REVIEW_CONTROL,
                                 REVIEW_PATH_TOOLS)
    bad += sketch.call_violations(calls, roots, REVIEW_CONTROL, REVIEW_PATH_TOOLS)
    bad += [f"transcript missing: {c}" for c in missing]  # fail closed: no transcript, no trust
    if bad:
        raise AgyViolation(", ".join(dict.fromkeys(bad)))
    if result is None or rc != 0 or result.get("status") != "SUCCESS":
        if sketch.quota_hit(err, result, out):
            raise AgyLimit(sketch.one_line(err[-300:] or "agy quota error", 300))
        raise AgyFailed(f"agy exited {rc}: {sketch.one_line(err or out, 300)}")
    answer = extract_answer(result)
    if not answer.strip():
        raise AgyFailed("agy returned an empty answer")
    reported = init_model(out)
    if reported != model:  # a review that names Sonnet must come from Sonnet
        raise AgyFailed(f"model mismatch: asked {model}, the stream says {reported}")
    usage = result.get("usage") if isinstance(result.get("usage"), dict) else {}
    return answer, {k: v for k, v in usage.items() if type(v) is int}, reported
