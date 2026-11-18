---
id: TASK-0093
title: "Lab matrix validity and size fields; registry golden_set_hash for matrix EXPs (ADR-0008, #374)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: simulation-engineer
category: lab
priority: low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0008]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "#374, ADR-0008 rev 3"
created: 2026-11-18
---

# TASK-0093 — Lab matrix validity and size fields; registry golden_set_hash for matrix EXPs (ADR-0008, #374)

## Цель
Поля валидности и объёма матриц Lab по ADR-0008 ред. 3 (#374).

## Описание
1. В `matrix.json`: `csv_bytes` (суммарный объём CSV), флаг `valid` (≥ 2 значений `threads`, `thread_mismatch = 0`, непустой `golden_set_hash`); длительность анализа — в журнал эксперимента (`analysis_seconds`).
2. Тесты Lab на новые поля (если тестовая папка вне зоны — список тестов в «Результат» для test-engineer).
`registry.py` (поле `golden_set_hash` в обязательных для матричных EXP) правит Orchestrator — опишите нужное правило в «Результат».

## Критерии приёмки
- [ ] Поля пишутся; selftest 48/48.

## Результат
