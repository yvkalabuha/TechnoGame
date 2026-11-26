---
id: TASK-0099
title: "Project slots reuse with generation (ADR-0005 #388)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M1
agent: developer
category: core
priority: medium
needs_owner: false
depends_on: []
related: [ADR-0005, TASK-0098]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "#388, ADR-0005"
created: 2026-11-18
---

# TASK-0099 — Project slots reuse with generation (#388)

## Цель
Привести `ProductsLayout` и `ProductsSystem` к ADR-0005 п. 3 (ред. 3, ACCEPTED).

## Описание
Колонка `generation` (int, +1 при занятии слота); выбор слота — наименьший свободный индекс; handle = (slot, generation); устаревший handle — «сущности нет», команда с ним отклоняется с `why_not`; создание отклоняется только при 12 занятых. `generation` входит в StateHash и сейв. Выполнять после серии EXP-0007 (#355).

## Критерии приёмки
- [x] Тесты: переиспользование слота, устаревший handle, отказ при 12 занятых, детерминизм L1/L2 (golden обновляются только с объяснением).
- [x] `dotnet test Singularity.sln`, `lab selftest` зелёные.

## Результат
**Сделано.** Колонки `prd.d{d}.generation`, `prd.d{d}.parentGen`, `prd.lp.g{g}.gen`; `FreeSlot` — наименьший индекс со стадией None или «снят»; `TakeSlot` — +1 (checked) и сброс всех колонок слота. Слот сценария на старте имеет generation 1 (первое занятие), свободный — 0. `ProductsSystem.Generation`, `Resolves` (единая функция разрешения handle), `CanonicalSlot` проверяет пару. `DesignCommand` (Derive/Cancel) и `FleetCommand` получили `Generation` (по умолчанию −1 = handle не передан); устаревший — `CommandRejection.StaleHandle` (18), состояние не меняется. `DesignCapacity` — только когда заняты все `max_designs`. SchemaVersion 8 → 9.
**Файлы:** `src/Singularity.Core/Products/{ProductsSystem,ProductsSystem.Fleet,ProductsLayout,IProductsInputs}.cs`, `src/Singularity.Session/Slice/SliceWorld.cs`, `src/Singularity.Lab/Models/Slice/{SliceObserver,SliceChecks}.cs`, тесты `ProductsSlotGenerationTests` (9) и правки существующих.
**Golden:** все полные хеши изменились из-за новых колонок и SchemaVersion 9 (траектория до месяца 149 не меняется; первое переиспользование слота — около месяца 265). Перезаписаны: Main seed1 m149, F17b Admit/YProd ×5, LinkedRun, ProductAiSession; fingerprint схемы. Проекции «старых колонок» (T6d, FleetAblation, ToolingUnmet) исключают новые колонки и останавливаются на первом переиспользовании слота.
**Проверка:** `dotnet build -c Release` 0 предупреждений; `dotnet test` зелёный; `lab selftest` 49/49.
**Открытые пункты:**
1. Handle доведён только до интерфейсов Core. `StartSeries`/`CloseLine` (SliceCommand, CommandCodec, лог команд), Session-адаптеры, PolicySystem и UI не передают `Generation` (по умолчанию −1: проверка пропускается). Нужна отдельная задача на сквозной handle (формат лога команд, ADR-0014).
2. «Освобождённый» слот = стадия «снят». Побочный эффект: после реюза теряется информация об альтернативах, уже опробованных в «снятом» проекте (novelty в `Create`). Нужно подтверждение владельца, что это соответствует #388.
3. `PolicySystem.Fleet.Candidate` выбирает линию-кандидата по индексу слота; после реюза больший индекс не означает более новый проект. Поведение AI не менялось.
4. ADR-0005 п. 3 говорит «начальное generation 0»; слоты, занятые сценарием на старте, получают 1 (первое занятие). Если это неверно, нужна правка ADR.

**Правки по ревью (CHANGES):**
- `FreeSlot` теперь функция только `Current`: слот свободен, если его стадия в снимке None/«снят» и ни одна команда этого шага его ещё не заняла (generation в Next равна generation в снимке). Слот, освобождённый cancel в этом же шаге, переиспользуется только на следующем шаге; `[Cancel d, Compose, Cancel d]` больше не отменяет новый проект. Тест `CancelThenCompose_InOneStep_...`. **Golden не изменились:** в прогонах с golden такой последовательности в одном шаге нет, все закреплённые хеши остались прежними.
- Один предикат `HandleHolds(stage, generation, handleGeneration)` с обёртками `Resolves` (Current) и `IsCurrent` (Next); документ `CanonicalSlot` исправлен (−1 и после переиспользования).
- `DesignView.Generation` (TestApiVersion 9 → 10, pinned `session-surface.v10.txt`, `JetSliceModel.SurfaceVersion` 10). `SliceObserver` сбрасывает `_tLaunch`, `_forcedSent`, а также `_firstSeries`/`_cum` (если cost_u100 ещё не измерен) по смене generation слота, а не по стадии: это ловит прямой вход в С через L_P `LaunchCanonical`.
- Тесты: `Generation_IsInTheStateHash` сравнивает два состояния, различающихся только generation; `ReuseAfterService_...` (В → «снят» → новый проект, колонки равны колонкам пустого слота, cohorts = 0); комментарий в `FleetRunStats` про технологическое `p.Gen` против generation слота.
- Известное отклонение: на путях Session/AI/UI по-прежнему передаётся голый индекс (Generation = −1, проверка пропускается); последующая работа — сквозной handle (формат лога команд, ADR-0014). Вторая последующая работа — `PolicySystem.Fleet.Candidate` выбирает линию по индексу слота.
- Больше не проверяются: `FleetAblation` на месяце 383 и поздние точки T6d (после первого переиспользования слота траектория уже не совпадает со сборкой без реюза).

- Review: CHANGES then APPROVE (dafb258). test-engineer added coverage (0f40a20). Merge 2a7aad2. Follow-ups: DEBT-0052..0055.
