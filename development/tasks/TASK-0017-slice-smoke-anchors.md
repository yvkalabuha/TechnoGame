---
id: TASK-0017
title: "Смоук калибровочных якорей C1–C3 на слоях знания и способности (T4s)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: simulation-engineer
category: lab
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0014, TASK-0015]
related: [HYP-0002]
approved_by: orchestrator
approved_at: 2026-11-03
approval_ref: DECISIONS_LOG #48
created: 2026-11-03
---

# TASK-0017 — Смоук калибровочных якорей C1–C3 на слоях знания и способности (T4s)

## Цель
Задача T4s спецификации среза: показать, что калибровочные якоря C1–C3 достижимы в диапазонах §8.1 на слоях знания и способности, до слоя изделий. Это не калибровка; `tech_lead`/`chg` не существуют и не считаются.

## Описание
Источник: `docs/simulation/slice-jet-aviation.md` (v0.7+) §8.1–§8.2, §11 T4s. Стенды: C3 (один актор), C2 (освоение ★-PRC при фиксированном выпуске), C1 в части канала выхода (`yield`). Прогон через Kernel `TickRunner` с дневным календарём и системами `Core.Knowledge` + `Core.Industry` на данных мода `core`; входы изделий — фиксированные заглушки по спецификации.

## Критерии приёмки
- Отчёт `docs/simulation/smoke-t4s.md`: для каждого якоря — достижим ли он в диапазоне параметров §8.1, на каких значениях; что не достижимо и почему.
- Скрипт/программа прогона воспроизводима одной командой; ни одной колонки лидерства.
