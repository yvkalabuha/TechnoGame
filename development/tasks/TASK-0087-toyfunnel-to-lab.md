---
id: TASK-0087
title: "ToyFunnel moves from Core to Lab (#375)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: developer
category: core
priority: low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0003]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "#375"
created: 2026-11-18
---

# TASK-0087 — ToyFunnel moves from Core to Lab (#375)

## Цель
Перенести ToyFunnel (учебная модель M0) из Core в Lab и снять исключение R4 ADR-0003 (#375).

## Описание
Найти ToyFunnel и его потребителей (Lab, тесты, EXP-0001). Перенести в `src/Singularity.Lab`, убрать из списка исключений архитектурного теста (`DoubleExceptions` или аналог). EXP-0001 должен воспроизводиться (если путь манифеста меняется — записать в «Результат»).

## Критерии приёмки
- [ ] В Core нет ToyFunnel; исключение R4 удалено; архитектурные тесты зелёные.
- [ ] `dotnet test Singularity.sln`, `lab selftest` (golden без изменений).

## Результат
