---
id: TASK-0086
title: "Architecture test: one writer per column (CoW open tracing in test mode)"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: developer         # ответственный агент
category: core
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0006, ADR-0017, ADR-0019, ADR-0021]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "ADR-0006 rule one writer per column; prerequisite named by ADR-0019/0021 gates; light"
created: 2026-11-18
---

# TASK-0086 — Architecture test: one writer per column (CoW open tracing in test mode)

## Цель
Проверять правило ADR-0006 «одну колонку в compute/merge пишет одна система» автоматически. Это условие ACCEPTED для ADR-0019 и ADR-0021.

## Описание
1. В Kernel — тестовый режим трассировки (выключен по умолчанию, без цены в игре): при открытии `Column<T>.Next` (CoW, ADR-0017) и при записи в merge фиксируется пара (система, колонка). Записывать идентификатор текущей системы через контекст тика, не через стек.
2. Тест в Session.Tests/Architecture.Tests: прогон среза M0 (несколько месяцев, с командами и зондом) — у каждой колонки не более одного писателя; список разрешённых исключений пуст.
3. Документировать режим в `docs/architecture/` (одна строка в ARCHITECTURE.md).

## Критерии приёмки
- [ ] Тест зелёный на текущем коде; на искусственной второй записи — падает с именами систем и колонки.
- [ ] Трассировка выключена в Release-игре (замер пустого тика не меняется).
- [ ] `dotnet test Singularity.sln` и `lab selftest` (golden без изменений) зелёные.

## Результат
Сделано: `ColumnWriteTrace` (Kernel) + `Table.Trace` (по умолчанию null). `TickRunner` пишет id текущей системы в `Table.TraceSystemId` перед Compute и Merge каждой системы (только при включённой трассировке; не стек). При включённой трассировке `Column<T>.Next` остаётся на медленном пути (`_open` не взводится, копия учтена отдельным `_copied`), поэтому видна каждая запись, а не только открытие. Горячий путь `Next` и пустой тик не изменены (ни одной новой проверки). Тест `tests/Singularity.Session.Tests/OneWriterPerColumnTests.cs`: срез M0, 6 месяцев, `PlayerScript` (команды) и зонд — нарушений нет, исключений нет; искусственная вторая запись падает с id систем и именем колонки (тест на `Table` с двумя писателями); трассировка по умолчанию выключена. Строка добавлена в `docs/architecture/ARCHITECTURE.md`.
Проверка: `dotnet test Singularity.sln` — все зелёные; `lab selftest` 48/48, golden не менялся. Замер пустого тика не делал (в Lab нет готового бенчмарка пустого тика): код ветки пустого тика и горячего пути `Next` не менялся, трассировка вынесена в медленный путь.
