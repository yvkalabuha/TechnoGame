---
id: TASK-0098
title: "Move remaining double code from Core.Simulation to Lab"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
- [x] В Core нет `double` (архитектурный тест без исключений R4).
- [x] `dotnet test Singularity.sln`, `lab selftest` зелёные, golden без изменений.

## Результат
- `ParamSet`, `LabModel` moved to `src/Singularity.Lab/Simulation` (`Singularity.Lab.Simulation`); `SimulationContractTests` to Lab.Tests; `Core_HasNoDouble` covers all of Core, R4 exceptions removed. Commits 8a70100, review fix, merge f958c95.
- Review: APPROVE. Tests 2250 pass / 3 skip (pre-existing); `lab selftest` 49/49; golden unchanged.
- Docs erratum: LAYER_CONTRACT.md, lab-cli.md. ADR-0003 text describes the state before the move (ACCEPTED, not edited).
