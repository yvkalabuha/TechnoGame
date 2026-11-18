---
id: TASK-0086
title: "Architecture test: one writer per column (CoW open tracing in test mode)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
