---
id: TASK-0108
title: "UI instruments: tokens, settings, contrast rules and GaugeControl (UI_INSTRUMENTS §8 steps 1-2)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: developer         # ответственный агент
category: client
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0101]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "#404, #409, UI_INSTRUMENTS §8 (light)"
created: 2026-11-18
---

# TASK-0108 — UI instruments: tokens, settings, contrast rules and GaugeControl (UI_INSTRUMENTS §8 steps 1-2)

## Goal
First implementation slice of the UI instruments (TASK-0101 concept, `docs/ui/UI_INSTRUMENTS.md`, decisions #404 and #409): steps 1–2 of §8.

## Description
1. Tokens `desk-line`, `desk-hatch`, `ink` for the instruments. Settings "Приборы на столе" and "Приборы-органы" (Выкл/Слабые/Обычные; Общие). §5 rules in `ContrastValidator`, with PASS/FAIL tests.
2. `GaugeControl`: the game speed as a needle gauge, three eras E1–E3 by game date (1945–69, 1970–99, 2000–2100, #409), a fallback "before" look, keys and tooltips unchanged.

Own procedural drawing, no external assets. Locked areas get no layer 2 (#409).

## Acceptance criteria
- [x] Contrast validator rules with tests; client builds; client tests pass.
- [x] `--shots` of the speed gauge in E1–E3 and "before"; ui-art-director visual review PASS.

## Результат
- Tokens, two settings, §5 contrast rules (47 checks), GaugeControl E1–E3 plus "before" (ae74dfd, b5b86a7; merge 422fe7e). Client tests 157/157.
- Code review APPROVE (minors fixed); ui-art-director REJECT then PASS (conditional: E1 at 100 % -> DEBT-0059). Key shots: E1 and E3 at 150 % in docs/ui/prototypes/instruments/shots/.
