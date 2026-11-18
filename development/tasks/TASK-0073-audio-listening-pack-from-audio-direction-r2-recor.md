---
id: TASK-0073
title: "Audio listening pack from AUDIO_DIRECTION r2 recordings"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0071]
approved_by: owner
approved_at: 2026-11-18
approval_ref: "DECISIONS_LOG #305, #309"
created: 2026-11-18
---


## Goal
Download the candidate recordings listed in docs/audio/AUDIO_DIRECTION_r2.md. Process them per its rules: trim, EQ, loudness, variations. Assemble a listening pack for the owner. Nothing enters the client until the owner approves (#309).

## Scope
- Use only the candidates with verified PD/CC0/CC BY licences (BigSoundBank CC0 is allowed). Re-check each licence on its page at download time. Skip any that turn out to be NC or SA.
- Extend tools/audio/prepare_external.py, or add a new script. It must be reproducible: pinned URLs and sha256 of the downloaded originals in a manifest. Do not commit originals larger than needed; commit processed OGG.
- Output docs/audio/listening-pack/:
  - per event, 1–3 candidates as OGG with a short label;
  - the five-layer design-bureau ambient bed as a 60 s mix;
  - the music candidates (Scott Buckley «Extrapolation» and «Cobalt», CC BY 4.0), trimmed to 60 s previews;
  - an index.html that plays each with its event name, source, author and licence. Self-contained: relative audio files, no external scripts.
- Attribution entries for every CC BY file.

## Acceptance
- index.html lists every event from the r2 sound map with at least one candidate, or "not found" for the gaps noted in r2.
- The total pack size stays reasonable (< 30 MB).
- If a download is refused, list it as not obtained. Do not work around it.
