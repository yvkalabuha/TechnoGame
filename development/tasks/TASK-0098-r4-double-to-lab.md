---
id: TASK-0098
title: "Move remaining double code from Core.Simulation to Lab"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M1
agent: developer
category: core
priority: medium
needs_owner: false
depends_on: []
related: [ADR-0003, TASK-0087]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "#389"
created: 2026-11-18
---

# TASK-0098 — Remaining double code from Core.Simulation to Lab (#389)

## Цель
Снять исключение R4 ADR-0003 полностью (#389 расширяет #375).

## Описание
Перенести `ParamSet.GetDouble`, `LabModel` и прочий код с `double` из `Singularity.Core.Simulation` в `src/Singularity.Lab`. Убрать `Singularity.Core.Simulation` из `DoubleExceptions` в `CoreArchitectureTests`. Выполнять после серии EXP-0007 (#355, #376): серия меряет текущую сборку.

## Критерии приёмки
- [ ] В Core нет `double` (архитектурный тест без исключений R4).
- [ ] `dotnet test Singularity.sln`, `lab selftest` зелёные, golden без изменений.
