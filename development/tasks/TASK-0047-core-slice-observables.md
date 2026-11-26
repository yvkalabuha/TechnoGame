---
id: TASK-0047
title: "Core: наблюдаемые величины для печатей среза (t_known, left, причина закрытия линии, варианты Requirement, отказы лицензии по деньгам)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false
depends_on: [TASK-0046]
related: [TASK-0044, ADR-0014]
approved_by: orchestrator
approved_at: 2026-11-13
approval_ref: "Спека среза v0.24 (A40 «Печать причины», A41 F17b «Печать», §5.5, §6.1, §6.11); slice-csv-format.md v0.4 §4.15"
created: 2026-11-13
tags: [task, core, economy, done]
---

# TASK-0047 — Core: наблюдаемые величины для печатей среза

## Цель
Добавить в Core наблюдаемые величины, которые спека v0.24 требует печатать. Сейчас Core их не хранит или не отдаёт. Нужны для T10/T11 и серий `L_C`/`L_P`.

## Описание
Источник: `docs/simulation/slice-csv-format.md` v0.4 §4.15 (список и цитаты спеки), `docs/architecture/SLICE_COMPOSITION.md` §12.6.
1. `t_known` — месяц, когда актор узнал узел. Нужен для `L_C`/`L_P` (спека §6.1). Хранится в `KnowledgeSystem` только в этих режимах или всегда — решает systems-architect.
2. `left` по месяцам ветки С (A40) и классы `COEXIST_MONEY`/`OTHER`.
3. Код причины закрытия линии: `YPROD`, `REPLACE`, `A40C_N`, `A40C_W`, горизонт. Плюс `left_share` (A41 F17b).
4. Варианты `Requirement` (`ProductPresets.Variants`): `n_variants`, `fv_producible`, `fv_superior` (§5.5, §7 S1).
5. Счётчик отказов ИИ от лицензии по деньгам (§6.11, A43.3 (б)).
Затем views Session (TASK-0046) отдают эти величины. Обновляется тест поверхности (`test_api_version`).

6. (Erratum спеки v0.24 r2 §6.1, О-13-1) `startKnownSince = −384` для узлов стартового знания в режимах `L_C`/`L_P`. Проверки `since < 0` в `IndustrySystem.Yield`, `ControlUnitCost`, `ProductsSystem.StepCanonical` → «неизвестен» ровно при −1. В `L_P` — `max(t_known, 0)` для канонического проекта. Main не затрагивается (−384 там не бывает), golden не меняется.
Размещение — `docs/architecture/SLICE_COMPOSITION.md` r4 §13 (#189); формат — `slice-csv-format.md` v0.5.

7. Тест поверхности v2 учитывает nullable-аннотации ссылочных типов (`NullabilityInfoContext`); новый эталон `session-surface.v2.txt`, v1 остаётся (SA, ревью TASK-0046).
8. Codex (TASK-0046, medium): неопределённые значения `GridStep` в `SliceWorld.ApplyGrid` → явный отказ, как для `Layer`.

## Правила
- Наблюдение не меняет динамику. Golden-хеши меняются, только если новое поле входит в хешируемое состояние. Изменение хеша — только с явной причиной и согласием Orchestrator.
- systems-architect заранее проверяет, куда и как ложится каждая величина.

## Критерии приёмки
- [x] Все 5 пунктов реализованы, у каждого есть тест.
- [x] `dotnet test` зелёный, L1/L2 сохранены.
- [x] Формат v0.4 §4.15 переведён в основные таблицы (simulation-engineer).

## REVIEW
- [x] systems-architect (2026-11-14) — соответствует §13; отклонения приняты; r6
- [x] Code review (reviewer, 2026-11-14) — accept; 3 minor + nit исправлены
- [x] Второе ревью (codex, head ee83683a) — accept: 1 medium → DEBT-0029; report sha256 bcaa1c850e2a79d593efc702d549711de20f40cd6f60e84e191e0ebd9ebb1ad9. Раунды: 4ade4ab (high Training — исправлено), ddda145 (high compose месяца 0 — правило пары в формате §4.6)
- [x] Тесты — полный `dotnet test` (Orchestrator, ee83683): Arch 83, Kernel 314, Core 1026, Session 308; 0 падений

## Результат
developer, 2026-11-14 (записал Orchestrator).
- (1) `t_known`: `KnowledgeSystem(trackKnownSince, startKnownSince = −384)`, колонки `know.since.N` только при флаге (Lc/Lp), `Merge` пишет месяц; адаптеры Session читают колонку; `NodeView.KnownSince : long?`.
- (2) `left`: след шага 7 в `ProductsSystem` (`LastLeftStart`, `LastLineMoney`), views `MonthStepView`/`ActorStepView`/`MoneyStepView`/`LineMoneyView`. `Training` = 0 у недопущенной линии (поведение не меняется).
- (3) `LineCloseReason {None, A40C, YProd, LaunchAll, LaunchCandidate}`, `PolicySystem.LastCloseReason`; `Closes : ImmutableArray<CloseView>`.
- (4) `ProductPresets.Variants`, `PolicySystem.RequirementVariants`; общий шаг `TryProducible` для `Compose`/`Variants`; `ActorInputsView.Requirement` — годовые месяцы, ProductAi, не Calibration.
- (5) `PolicySystem.LastMoneyRefusals`, `LicenceRefusalView`.
- (6) `KnownSinceMarks.Unknown = −1`, `StartKnown = −384`; проверки `== −1`; `StepCanonical` — `max(since, 0)`; `Layer.Lc/Lp` отклоняются до таблиц контролей T12.
- (7) `TestApiVersion = 2`, `session-surface.v2.txt` с nullable (`NullabilityInfoContext`), v1 сохранён.
- (8) `GridStep` вне enum → `ArgumentOutOfRangeException`.
- `[Observation]` на 4 аксессорах; IL-тест `ObservationTests`.
- `dotnet test`: Architecture 83, Kernel 314 (1 skip), Core 1026, Session 307 (2 skip); 0 падений; golden и `SchemaVersion` main без изменений.
- Открыто: Session-часть `t_known` проверяема только после таблиц Lc/Lp (T12); формат v0.5 — уточнить `Training` для недопущенной линии (COEXIST_MONEY); переименовать `Layer_EveryDefinedValue_IsSupported`.
