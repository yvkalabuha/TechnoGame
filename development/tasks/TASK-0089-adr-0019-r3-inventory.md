---
id: TASK-0089
title: "ADR-0019 R3: inventory of flow columns and non-linear monthly rules in M0 systems"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: simulation-engineer
category: core
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0019]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "ADR-0019 ACCEPTED (#353), Research R3"
created: 2026-11-18
---

# TASK-0089 — ADR-0019 R3: inventory of flow columns and non-linear monthly rules in M0 systems

## Цель
ADR-0019 «Research до ACCEPTED» R3: опись колонок-потоков и нелинейных месячных правил систем M0 для перевода на дневные срезы.

## Описание
По `docs/decisions/ADR-0019-daily-slices-228.md` (R3) и коду `src/Singularity.Core`: для каждой системы — роль (Flow/Decision/Event), какие колонки пишет, какие величины линейны по времени (режутся `DaySlice` годовой нормы #345), какие нелинейны (затухание, рампы, пороги) и какой дневной параметр нужен (`DayRate`), где используется RNG и с каким ключом. Отчёт — `docs/simulation/adr-0019-r3-inventory.md`, таблица + список рисков и оценка объёма кода #228.

## Критерии приёмки
- [ ] Покрыты все системы среза M0; для каждой нелинейной формулы — предложенный дневной эквивалент и ожидаемая погрешность.

## Результат
