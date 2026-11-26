# Kernel под несколько таблиц: набросок API и оценка правки (ADR-0020 R2)

> Автор: systems-architect, 2026-11-18. Задача: TASK-0090. Основа: ADR-0020 rev 3 (§1 п. 1–8, «Проверка» п. 1–3), `docs/reviews/adr-0019-0020-reviewer.md` (раздел ADR-0020), код `src/Singularity.Kernel/*` и `src/Singularity.Session/Slice/SliceWorld.cs` на 9c7f4f7.
> Статус: записка с предложениями. Код не менялся. Golden-вектор (§5) задаёт байты прообраза; дайджест SHA-256 считает developer первым тестом (у меня нет запуска кода; см. OPEN-5).

## Краткий вывод
- [Предл.] Новый тип `World` (упорядоченный список именованных `Table`) — единственная единица тика, хеша и версии. `Table` получает `Name` и `SchemaVersion`. `ITickSystem` получает одно обязательное свойство `Table? ChunkTable`; `Compute`/`Merge`/`Post` — без изменения сигнатур. `TickContext.Table` → `TickContext.World` + `TickContext.ChunkTable`.
- [Предл.] `TickRunner` хранит число чанков **на систему** (`ChunkCountOf(s)`), `_failures` — по максимуму; `BeginTick`/`Swap`/`EndTick` идут по всем таблицам в порядке списка. `ChunkTable = null` — система только merge/post (ответ на замечание reviewer о 5000 пустых compute для `ScenarioEventSystem`).
- [Предл.] `ShortColumn` + `Column.Kind` (enum) — отпечаток схемы переезжает в Kernel (`WorldSchema.Fingerprint`) с исчерпывающим `switch`.
- [Факт: grep на 9c7f4f7] Объём: Kernel — 6 файлов + 1 новый (~+300/−80 строк); Core — 6 файлов, по 1–10 строк; Session — 3; Lab — 6; тесты — ~45 файлов, механически (91 вызов `new Table(`, ~60 `new TickRunner(`/`StateHash.Compute`, ~35 реализаций `ITickSystem`). [Предп.] 1,5–2,5 дня developer + 0,5 дня перевыпуск golden.
- [Факт] Все закреплённые хеши меняются (новый формат, ADR-0020 п. 7): ~57 литералов в `src/`+`tests/`, ожидания в `lab/manifests/EXP-0002*.json`. **Миграцию делать после закрытия EXP-0007** (идёт, TASK-0066), иначе его golden несравним.

## 1. Сигнатуры

### 1.1 `Table` (изменения)
```csharp
public sealed class Table
{
    public Table(string name, int rowCount, int schemaVersion = 0); // name: ^[a-z][a-z0-9_]*$, ≤ 64 байт UTF-8
    public string Name { get; }
    public int SchemaVersion { get; }          // пишется в заголовок хеша этой таблицы (ADR-0020 п. 7)
    public World? World { get; }               // задаётся один раз конструктором World; вторая привязка — исключение
    public ShortColumn AddShort(string name);  public ShortColumn GetShort(string name);
    // Trace, TraceSystemId, FullCopyMode — переезжают в World (см. 1.2); Table.Trace удаляется.
    // BeginTick/Swap/EndTick остаются internal, их вызывает только World.
}
```
- [Предл.] `SchemaVersion` — свойство таблицы, а не аргумент `StateHash.Compute`: в формате п. 7 версия своя у каждой таблицы, иначе её неоткуда взять. `Table(int rowCount)` удаляется (без перегрузки-заглушки: имя таблицы входит в хеш, имя по умолчанию спрячет ошибку).

### 1.2 `World` (новый, `World.cs`)
```csharp
public sealed class World
{
    public World(params Table[] tables);       // ≥ 1 таблица; имена уникальны (Ordinal); порядок = порядок хеша и сейва
    public IReadOnlyList<Table> Tables { get; }
    public Table Get(string name);             // KeyNotFoundException
    public long Version { get; }               // Σ Table.Version (ADR-0020 п. 8); считается на лету, O(таблиц)
    public bool FullCopyMode { get; set; }     // применяется ко всем таблицам
    public ColumnWriteTrace? Trace { get; set; }
    internal int TraceSystemId { get; set; }
    internal void BeginTick();                 // все таблицы в порядке списка
    internal void Swap();                      // все таблицы; каждая растит свою Version, если что-то свопнула
    internal void EndTick(bool completed);     // все таблицы, в finally
}
```
- Таблицы добавляются только в конструкторе: число и порядок таблиц постоянны за прогон (как `n_rows`, ADR-0020 п. 3).
- `Column.Open` проверяет `Owner.InTick` как сейчас; флаг ставит `World.BeginTick` **всем** таблицам → `Load` любой таблицы мира внутри тика бросает (ADR-0020 «Проверка» п. 3). Таблица вне мира: `Next` бросает (флаг не поднят — текущее поведение), `Load` разрешён — это setup.
- Трасса записывает `"{table}.{column}"`: имена колонок уникальны только внутри таблицы (`status` может быть в двух).

### 1.3 `TickContext`, `ITickSystem`
```csharp
public readonly struct TickContext
{
    public World World { get; }
    public Table? ChunkTable { get; }          // таблица чанков текущей системы; null в Post и у merge-only системы
    public CounterRng Rng { get; } public long Tick { get; } public TickCalendar? Calendar { get; }
}
public interface ITickSystem
{
    int SystemId { get; }
    Table? ChunkTable { get; }                 // новая обязательная; читается один раз в конструкторе TickRunner
    void Compute(in TickContext context, int chunk, int rowStart, int rowEnd);   // rowStart/End — строки ChunkTable
    void Merge(in TickContext context, int chunkCount);                          // chunkCount — по ChunkTable, 0 при null
    void Post(in TickContext context);
    Cadence Cadence => Cadence.Daily;
}
```
- [Предл.] `ChunkTable` без реализации по умолчанию: умолчание «первая таблица мира» скрыло бы ошибку выбора таблицы (именно она меняет L2).
- `ChunkTable = null`: compute не вызывается, `Merge(ctx, 0)`. Для `ScenarioEventSystem` (ADR-0020 п. 5, 25) — вся работа в merge; ChunkSize 1 больше не даёт тысячи пустых вызовов.

### 1.4 `TickRunner`
```csharp
public TickRunner(World world, ulong seed, IReadOnlyList<ITickSystem> systems, int chunkSize, int maxThreads, TickCalendar? calendar = null);
public static int ChunkCountFor(int rowCount, int chunkSize);   // без изменений: системы строят буферы до раннера
public int ChunkCountOf(int systemIndex);                        // замена ChunkCount
public World World { get; }
```
- Конструктор: `system.ChunkTable` — `null` или таблица **этого** мира по ссылке (`ReferenceEquals`), иначе `ArgumentException`; `_chunkCounts[s] = ChunkCountFor(ChunkTable?.RowCount ?? 0, chunkSize)`; `_failures = new TickComputeException?[max(_chunkCounts)]`, перед каждой системой `Array.Clear(_failures, 0, _chunkCounts[s])`.
- `ComputeChunk` режет по `ChunkTable.RowCount` своей системы; `TickContext` строится на систему (struct, без аллокаций).
- `RunTick`: `_world.BeginTick()` → compute по системам → merge → `_world.Swap()` → `finally _world.EndTick(completed)` → post. Пустой тик (ADR-0006) — без изменений.
- Порядок цепочки чанков и правило «границы зависят только от `chunkSize`» не меняются: для каждой системы граница — функция `(ChunkTable.RowCount, chunkSize)`.

### 1.5 Колонки, хеш, отпечаток
```csharp
public enum ColumnKind { Long, Int, Short }                 // только добавление
public abstract class Column { public abstract ColumnKind Kind { get; } ... }
public sealed class ShortColumn : Column<short> { ElementSize = 2; WriteInt16LittleEndian }
public static class StateHash { public static byte[] Compute(World world); public static string ComputeHex(World world); }
public static class WorldSchema { public static string Fingerprint(World world); }
```
- `StateHash.Compute(Table, int)` удаляется: один формат (ADR-0020 п. 7, срез M0 — в теге, #346).
- `WorldSchema.Fingerprint`: по таблицам — `"table:{name}:{rows}:{schema_version}\n"`, по колонкам — `"{name}:{code}\n"`, `code = Kind switch { Long => "l", Int => "i", Short => "s", _ => throw }`. `SliceWorld.SchemaFingerprint:410` вызывает его (сейчас `c is IntColumn ? "i" : "l"` — short печатался бы как `l`, замечание reviewer).
- Быстрый путь `AppendCurrentTo` (`MemoryMarshal.AsBytes`) для `short` годится без правки; тест «байтовый путь = быстрый путь» обязателен.

### 1.6 Кэш хеша (Session)
`SliceSession`: `_hashVersion == _world.World.Version` вместо `Table.Version`. `World.Version` монотонна: каждое слагаемое монотонно (ADR-0020 п. 8, reviewer minor). Тип остаётся `long`.

## 2. Формат хеша мира (ADR-0020 п. 7, уточнения)
`int32 table_count`; по таблице: `int32 name_len` (**в байтах UTF-8**), байты имени, `int32 schema_version`, `int32 n_rows`, `int32 n_cols`, затем `Current` колонок в порядке регистрации (LE, 8/4/2 байта). Имена колонок и `Kind` в хеш не входят (как сейчас): их покрывает `schema_version` + тест отпечатка. [Предл.] Ограничение имени `^[a-z][a-z0-9_]*$` убирает вопросы нормализации Unicode; длина-префикс исключает склейку «имя + заголовок».

## 3. План миграции систем
| Шаг | Что | Файлы |
|---|---|---|
| 1 | Kernel: `World`, `ShortColumn`, `ColumnKind`, `Table.Name/SchemaVersion`, трасса в `World`, раннер, хеш, `WorldSchema`; тесты Kernel переписаны | `World.cs` (нов.), `Table.cs`, `Column.cs`, `ColumnWriteTrace.cs`, `TickRunner.cs`, `StateHash.cs`; 14 файлов `tests/Singularity.Kernel.Tests` |
| 2 | Core: `public Table? ChunkTable => _table;` в 4 системах (таблица уже приходит в конструктор); `ToyFunnelModel` (с TASK-0087 — в Lab, `src/Singularity.Lab/Models/ToyFunnel/`) и `ILabModel` — `World World` вместо `Table Table` + `SchemaVersion` переезжает в таблицу | `IndustrySystem.cs`, `KnowledgeSystem.cs`, `PolicySystem.cs`, `ProductsSystem.cs`, `ToyFunnelModel.cs`, `Simulation/LabModel.cs` |
| 3 | Session: мир из одной таблицы `actors` (`SchemaVersion` = `SliceWorld.SchemaVersion`); `SchemaFingerprint` → Kernel; кэш по `World.Version`; `DailyProbeSystem.ChunkTable` | `SliceWorld.cs`, `SliceSession.cs`, `DailyProbeSystem.cs` |
| 4 | Lab: раннер и golden от `World` | `RunExecutor.cs`, `Bench/CopyBench.cs`, `Models/Test/RandomWalkTestModel.cs`, `Smoke/C1Stand.cs`, `C5Stand.cs`, `SmokeT4s.cs` |
| 5 | Тесты Core/Session/Lab: механическая замена конструкторов, `ChunkTable` в тестовых системах | ~29 файлов |
| 6 | Перевыпуск golden: `StateHashTests`, `ToyFunnelParityTests` (23), Session (F17b, T6d, ToolingUnmet, …), `SliceChecks.cs`, `SelfTest.cs`, `lab/manifests/EXP-0002*.json` — одним коммитом с записью «формат хеша ADR-0020 п. 7» | ~15 файлов |

- Шаги 1–5 — один PR (иначе сборка красная), шаг 6 — отдельный коммит в нём же. Новых таблиц миграция не добавляет: `regions`/`provinces`/`events` — задачи 1.6a/2.x.
- [Предл.] Проверка «перевыпуск честный»: до шага 6 прогнать слайс-тесты на старом коде и на новом с **одной** таблицей, сравнить траектории (не хеши) — ряды метрик должны совпасть побитно.

## 4. Оценка объёма
| Слой | Файлы | Строки [Предп.] | Характер |
|---|---|---|---|
| Kernel prod | 6 + 1 нов. | +300 / −80 | содержательно |
| Kernel tests | 14 + 2 нов. (`WorldTests`, `WorldHashGoldenTests`) | +350 / −150 | содержательно + механика |
| Core prod | 6 | +20 / −10 | механика |
| Session prod | 3 | +25 / −15 | механика |
| Lab prod | 6 | +30 / −20 | механика |
| Тесты Core/Session/Lab | ~29 | ±250 | механика |
| Golden | ~15 | литералы | перевыпуск |
[Предп.] Итого 1,5–2,5 дня developer, 0,5 дня reviewer. Риск — не объём, а перевыпуск golden во время EXP-0007.

## 5. Golden-вектор: 3 таблицы
Мир: `actors` (schema 1, 2 строки: `active` int `[1, 0]`, `budget` long `[1 000 000, −1]`), `provinces` (schema 1, 3 строки: `owner` short `[0, 1, −1]`, `controller` short `[0, 1, 1]`), `events` (schema 1, **0 строк**: `status` int, `effective_tick` int). Прообраз — 109 байт (hex, по строкам — поля):
```
03000000                                   table_count = 3
06000000 616374 6f7273                     "actors"
01000000 02000000 02000000                 schema 1, rows 2, cols 2
01000000 00000000                          active  = [1, 0]
40420f0000000000 ffffffffffffffff          budget  = [1000000, -1]
09000000 70726f76696e636573                "provinces"
01000000 03000000 02000000                 schema 1, rows 3, cols 2
0000 0100 ffff                             owner      = [0, 1, -1]
0000 0100 0100                             controller = [0, 1, 1]
06000000 6576656e7473                      "events"
01000000 00000000 02000000                 schema 1, rows 0, cols 2
```
Тест: (1) прообраз собирается вручную и сравнивается с `IncrementalHash` по миру побайтно (обёртка-перехватчик или внутренний `StateHash.WritePreimage(World, Stream)` [Предл.]); (2) дайджест = `SHA256(прообраз)` закрепляется литералом; (3) перестановка `provinces`/`events`, имя `event`, `events` с 3 колонками — другие дайджесты; (4) `ShortColumn` поэлементный путь = быстрый путь.

## 6. Покрытие замечаний reviewer
| Замечание | Где |
|---|---|
| `TickContext.Table` одна | 1.3 |
| `TickRunner._table` / `ChunkCount` / `_failures` | 1.4 |
| `Merge(ctx, chunkCount)` | 1.3 (сигнатура та же, счёт по `ChunkTable`) |
| буферы систем (`ChunkCountFor`, `SliceWorld:108`) | 1.4 (статический метод остаётся), 3 шаг 3 |
| `BeginTick`/`EndTick` всех таблиц | 1.2 |
| `ShortColumn`, отпечаток `:403` (сейчас `:410`) | 1.5 |
| `World.Version = Σ` | 1.2, 1.6 |
| заголовок хеша | 2, 5 (маркер −1 снят ADR-0020 «Разногласия» 7) |
| 5000 пустых compute | 1.3 (`ChunkTable = null`) |
| тесты: golden 3 таблиц, short, кэш по provinces, `Load`/`Next` в тике, 0 строк | 5, 1.2 |

## 7. Открытые вопросы
1. [Предл.] Проверять ли в тестовом режиме, что compute открывает `Next` только колонок своей `ChunkTable` (ADR-0020 п. 2)? Трасса знает систему, таблицу и фазу — дёшево добавить `Phase` в запись. Рекомендую да, в шаге 1.
2. Reviewer TASK-0080 (minor): `InTick` гаснет в `EndTick` до Post, поэтому `Load` в Post разрешён вопреки ADR-0017 п. 12. Миграция трогает те же строки — закрыть здесь же (`EndTick` после Post) или отдельной задачей?
3. `ILabModel.SchemaVersion` уходит в таблицы: формат `run.json` Lab (`golden_hash`) не меняется, но старые `golden_hash` несравнимы — пометка в манифестах EXP-0002 или их перевыпуск?
4. Сейв (ADR-0013): писатель по таблицам ещё не написан (в `Singularity.Session` нет кода сейва) — `save_format_version` растёт при первом коде сейва, не в этой миграции.
5. Дайджест §5 не посчитан мной (нет исполнения кода): developer пишет тест прообраза первым и закрепляет дайджест; reviewer сверяет прообраз с таблицей §5 вручную.
