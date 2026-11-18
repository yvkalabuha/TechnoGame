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
Сделано: ToyFunnel перенесён из Core в Lab (`git mv`, история сохранена).
- `src/Singularity.Core/Science/ToyFunnel` -> `src/Singularity.Lab/Models/ToyFunnel`, пространство имён `Singularity.Lab.Models.ToyFunnel`. Каталог `Core/Science` стал пустым и исчез.
- Тесты `tests/Singularity.Core.Tests/ToyFunnel` и `Reference/toy_funnel_refs.py` -> `tests/Singularity.Lab.Tests/` (пространство имён `Singularity.Lab.Tests.ToyFunnel`, путь к скрипту в комментарии обновлён).
- `CoreArchitectureTests.cs`: из `DoubleExceptions` удалена запись ToyFunnel; осталось только `Singularity.Core.Simulation`.
- `ModelRegistry.cs` — новый `using`; `SelfTest.cs` — проверка источника модели теперь ждёт сборку `Singularity.Lab`.
- Манифест EXP-0001 не менялся: имя модели `toy-funnel` то же, путей манифеста перенос не касается.
Проверка: `dotnet test Singularity.sln` — всё зелёно (Architecture 111, Core 993, Lab 121, Session 372 + 2 skip, Localization 131); `lab selftest` — 48/48, golden не изменился.
Не тронуто (вне зоны): ссылки на `Core.Science.ToyFunnel` в ADR-0003, LAYER_CONTRACT, TECH_DEBT — их правит Orchestrator.
