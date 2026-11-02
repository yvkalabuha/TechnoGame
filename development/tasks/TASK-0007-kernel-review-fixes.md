---
id: TASK-0007
title: "Kernel: исправления по ревью и аудиту тестов"
status: IN_PROGRESS
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0001]
related: [EXP-0001]
approved_by: orchestrator
approved_at: 2026-11-02
approval_ref: light follow-up of TASK-0001 (owner-approved pilot, DECISIONS_LOG #12)
created: 2026-11-02
---

# TASK-0007 — Kernel: исправления по ревью и аудиту тестов

## Цель
Закрыть находки reviewer и test-engineer по TASK-0001 до реализации модели в Core (TASK-0004).

## Описание
1. **F1 (баг):** переполнение `ChunkCount` при большом `chunkSize` (TickRunner, конструктор) → 0/−1 чанков, Compute не вызывается. Тест `TickRunnerEdgeTests.HugeChunkSize_StillGivesOneChunk` сейчас падает.
2. **IL-тест, ложные пропуски:** `double.Pow/Exp/Log/Sin/Cbrt/Round` и прочие статические методы `System.Double` — тот же allow-list, что для `System.Math`; запрет `System.Half`; обход `Dictionary`/`HashSet` через `.Keys`/`.Values` (KeyCollection/ValueCollection), через `IEnumerable<>.GetEnumerator` и через LINQ. Негативные примеры в `DeterminismRulesTests`.
3. **Контракт ITickSystem:** записать в doc, что всё постоянное состояние системы — только в колонках Table (иначе StateHash его не видит).
4. **Post:** передавать в Post тик нового состояния (t+1) или переименовать поле так, чтобы ошибиться было нельзя; обновить тест `Post_ContextTick_IsTheTickThatWasComputed`.
5. Исключение в Compute: одинаковый тип исключения при 1 и N потоках.
6. `ChunkCount` доступен из Kernel до создания систем; системы не считают его сами.
7. `Fx6.FromDouble` doc: `floor(fl(value·10^6))`. `DetMath.PowInt`/`PowDyadic`: проверка `IsFinite` результата.
8. `Column<T>`: конструктор `private protected`.
9. **F2 (round-trip Fx6→double→Fx6):** не менять поведение; добавить в doc `Fx6` правило: запасы (деньги, ресурсы, население) обновляются только Fx6-арифметикой; через double проходят только производные величины. Окончательно — в ADR-0003 (systems-architect).

## Критерии приёмки
- [ ] `dotnet test Singularity.sln` — все тесты зелёные, включая 52 теста аудита test-engineer.
- [ ] Для каждого пункта 2 — падающий до исправления негативный пример.
- [ ] Нет изменений эталонных векторов RNG и StateHash (или явное обоснование).

## REVIEW
- [ ] Code review (reviewer)
- [ ] Тесты (test-engineer)

## Результат
Сделано (developer, 2026-11-02). Проверка: `dotnet build` — 0 предупреждений; `dotnet test Singularity.sln` — Architecture 26/26 (было 14), Kernel 147/147 (было 135, из них 3 падали по F1). Kernel-тесты прогнаны 5 раз подряд — стабильно.

1. **F1:** `TickRunner.ChunkCountFor` считает в `long`; границы чанка в `ComputeChunk` — тоже в `long`. `HugeChunkSize_StillGivesOneChunk` зелёный.
2. **IL-правила** (`tests/Singularity.Architecture.Tests/DeterminismRules.cs`): статические методы `System.Double` проходят через allow-list (allow-list `System.Math` + `IsFinite/IsNaN/...`, операторы `op_*`); `System.Half` запрещён; `Dictionary.KeyCollection/ValueCollection.GetEnumerator` запрещён; консервативное правило: метод, который касается неупорядоченной коллекции, не может перечислять ничего косвенно (`IEnumerable<>.GetEnumerator`, `System.Linq.Enumerable`, любой вызов с параметром `IEnumerable`/`IEnumerable<>`). 12 новых негативных примеров в `DeterminismRulesTests` — все 12 падали до правки правил; `Clean` дополнен (double.IsFinite/Sqrt, поиск в Dictionary/HashSet, LINQ по List) и остаётся чистым.
3. Doc `ITickSystem`: постоянное состояние только в колонках `Table`.
4. `Post` получает `TickContext` с `Tick = t+1` (= `runner.Tick`); doc `TickContext.Tick` — «индекс состояния в `Current`». Аудит-тест переименован в `Post_ContextTick_IsTheTickOfTheNewState`, ожидает 2.
5. Исключение из `Compute` всегда `TickComputeException` (SystemId, Chunk, Tick, inner) при 1 и N потоках; при параллельном прогоне — минимальный чанк среди наблюдённых. Тест `ComputeException_SameTypeAndChunk_ForAnyThreadCount` (1/4/16 потоков).
6. `public static int TickRunner.ChunkCountFor(rowCount, chunkSize)`; `DiffusionTestSystem` в тестах использует его.
7. Doc `Fx6.FromDouble`: `floor(fl(value·10^6))`. `DetMath.PowDyadic` бросает `OverflowException`, если `x^|e|` или результат не конечны (переполнение `PowInt` ловится через ту же проверку). Аудит-тест `PowDyadic_LargeExponent_OverflowsToInfinity_Documented` заменён на `PowDyadic_LargeExponent_Overflow_Throws` (строже).
8. Конструктор `Column<T>` — `private protected`; `ElementSize`/`WriteLittleEndian` — тоже `private protected` (protected-члены у класса, который нельзя наследовать вне Kernel, бессмысленны). Тест `ColumnOfT_CannotBeDerivedOutsideKernel`.
9. Doc `Fx6`: запасы обновляются только Fx6-арифметикой, через double — только производные величины; окончательно — ADR-0003.

Эталонные векторы RNG и StateHash не менялись (файлы `CounterRngTests`, `StateHashTests` не трогались; хеши L1/L2 совпадают).

Файлы: `src/Singularity.Kernel/{TickRunner,Column,Fx6,DetMath}.cs`; `tests/Singularity.Architecture.Tests/{DeterminismRules,DeterminismRulesTests}.cs`; `tests/Singularity.Kernel.Tests/{TickRunnerTests,TableTests}.cs`, `Audit/{TickRunnerEdgeTests,DetMathPropertyTests}.cs`.
