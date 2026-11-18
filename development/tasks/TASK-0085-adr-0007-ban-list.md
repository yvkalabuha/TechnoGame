---
id: TASK-0085
title: "ADR-0007: extend the ban list (string.GetHashCode, Concurrent*, Channel)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: developer         # ответственный агент
category: core
priority: low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0007]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "ADR-0007 rev 2 gap list, light"
created: 2026-11-18
---

# TASK-0085 — ADR-0007: extend the ban list (string.GetHashCode, Concurrent*, Channel)

## Цель
Закрыть пробелы теста `DeterminismRules`, найденные при ревизии ADR-0007 (rev 2).

## Описание
Запретить в Kernel, Core, Session: `string.GetHashCode` (значение зависит от процесса), `ConcurrentQueue`, `ConcurrentStack`, `ConcurrentBag`, `System.Threading.Channels.Channel`. Сначала убедиться, что в коде их нет; если есть — список в «Результат», не переписывать без согласования.

## Критерии приёмки
- [ ] Тест падает на искусственном использовании каждого запрещённого API и зелёный на текущем коде.
- [ ] `dotnet test Singularity.sln` зелёный.

## Результат
