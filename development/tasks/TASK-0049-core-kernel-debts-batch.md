---
id: TASK-0049
title: "Долги Core/Kernel: DEBT-0004, 0006, 0011, 0012, 0013, 0017, 0021"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: core
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: orchestrator
approved_at: 2026-11-14
approval_ref: "Владелец 2026-11-14: «долги закончи»"
created: 2026-11-14
---

# TASK-0049 — Долги Core/Kernel: DEBT-0004, 0006, 0011, 0012, 0013, 0017, 0021

## Цель
Закрыть долги ядра DEBT-0004, 0006, 0011, 0012, 0013, 0017, 0021 (`development/TECH_DEBT.md`; полный текст каждого — там).

## Описание
- 0004: `DetMath.CheckFinite` — сообщение через `InvariantCulture`.
- 0006: тест инвариантов ToyFunnel — по `RowCount`, без жёстких 6 стран. Расхождение «Merge vs post» описать одной строкой в спеке ToyFunnel или в контракте (что место проверки оправдано).
- 0011: атрибут `[Scratch]` и IL-проверка ADR-0013 §4 (scratch не читается вне своей системы, не хешируется), с исключениями для дескрипторов колонок и неизменяемых предвычисленных данных. Переиспользовать механизм `[Observation]` (TASK-0047) без дублирования.
- 0012: транзитивная IL-проверка «нет RNG в способности» (через `IIndustryInputs` и другие namespace) и явная проверка источника skilled-запросов.
- 0013: члены `IIndustryInputs` — abstract; Lab (`SmokeT4s`, `LineStand`) реализует их явно.
- 0017: загрузчик мода нормализует CRLF до парсинга; тест на CRLF-мод.
- 0021: план оснастки A39 считается один раз на (актор, месяц) — кэш в scratch или индексная сортировка без LINQ/аллокаций.

## Правила
- Golden-хеши и поведение не меняются ни в одном пункте. Если меняются — остановиться и описать.
- `src/Singularity.Lab/Models/Slice/*` в это время правит simulation-engineer (TASK-0044) — не трогать.

## Критерии приёмки
- [ ] Тест на каждый пункт; полный `dotnet test` зелёный; golden без изменений.
- [ ] Второе ревью Codex — до чистого accept (#181).

## REVIEW
- [ ] Code review (reviewer)
- [ ] Второе ревью (codex)

## Результат

Сделано (golden и поведение не менялись):
- 0004: `DetMath.CheckFinite` форматирует через `InvariantCulture`.
- 0006: `ToyFunnelInvariantTests` — по `RowCount`; спека ToyFunnel §5 поясняет, почему проверка стоит в Merge.
- 0011: `[Scratch]` (поля), `ScratchTests` + общий `OwnerOnlyScan` (его же использует `ObservationTests`). Правила: поле private, не Table/Column, не читается вне своего класса (в т.ч. через не-private член, отдающий буфер); любой изменяемый буфер системы — `[Scratch]` либо в явном списке предвычисленных. Помечены буферы Knowledge/Industry/Products/Policy.
- 0012: `CapabilityNoRngTests` транзитивен (вызовы + реализации интерфейсов в Core/Session); `WorkforceRequestSourceTests` — источник skilled-запросов.
- 0013: `EffortMultiplier` и `ToolingInvest(..., out unmet)` — abstract; Lab (`LineStand`) и тестовые реализации — явно.
- 0017: `StrictYaml.Parse` нормализует CRLF; тест `CrlfMod_LoadsToTheSameHashes`.
- 0021: выбран вариант «индексная сортировка»: LINQ в `ToolingPlan` заменён сортировкой span с детерминированным tie-break (`Position`), без аллокаций. Кэш плана не делался: Plan зовут и в compute (параллельно), и после merge — кэш потребовал бы инвалидации.
