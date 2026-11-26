# Профиль тика M0 на синтетическом мире (решение #283)

> Связано: ADR-0016 (ACCEPTED), EXP-0007 (П6 «Профиль»), TASK-0065 (`src/Singularity.Lab/Bench/SynthWorld.cs`), правила `.claude/rules/csharp-core.md`.
> Статус: профиль до оптимизации, не замер EXP-0007 (условия П5 не соблюдались). Сырые данные: `lab/results/tick-profile-m0/` (не коммитятся).

## Краткий вывод
- [Факт: замеры ниже] Месяц (200 акторов, 2000 узлов, CrossEdges, t8) стоит **80–106 с**, а не ~40 с (смоук TASK-0065); (6, 2000) — 6,7–8,0 с, а не 2,1 с; (200, 500) — 4,6–5,1 с. Рост по узлам ≈ **кубический** (P = 160 → 651 процессов, ×4,07: `Allocate` одного актора 14,3 → 832 мс, ×58 ≈ 4,07^2,9).
- [Факт: профиль] Горячее место — не `KnowledgeGraph.RowOf` сам по себе, а **`EffortAllocator.Allocate` → `CbCultureRule.EUseful` → `Lead`**: 75,5 % managed CPU в (200, 2000). `EUseful(p)` заново вычисляет `Lead(c, d)` — проход по всем процессам кластера, а каждый шаг прохода зовёт `Knows` (линейный `RowOf`, O(узлы)) и `Planned` (строит `PrcSet` всех 12 дизайнов с аллокацией). Итого O(P²·узлы) на сайт ⇒ O(узлы³).
- [Факт] `Allocate` вызывается многократно на одном и том же снимке: на сайт в Industry, на актор в `ViewBuilder.Inputs` (Session, **последовательно на главном потоке**: 75 % стенного времени в (200, 2000)), на строку линии в Products (`TrainingBudget`). `ToolingPlan.Compute` строит план всего актора **на каждую из 12 ячеек** сайта.
- [Факт] Сборщик мусора: 7,4 ГБ аллокаций на месяц в (200, 2000), 16 % CPU — финализаторы и `ArrayPool.Trim` после gen2 (источник — `DesignRules.PrcSet` в `Planned`).
- [Факт] Kernel/Session дневного тика: `StateHash` 37,7 мс на (200, 2000) (370 МБ/с при `sha_ni`), копия 1,9 мс; дневной `ViewBuilder.Snapshot` — 0,17 с/день на (200, 500) (`Nodes`: линейный `IndustryLayout.RowOf` на каждый узел каждого актора).
- [Факт: реплика F1–F3 вне Core, 0 расхождений] `E_useful` всех процессов одного сайта: 261 → 0,74 мс (200, 2000); 464 → 1,18 мс (6, 2000); 9,4 → 0,18 мс (200, 500).
- [Предп.] После F1–F8 месяц (200, 2000, t8) ≈ **0,6–1,2 с** (×80–150). Бюджет ADR-0016 (p95 ≤ 500 мс) этим раундом, вероятно, ещё **не** достигается; остаток — `ToolingPlan`, хеш (K_d ≈ 0,3 с), Yield. Второй раунд — по новому профилю.

## 1. Метод
- Сборка Release, .NET 10.0.14 (SDK 10.0.403), Linux 6.8, 16 логических CPU, сервер GC выкл. Код: рабочее дерево поверх `894fe93` (включая незакоммиченные TASK-0063/0065).
- Харнесс — временный консольный проект вне репозитория (scratchpad), ссылается на `Singularity.Lab`: `SynthWorldGenerator.Build(new SynthWorldSpec(a, n, CrossEdges))`, `SliceSession.Create(... Seed = 1, Threads = 8, ChunkSize = 8, DailyTickProbe = m|d)`, `StepThroughMonth(i)` с Stopwatch на месяц. Команд нет (в отличие от П2 EXP-0007). Core/Session не менялись.
- Профилировщик: `dotnet-trace collect --profile dotnet-sampled-thread-time` (EventPipe SampleProfiler), конвертация в speedscope, свой разбор: exclusive = время листа `CPU_TIME`, отнесённое к вызывающему методу (managed CPU); ожидания пула (`UNMANAGED_CODE_TIME` под `WaitNative` и т. п.) исключены. Инлайнинг JIT сливает мелкие методы в вызывающий (например, `DStar`, `Pick`, `Limiter0`, `Eligible`, `KnowsProcess`, `RowOf` частично видны как exclusive `EUseful`/`Lead`).
- Счётчики вызовов — прокси `ICultureSnapshot`, считающий вызовы при одном `EffortAllocator.Allocate(actor 0)` (снимок месяца 2).
- Реплика исправлений F1–F3 — вне Core: кэш `Knows`/`Planned` на сайт + таблица `Lead(c, d)` через делегаты на internal-методы `CbCultureRule`; сверка `E_useful` с оригиналом по всем процессам.
- Точки: (6, 2000) 3–4 мес., (200, 500) 4 мес. месячный + 3 мес. дневной, (200, 2000) 3 мес. Месяц 0 в таблице — только тик 0 (системный); месяцы ≥ 1 включают дни.

## 2. Сырые числа
| Точка | Режим | Месяцы, мс | Аллок./мес | gen2 | Файл |
|---|---|---|---|---|---|
| (6, 2000) | M | 7989, 7765, 6667, 6825 | 235 МБ | 4 | `time-a6-n2000.txt` |
| (200, 500) | M | 5050, 5079, 4554, 4685 | 1,03 ГБ | 50 | `time-a200-n500.txt` |
| (200, 500) | D | 3285, 7994, 7930 | 1,48 ГБ | 57 | `time-a200-n500-d.txt` |
| (200, 2000) | M | 106206, 81107, 104851 | 7,41 ГБ | 49 | `time-a200-n2000.txt` |

Размеры: P (процессы) = 20 / 160 / 651 при 61 / 500 / 2000 узлах; кластеров 4; дизайнов 12; один сайт на актор. Таблица: 3344 колонки × 200 строк (500 узлов), 8799 × 200 (2000 узлов).

Один `Allocate(actor 0)` (`counts.txt`): P = 20 → 1,75 мс; P = 160 → 14,3 мс; P = 651 → 832 мс. Вызовы снимка при P = 651: `Knows` 346 459, `V` 152 663, `Planned` 143 712, `H` 8816, `GrowthNominal` 5004, `Licensed` 3336.

Хеш/копия (`hash-copy.txt`): (200, 500) 14,3 / 0,54 мс; (200, 2000) 37,7 / 1,88 мс.

## 3. Top-10 по exclusive managed CPU, (200, 2000), месячный, 3 мес. (1493 с CPU всех потоков)
| # | Метод | Доля | Слой | Сложность (на месяц) |
|---|---|---|---|---|
| 1 | `CbCultureRule.EUseful` (+ инлайн `DStar`, `Pick`, `Limiter0`, `KStar`, `Eligible`, `Knows`→`RowOf`) | 51,6 % | Core/Industry | O(P · a · P_c · (узлы + D·\|prc\|)) на сайт; a ≈ 1–3 активных измерения, P_c ≈ P/4 |
| 2 | `CbCultureRule.Lead` | 22,5 % | Core/Industry | O(P_c · (узлы + D·\|prc\|)) на вызов; вызовов O(P·a) на сайт |
| 3 | `GC.RunFinalizers` | 12,1 % | runtime | следствие 7,4 ГБ/мес |
| 4 | `SharedArrayPool.Trim` | 4,3 % | runtime | колбэк gen2 |
| 5 | `ToolingPlan.CellTarget` | 2,9 % | Core/Policy | O(P) на ячейку; план целиком строится 12× на сайт |
| 6 | `Gen2GcCallback.Finalize` | 1,9 % | runtime | gen2 |
| 7 | `KnowledgeGraph.RowOf` (неинлайн. часть) | 1,1 % | Core/Knowledge | O(узлы) на вызов; ~30 мест вызова |
| 8 | `ArraySortHelper.InsertionSort` (`DesignRules.PrcSet`) | 0,5 % | Core/Products | O(D·\|prc\| log) на `Planned` |
| 9 | `SpanHelpers.IndexOf` (`Planned`, `ExpectedVolume`) | 0,4 % | Core/Products | O(D·\|prc\|) |
| 10 | `ToolingPlan.Compute` | 0,3 % | Core/Policy | O(сайты + P) × 12 ячеек |

Inclusive: `EffortAllocator.Allocate` 75,5 %; из него Industry compute ≈ 63 %, `ViewBuilder.Inputs` (Session) ≈ 34 %, Products `TrainingBudget` ≈ 3 % (доли от времени `Allocate`). `ToolingPlan.Compute` 4,5 %; Products 2,4 %.

**Стенное время главного потока** (200, 2000): `StepOne` 510 с, из них `ViewBuilder.Inputs` 385 с (последовательно, рабочие потоки простаивают), `RunTick` 119 с, `ViewBuilder.Snapshot` 7 с, `StateHash` 0,3 с. В (200, 500): 30 / 18,4 / 10,4 / 2,1 / 0,15 с.

**Kernel/Session отдельно.** Месячный режим: Kernel (копия, хеш, swap) < 0,1 % — не горячее место. Дневной (200, 500, 3 мес.): `ViewBuilder.Snapshot` 17,3 с (16,1 с — `Nodes`: `IndustryLayout.RowOf` на узел, проход `LastLicenceGrants` на узел, `Industry.Profile` на процесс), `StateHash` 1,3 с, `CopyCurrentToNext` 0,09 с. Оценка `K_d` (200, 500) ≈ (7,96 − 4,6) с ≈ 3,3 с/мес — почти целиком снимок view.

Профиль (6, 2000): `EUseful` 39 %, `Lead` 15 %, `PollGC` 32 % (ожидание GC); `ViewBuilder.Inputs` 42 % inclusive. При 6 акторах один чанк — параллелизма нет.

## 4. Исправления (спецификация задачи для developer)
Общие требования: поведение и `StateHash` неизменны (golden hash всех существующих тестов и прогонов Lab — тот же); никакой итерации по `Dictionary`/`HashSet`; кэши — массивы по индексу; всё, что вычисляется в compute, — чистая функция снимка (Current). Каждый пункт — отдельный коммит с тестом эквивалентности.

**F1. O(1) поиск строки по номеру (Core/Knowledge, Core/Industry, Session).** [Предл.]
- `KnowledgeGraph.RowOf`, `IndustryLayout.RowOf`: плотный `int[] _rowOfNumber` (размер `MaxNodeNumber + 1`, −1 = нет), строится в `Create`/`FromData`; либо бинарный поиск по отсортированной копии номеров (как `ProductsLayout.ProcIndex`).
- `IndustryInputsAdapter.KnowsProcess`: заранее посчитанный `int[] processRowToGraphRow` (в `SliceWorld`), без поиска.
- Детерминизм: неизменяемые массивы, порядок не участвует. Тест: для всех номеров RowOf новый == старый линейный.

**F2. `Planned`/`ExpectedVolume`/`GrowthNominal` без аллокаций (Core/Products).** [Предл.]
- Хранить `prc_set` дизайна (actor, d) кэшем, пересчитываемым там же, где меняются альтернативы/стадия (merge Products), или строить на месяц `bool[]`/битовую маску «процесс в плане актора» по стадиям (П, О, С отдельно — нужны для `ExpectedVolume` и `GrowthNominal`).
- Минимум без смены структуры: `PrcSet(actor, d)` → `ReadOnlySpan<int>` из кэша, без `List`+`Sort` на вызов.
- Детерминизм: кэш — функция колонок Current; инвалидация в Post/Merge Products и при командах. Тест: свойство «кэш == пересчёт» после каждого тика на (24, 180) с командами.

**F3. Таблица `lead(site, c, d)` в `CbCultureRule` (Core/Industry).** [Предл.]
- Добавить в `ICultureRule` пакетную функцию `void EUsefulAll(ICultureSnapshot s, int site, long month, Span<Fx6> eu)`: один раз считает `Lead(c, d)` для всех C·3 ячеек (O(P) на сайт), затем `DStar`/`Pick`/`EUseful` читают таблицу. `EffortAllocator.Allocate` зовёт её вместо цикла `EUseful`. Одиночный `EUseful` (UI, why_not, `ToolingPlan` V2) остаётся.
- Порядок и правила ничьих не меняются: проход `l.Order`, строгое `>`, первый по номеру.
- Аналогично `LeadsAny`/`Limiter` (why_not) — по желанию.
- Тест: на мирах (5, 61), (24, 180), (96, 500) в нескольких месяцах `EUsefulAll` == поэлементный `EUseful` для каждого сайта; плюс golden hash. Реплика уже дала 0 расхождений (`fix-replica.txt`).

**F4. Один `Allocate` на (актор, месяц) (Core/Industry + Session).** [Предл., требует проверки systems-architect]
- Сейчас: на сайт в compute, на актор в `ViewBuilder.Inputs`, на (актор, линия) в `TrainingBudget`, плюс why_not.
- Вариант A (без общего кэша): `ViewBuilder.Inputs` строить параллельно по акторам (результаты в массив по индексу актора — детерминированно), а `ProductsInputsAdapter.TrainingBudget` считать `Plan`/`RestActor` один раз на актора внутри `Purchases` (локальная переменная цикла по линиям).
- Вариант B (мемо): `EffortAllocation?[]` по актору с ключом «тик снимка»; заполнение через `Interlocked.CompareExchange` (значение — чистая функция снимка, поэтому выигравший поток не влияет на результат); сброс при `CopyCurrentToNext`/swap и при применении команд. Это не состояние (как `_lastRest`, ADR-0013 п. 4), не хешируется и не сохраняется; но правило «compute пишет только свои колонки/буферы чанка» формально задето — нужна запись в ADR-0013/Kernel-заметке.
- Тест: L2 (threads 1/4/8/16, ChunkSize 1/8/32) — одинаковый `StateHash`.

**F5. План оснастки один раз на сайт (Core/Industry, Session).** [Предл.]
- `IndustrySystem.Compute`: `ToolingPlanResult` актора строится один раз на сайт перед циклом ячеек (c, d); `StepTooling` читает `Applied(s, c, d)`/`Unmet`. План читает только Current и `restActor`, одинаковый для 12 ячеек, поэтому результат тот же. Нужен метод в `IIndustryInputs`, отдающий план целиком.
- `ToolingPlan.Compute`: перебирать только сайты актора (список сайтов на актор в `IndustryLayout`), а не все `SiteCount` (сейчас O(сайты) на вызов и массивы размера `SiteCount·C·3` на вызов).

**F6. Сайты актора — индекс (Core/Industry).** [Предл.] `IndustryLayout.SitesOf(actor)` (CSR-массивы). `EffortAllocator.Weight`/`SplitBySite`, `ToolingPlan.Compute` сейчас проходят все сайты на каждый процесс: O(P·N) на актор ⇒ O(P·N²) в месяц.

**F7. Дневной снимок view (Session).** [Предл.]
- `ViewBuilder.Nodes`: F1 для `IndustryLayout.RowOf`; лицензионные гранты — один проход `LastLicenceGrants` в `bool[]` на актора, а не проход списка на каждый узел.
- Тик без месячного шага не меняет ничего, что читают `ActorView`/`NodeView`/`ProcessView` (системы месячные; DailyProbe ничего не пишет): переиспользовать неизменяемые `ActorView` прошлого снимка, менять только тик/хеш. Проверка: ассерт в тесте «снимок дня == снимок, построенный заново».

**F8. Хеш без поэлементной сериализации (Kernel).** [Предл.]
- `Column<T>.AppendCurrentTo`: на little-endian хосте (`BitConverter.IsLittleEndian`) подавать `MemoryMarshal.AsBytes(_current.AsSpan())` одним вызовом; байты те же ⇒ хеш тот же (формат не меняется, не gate). Ожидание [Предп.]: 370 МБ/с → 1,5–2 ГБ/с (sha_ni), 37,7 → ~8–10 мс.
- Дальше (gate+research, ADR-0002/Kernel-ADR, не в этом раунде): инкрементальный хеш только изменённых колонок или более быстрый некриптографический хеш для L2 в дневном режиме.

Порядок работ: F1, F2, F3 (главный выигрыш), затем F5, F6, F4-A, F7, F8. После каждого — `selftest` (раздел `synth:`) и golden hash.

## 5. Оценка после исправлений (200, 2000, t8)
[Предп.; на основе реплики F1–F3 и долей профиля; без замера кода]
| Часть | Сейчас | После F1–F8 |
|---|---|---|
| `Allocate` всех вызовов (CPU) | ~280 с/мес CPU | 0,2–1 с CPU (≈ 0,74 мс `E_useful` + ~0,2 мс веса/раздела на вызов; 200–1000 вызовов) |
| `ViewBuilder.Inputs` (стена, главный поток) | ~95–130 с | 0,05–0,3 с (F3 + F4-A параллельно) |
| `ToolingPlan` (CPU) | ~22 с | 1–2 с (F1, F5, F6) |
| Products, Policy, Yield, прочее (CPU) | ~5–10 с | 2–4 с (F2 убирает аллокации) |
| GC | 16 % CPU, 7,4 ГБ/мес | [Предп.] < 1 ГБ/мес |
| Дневная часть `K_d` | ~30 × (38 мс хеш + снимок) | 30 × (8–10 мс хеш + ~0 снимок) ≈ 0,25–0,3 с |
| **Месяц T** | **80–106 с** | **≈ 0,6–1,2 с** |

Вывод [Предп.]: после первого раунда (200, 2000) остаётся выше бюджета ADR-0016 (500 мс p95) и выше `K_d ≤ 50` мс (хеш один даёт ~0,3 с/мес). Без F8-gate (инкрементальный хеш) `K_d` не уложится. Решение о EXP-0007 — по новому профилю после F1–F8.

## 6. Ограничения
- Профиль — один seed, 3–4 месяца, без команд; машина не i7-11800H и без контроля частоты; трасса добавляет накладные расходы (стенные числа под трассой ~1,5–2× выше). Доли, а не абсолютные времена.
- Инлайнинг JIT: часть стоимости `RowOf`/`Knows`/`Planned` учтена внутри `EUseful`/`Lead`; счётчики вызовов (прокси) это восполняют.
- Число вызовов `Allocate` на месяц выведено из стеков, не посчитано прямо.
