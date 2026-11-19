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
1. FlaskControl: Science (research progress) and Production (stocks), with deficit hatching; forbidden under plates and in tables; checked with a greyscale shot.
2. CaliperControl: Finance budget shares, step 5, the number and the order as for the current bar, keyboard arrows, eras E1–E3.
3. PencilMarker on the Science and Finance input fields.
4. DEBT-0059: at 100 % UI scale the E1 speed gauge has a larger dial and stronger tick contrast.

Locked areas get no layer 2 (#409). Own procedural drawing. No per-frame allocations in `_Draw()`.

## Acceptance criteria
- [ ] Client tests and contrast rules pass; `--shots` of each control in E1–E3 at 100 % and 150 %.
- [ ] Code review APPROVE; ui-art-director PASS.
