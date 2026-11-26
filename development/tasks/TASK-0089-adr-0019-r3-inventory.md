---
id: TASK-0089
title: "ADR-0019 R3: inventory of flow columns and non-linear monthly rules in M0 systems"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
tags: [task, core, economy, done]
---

# TASK-0089 — ADR-0019 R3: inventory of flow columns and non-linear monthly rules in M0 systems

## Цель
ADR-0019 «Research до ACCEPTED» R3: опись колонок-потоков и нелинейных месячных правил систем M0 для перевода на дневные срезы.

## Описание
По `docs/decisions/ADR-0019-daily-slices-228.md` (R3) и коду `src/Singularity.Core`: для каждой системы — роль (Flow/Decision/Event), какие колонки пишет, какие величины линейны по времени (режутся `DaySlice` годовой нормы #345), какие нелинейны (затухание, рампы, пороги) и какой дневной параметр нужен (`DayRate`), где используется RNG и с каким ключом. Отчёт — `docs/simulation/adr-0019-r3-inventory.md`, таблица + список рисков и оценка объёма кода #228.

## Критерии приёмки
- [ ] Покрыты все системы среза M0; для каждой нелинейной формулы — предложенный дневной эквивалент и ожидаемая погрешность.

## Результат
Опись — `docs/simulation/adr-0019-r3-inventory.md` (simulation-engineer; перенесено Orchestrator). SliceRoles: KnowledgeFlow; IndustryDecision + IndustryFlow; ProductsFlow; PolicyDecision + PolicyFlow. Перевод параметров — 5 классов (P1 ×12 + DaySlice; P2 DayRate; P3 годовой эквивалент приращения + DaySlice; P4 даты AddMonths; P5 без изменений). 21 нелинейное правило с дневным эквивалентом и аналитической оценкой погрешности (обычно ≤ 1 %, исключения §5). Риски R-1…R-8; R-1 (пороги против дневных объёмов, ×30) и R-2 (разовые деньги сгорают на дневном шаге) — высокие, решить до кода #228. Объём #228 ≈ 2,5–3,9 тыс. строк src/ и 96 тестовых файлов. Погрешности проверяет EXP после кода #228 (§9).
