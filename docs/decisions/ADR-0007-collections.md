---
id: ADR-0007
title: "Запрет коллекций с нестабильным порядком обхода"
status: PROPOSED
date: 2026-11-02
supersedes:
superseded_by:
related: [ADR-0002, ADR-0003, ADR-0004, ADR-0006, ADR-0017]
approved_by:
approved_at:
approval_ref:
---

# ADR-0007 — Запрет коллекций с нестабильным порядком обхода

> После ACCEPTED текст не меняется. Изменение решения — новый ADR с `supersedes`.
> Ревизия 2 (systems-architect, 2026-11-18): текст приведён к архитектурному тесту `DeterminismRules`, который действует с M0. Добавлены контекст, альтернативы, последствия.

## Краткий вывод
- Правило действует и проверяется IL-тестом на сборках Kernel, Core и Session; Roslyn-анализатор не нужен.
- Тест строже ревизии 1: обходить нельзя любые hash- и thread-упорядоченные коллекции, включая интерфейсы `IDictionary`/`ISet`, Frozen- и Immutable-хеш-коллекции. Метод, который касается такой коллекции, не может ничего перечислять косвенно (LINQ, `foreach` по интерфейсу).
- Параллелизм — только `Parallel.For` по чанкам в `TickRunner` (и в построении views Session); `Parallel.ForEach` и `AsParallel` запрещены целиком.
- Две дыры теста, которые стоит закрыть: `string.GetHashCode` (рандомизирован по процессу) и `ConcurrentQueue`/`ConcurrentStack` (порядок зависит от потоков).

## Контекст
- [Факт] Порядок обхода `Dictionary`/`HashSet` зависит от хешей и истории вставок и удалений; `string.GetHashCode` в .NET Core рандомизирован на процесс. Порядок `Concurrent*` и `Parallel.ForEach` зависит от планировщика. Любой такой порядок, влияющий на состояние, ломает L1/L2 (ADR-0002).
- [Факт, код] `tests/Singularity.Architecture.Tests/DeterminismRules.cs` (общий для ADR-0003, -0004, -0007):
  - неупорядоченные: `Dictionary<,>` (и `KeyCollection`/`ValueCollection`), `HashSet<>`, `ConcurrentDictionary<,>`, `ConcurrentBag<>`, `IDictionary<,>`, `IReadOnlyDictionary<,>`, `ISet<>`, `IReadOnlySet<>`, `FrozenDictionary<,>`, `FrozenSet<>`, `ImmutableDictionary<,>`, `ImmutableHashSet<>` (и их `Builder`), `IImmutableDictionary<,>`, `IImmutableSet<>`, `Hashtable`, `IDictionary`, `HybridDictionary`, `StringDictionary`;
  - запрещены `GetEnumerator`/`CopyTo` у них, `Keys/Values/Items` у Frozen-коллекций; консервативное правило: метод, который касается неупорядоченного типа, не перечисляет ничего косвенно;
  - запрещены члены: `DateTime.Now/UtcNow/Today`, `DateTimeOffset.Now/UtcNow`, `Environment.TickCount(64)`, `Guid.NewGuid`, `Parallel.ForEach(Async)`, `ParallelEnumerable.AsParallel`, а также типы `Random` и `Stopwatch`.
- [Факт, код] Тест применяется к Kernel (`KernelArchitectureTests`), Core (`CoreArchitectureTests`), Session (`SessionArchitectureTests`); отрицательные фикстуры — `DeterminismRulesTests.cs`. Lab не проверяется: он вне состояния мира.
- [Факт, код] Поиск по ID разрешён и используется (`ObjectStore` — `SortedDictionary` с ordinal). `Parallel.For` есть в `TickRunner` (compute по чанкам) и `ViewBuilder` (views, по строкам).
- [Факт] ADR-0017 п. 16 (ACCEPTED) — единственная разрешённая общая запись в compute: открытие `Column<T>.Next` под `lock`.

## Решение
1. **Сфера.** Сборки, влияющие на состояние мира и на то, что из него показывается: `Singularity.Kernel`, `Singularity.Core`, `Singularity.Session`. Правило применяется ко всему коду этих сборок, а не только к «коду, влияющему на состояние»: отличить его автоматически нельзя.
2. **Запрещено перечислять** hash- и thread-упорядоченные коллекции (полный список — в `DeterminismRules.UnorderedCollections`/`UnorderedNonGeneric`), получать от Frozen-коллекций `Keys/Values/Items`. Метод, касающийся такой коллекции, не перечисляет ничего косвенно; поиск выносится в отдельный метод.
3. **Разрешено:** массивы, `List<T>`, `ImmutableArray<T>`, сортированные коллекции с явным компаратором (`StringComparer.Ordinal` для строк), обход по индексу, поиск по ключу в любой коллекции.
4. **Параллелизм:** только `Parallel.For` по индексу чанка с записью в свои колонки или буферы чанка; merge — в порядке чанков (ADR-0006, ADR-0017 п. 16). `Parallel.ForEach`, `AsParallel`, общие изменяемые коллекции в compute запрещены.
5. **Источники недетерминизма запрещены:** `DateTime.Now/UtcNow/Today`, `DateTimeOffset.Now/UtcNow`, `Environment.TickCount`, `Guid.NewGuid`, `Random`, `Stopwatch` (замеры — только в хосте Lab, ADR-0016).
6. **[Предл.] Расширение списка** (задача developer, light): `string.GetHashCode()`/`HashCode.Combine` над строками, `ConcurrentQueue<>`, `ConcurrentStack<>`, `System.Threading.Channels.Channel<>` как неупорядоченные.

## Рассмотренные альтернативы
| # | Вариант | Плюсы | Минусы |
|---|---|---|---|
| **A** | **IL-тест на рефлексии (выбран, работает с M0)** | без отдельного пакета; видит компилированный код, в т.ч. лямбды и итераторы | консервативен (ложные срабатывания лечатся выносом поиска в метод); нет подсказки в IDE |
| B | Roslyn-анализатор | ошибка прямо в IDE | отдельный проект и версия Roslyn; не видит IL после lowering; дублирует A |
| C | Только ревью и правило `csharp-core.md` | нулевая стоимость | ошибки проходят, L2 ловится лишь поздно хешами |
| D | Разрешить обход `Dictionary` при «не влияет на состояние» | меньше ограничений | автоматически не проверить |

## Последствия
- Плюсы: класс ошибок «порядок хеша» закрыт тестом на всех трёх сборках мира; новые типы коллекций добавляются одной строкой.
- Минусы: ложные срабатывания консервативного правила; авторы должны выносить поиск по словарю в отдельный метод.
- Обязательно: новая сборка, участвующая в состоянии мира, подключает `DeterminismRules.Check` в своём архитектурном тесте.

## Открыто перед ACCEPTED (вопрос владельцу)
1. Включить расширение п. 6 в ADR до ACCEPTED (тогда gate после задачи developer) или принять ADR сейчас, а расширение сделать light-задачей внутри принятого правила? [Предл.] Второе: п. 6 не меняет принцип, только список.

## Проверка
- `tests/Singularity.Architecture.Tests`: `Kernel_FollowsDeterminismRules`, `Core_FollowsDeterminismRules`, `Session_FollowsDeterminismRules_AndHasNoDouble`; фикстуры нарушений — `DeterminismRulesTests.cs` (в т.ч. `Parallel.ForEach`).
- L2 в CI: равенство хешей при разном числе потоков и `ChunkSize` (`TickRunnerL2StressTests`, `matrix` Lab — `thread_mismatch = 0`).
