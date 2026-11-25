"""Gemini visual second opinion through the Antigravity CLI subscription (TASK-0190, #441, #544).

The frames are copied into a private workspace that is the only readable folder (rules plus a bwrap Box, as in
agyreview); agy opens them with its file viewer. --dangerously-skip-permissions is never passed, the shell, writes,
URLs and MCP are denied, the stream and the transcripts are audited afterwards. Keys are never read here."""

import functools
import re
import shutil
import tempfile
from pathlib import Path

from . import agyreview

MODEL = "gemini-4-neon-high"  # the model of the 3D comparison (#544); list: `agy models`
DEFAULT_TIMEOUT_S = 900
AGY_RPM = 2
AGY_RPD = 20  # careful guess of the subscription's daily room, like sketch.AGY_RPD; shared agy-usage.json
OVERLOAD_RE = re.compile(r"\b503\b|overload|No capacity|UNAVAILABLE|capacity", re.I)
EXT = {"image/png": ".png", "image/jpeg": ".jpg"}


class AgyOverloaded(agyreview.AgyFailed):
    """Server capacity error: the attempt is not counted against the daily limit."""


def workspace_files(frames):
    """[(name, mime, bytes)] -> [(file name inside the workspace, frame name)]: neutral names, no path tricks."""
    return [(f"frame{i:02d}{EXT[mime]}", name) for i, (name, mime, _) in enumerate(frames, 1)]


def build_prompt(system, task, criteria, files, ws, direction=None):
    lines = [system, "", f"Task: {task}",
             "Open every frame with the file viewer (read_file) before you answer. Frame files (path = frame name to use in findings):"]
    lines += [f"- {ws}/{f} = {name}" for f, name in files]
    lines += ["", "Criteria:", criteria]
    if direction is not None:
        lines += ["", "Visual direction:", direction]
    lines += ["", "Do not use any other tool. Answer only with the JSON object of the schema."]
    return "\n".join(lines)


def run(prompt, frames, schema_file, model=MODEL, timeout_s=DEFAULT_TIMEOUT_S, runner=None, brain_dir=None,
        which=shutil.which, ws_parent=None):
    """(answer text, usage dict, model name). prompt is a function of the workspace path. Errors as agyreview.run_agy,
    plus AgyOverloaded for a capacity error."""
    from . import sketch
    from .sandbox import SandboxUnavailable

    parent = Path(ws_parent or sketch.AGY_WS_DIR)
    parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="agy-visual-", dir=parent) as d:
        ws = Path(d).resolve()
        files = workspace_files(frames)
        for (f, _), (_, _, data) in zip(files, frames):
            (ws / f).write_bytes(data)
        text = prompt(ws, files)
        try:
            if runner is not None:
                return agyreview._run_agy(text, ws, schema_file, model, timeout_s, brain_dir or sketch.BRAIN_DIR, None,
                                          runner, which)
            with sketch.private_box(cwd_writable=False) as (box, private_brain):
                return agyreview._run_agy(text, ws, schema_file, model, timeout_s, private_brain, sketch.BRAIN_DIR,
                                          functools.partial(sketch.default_runner, box=box), which)
        except sketch.SketchError as e:
            if e.code == "sandbox_unavailable":
                raise SandboxUnavailable(str(e)) from None
            raise agyreview.AgyFailed(f"{e.code}: {e}") from None
        except agyreview.AgyFailed as e:
            if OVERLOAD_RE.search(str(e)) and not isinstance(e, AgyOverloaded):
                raise AgyOverloaded(str(e)) from None
            raise
        except OSError as e:
            raise agyreview.AgyFailed(f"agy could not start: {e}") from None
