---
id: TASK-0165
title: "M1: Kernel entity tables mechanism (ADR-0029, T-M1-03), no golden change"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M1
agent: developer
category: core
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "owner answer 2026-11-25 (M1 parallel); docs/architecture/notes/m1-task-plan.md"
created: 2026-11-25
---

# TASK-0165 — M1: Kernel entity tables mechanism (ADR-0029, T-M1-03), no golden change

## Цель
Механизм отдельных таблиц сущностей в Kernel по ADR-0029 (World с несколькими таблицами, handle (row, generation), дочерняя таблица NextRange, однопоточное открытие CoW) без смены эталонов.

## Описание

## Критерии приёмки
- [ ]

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
