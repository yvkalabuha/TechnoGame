---
id: TASK-0012
title: "Эталон для сверки среза: три формулы и ручная траектория (Python stdlib)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: simulation-engineer
category: lab
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [HYP-0002]
approved_by: orchestrator
approved_at: 2026-11-03
approval_ref: DECISIONS_LOG #48, #49
created: 2026-11-03
---

# TASK-0012 — Эталон для сверки среза: три формулы и ручная траектория (Python stdlib)

## Цель
Независимый эталон для сверки C#-реализации среза (решение #42) — задача T9 спецификации.

## Описание
Источник: `docs/simulation/slice-jet-aviation.md` §11 T9, §6.4 (P0), контракт v2.2 §4.1–4.2.
Python stdlib, вне C#-кода, в `lab/reference/jet_slice/`:
1. Эталонные значения трёх формул: `culture_H`, `production_profile`, hazard открытия — таблица входов и выходов.
2. Ручная эталонная траектория: 1 актор, ~5 узлов, ~24 тика, состояние по тикам; покрывает порядок merge, команду ИИ t → t+1, цикл бюджет → прогресс → hazard → открытие с заданными `NextU64`.
3. Перечень инвариантов и тестов детерминизма для T8.
Числа узлов можно взять условные; от TASK-0011 не зависит.

## Критерии приёмки
- Скрипт воспроизводим (одна команда, без внешних пакетов); вывод в виде, удобном для теста T8.
- Формулы ссылаются на разделы контракта; расхождения с контрактом записаны, не исправлены молча.
