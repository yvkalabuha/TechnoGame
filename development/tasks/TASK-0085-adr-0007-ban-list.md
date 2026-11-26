---
id: TASK-0085
title: "ADR-0007: extend the ban list (string.GetHashCode, Concurrent*, Channel)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
tags: [task, determinism, testing, done]
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
**Проверка кода.** В Kernel, Core, Session нет `ConcurrentQueue/Stack/Bag`, `Channel` и `string.GetHashCode` (тест зелёный на текущем коде; прочие `GetHashCode` в src — собственные для структур, не строковые). Переписывать нечего.

**Сделано.** В `DeterminismRules`:
- запрещены любые использования `ConcurrentQueue<>`, `ConcurrentStack<>`, `ConcurrentBag<>`, `Channel`, `Channel<>`, `ChannelReader<>`, `ChannelWriter<>` (поля, сигнатуры, локали, вызовы) через `ContainsForbiddenType`;
- запрещён `string.GetHashCode`. C# компилирует вызов как `Object.GetHashCode`, поэтому в IL-сканере добавлен простой разбор статического типа предыдущей инструкции (ldstr, ldarg, ldloc, ldfld, результат вызова); ограничение: получатель типа `T` или значение после арифметики/ветвления не распознаётся.
- В `DeterminismRulesTests` добавлены 5 искусственных образцов (string hash, Queue, Stack, Bag, Channel).

**Файлы.** tests/Singularity.Architecture.Tests/DeterminismRules.cs, DeterminismRulesTests.cs.

**Проверка.** `dotnet test tests/Singularity.Architecture.Tests`: 111/111 зелёные. Полный `dotnet test Singularity.sln`: см. итог в отчёте оркестратору.
