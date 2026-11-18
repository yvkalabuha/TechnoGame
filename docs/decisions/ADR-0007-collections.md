---
id: ADR-0007
title: "Запрет коллекций с нестабильным порядком обхода"
status: PROPOSED
date: 2026-11-02
supersedes:
superseded_by:
related: [ADR-0002, ADR-0003, ADR-0004, ADR-0006, ADR-0016, ADR-0017, ADR-0019, ADR-0020, TASK-0087]
approved_by:
approved_at:
approval_ref:
---

# ADR-0007 — Запрет коллекций с нестабильным порядком обхода

> После ACCEPTED текст не меняется. Изменение решения — новый ADR с `supersedes`.
> Ревизия 3 (systems-architect, 2026-11-18): правки gate rev 2 (`docs/reviews/adr-0003-0005-0007-0008-critic.md`, `…-advocate.md`, `adr-0007-deepseek.md`). Уже действующие запреты кода (`BannedConcurrent`, `string.GetHashCode`) перенесены в решение; добавлены запреты хеша строк, культурозависимых сравнений, потоковых примитивов; правило ничьей в сортировке; allowlist мест `Parallel.For`; проверка ToyFunnel в Lab (#375). Вопрос владельцу снят.
> Ревизия 2 (2026-11-18): текст приведён к архитектурному тесту `DeterminismRules`, действующему с M0.

## Краткий вывод
- Правило проверяется IL-тестом `DeterminismRules` на Kernel, Core, Session и на пространстве имён ToyFunnel в Lab.
- Запрещено перечислять hash-упорядоченные коллекции; потокоупорядоченные (`Concurrent{Queue,Stack,Bag}`, `Channel*`) запрещены целиком; `string.GetHashCode` запрещён. Это уже в коде.
- Новые запреты (light-задача developer): хеш строк через `HashCode`/`StringComparer`/`EqualityComparer<string>`; сравнение и сортировка строк, зависящие от культуры; `Task`/`Thread`/`Parallel.Invoke`/`BlockingCollection`.
- Параллелизм — только `Parallel.For` в двух местах по allowlist: `TickRunner.RunTick` и `ViewBuilder.Inputs`.
- Сортировка: ключи уникальны или есть явная ничья (по индексу).

## Контекст
- [Факт] Порядок обхода `Dictionary`/`HashSet` зависит от хешей и истории вставок и удалений; `string.GetHashCode`, `StringComparer.Ordinal.GetHashCode(string)`, `EqualityComparer<string>.Default` в .NET Core рандомизированы на процесс (Marvin). Порядок `Concurrent*`, каналов и `Parallel.ForEach` зависит от планировщика. Культурное сравнение строк зависит от `CurrentCulture` и версии ICU/NLS — от ОС. Любой такой порядок, влияющий на состояние, ломает L1/L2 или L3 (ADR-0002).
- [Факт, код] `tests/Singularity.Architecture.Tests/DeterminismRules.cs` (общий для ADR-0003, -0004, -0007):
  - `UnorderedCollections`/`UnorderedNonGeneric`: `Dictionary<,>` (и `KeyCollection`/`ValueCollection`), `HashSet<>`, `ConcurrentDictionary<,>`, `ConcurrentBag<>`, `IDictionary<,>`, `IReadOnlyDictionary<,>`, `ISet<>`, `IReadOnlySet<>`, `FrozenDictionary<,>`, `FrozenSet<>`, `ImmutableDictionary<,>`, `ImmutableHashSet<>` (и `Builder`), `IImmutableDictionary<,>`, `IImmutableSet<>`, `Hashtable`, `IDictionary`, `HybridDictionary`, `StringDictionary`. У них запрещены `GetEnumerator`/`CopyTo`, у Frozen — `Keys/Values/Items`;
  - `BannedConcurrent` — **любое использование** (не только обход): `ConcurrentQueue<>`, `ConcurrentStack<>`, `ConcurrentBag<>`, `Channel`, `Channel<>`, `ChannelReader<>`, `ChannelWriter<>`; фикстуры `UsesConcurrentQueue`, `UsesChannel`;
  - `string.GetHashCode` (в т.ч. `object.GetHashCode` над строковым получателем) — фикстура `UsesStringHashCode`;
  - косвенный обход (`EnumeratesIndirectly`): в методе, который касается неупорядоченного типа, запрещён **любой** вызов `GetEnumerator` (включая `foreach` по `List<T>`), любой метод `Enumerable` и любой вызов с параметром `IEnumerable`/`IEnumerable<>` (`string.Join`, `new List<T>(IEnumerable)`);
  - запрещены члены `DateTime.Now/UtcNow/Today`, `DateTimeOffset.Now/UtcNow`, `Environment.TickCount(64)`, `Guid.NewGuid`, `Parallel.ForEach(Async)`, `ParallelEnumerable.AsParallel`, типы `Random`, `Stopwatch`.
- [Факт, код] Не ловятся: `HashCode.Combine`/`HashCode.Add` со строковым аргументом, `StringComparer.*.GetHashCode(string)`, `EqualityComparer<string>.Default.GetHashCode`; культурные сравнения (`string.Compare` без `StringComparison`, `string.CompareTo`, `Comparer<string>.Default`, `SortedDictionary<string,…>`/`SortedSet<string>` без компаратора, `OrderBy` по строке без компаратора, `Array.Sort`/`List.Sort` строк без компаратора); `Task.Run`, `Thread`, `Parallel.Invoke`, `BlockingCollection<>`.
- [Факт, код] Тест получает `Assembly.GetTypes()` — включая вложенные и сгенерированные компилятором типы (тела лямбд `<>c`, итераторы, async). Поэтому лямбды и итераторы покрыты.
- [Факт, код] `Parallel.For` в сборках мира: `TickRunner.RunTick` (compute по чанкам, `src/Singularity.Kernel/TickRunner.cs`) и `ViewBuilder.Inputs` (views по акторам, `src/Singularity.Session/Views/ViewBuilder.cs`). В Lab — много мест (`MatrixRunner`, стенды Smoke); Lab вне правила.
- [Факт] Единственная общая запись в compute — идемпотентное открытие `Column<T>.Next` под `lock` (ADR-0017 п. 16; внесено в `.claude/rules/csharp-core.md`). Порядок merge — ADR-0006 и ADR-0017 п. 11 (swap в порядке регистрации); для нескольких таблиц — ADR-0020 п. 2.
- [Факт, код, TASK-0087] ToyFunnel перенесён в Lab (`Singularity.Lab.Models.ToyFunnel`); его golden — `tests/Singularity.Lab.Tests/ToyFunnel/ToyFunnelParityTests.cs`. Lab `DeterminismRules` не проверяется.

## Решение
1. **Сфера.** Сборки мира и того, что из него показывается: `Singularity.Kernel`, `Singularity.Core`, `Singularity.Session` — весь код, а не только «влияющий на состояние» (отличить автоматически нельзя). Плюс в Lab — пространство имён `Singularity.Lab.Models.ToyFunnel` (golden-модель EXP-0001): `DeterminismRules.Check` без `forbidDouble`.
2. **Запрещено перечислять** hash-упорядоченные коллекции (`UnorderedCollections`/`UnorderedNonGeneric`) и брать у Frozen-коллекций `Keys/Values/Items`. Метод, касающийся такой коллекции, не вызывает ничего, что перечисляет косвенно (любой `GetEnumerator`, `Enumerable.*`, параметр `IEnumerable`); поиск выносится в отдельный метод.
3. **Запрещены целиком** потокоупорядоченные контейнеры (`BannedConcurrent`: `ConcurrentQueue<>`, `ConcurrentStack<>`, `ConcurrentBag<>`, `Channel*`) и, новое, `BlockingCollection<>`.
4. **Хеш строк запрещён:** `string.GetHashCode` (действует); новое — `HashCode.Combine`/`HashCode.Add` с аргументом типа `string`, `StringComparer.*.GetHashCode(string)`, `EqualityComparer<string>.Default.GetHashCode`. Поиск по строковому ключу в словаре разрешён (п. 7): порядок хеша наружу не выходит, обход запрещён п. 2.
5. **Культурозависимые сравнения строк запрещены** (новое): `string.Compare`/`StartsWith(string)`/`EndsWith(string)`/`IndexOf(string)`/`LastIndexOf(string)` без `StringComparison.Ordinal*` (в .NET они культурные по умолчанию; `string.Equals` без аргумента ordinal и разрешён), `string.CompareTo`, `Comparer<string>.Default`, `ToLower()`/`ToUpper()` без `Invariant`, `StringComparer.CurrentCulture*`/`InvariantCulture*`, `CompareInfo.*`; конструкторы `SortedDictionary`/`SortedSet`/`SortedList`/`ImmutableSorted*` с ключом `string` без `IComparer`; `OrderBy`/`ThenBy`/`Array.Sort`/`List.Sort` по строкам без компаратора. Строки сравниваются только `StringComparer.Ordinal`/`StringComparison.Ordinal`.
6. **Сортировка.** `Array.Sort`/`List.Sort` нестабильны. Ключ сортировки, влияющей на состояние, уникален или дополнен явной ничьей (индекс строки, ordinal ID). Для L1/L2 нестабильность безопасна (тот же алгоритм), правило защищает L3 и обновления runtime. Проверяется ревью (пункт чек-листа reviewer), не тестом.
7. **Разрешено:** массивы, `List<T>`, `ImmutableArray<T>`, сортированные коллекции с явным ordinal-компаратором для строк, обход по индексу, поиск по ключу в любой коллекции.
8. **Параллелизм.** Только `Parallel.For` по индексу чанка (или строки view) с записью в свои колонки или буферы чанка; merge — в порядке чанков (ADR-0006, ADR-0017 п. 11, ADR-0020 п. 2). Места вызова — **allowlist**: `TickRunner.RunTick` (Kernel), `ViewBuilder.Inputs` (Session); новое место — правкой allowlist с ревью. Запрещены `Parallel.ForEach(Async)`, `Parallel.Invoke`, `AsParallel`, `Task.Run`/`Task.Factory.StartNew`, `new Thread`, `ThreadPool.*`, общие изменяемые коллекции в compute.
9. **Источники недетерминизма запрещены:** `DateTime.Now/UtcNow/Today`, `DateTimeOffset.Now/UtcNow`, `Environment.TickCount(64)`, `Guid.NewGuid`, `Random`, `Stopwatch` (замеры — только в хосте Lab, ADR-0016).

## Рассмотренные альтернативы
| # | Вариант | Плюсы | Минусы |
|---|---|---|---|
| **A** | **IL-тест на рефлексии (выбран, работает с M0)** | без пакета; видит компилированный код, включая лямбды и итераторы | консервативен: в методе с поиском по словарю запрещён и `foreach` по `List<T>`, и `string.Join` (лечится выносом поиска в метод); нет подсказки в IDE |
| B | Свой Roslyn-анализатор | ошибка в IDE | отдельный проект; не видит IL после lowering; дублирует A |
| B′ | `Microsoft.CodeAnalysis.BannedApiAnalyzers` + `BannedSymbols.txt` (advocate) | ошибки в IDE и при сборке без своего кода | пакет в Core (правка `CoreProject_PackageReferences_AreExactlyApproved`); не ловит обход и косвенные случаи. Необязательное дополнение к A, отдельной light-задачей |
| C | Только ревью и `csharp-core.md` | нулевая стоимость | ошибки проходят, L2 ловится поздно хешами |
| D | Разрешить обход `Dictionary` при «не влияет на состояние» | меньше ограничений | автоматически не проверить |
| E | Стабильная сортировка (`OrderBy`) везде вместо правила ничьей | порядок равных определён | аллокации LINQ в горячем пути; ничья всё равно должна иметь смысл для L3 |

## Последствия
- Плюсы: «порядок хеша» и «порядок потоков» закрыты тестом для перечисленных API; культура ОС не влияет на порядок; новые API добавляются строкой списка и фикстурой.
- Минусы: ложные срабатывания консервативного правила п. 2; правило ничьей п. 6 — только ревью; API вне списков (новые типы .NET) не покрыты до добавления.
- Обязательно: новая сборка мира подключает `DeterminismRules.Check` в своём архитектурном тесте; новое место `Parallel.For` — через allowlist.

## Проверка
- `tests/Singularity.Architecture.Tests`: `Kernel_FollowsDeterminismRules`, `Core_FollowsDeterminismRules`, `Session_FollowsDeterminismRules_AndHasNoDouble`; фикстуры — `DeterminismRulesTests.cs` (в т.ч. `Parallel.ForEach`, `UsesStringHashCode`, `UsesConcurrentQueue`, `UsesChannel`).
- [Предл., light developer] Фикстуры и правила: `HashCode.Combine(string)`, `HashCode.Add(string)`, `StringComparer.Ordinal.GetHashCode(string)`, `EqualityComparer<string>.Default.GetHashCode`; `string.Compare(a, b)`, `a.CompareTo(b)`, `new SortedDictionary<string, int>()`, `list.Sort()` над `List<string>`, `OrderBy(s => s)`; `Task.Run`, `new Thread`, `Parallel.Invoke`, `BlockingCollection<int>`. Положительные: те же с `StringComparer.Ordinal`/`StringComparison.Ordinal`.
- [Предл., light developer] `ParallelFor_CallSites_AreAllowListed`: в Kernel/Core/Session вызовы `Parallel.For` только из `TickRunner.RunTick` и `ViewBuilder.Inputs` (включая их лямбды).
- [Предл., light developer] `LabArchitectureTests.ToyFunnel_FollowsDeterminismRules`: `DeterminismRules.Check` по типам `Singularity.Lab.Models.ToyFunnel` (без `forbidDouble`); golden — `ToyFunnelParityTests` в `tests/Singularity.Lab.Tests/ToyFunnel/`.
- L2 в CI: `TickRunnerL2StressTests` (потоки × `ChunkSize`), Lab `selftest` (1 и 4 потока).

## Разногласия
| # | тема | позиции | итог |
|---|---|---|---|
| 1 | «Дыры» `string.GetHashCode`, `ConcurrentQueue/Stack`, `Channel` | ревизия 2: не закрыты; critic, advocate, DeepSeek: уже в коде | Перенесены в решение как действующие (п. 3–4); вопрос владельцу снят |
| 2 | Культурные сравнения | advocate, DeepSeek: не ловятся | Запрет п. 5, light-задача |
| 3 | `ViewBuilder` с `Parallel.For` | DeepSeek: план (F4-A ADR-0017), не код | Факт кода: `ViewBuilder.Inputs` вызывает `Parallel.For` (`ViewBuilder.cs`); оставлено в allowlist |
| 4 | Ссылка на ADR-0017 п. 16 для порядка merge | DeepSeek: п. 16 — исключение `lock`, не порядок | Порядок merge — ADR-0006, ADR-0017 п. 11, ADR-0020 п. 2; п. 16 — только про `lock` (внесено в `csharp-core.md`) |
| 5 | ToyFunnel в Lab | critic: выпал из проверки | `Check` для его пространства имён (п. 1) |
| 6 | BannedApiAnalyzers | advocate: добавить | Необязательное дополнение (B′), не часть решения |
| 7 | Синтезированный `GetHashCode` у record (вызывает `EqualityComparer<string>.Default.GetHashCode`) | TASK-0092: десятки record в Core/Session | Исключён по `CompilerGeneratedAttribute`: значение идёт только в поиск, обход хеш-коллекций запрещён отдельно; ручной `GetHashCode` со строкой ловится (Orchestrator, light, 2026-11-18) |
