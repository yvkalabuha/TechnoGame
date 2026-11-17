---
id: TASK-0063
title: "Session DailyTickProbe flag for direct daily-mode benchmark (ADR-0016 T1a)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: core
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0016, EXP-0007]
approved_by: orchestrator
approved_at: 2026-11-17
approval_ref: "ADR-0016 ACCEPTED (#276), light task inside approved plan"
created: 2026-11-17
---


## Goal (T1a)
Execution-only flag `DailyTickProbe` in `SliceRunConfig`: an empty daily system so that every day is a non-empty tick (table copy, hash, view snapshot), per ADR-0016 §5 and note §2. It adds no columns. A test proves that slice golden hashes are unchanged with the flag off, and that the flag is execution-only (no StateHash effect beyond tick scheduling, as specified in the note).

## Inputs
- docs/decisions/ADR-0016-tick-budget.md (ACCEPTED, rev 5)
- docs/architecture/notes/tick-benchmark-m0.md §2, §5
- DECISIONS_LOG #269–#272, #276

## Acceptance
- Matches the ADR-0016 rules; the full `dotnet test` is green; zone_check is clean.

## Результат
**Сделано.** Флаг `SliceRunConfig.DailyTickProbe` (execution-only, по умолчанию выключен, не хешируется, колонок не добавляет). При флаге в runner добавляется пустая дневная система `DailyProbeSystem` (SystemId 900, последняя в списке; Compute/Merge/Post пустые), поэтому каждый день `RunTick` копирует таблицы и делает swap. `StepOne` при флаге делает хеш и снимок на каждом тике. Как требует note §2, исход среза не меняется: тот же итоговый StateHash.
- `TestApiVersion` 7 -> 8 (правило ADR-0014 п. 6 для любого изменения `SliceRunConfig`); закреплён `Api/session-surface.v8.txt`; `ApiSurfaceTests` ждёт 8.
- Тесты `DailyTickProbeTests` (4): флаг выключен по умолчанию, прогон по умолчанию = явный «выкл»; флаг вкл/выкл дают один StateHash и один View.StateHash (14 мес.); при флаге 90 хешей за 90 дней, без флага < 10; L1/L2 с флагом: потоки 1/4, ChunkSize 1/3 дают один хеш.
- Golden-хеши: все существующие golden-тесты Session зелёные без правок.

**Файлы:** `src/Singularity.Session/Slice/DailyProbeSystem.cs` (новый), `.../SliceRunConfig.cs`, `.../SliceWorld.cs`, `src/Singularity.Session/SliceSession.cs`, `tests/Singularity.Session.Tests/DailyTickProbeTests.cs`, `.../Api/session-surface.v8.txt`, `.../Api/ApiSurfaceTests.cs`.

**Проверка:** `dotnet test`: Kernel 316 пройдено/1 пропущен; Core 1058/0; Session 349/2 пропущено, 0 упавших. Lab.Tests не запустились: Lab не собирается (см. ниже).

**Блокер вне зоны:** `src/Singularity.Lab/Models/Slice/JetSliceModel.cs:19` держит `SurfaceVersion = 7` и сверяет с `TestApiVersion`; после bump до 8 компилятор даёт CS0162 (ошибка сборки Lab). Нужна правка в одну строку `SurfaceVersion = 8` (Lab запрещён по условию задачи; файл правит агент TASK-0065). Lab.Tests не прогонялись.
