---
id: TASK-0090
title: "ADR-0020 R2: multi-table Kernel API sketch and hash-bytes check"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: systems-architect
category: core
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0020, ADR-0017]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "ADR-0020 ACCEPTED (#354), Research R2"
created: 2026-11-18
---

# TASK-0090 — ADR-0020 R2: multi-table Kernel API sketch and hash-bytes check

## Цель
ADR-0020 «Research до ACCEPTED» R2: набросок API мира из нескольких таблиц в Kernel и оценка правки `ITickSystem`/`TickRunner`; проверка байтов хеша.

## Описание
По ADR-0020 rev 3 и замечаниям reviewer (`docs/reviews/adr-0019-0020-reviewer.md`, раздел ADR-0020): `TickContext.Table`, `TickRunner._table/ChunkCount/_failures`, `Merge(ctx, chunkCount)`, буферы систем, `BeginTick/EndTick` для всех таблиц, `ShortColumn` и отпечаток схемы, `World.Version`, заголовок хеша с маркером. Результат — `docs/architecture/notes/multi-table-api.md`: сигнатуры, план миграции систем Core, список затронутых файлов, оценка объёма, golden-вектор байтов хеша для 3 таблиц.

## Критерии приёмки
- [ ] Набросок покрывает все пункты reviewer; оценка объёма по файлам; открытые вопросы перечислены.

## Результат
Набросок API — `docs/architecture/notes/multi-table-api.md` (systems-architect, 2026-11-18; перенесено Orchestrator). `World` — упорядоченный список именованных `Table` (`Name`, `SchemaVersion`), `World.Version = Σ`; `BeginTick`/`Swap`/`EndTick` по всем таблицам, трасса пишет `table.column`. `ITickSystem.ChunkTable` (`null` — только merge/post); `TickContext.Table` → `World` + `ChunkTable`; число чанков на систему (`ChunkCountOf`). `ShortColumn`, `ColumnKind`, отпечаток схемы в Kernel (`WorldSchema`, исчерпывающий switch). План миграции в 6 шагов, прообраз golden-вектора хеша 3 таблиц (109 байт; дайджест закрепит developer первым тестом). Объём [Предп.]: Kernel 7 файлов, Core 6, Session 3, Lab 6, ~45 тестовых файлов механически; ~57 закреплённых хешей перевыпускаются — миграцию делать после EXP-0007. Все замечания reviewer закрыты (§6), открытое — §7.
