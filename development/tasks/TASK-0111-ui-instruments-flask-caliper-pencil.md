---
id: TASK-0111
title: "UI instruments: Flask, Caliper, Pencil controls and E1 gauge legibility at 100% (UI_INSTRUMENTS §8 steps 3-5, DEBT-0059)"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: developer         # ответственный агент
category: client
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0108]
approved_by: orchestrator
approved_at: 2026-11-19
approval_ref: "#404, #409, UI_INSTRUMENTS §8 (light)"
created: 2026-11-19
---

# TASK-0111 — UI instruments: Flask, Caliper, Pencil controls and E1 gauge legibility at 100% (UI_INSTRUMENTS §8 steps 3-5, DEBT-0059)

## Goal
Next slice of the UI instruments (`docs/ui/UI_INSTRUMENTS.md` §8 steps 3–5), plus DEBT-0059.

## Description
1. FlaskControl: Science flask: level = research progress; hatching = threshold reached without discovery; stock-deficit hatching belongs to the Production flask (follow-up task) and is checked here only by the standalone greyscale probe. Forbidden under plates and in tables. The Production stock flask is moved to a follow-up task (one Session alloy-coverage field; Orchestrator decision of 2026-11-19; the ids are not in this branch yet, the Orchestrator adds the links on landing).
2. CaliperControl: Finance budget shares, step 5, the number and the order as for the current bar, keyboard arrows, eras E1–E3.
3. PencilMarker on the Science and Finance input fields.
4. DEBT-0059: at 100 % UI scale the E1 speed gauge has a larger dial and stronger tick contrast.

Locked areas get no layer 2 (#409). Own procedural drawing. No per-frame allocations in `_Draw()`.

## Acceptance criteria
- [ ] Client tests and contrast rules pass; `--shots` of each control in E1–E3 at 100 % and 150 %.
- [ ] Code review APPROVE; ui-art-director PASS.

## Scope after owner decision #417 and Orchestrator decisions O-004, O-008 (2026-11-19)
This section replaces the matching lines of "Description" and is the scope a review checks against:
- The speed gauge as an instrument is NOT shipped. The speed control is the plain four-cell meter with a text value and unit next to it. DEBT-0059 (E1 dial at 100 %) is closed by removing the dial.
- PencilMarker is on Finance only: Science has no input field in the client.
- Finance has one caliper for the selected share (not five); the other shares are plain rows.
- Each instrument has one reading layout in every era; the era changes only the skin.
- Frames: the minimal screen is Full HD (#424, #425). Frames at 1920x1080, UI scale 100 % and 150 %, come with TASK-0117 (scale policy); until then frames are resamples of the 1600x900 render.
