# Сборка мира среза «реактивная авиация» (composition root) и журнал команд

> Автор: systems-architect, 2026-11-03. Статус: **[Предл.]**, вход для T6 (ИИ и события) и закрытия DEBT-0016. Не ADR: всё ниже — локальные технические решения внутри ADR-0004, 0006, 0013, 0014 (ACCEPTED). Места, где нужно решение владельца, собраны в §11.
> r1 (2026-11-03): под контракт v2.4 r3 — команда `cancel_design` (F14, #123) и вход `TrainingBudget` (F15, #125): §3, §4, §5, §7.2, §7.3.
> r2 (2026-11-03): устаревшее по замечанию reviewer — правило оснастки называется `ToolingPlan` (не `ToolingRule`); `PlannedVolume` включает `proc_v` (§2, §4); под контракт v2.4 r4 (#128) — `v_exp` с `max` и `home(a)`, `planned` со стадией С (§4).
> Источники: ADR-0002/0004/0006/0013/0014; `docs/architecture/LAYER_CONTRACT.md` §4.0, §5; спека среза `docs/simulation/slice-jet-aviation.md` §2, §3.4, §3.5 A8/A15/A30/A36/A37, §4, §6.3, §6.8, §9.1, §11; код `src/` на коммите 4d75ae0.

## Краткий вывод
- **Сборка мира среза — только в новой сборке `Singularity.Session`** (ADR-0014 п. 1): класс `SliceWorld` строит одну `Table` (строка = актор = site, A27), пять месячных систем в фиксированном порядке, адаптеры входов, потоки RNG и фазу инициализации. `TickRunner` создаёт только `SliceSession`. Core composition root не содержит.
- **Доменная логика остаётся в Core.** Адаптеры в Session — тонкая делегация к публичным чистым функциям Core (`BudgetMath`, `ToolingPlan`, `ProductsSystem.Planned/PlannedVolume`). Формул в Session нет.
- **Две новые системы Core:** `Core.Policy.PolicySystem` (ИИ + бюджет + принятие лицензий + программа качества) и `Core.Events.EventSystem` (перебой сплава, предложения лицензий). Только они и `Knowledge` получают `CounterRng`. `Industry` и `Products` его не получают; адаптеры их входов RNG не используют (архитектурный тест расширяется на адаптеры).
- **Решения ИИ — колонки `pol.*`**: пишутся в merge месяца t, читаются compute месяца t+1 (ADR-0006). В журнал не идут. Флага `force` у ИИ нет **структурно**: колонки и тип решения ИИ его не содержат.
- **Внешние команды** (игрок, harness, прокси FORCE §6.8) — типизированные записи Core с ключом `(apply_tick, source_class, source_id, local_n)`. Session применяет их в начале тика, записывая в колонки `cmd.*` (владелец — `CommandState`, Core). Журнал replay = только внешние команды; кодек один для `commands.log` и для очереди в сохранении.
- **Валидация в два шага:** форма команды и право источника (повтор фокуса, `force` только от внешнего источника, сумма долей = 1) проверяются при `Submit`; проверки по состоянию (известен ли процесс, допуск серии, свободный слот) — при применении. Отказы по состоянию остаются счётчиками в таблице (уже есть) и строкой в логе прогона.
- **Хеш и сохранение:** ADR-0013 не меняется. Конфигурация прогона (`FIX_*`, абляции, ячейки профилей, плечи) и отпечаток параметров — колонки `run.*`: они входят в хеш и в снимок без новых полей заголовка. Очередь ожидающих внешних команд — секция кодека журнала (толкование ADR-0013 п. 1 + п. 5, §8; подтверждает Orchestrator).
- **Новый ADR не нужен**, если владелец согласен, что секция очереди в файле сохранения — исполнение п. 5 ADR-0013, а не изменение формата (§11, П-1).

## 1. Исходное состояние [Факт: код 4d75ae0]
- Сборки `Singularity.Session` нет. Связанные миры собираются вручную в тестах: `tests/.../Products/ProductsLinkedRunTests.cs` (правило V23, ИИ-заглушка, 384 мес.) и `ProductsCbLinkedRunTests.cs` (C-B, golden-хеш 60 мес.; там `Planned/PlannedVolume` подключены к `ProductsSystem`). В собранной игре этих связей нет: в `IIndustryInputs` по умолчанию `EffortBudget = 0`, `EffortFocus = −1`, `PlannedVolume = 0`, `Planned = false`, `ToolingInvest(rest)` игнорирует `rest`.
- Lab `SmokeT4s.LineStand` реализует `IIndustryInputs` и сам строит `TickRunner` с `IndustrySystem`.
- `TickContext.Rng` доступен любой системе; `KnowledgeSystem` получает свой `CounterRng` в конструкторе. `CapabilityNoRngTests` проверяет пространства имён `Core.Industry`/`Core.Products`, но не транзитивно (реализации входов не видит).
- `ProductsSystem.LayoutVersion = 2` нигде не входит в хеш: тесты передают в `StateHash` версию схемы `1`. `StateHash` всё ещё хеширует тик (шаг ADR-0013 «Последствия» не сделан).
- Команды изделия приходят через `IProductsInputs` (`DesignCommands`, `StartSeries`, `CloseLine`, `LineRate`); отказы — счётчики `_rejected` по актору (колонки). Слой не знает отправителя и не может сам проверить `force`.
- `EffortAllocator.Allocate` бросает исключение при повторе фокуса: сейчас повтор, дошедший до тика, валит прогон.

## 2. Сборки и классы
`Kernel ← Core ← Session` (Testing — позже, часть G). В T6 добавляется только `Session`.

| Где | Что | Зачем |
|---|---|---|
| `Singularity.Session/Slice/SliceWorld.cs` | Сборка: `GameData` + `ScenarioData` + `SliceRunConfig` + параметры → `Table`, системы, адаптеры, календарь, потоки RNG; `Initialize()` (фаза инициализации) | Единственный путь сборки (ADR-0014 п. 1) |
| `Singularity.Session/Slice/*InputsAdapter.cs` | `KnowledgeInputsAdapter`, `IndustryInputsAdapter`, `ProductsInputsAdapter`, `PolicyInputsAdapter` | Межслойные входы; чтение только снимка |
| `Singularity.Session/SliceSession.cs` | `Submit`, `Step(n)` (синхронно, поток вызывающего), применение команд на границе тика, хеш последнего post, запись `commands.log` | ADR-0014 пп. 2, 4 |
| `Singularity.Core/Policy/` | `PolicySystem`, `IPolicyInputs`, `PolicyParams`, `BudgetMath`, `ToolingPlan` (план оснастки A39; r2: в r0–r1 ошибочно `ToolingRule`), пресет `adaptive` | ИИ среза §4, бюджет §3.4 |
| `Singularity.Core/Events/` | `EventSystem`, `EventRng` (потоки 3, 4) | События §4, §9.1 |
| `Singularity.Core/Commands/` | `SliceCommand` (записи), `CommandKey`, `SourceClass`, `CommandValidator`, `CommandState` (колонки `cmd.*`), `CommandCodec` | Типизированные команды ADR-0014 п. 2 |

[Предл.] `SliceWorld` и адаптеры — `internal` в Session; Lab и тесты получают мир только через `SliceSession`. Многопоточная очередь, `IReadOnlySession` с публикацией снимков, `ITestSession`, транспорт — **не в T6**: их делают вместе с первым потребителем (T10 Lab — read views; часть G — остальное).

## 3. Владение системами и колонками
Порядок систем (он же порядок merge и регистрации колонок, т. е. часть схемы): **Knowledge → Industry → Products → Events → Policy**; затем `CommandState` и `run.*`. Все системы месячные. Каждую колонку пишет один владелец (контракт `ITickSystem`).

| Владелец | Колонки (префикс) | RNG |
|---|---|---|
| `KnowledgeSystem` | `know.*` (как сейчас) | поток K: `KNOW` (1); `PRIO_J` (2) вычисляет адаптер знания как чистую функцию ключа |
| `IndustrySystem` | `ind.*` | нет |
| `ProductsSystem` | `prd.*`, счётчики отказов | нет |
| `EventSystem` | `evt.dis.left`, `evt.dis.deficit` по актору; `evt.lic.offer.{seller}` (поколение, цена) в строке покупателя | поток E: `EVT_DIS` (3), `EVT_LIC` (4) |
| `PolicySystem` | `pol.profile`; `pol.budget` (= `budget_a(t)` месяца, который сейчас считается); доли R/cap/D/proc/QP; `pol.qp`; `pol.req.*`; одноразовые решения месяца: `pol.design.*`, `pol.series.d{i}.rate`, `pol.close.d{i}`, `pol.cancel.d{i}` (r1, A40), `pol.train.d{i}` (r1, цель учебной серии A41), `pol.rate.d{i}`, `pol.lic.accept.{seller}`; счётчик повторов `Requirement` по (актор, `g*`) (A40); лицензии `pol.lic.*` и переводы денег | поток A: `AI` (5) |
| `CommandState` (не система: пишет только между тиками) | `cmd.*` (§7.3) | нет |
| `SliceWorld` (только при сборке) | `run.*`: флаги `FIX_K/E/A`, абляции и режимы (`ProductsMode`, `IndustryMode`, правило культуры), ячейки профилей, плечо, отпечаток параметров | `START` (6) — один раз при сборке стартовых структур (`START_PERT`) |

- [Предл.] **`pol.budget` хранит бюджет текущего месяца**: фаза инициализации пишет `budget(0)`, merge месяца t пишет `budget(t+1) = BudgetMath.Next(budget(t), g_m)` (A12). Все входы «доля·бюджет» месяца t читают колонку снимка как есть — без пересчёта в адаптерах.
- Одноразовые решения ИИ (`pol.design.*`, `pol.series.*`, `pol.close.*`) Policy переписывает **каждый** месяц (вне годового месяца — пустые значения), поэтому они действуют ровно в одном месяце t+1.
- Лицензия: предложение — `EventSystem` (t mod 12 = 0); решение покупателя — `PolicySystem` по профилю (или команда игрока); перевод денег продавцу пишет строку другого актора, поэтому идёт сообщением в merge Policy в порядке `(seller, buyer)`.
- Изменяемых полей экземпляра вне `[Scratch]` нет (ADR-0013 п. 4). `IndustrySystem._lastRest` проверить при ревью T6 (сейчас это `Fx6[]` вне таблицы).

## 4. Межслойные входы (адаптеры)
Все члены — чистые функции снимка (Current до compute месяца t) и аргументов, как требуют интерфейсы.

| Вход | Источник |
|---|---|
| `IKnowledgeInputs.ResearchBudget` | `share_R·pol.budget` |
| `Priority`, `J` | профиль `pol.profile` → `prio`; `KnowledgeRng.J(rngK, year(t), …)`, год по A15 (`month == 0 ? 0 : (month−1)/12`) |
| `DisciplineVolume` | `IndustrySystem.DisciplineVolume` |
| `LicensedEngines` | `pol.lic.*`, купленные в месяце t−1 |
| `IIndustryInputs.KnowsProcess` | `KnowledgeSystem.IsKnown` |
| `EffortBudget` | `share_D·pol.budget` (ровно то, что Policy списывает) |
| `EffortFocus` | `cmd.focus.k` (у ИИ фокуса нет → −1) |
| `Planned`, `PlannedVolume` | `ProductsSystem.Planned(owner(site), number)`. `PlannedVolume` = `v_exp(site, p)` целиком, **вместе с `proc_v`** [Факт: код до r4 — `IndustryInputsAdapter.PlannedVolume = Industry.ProcV + Products.PlannedVolume(owner)`; `EffortAllocator` `proc_v` сам не добавляет — текст r0–r1 был неверен]. С контракта v2.4 r4 (#128): `v_exp = max(proc_v, V_C) + V_PO` только на `home(a)`, на прочих site — `proc_v`; `planned` — стадии П, О, С (контракт §4.1a). [Предл.] `max` и выбор `home` — функция Core (например, `ProductsSystem.ExpectedVolume(actor, number, procV, isHome)`), адаптер только передаёт `ProcV` снимка |
| `ToolingInvest(…, rest_a)` | `Core.Policy.ToolingPlan.Compute(...).Invest(...)`: A30 → `rest_a` → бюджет способности, ограничения A22. Чистая функция снимка, `rest_a` и `share_cap·pol.budget` |
| `QualityProgramme`, `LicenceShare` | `pol.qp`, `pol.lic.*` |
| `AlloyDeficit`, `AlloyCap` | `evt.dis.*`; данные сценария |
| `W`, `Hcap`, `WorkforceAvailable`, `KnownSince` | данные сценария (§1.1); `KnownSince` — только в режиме L_C |
| `IProductsInputs.ProcurementBudget` | `share_proc·pol.budget` |
| `DesignCommands`, `StartSeries`, `CloseLine`, `LineRate` | слияние `cmd.*` (внешние) и `pol.*` (ИИ) **в порядке ключа** §7.1; `SeriesStart.Force` берётся только из `cmd.*`; для решения ИИ всегда `false`. r1: `cancel_design` — элемент `DesignCommands` (вид `Cancel`, `Target = design_id`) [Предл.]: тот же порядок команд, что у `compose`/`derive` (контракт §4.4d шаг 2) |
| `TrainingBudget(a, d, t)` (r1, F15) | `pol.train.d` (решение месяца t−1) ? `T_a(t)` : 0, где `T_a = B_cap − taken_cap` — `Core.Policy.ToolingPlan` того же месяца (тот же вызов, что в `ToolingInvest`; план одинаков во всех вызовах месяца, A39). Чистая функция снимка |

- [Предл.] После появления адаптеров **убрать переходные значения по умолчанию** в `IIndustryInputs` (`EffortBudget`, `EffortFocus`, `PlannedVolume`, `Planned`, перегрузка `ToolingInvest` с `rest`) и в `IKnowledgeInputs.LicensedEngines`: пропущенный вход должен быть ошибкой компиляции. `LineStand` (Lab) реализует их явно.
- `ToolingInvest` — **правило, вызываемое в compute способности** месяца t (контракт §4.0: `rest_a` — аргумент того же compute), а не решение с лагом t → t+1. Решениями здесь являются только доли и профиль. Так уже работает `ProductsCbLinkedRunTests` (§11, О-3).

## 5. ИИ и события
- `PolicySystem.Compute(t)` по снимку: годовая часть при `t mod 12 = 0` и `t > 0` (доли, `prio`, `Requirement` → `compose_design` пресетом профиля, включая `adaptive`); месячная часть (темп линий, учебная серия, запуск серии при `gate_open ∧ D_hidden ≤ k_P·D_ref`, закрытие линий, программа качества, принятие лицензии; r1: `cancel_design` по A40 — первым в месячной части, порядок спеки A41). `IPolicyInputs` читает изделие и способность только через их публичные функции снимка.
- `EventSystem.Compute(t)`: hazard перебоя `EVT_DIS` (n = 0 — наступление, n = 1 — длительность 6…18), предложения `EVT_LIC` в годовой месяц; состояние — колонки `evt.*`; действует с t+1, как любое решение (§11, О-4).
- **Потоки:** `SliceWorld` создаёт `CounterRng(seed_src)` на источник: K (`KNOW`, `PRIO_J`), E (`EVT_*`), A (`AI`), `START`; `seed_src = 0x5EED` под соответствующим `FIX_*` (§6.3). `TickRunner` получает `seed` прогона, но системы среза **не читают `TickContext.Rng`** (иначе `FIX_*` обходится).
- **Прокси FORCE (§6.8)** — не ИИ, а внешний источник `Harness` в Lab: после шага месяца t он читает снимок через Session и подаёт `start_series(…, force = true)`; команда применяется в начале следующего тика и действует в t+1 — та же задержка, что у ИИ.
- **Плечо DERIV** — флаг в `run.*`; `derive` подаёт `PolicySystem`.

## 6. Фаза инициализации и порядок тика
1. `SliceWorld`: данные → стартовые структуры (`START_PERT` здесь) → `Table` и системы → `run.*`.
2. `Initialize()` (до тика 0, ADR-0006): `PolicySystem.Initialize` пишет в Current профиль (`AI`, `tick = 0`), доли, `prio`, `budget(0)`, `Requirement` и решения месяца 0 (A8). Ключ RNG `j` года 0 — `tick = 0` (A15). Отдельного интерфейса фазы в Kernel не вводить, пока она нужна одной системе.
3. Каждый тик d: `SliceSession` применяет внешние команды с `apply_tick = d` (запись в `cmd.*`, Current) → `TickRunner.RunTick()`. Если команды применены, а ни одна система не активна, Session сам считает `StateHash` («непустой тик», ADR-0006). Изменять `TickRunner` не нужно.
4. Месячный шаг t: compute всех систем по снимку → merge в порядке §3 (`report_output`/`reserve_workforce` из почтового ящика изделия — в merge способности) → swap → post.

## 7. Команды
### 7.1 Модель
- `SliceCommand` — запись Core: `Key = (ApplyTick, SourceClass, SourceId, LocalN)` + тип и аргументы (только `int`/`long`/`Fx6.Raw`).
- [Предл.] `SourceClass`: `Player = 0`, `Harness = 1`, `Ai = 2`. При равном `apply_tick` раньше применяется игрок. У решений ИИ `apply_tick = d(t+1)`, `source_id` = номер актора, `local_n` = номер слота колонки.
- `local_n` внешней команды задаёт отправитель. `Submit` отклоняет дубликат ключа и немонотонный `local_n` у источника. Счётчиков вне таблиц у Session нет.
- `apply_tick` назначает Session: ближайшая граница тика после получения (ADR-0014 п. 2).

### 7.2 Типы команд среза
| Команда | Источник | Статус |
|---|---|---|
| `compose_design(actor, g*, alts[5])` | игрок, ИИ | нужна (A38 шаг 2) |
| `derive(actor, parent, alts[5])` | игрок, ИИ (только DERIV) | нужна |
| `start_series(actor, design, rate, force)` | игрок, harness (`force` разрешён); ИИ — без поля `force` | нужна |
| `close_line(actor, design)` | игрок, ИИ | нужна (С → В) |
| `set_line_rate(actor, design, rate)` | ИИ; игрок [Предл.] | нужна ИИ (`LineRate`) |
| `set_budget_shares(actor, R, cap, D, proc, QP)` | игрок; у ИИ — колонки `pol.*` | нужна |
| `set_effort_focus(actor, p1..p3)` | только игрок | нужна |
| `accept_licence(actor, seller)` | игрок; у ИИ — `pol.lic.accept` | нужна |
| `invest_tooling_M` | — | **не команда**: в контракте v2.4 §5 её нет; это правило `ToolingPlan` при доле `cap`. Команда игрока — решение game-designer |
| ~~`abandon_project`~~ → `cancel_design(actor, design)` | игрок (П или О, когда хочет); ИИ — только по A40 | r1: нужна (контракт v2.4 r3 §4.4e, F14, #123); прежняя строка «нет в контракте, не вводить в T6» снята |

### 7.3 Где команда «в силе»
Применение = запись в `cmd.*` (Current) — состояние, поэтому оно попадает в хеш и в сохранение (ADR-0013 п. 4).
- Постоянные настройки: `cmd.shares.*` + флаг «задано игроком», `cmd.focus.0..2`.
- Одноразовые: фиксированное число слотов на актора с полем `stamp` = индекс месячного шага, который их потребляет (следующий после `apply_tick`). Адаптер видит слот только при `stamp == t`, поэтому очистка не нужна. [Предп.] 4 слота `compose/derive` и по одному `start_series`/`close_line`/`rate`/`cancel` (r1) на проект; переполнение — отказ при `Submit`.

### 7.4 Валидация
- **При `Submit` (форма и право, без состояния):** актор существует; `force = true` только при `SourceClass ∈ {Player, Harness}`; фокус — не больше 3, **без повторов**, номера процессов есть в раскладке; доли ≥ 0, сумма = 1 точно в `Fx6`; длина `alts` = 5. Отказ при `Submit` — исключение у отправителя; в журнал команда не попадает.
- **При применении (по состоянию):** `p ∈ known_processes(actor)` для фокуса — `CommandValidator` читает снимок знания. Знание не забывается, а между месячными шагами состояние не меняется, поэтому проверка при применении равна проверке при потреблении. Проверки изделия (слот, КБ, допуск, `PlatformThreshold`…) остаются в `ProductsSystem` как сейчас.
- `EffortAllocator` продолжает бросать исключение при повторе: это защита инварианта, до неё повтор не доходит.

### 7.5 Журнал
- `commands.log` (ADR-0013 п. 9): все **принятые при `Submit`** внешние команды в порядке ключа, кодек `CommandCodec`. [Предл.] Формат — JSON Lines, ключи в каноническом порядке, числа — целые (`Fx6.Raw`) строкой (ADR-0014 «Последствия»). Отказы по состоянию воспроизводятся сами и в журнал отдельно не пишутся; лог исходов (`accepted`/`rejected:reason`) — наблюдение, не состояние.
- Решения ИИ в журнал не пишутся: они в `pol.*` и воспроизводятся симуляцией. Инвариант P0 «в журнале ИИ нет `force = true`» проверяется так: `series_forced = 1` бывает только у проекта, на который в `commands.log` есть внешняя `start_series(…, force = true)`; в main (журнал пуст) `series_forced ≡ 0`.

## 8. Хеш и сохранение (ADR-0013 без изменений)
- `StateHash` покрывает `pol.*`, `evt.*`, `cmd.*`, `run.*` автоматически: это колонки.
- [Предл.] **Версия схемы мира** `SliceWorld.SchemaVersion` — одно целое для заголовка ADR-0013 п. 2. Тест: отпечаток схемы (имена и типы колонок по порядку) сравнивается с эталонной парой «отпечаток ↔ версия»; изменение раскладки без повышения версии валит CI. Константы `LayoutVersion` систем остаются входом этого теста, но в хеш отдельно не идут. Одновременно — шаг ADR-0013 «Последствия»: убрать тик из `StateHash` (отдельная задача Kernel; golden-хеши пересоздаются с причиной).
- Конфигурация и параметры прогона — `run.*` (§3): загрузка сверяет их с конфигурацией сборки; расхождение — громкая ошибка. Новых полей заголовка не нужно.
- Очередь ожидающих внешних команд — секция после таблиц, тем же `CommandCodec` (ADR-0013 п. 5 требует «в снимок тем же кодеком», п. 1 секцию не перечисляет; §11, П-1). Для T6 сохранение не требуется; секция нужна к главному тесту save/load (T8).

## 9. Файлы
**Добавить:** `src/Singularity.Session/` (csproj → Core, Kernel; `Slice/SliceWorld.cs`, `Slice/*InputsAdapter.cs`, `Slice/SliceRunConfig.cs`, `SliceSession.cs`); `src/Singularity.Core/Policy/*`, `Events/*`, `Commands/*` (§2); `tests/Singularity.Session.Tests/` (связанный прогон, golden, журнал); архитектурные тесты (§10).
**Изменить:** `IIndustryInputs`, `IKnowledgeInputs` — снять переходные значения по умолчанию; `IProductsInputs.SeriesStart` — без изменений (право на `force` проверяет Session); `SmokeT4s.LineStand` — явные реализации; `CapabilityNoRngTests` — плюс адаптеры `Industry`/`Products`.
**Перенести:** golden 60 мес. из `ProductsCbLinkedRunTests` → `Session.Tests` (мир через `SliceSession`, новый golden с причиной «composition root T6»). `ProductsLinkedRunTests` (V23 + заглушка ИИ) — удалить после переноса; путь R0 v2.3 остаётся абляцией через `run.*` (§6.2 спеки). Стенды одного слоя с фейками (`*SystemTests`, `IndustryTwoSiteTests`) остаются тестами уровня 1.
**Не делать:**
- общий `IWorldBuilder`/реестр систем/DI-контейнер — мир один, сборщик один;
- интерфейс фазы инициализации в Kernel; изменения `TickRunner`;
- `ITestSession`, транспорт, многопоточную очередь, публикацию снимков до их первого потребителя;
- универсальный «patch колонок» вместо типизированных команд (ADR-0014 п. 3);
- формулы в Session (бюджет, правило оснастки, `v_exp`): только вызовы Core;
- поле `force` в решениях ИИ или флаг «ИИ может форсировать» для плеча FORCE;
- команды `invest_tooling_M`, `abandon_project` без решения game-designer.

## 10. Проверки
- Архитектура: Core не ссылается на Session; Lab не вызывает конструктор `TickRunner` и не создаёт `Table` для систем Core мимо Session (исключение — помеченные стенды `LineStand`, §11 Р-4); `Industry`, `Products` и их адаптеры не используют `CounterRng` и `TickContext.Rng`; системы среза не читают `TickContext.Rng`.
- L1/L2: связанный мир через `SliceSession`, 1/4/8 потоков, хеш каждый месяц равен; golden 60 и 384 мес.
- Порядок команд: команды трёх источников с одинаковым `apply_tick` в любом порядке `Submit` дают одно состояние; replay `commands.log` даёт тот же хеш, тик и дату.
- Тик только с командой выполняет хеш; пустой — нет.
- `Submit`: повтор фокуса, `force` от `Ai`, сумма долей ≠ 1 → отказ; повтор до `EffortAllocator` не доходит.
- `FIX_K/E/A`: смена `seed` не меняет выборки зафиксированного источника.
- Инвариант FORCE (§7.5) на main и на плече FORCE.

## 11. Риски и открытые вопросы
**Нужно решение владельца**
- **П-1.** Секция очереди внешних команд в файле сохранения после таблиц — исполнение ADR-0013 п. 5 (моё прочтение) или изменение формата п. 1 (тогда нужен ADR с `supersedes: ADR-0013`)? До T8 не блокирует.

**Открыто (решает Orchestrator / simulation-engineer)**
- **О-1. T6 зависит от T7.** Правила ИИ §4 читают `why_not`, `limiter`, `eta_months` (программа качества P1/P3, лицензия P3, учебная серия). Без T7 их нельзя сделать честно. [Предл.] T7 (функции `why_not` как чистые функции снимка) — до части T6 с этими правилами или в одной задаче; заглушки запрещены.
- **О-2.** Порядок `SourceClass` (игрок раньше ИИ) — значение, которое ADR-0014 оставил открытым; фиксируется здесь, после реализации не меняется без смены схемы журнала.
- **О-3.** `ToolingInvest` — правило внутри compute t (как в контракте §4.0 и в текущих тестах), а не решение ИИ с лагом. Подтвердить сверкой с эталоном T9.
- **О-4.** Перебой сплава, наступивший в compute t, действует с t+1 (как решение)? Подтвердить по эталону; спека §4 момент не задаёт.
- **О-5.** Число слотов одноразовых команд (4 на актора в месяц) — [Предп.]; game-designer подтверждает для игрока.

**Риски**
- Р-1. Пересоздание golden-хешей (новые колонки, порядок систем, позже — тик вне хеша). Защита: причина в задаче, один пересчёт на шаг.
- Р-2. Неполная транзитивность теста «нет RNG»: правило, вызванное из адаптера, может тянуть RNG через другой тип Core. Защита: тест на адаптеры + ревью исходящих вызовов `ToolingPlan`.
- Р-3. Конфигурация прогона вне таблиц (например, флаг абляции только в конструкторе) даст сохранение, которое продолжится не так. Защита: `run.*` + сверка при загрузке.
- Р-4. `SmokeT4s.LineStand` — мир из системы Core мимо Session (ADR-0014 п. 1). [Предл.] Оставить как стенд одного слоя с маркировкой отчётов «не свидетельство Core», после T6 не расширять.
- Р-5. `IndustrySystem._lastRest` (`Fx6[]` вне таблицы) может нарушать ADR-0013 п. 4, если значение читается в следующем тике. Проверить в ревью T6.
- Р-6. `ARCHITECTURE.md` устарел (ADR-0006 указан как PROPOSED, нет слоя Session). Обновит systems-architect отдельно.

## Как это закрывает DEBT-0016
| Пункт долга | Где закрывается |
|---|---|
| `Planned`/`PlannedVolume` не подключены, связанный прогон на V23 | §4 адаптеры; §9 перенос golden в `Session.Tests`, удаление V23-прогона |
| `LayoutVersion` вместо модуля сохранения | §8 `SchemaVersion` + отпечаток схемы; сам модуль save — T8 |
| нет валидатора повторов фокуса | §7.4 `Submit` |
| счётчики отказов вместо журнала команд | §7.5 `commands.log` внешних команд; счётчики остаются как метрика |
| нет пресета `adaptive` | §2 `Core.Policy` (T6) |
| `force` проверяется только источником | §7.4 + структурный запрет у ИИ (§4, §7.5) |
