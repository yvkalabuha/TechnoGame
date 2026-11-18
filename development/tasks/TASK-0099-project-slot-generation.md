---
id: TASK-0099
title: "Project slots reuse with generation (ADR-0005 #388)"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
- [ ] Тесты: переиспользование слота, устаревший handle, отказ при 12 занятых, детерминизм L1/L2 (golden обновляются только с объяснением).
- [ ] `dotnet test Singularity.sln`, `lab selftest` зелёные.

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
