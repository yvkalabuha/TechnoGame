---
id: TASK-0164
title: "M1: tag the M0 slice where EXP-0002 goldens still match (K6, T-M1-02)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M1
agent: simulation-engineer
category: lab
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "owner answer 2026-11-25 (M1 parallel); docs/architecture/notes/m1-task-plan.md"
review_ref: "orchestrator: note docs/simulation/m0-slice-tag.md read; tag m0-slice-exp0002 -> e15a8bd verified"
created: 2026-11-25
tags: [task, determinism, platform, done]
---

# TASK-0164 — M1: tag the M0 slice where EXP-0002 goldens still match (K6, T-M1-02)

## Цель
Найти коммит, где эталоны EXP-0002 (golden_set_hash main 183e47b9…, det 824f4ded…) ещё совпадают, и предложить тег среза M0 (K6, ADR-0027 п. 5) до слияния TASK-0151…0153.

## Описание

## Критерии приёмки
- [ ]

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
