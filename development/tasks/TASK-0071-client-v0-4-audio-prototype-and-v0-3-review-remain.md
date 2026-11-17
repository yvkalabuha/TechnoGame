---
id: TASK-0071
title: "Client v0.4: audio prototype and v0.3 review remainder"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0070]
approved_by: owner
approved_at: 2026-11-17
approval_ref: "DECISIONS_LOG #296, #298, #301"
created: 2026-11-17
---


## Goal
Client v0.4 in src/Singularity.Client.Spike. Two parts: the audio prototype (#298) and the remaining review items from v0.3 (#301). Core, Session and Kernel stay unchanged.

## Part A: audio prototype
- Spec: docs/audio/AUDIO_DIRECTION.md (sound map, priorities P0–P3, fatigue rules, synthesis recipes for ~15 sounds, LUFS targets, Godot notes).
- Write an offline synthesis generator in `tools/audio/`. Python with numpy, or C#. It must be deterministic and write OGG/WAV into the client assets. The recipes are those of the direction document; normalise to the target loudness.
- In the client:
  - audio buses (Master, Music, Ambient, UI, Alerts);
  - an `AudioDirector` that maps simulation and UI events to sounds by priority;
  - fatigue rules: aggregation, voice limit, ducking;
  - alerts can be turned down but not off (#298);
  - volume sliders in a small settings popover.
- Music: 1–2 temporary CC0 tracks, played in sessions with pauses (#298), with sources and licences in LICENSES.md. Ambient: one CC0 or CC BY bed. Licences PD/CC0/CC BY only; record each file.
- If a download is refused, stop and report.

## Part B: v0.3 review remainder
- Code review (TASK-0070):
  - SignalField.cs:150 maps by `.Actor` (medium);
  - the language segment shows the active language;
  - Applied/pending status edge cases and non-command job errors;
  - F3 marks the panel dirty;
  - InvariantCulture formatting;
  - Fatal log printed once;
  - the `GetTranslationObject` deprecation;
  - the power segment localized;
  - the launcher shows import errors.
- Visual review (docs/ui/prototypes/client-v02-review.md, «Второе ревью (v0.3)»):
  - no API names in the text;
  - rewrite the «Цели нет» phrase;
  - the leader line and the goal label must not overlap;
  - acknowledged signal = calm frame with a red ■;
  - Jura heading contrast;
  - no faux-bold;
  - numbers in prose set in mono;
  - comma decimal separator in RU;
  - the orphan "13" and the missing №20;
  - the insert height and screen B fill.

## Acceptance
- `./run-globe-spike.sh` and `--shots` work, and the shots include the "снят" and "СБОЙ" signal states.
- An audio demo: the `--shots` run also records a short WAV of an event sequence into docs/ui/prototypes/client-v04/ for review.
- The main solution's full `dotnet test` is green.
