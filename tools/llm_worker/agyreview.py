"""Second reviewer through the Antigravity CLI: Claude Sonnet 5.5 when the Codex limit is exhausted (#456).

`agy -p` runs read-only in the clean review worktree: the shell, file writes, URLs and MCP are denied up front
(sketch.DENY_RULES plus a per-run read scope), --dangerously-skip-permissions is never passed, the environment is the sketch allowlist, and
the stream plus the transcripts are checked afterwards: any tool outside the allowlist, or a file path outside the
worktree and this run's own brain folders, refuses the review. Keys are never read here."""

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


def command(agy, prompt, schema_file, model, timeout_s, settings):
    return [agy, "-p", prompt, "--model", model, "--print-timeout", f"{timeout_s}s", "--mode", "plan",
            "--output-format", "stream-json", "--json-schema", str(schema_file), "--settings", settings]


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
    AgyFailed on anything else."""
    from . import sketch  # sketch -> visual -> review -> agyreview: imported late to avoid a cycle

    brain_dir = brain_dir or sketch.BRAIN_DIR
    runner = runner or sketch.default_runner
    try:
        agy = sketch.find_agy(which)
        # Reads are limited by rules, not only checked afterwards: the diff is untrusted and may carry injected instructions.
        with sketch.settings_file([worktree, brain_dir], [brain_dir]) as settings:
            rc, out, err = runner(command(agy, prompt, schema_file, model, timeout_s, settings), str(worktree), timeout_s + 60)
    except sketch.SketchError as e:
        raise AgyFailed(f"{e.code}: {e}") from None
    ids, result, _ = sketch.parse_stream(out)
    calls, missing = sketch.transcript_calls(brain_dir, ids)
    roots = sketch.run_roots(worktree, brain_dir, ids)
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
    usage = result.get("usage") if isinstance(result.get("usage"), dict) else {}
    return answer, {k: v for k, v in usage.items() if type(v) is int}, init_model(out) or model
