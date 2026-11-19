---
id: TASK-0115
title: "End-to-end project handle through Session, AI, UI and command log v2 (DEBT-0052)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0113]
related: [TASK-0099, TASK-0113, ADR-0005, ADR-0014]
approved_by: orchestrator
approved_at: 2026-11-19
approval_ref: "DEBT-0052, ADR-0005 p.3 (light)"
created: 2026-11-19
---

# TASK-0115 — End-to-end project handle through Session, AI, UI and command log v2 (DEBT-0052)

## Goal
Close DEBT-0052. The project handle `DesignHandle(Slot, Generation)` becomes mandatory through Session, AI, UI and the command log. Design: `docs/architecture/notes/end-to-end-project-handle.md` (tasks H1–H5, §2–§6).

## Description
- Real bug on current main, per the note: an AI cancel decided in month t applies after an external compose has retaken the slot, and cancels the new project. The same holds for a stale client view, ForceProxy keyed by (actor, design), and WhyNotTarget / CancelPending using bare indices.
- `commands.log` v2 with a header line; v1 logs are rejected loudly, with no migration: no saved logs exist and replay is a build artefact (ADR-0013 p.9). This is an Orchestrator decision.
- SeriesStarts/LineCloses take up to two candidates; Core applies the first one that resolves.
- SchemaVersion and TestApiVersion are bumped. Trajectories stay unchanged: check by projections.
- Starts after TASK-0113 is merged: it touches the same Policy and DesignView code.

## Acceptance criteria
- [ ] Tests H1–H5 from the note: a stale handle from UI, from AI (test H3 fails on old main) and from replay; L2; a property test.
- [ ] SLICE_COMPOSITION.md §7.5 and LAYER_CONTRACT.md updated.
- [ ] `dotnet test`, `lab selftest` pass; reviewer APPROVE.

## Результат
**Сделано (developer, ветка worktree).** `DesignHandle(Slot, Generation)` обязателен во всех слоях, значения «−1 = не передан» нет.
- **Core:** `DesignHandle`; `DesignCommand(Kind, Requirement, Alts, Design)` с фабриками `Compose/Derive/Cancel`; `FleetCommand.Design` и `SeriesStart.Design` — handle; `IProductsInputs.SeriesStarts/LineCloses` отдают до двух кандидатов (внешний, затем ИИ) в `Span` без аллокаций; `ProductsSystem` безусловно проверяет `IsCurrent` для Derive/Cancel/Retire/Sell/StartSeries/CloseLine, применяет первого разрешимого кандидата, каждого устаревшего считает `StaleHandle`; `Resolves(actor, handle)` и `HandleOf`. Кандидат не своего слота — `ProductsInvariantException`.
- **Команды и журнал:** `SliceCommand` с `DesignHandle`; `CommandValidator`: `BadHandle` при `Generation < 1` (устаревание при `Submit` не проверяется); `CommandState` колонки `cmd.dsn/flt/series/close.*.gen`; `CommandCodec` v2: заголовок `{"commands_log":2}`, `design_gen`/`parent_gen`, журнал без заголовка (v1), версия ≠ 2 и строка без `*_gen` — `FormatException` (O-003, без миграции).
- **ИИ:** `PolicyDesign.SlotGeneration`; колонки `pol.dsn/flt/start/close.*.gen`, generation берётся из снимка в момент решения; `CancelPending` сравнивает handle; `IPolicyInputs.NewestSeriesDesign` и `IPolicyEvents.WhyNotDesign` — handle.
- **Session/harness/UI:** `ProductsInputsAdapter` только переносит handle (проверок нет); `ForceProxy._sent` по `(actor, DesignHandle)`; `WhyNotTarget.Design(DesignHandle)`: устаревший handle даёт ответ пустого слота (пустой список; отдельной причины `StaleHandle` в `WhyNotCause` нет, см. отклонение); `DesignView.Handle`; `ActorPanel` берёт handle из `DesignView` и включает generation в сигнатуру перестройки кнопок.
- **Версии:** `SchemaVersion` 10 → 11, `TestApiVersion` 12 → 13, `session-surface.v13.txt` (единственное отличие от v12: `DesignView.Handle` и номер).

**Отклонения от записки.** (1) Устаревший `why_not` возвращает пустой список (как пустой слот, так требует тест H5), а не одну ссылку `StaleHandle`: новая причина в `WhyNotCause` не нужна. (2) Поведение изменилось в одном месте: `start_series` проекта, снятого раньше в том же шаге, и handle пустого слота / вне диапазона теперь `StaleHandle`, а не молчание / `BAD_CANCEL` / `BAD_DERIVE` / `BAD_RETIRE{STAGE}` / `BAD_SELL{STAGE}`. Ожидания пяти тестов обновлены (`ProductsR4Tests` Cancel_Validator, `ProductsV24SystemTests` Derive_Validator, `FleetCommandTests` Retire/Sell refusals, `ProductAiSessionTests` CancelDesign). Golden это не затрагивает: до первого переиспользования слота таких команд в прогонах нет.

**Golden.** Перезаписаны с комментарием в тесте: `F17bTests` (Admit ×5, YProd ×5), `SeriesSwitchTests`/`CalibrationParamsTests`/`ViewTests` (Main seed1 m149), `LinkedRunTests.Golden60`, `ProductAiSessionTests.Golden150`, fingerprint схемы (`SchemaTests`, v11). Причина — только новые колонки `*.gen` и `SchemaVersion` 11. Траектории не изменились: проекции без новых колонок совпали (`T6dOff`, `ToolingUnmet` seed 41 на 384 мес., `FleetAblation`; `ColumnProjection.IsSlotGenerationColumn` исключает `cmd.*.gen`/`pol.*.gen`), `SlotReuseL2Tests` подтверждает, что до месяца golden ни один слот не переиспользован.

**Тесты (новые).** `ProductsHandleResolutionTests` (H1: шесть видов устаревших команд, «внешний устаревший + ИИ валидный», два валидных кандидата, `default`-handle, property с handle из случайного прошлого вида, 6 seeds), `ProductAiRunTests.H3_*` (AI cancel + внешний compose; L2 при 2/4 потоках), `DesignHandleTests` (H2: заголовок, round-trip, v1/без заголовка/версия 3/нет `*_gen`; `BadHandle` при Submit; stale из UI; stale из replay; правка `design_gen` в журнале; L2 по сессии; H4: ForceProxy по handle; H5: why_not). Мутации: без проверки в `Cancel` падают H3 и два теста H1; с ForceProxy без generation и без проверки в WhyNot падают соответствующие тесты. **H3 на старом main не запускался** (отдельную копию main собирать нельзя в зоне): доказано мутацией — возврат старой ветки «Cancel без проверки handle» (эквивалент `Generation = −1` на main) роняет H3.

**Проверка.** Собрано и зелёное: `Singularity.Core.Tests` 1029, `Singularity.Session.Tests` 402 (2 пропущены, как раньше), `Singularity.Kernel.Tests`, `Singularity.Client.Spike` build. **Не проверено** (зона developer не включает `src/Singularity.Lab/**`): `dotnet build Singularity.sln` красный на 4 ошибках в Lab, `Lab.Tests`, `Architecture.Tests` (ссылаются на Lab), `lab selftest`, `TickPathAllocationTests`. Правки Lab — в OPEN отчёта и ниже.

**Нужны правки вне зоны (Orchestrator / simulation-engineer).**
1. `src/Singularity.Lab/Models/Slice/JetSliceModel.cs`: `SurfaceVersion = 12` → `13` (строка 19); строка ~236 `observer.ForcedStart(start.Actor, start.Design, month)` → `start.Design.Slot`.
2. `src/Singularity.Lab/Models/Slice/SliceObserver.cs` строка ~734 (`fleet_cmd`): `I(c.Design)` → `I(c.Design.Slot)`, `sv.Designs[c.Design]` → `sv.Designs[c.Design.Slot]`.
3. `docs/architecture/SLICE_COMPOSITION.md` §7.5: «`commands.log` … JSON Lines» дополнить: «Первая строка — заголовок `{"commands_log":2}` (`CommandCodec.LogVersion`). Команда о проекте несёт handle `(slot, generation)`: `design` + `design_gen` (`parent` + `parent_gen` у derive); отсутствие `*_gen` — ошибка формата. Журнал без заголовка (v1) и версия выше известной отклоняются `FormatException`, миграции нет (ADR-0005 п. 3, ADR-0013 п. 9, ORCH_DECISIONS O-003). Устаревание handle проверяется только при применении (compute месяца), не при `Submit`; команда с устаревшим handle попадает в журнал и при replay отклоняется по состоянию (`StaleHandle`)». В п. «Отказы по состоянию» добавить `StaleHandle`; в строках про `start_series`/`close_line` — «до двух кандидатов (внешний, затем ИИ); применяется первый разрешимый, каждый устаревший считается `StaleHandle`». В §7.3/7.4: `CheckForm` отказывает `BadHandle` при `Generation < 1`.
4. `docs/architecture/LAYER_CONTRACT.md`: в §5 (команды) и §4.4e/§4.4c: `cancel_design`, `derive`, `start_series`, `close_line`, `retire_cohort`, `sell_cohort` принимают `design` как handle `(slot, generation)`; «значения "не передан" нет»; отказ `STALE_HANDLE` (код 18) для handle, который не разрешается в занятый слот (пустой слот, вне диапазона, уже снят/перезанят) — вместо `BAD_CANCEL`/`BAD_DERIVE`/`BAD_RETIRE{STAGE}`/`BAD_SELL{STAGE}` в этих случаях; в §3.1 колонки `cmd.*.gen`, `pol.*.gen`; в §6 `why_not(design)` принимает handle, устаревший — как пустой слот. Строку про «Слоты не освобождаются и не переиспользуются» (G4-8) привести в соответствие с ADR-0005 п. 3 (если ещё не сделано в TASK-0099).
