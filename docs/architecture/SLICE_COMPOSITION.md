# Сборка мира среза «реактивная авиация» (composition root) и журнал команд

> Автор: systems-architect, 2026-11-03. Статус: **[Предл.]**, вход для T6 (ИИ и события) и закрытия DEBT-0016. Не ADR: всё ниже — локальные технические решения внутри ADR-0004, 0006, 0013, 0014 (ACCEPTED). Места, где нужно решение владельца, собраны в §11.
> r1 (2026-11-03): под контракт v2.4 r3 — команда `cancel_design` (F14, #123) и вход `TrainingBudget` (F15, #125): §3, §4, §5, §7.2, §7.3.
> r2 (2026-11-03): устаревшее по замечанию reviewer — правило оснастки называется `ToolingPlan` (не `ToolingRule`); `PlannedVolume` включает `proc_v` (§2, §4); под контракт v2.4 r4 (#128) — `v_exp` с `max` и `home(a)`, `planned` со стадией С (§4).
> r3 (2026-11-13, TASK-0046 п. 1): §12 — проект поверхности read views Session и переключателей серий для Lab (T10). Статус §12 — **[Предл.]**, утверждает Orchestrator.
> r4 (2026-11-13, TASK-0047): §13 — размещение пяти наблюдаемых величин формата v0.4 §4.15 (`t_known`, `left`, причина закрытия линии, варианты `Requirement`, отказы лицензии по деньгам) и `TestApiVersion = 2`. Закрывает пробелы §12.5 (частично: `t_known`) и §12.6. Статус §13 — **[Предл.]**, утверждает Orchestrator.
> r5 (2026-11-13, ревью TASK-0046 коммит 2712299): §12.1 — конструкторы views публичные (позиционные records), только init; §13.7 — текст поверхности v2 с nullable-аннотациями, Lab читает views по имени свойств.
> r6 (2026-11-14, ревью TASK-0047 коммит 4ade4ab, принято Orchestrator): §13.2, §13.9 — О-13-1 закрыт: `startKnownSince = −H = −384` (спека §6.1 v0.24 r2, `KnownSinceMarks.StartKnown`), по умолчанию, значения > −H запрещены; §13.3 — (после второго ревью Codex) `Training` = фактический `TrainingBudget` входа у каждой линии стадии С, в том числе недопущенной (`FromTraining = 0`), COEXIST_MONEY в формате v0.5 предлагается определять по `DesignView.Bind == Money` при нулевом выпуске.
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
- r3: **read views для Lab (§12)** — один неизменяемый `SliceView` на непустой тик: состояние после post и входы месячного шага в том виде, в каком их видел compute. `TestApiVersion = 1`. Переключатели серий: `L_K`, `L_K0`, `L_C0`, сетка P5, `EFFORT_SHARE`. `L_C` и `L_P` без правки Core невыполнимы (нет состояния `t_known`). `COMP_V010` не делается (спека §6.8 v0.12).
- r4: **наблюдения шага (§13)** — `left`, причина закрытия, варианты `Requirement`, отказы по деньгам — не колонки, а след последнего шага (scratch, как `KnowledgeSystem.LastDiscoveries`): не хешируются, не сохраняются, симуляция их не читает (IL-тест). `t_known` — колонки `know.since.N` только в режимах `L_C`/`L_P`, где его читает симуляция. Golden-хеши и отпечаток схемы main не меняются.

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

(TASK-0115) `design`/`parent` во всех командах — `DesignHandle(Slot, Generation)` (ADR-0005 п. 3), не голый индекс слота: `derive`, `start_series`, `close_line`, `cancel_design`, `retire_cohort`, `sell_cohort`. `compose_design` handle не несёт (создаёт проект). [Факт: `SliceCommand.cs`, `DesignHandle.cs`]
| `set_line_rate(actor, design, rate)` | ИИ; игрок [Предл.] | нужна ИИ (`LineRate`) |
| `set_budget_shares(actor, R, cap, D, proc, QP)` | игрок; у ИИ — колонки `pol.*` | нужна |
| `set_effort_focus(actor, p1..p3)` | только игрок | нужна |
| `accept_licence(actor, seller)` | игрок; у ИИ — `pol.lic.accept` | нужна |
| `invest_tooling_M` | — | **не команда**: в контракте v2.4 §5 её нет; это правило `ToolingPlan` при доле `cap`. Команда игрока — решение game-designer |
| ~~`abandon_project`~~ → `cancel_design(actor, design)` | игрок (П или О, когда хочет); ИИ — только по A40 | r1: нужна (контракт v2.4 r3 §4.4e, F14, #123); прежняя строка «нет в контракте, не вводить в T6» снята |

### 7.3 Где команда «в силе»
Применение = запись в `cmd.*` (Current) — состояние, поэтому оно попадает в хеш и в сохранение (ADR-0013 п. 4).
- Постоянные настройки: `cmd.shares.*` + флаг «задано игроком», `cmd.focus.0..2`.
- Одноразовые: фиксированное число слотов на актора с полем `stamp` = индекс месячного шага, который их потребляет (следующий после `apply_tick`). Адаптер видит слот только при `stamp == t`, поэтому очистка не нужна. [Факт: `CommandState.cs`, `CommandLimits`] 4 слота `compose`/`derive`/`cancel` на актора (`cmd.dsn.K.*`), 8 слотов команд парка `retire`/`sell` на актора (`cmd.flt.K.*`), по одному `start_series` и `close_line` на проект (`cmd.series.D.*`, `cmd.close.D.*`); переполнение — отказ при `Submit`. (Прежний текст «4 слота `compose/derive` и по одному `start_series`/`close_line`/`rate`/`cancel` на проект» снят как расходящийся с кодом: `cancel` занимает слот `cmd.dsn`, слота `rate` нет.)
- (TASK-0115) Каждая колонка ссылки на проект имеет парную колонку поколения слота: `cmd.dsn.K.gen` (рядом с `cmd.dsn.K.target`), `cmd.flt.K.gen`, `cmd.series.D.gen`, `cmd.close.D.gen`; у решений ИИ — `pol.dsn.K.gen`, `pol.flt.K.gen`, `pol.start.D.gen`, `pol.close.D.gen`. `SliceWorld.SchemaVersion = 11`, `SliceSession.TestApiVersion = 13`.

### 7.4 Валидация
- **При `Submit` (форма и право, без состояния):** актор существует; `force = true` только при `SourceClass ∈ {Player, Harness}`; фокус — не больше 3, **без повторов**, номера процессов есть в раскладке; доли ≥ 0, сумма = 1 точно в `Fx6`; длина `alts` = 5. Отказ при `Submit` — исключение у отправителя; в журнал команда не попадает.
- **При применении (по состоянию):** `p ∈ known_processes(actor)` для фокуса — `CommandValidator` читает снимок знания. Знание не забывается, а между месячными шагами состояние не меняется, поэтому проверка при применении равна проверке при потреблении. Проверки изделия (слот, КБ, допуск, `PlatformThreshold`…) остаются в `ProductsSystem` как сейчас.
- `EffortAllocator` продолжает бросать исключение при повторе: это защита инварианта, до неё повтор не доходит.

### 7.5 Журнал
- `commands.log` (ADR-0013 п. 9): все **принятые при `Submit`** внешние команды в порядке ключа, кодек `CommandCodec`. Формат — JSON Lines, ключи в каноническом порядке, идентификаторы — целые JSON, `Fx6.Raw` — целое строкой (ADR-0014 «Последствия»). Отказы по состоянию воспроизводятся сами и в журнал отдельно не пишутся; лог исходов (`accepted`/`rejected:reason`) — наблюдение, не состояние.
- **Формат v2 (TASK-0115, DEBT-0052) [Факт: `CommandCodec.cs`, `LogVersion = 2`].** Первая строка — заголовок `{"commands_log":2}` (`EncodeLog` пишет его всегда, даже у пустого журнала; `Encode` одной команды — без заголовка). Ссылка на проект — пара `design` + `design_gen` (у `derive_design` — `parent` + `parent_gen`; `compose_design` ссылки не несёт); поколение — то, которое видел отправитель. `DecodeLog` отвергает `FormatException`: текст без заголовка (журнал v1 с голыми индексами слота; пустой текст тоже), заголовок с версией ≠ 2, строку команды с `design`/`parent` без парного `*_gen`. Миграции v1 → v2 нет (сохранённых журналов нет, replay — артефакт сборки; ADR-0013 п. 9, O-003). Прежний формат v1 («`"design":d` без поколения», без заголовка) снят.
- **Устаревший handle.** При `Submit` проверяется только форма: слот в диапазоне `max_designs` (иначе `BadDesign`) и `Generation ≥ 1` (иначе `BadHandle`). Устаревание — только при применении: `ProductsSystem` отклоняет команду с `StaleHandle` (LAYER_CONTRACT §4.4); такая команда лежит в журнале и при replay отклоняется так же, как прочие отказы по состоянию.
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

## 12. Read views и переключатели серий для Lab (r3, TASK-0046) [Предл.]
Потребитель — модели Lab `JetSlice` и `JetSliceCalib` (T10, формат `docs/simulation/slice-csv-format.md` v0.3). Основание — ADR-0014 п. 5–6. [Факт: код `src/Singularity.Session`, `src/Singularity.Core` на HEAD 854ad45] Почти всё нужное уже есть в публичных методах Core. Views только собирают это в неизменяемый снимок. Формул и новых систем в них нет. Пробелы перечислены в §12.6.

### 12.1 Модель снимка
- **Одна публикация на непустой тик.** После post непустого тика (ADR-0006: выполнен месячный шаг или применены команды) `SliceSession` собирает `SliceView` и публикует его одной записью ссылки (`Volatile.Write`). После пустого тика ссылка не меняется. Начальный снимок публикуется в `Create` (`Month = −1`, `Inputs = null`): из него берутся значения «−1, на старте».
- **Две части снимка.**
  1. **Состояние после post** — колонки `Current`. Это снимок, который прочитает compute месяца `Month + 1`. Чистые функции Core на этой части вызываются с `month = Month + 1`.
  2. **`Inputs` — входы месячного шага `Month` в том виде, в каком их видел его compute.** Session собирает их в начале месячного тика: после `Policy.ApplyDeals` и после применения команд, до `Runner.RunTick()`. Публикуются вместе с post того же тика. Брать их из предыдущего снимка нельзя: `ApplyDeals` меняет состояние в начале тика, поэтому план оснастки и класс `ClassifyGiveUp` в месяц покупки лицензии разошлись бы с тем, что видел compute.
- **Связь со строками CSV.** Строка «месяца t» (формат §0) — снимок с `Month = t`. Поля «снимок t» (формат §4.9–4.12) — `View.Inputs` того же снимка. Решения ИИ месяца t (действуют с t+1) — `ActorView.Decisions` того же снимка.
- **Неизменяемость.** Все типы — `sealed record` / `readonly record struct`. Коллекции — только `ImmutableArray<T>`. Ссылок на системы, `Table` или колонки нет. Изменяемые массивы Core (`int[]` в `DesignCommand`, `ProductsSystem.Alternatives`, `PrcSet`) копируются. Конструкторы публичные, свойства только `init` (r5: у позиционного record основной конструктор нельзя сделать `internal`; [Факт: эталон `session-surface.v1.txt`, коммит 2712299]). Неизменяемость от этого не страдает: view, собранный вне Session, на состояние мира не влияет. Сигнатуры конструкторов входят в эталон поверхности (§12.3).
- **Режим чтения** — опция исполнения `SliceRunConfig.ViewMode` (как `Threads`). В `run.*` не пишется и хеш не меняет. При `Calibration` Session не вызывает `KeyOf`, `MaxFleetValue` и `WhyNot`: `ActorView.Key` и `FleetValue` = `null`, а `SliceSession.WhyNot`/`CurrentRequirement` бросают `InvalidOperationException`. Так запрет спеки §8.2 п. 2 (формат §5 «не вызывает вообще») выполняется структурно, а не только по заголовку CSV.
- **Цена.** 5 акторов × (12 проектов + 61 узел) + один `ToolingPlan.Compute` на актора в месяц. Адаптер и так пересчитывает этот план при каждом вызове (A39). `KeyOf` считается каждый месяц (лента `lead`, формат §4.1). `MaxFleetValue` — только в снимках с `Month mod 12 = 0` (формат §4.2), в остальных `null`.

### 12.2 Поверхность (точные сигнатуры)
Пространство имён `Singularity.Session`. Типы Core (`Fx6`, `CivilDate`, `PolicyProfile`, `BudgetShares`, `TechKey`, `DesignStage`, `GiveUpClass`, `FleetCommand`, `LineBind`) переиспользуются: они уже публичны и неизменяемы.
```csharp
public sealed partial class SliceSession
{
    public const int TestApiVersion = 1;            // ADR-0014 п. 6; до TASK-0046 версии не было (= 0)
    public SliceLayoutView Layout { get; }           // один раз в Create
    public SliceView View { get; }                   // последняя публикация; не null после Create
}

public enum SliceViewMode { Full = 0, Calibration = 1 }

public sealed record SliceLayoutView(
    ImmutableArray<string> ActorIds,                 // строка актора = индекс (порядок сценария)
    ImmutableArray<int> NodeNumbers,                 // строка графа знания → номер узла
    ImmutableArray<int> NodeGen,
    ImmutableArray<int> ProcessNumbers,              // строка IndustryLayout → номер PRC
    ImmutableArray<int> ProcessTier,
    ImmutableArray<string> Clusters,                 // порядок раскладки
    ImmutableArray<int> GenApp,                      // APP-узел поколения 1…G
    int Designs, int Bureaus, int Generations, int TAb, Fx6 MasteredYield, Fx6 FvBudget);

public sealed record SliceView(
    long Tick, CivilDate Date, string StateHash,
    long Month,                                      // последний выполненный месячный шаг; −1 до первого
    ImmutableArray<ActorView> Actors,                // post-состояние
    MonthInputsView? Inputs);                        // null, если в этом тике месячного шага не было

public sealed record ActorView(
    int Actor, PolicyProfile Profile,
    ImmutableArray<Fx6> KD,                          // KnowledgeSystem.KD по дисциплинам
    int FieldedGen,                                  // ProductsSystem.FieldedGen(a, Month+1)
    TechKey? Key,                                    // KeyOf(a, Month+1); null в Calibration
    Fx6? FleetValue,                                 // MaxFleetValue(a, Fx6.One, FvBudget, Month+1); только Month mod 12 = 0 и Full
    ImmutableArray<BureauView> Bureaus,
    QualityView Quality, LicenceView Licence, DisruptionView Disruption,
    ImmutableArray<NodeView> Nodes,                  // по строкам графа
    ImmutableArray<DesignView> Designs,              // по слотам 0…Designs−1
    PolicyDecisionView Decisions);

public sealed record NodeView(int Number, bool Known, bool ByLicence, ProcessView? Process);  // ByLicence: в LastLicenceGrants этого шага
public readonly record struct ProcessView(
    Fx6 Yield, Fx6 V, bool Admitted, Fx6 UnitCostMult, bool Mastered, Fx6 LicenceShare);    // home-site (A27: site = актор)

public sealed record DesignView(
    int Design, DesignStage Stage, int PlatformGen, int Gen, int Parent, int StageOMonth,
    bool BuiltAny, bool Cancelled, Fx6 Rate, Fx6 UnitCost, Fx6 UnitCostFloor,
    Fx6 InService, Fx6 Readiness, Fx6 Perf, LineBind Bind, bool AiLine,
    ImmutableArray<int> PrcSet);
public readonly record struct BureauView(int Bureau, int Design, long Granted, long Required);
public readonly record struct QualityView(bool Active, int Start, int TriggerCount, int TrainingTarget);
public sealed record LicenceView(int Gen, int Seller, int Start, ulong Mask, ulong OfferMask, Fx6 Flow,
    ImmutableArray<long> Refusals,                   // BadLicenceCount по BadLicence (без None)
    bool Active);                                    // v3: SliceWorld.LicenceActive(a) снимка (read view)
public readonly record struct DisruptionView(int Start, int End);

public sealed record PolicyDecisionView(             // решения compute Month, действуют с Month+1
    ImmutableArray<DesignCommandView> DesignCommands,// AiDesignCommands(a, Month+1)
    ImmutableArray<StartView> Starts,                // AiStart(a, d, Month+1) ≠ null
    ImmutableArray<int> Closes,                      // AiClose(a, d, Month+1)
    ImmutableArray<FleetCommand> Fleet,              // AiFleetCommands(a, Month+1)
    ImmutableArray<int> Repeats);                    // Repeats(a, g), g = 1…G
public sealed record DesignCommandView(DesignCommandKind Kind, int Target, ImmutableArray<int> Alts);
public readonly record struct StartView(int Design, Fx6 Rate);

public sealed record MonthInputsView(long Month, int GFront, ImmutableArray<ActorInputsView> Actors);
public sealed record ActorInputsView(
    int Actor, Fx6 Budget, BudgetShares Shares, Fx6 ProcurementBudget,   // Policy.Budget, SharesOf, ProcurementBudget — месяц Month
    ToolingLedgerView Tooling, CapabilityView Capability,
    ImmutableArray<DesignInputsView> Designs);
public sealed record ToolingLedgerView(
    Fx6 Rest, Fx6 CapBudget, Fx6 Total, Fx6 TakenRest, Fx6 TakenCap, Fx6 RestReturned, Fx6 TrainingBudget,
    int Participating,                               // число ячеек с Participates
    ImmutableArray<Fx6> UnmetByCluster);             // Σ Unmet по site актора и измерениям, порядок Layout.Clusters
public readonly record struct CapabilityView(Fx6 AlloySupply, Fx6 AlloyDeficit);
public readonly record struct DesignInputsView(
    int Design, DesignStage Stage, int IdleMonths, bool GateOpen, Fx6 Hidden, bool Admitted,
    Fx6 YMin, int ArgP,                              // min AdmittedYield по PrcSet; ArgP — номер PRC, −1 без набора
    GiveUpClass? GiveUp);                            // ClassifyGiveUp(a, d, Month) только в стадии О
```
Источники полей — только вызовы, которые уже делают адаптеры и `T6eHarness`:
- `ToolingLedgerView` — `IndustryInputsAdapter.Plan(a, Month, Industry.RestActor(a, Month))`. Это тот же вызов, что в `ToolingInvest`, поэтому значения совпадают с планом compute.
- `YMin`/`ArgP` — `ProductsInputsAdapter.AdmittedYield`; при равенстве берётся первый в порядке `PrcSet`, как в печати T6e2.
- `CapabilityView` — `Industry.Alloy(a, Month).Supply`, `Policy.AlloyDeficit(a, Month)`.
- `BureauView` — `Industry.Granted(a, EngineerCategory, ProductsLayout.BureauRequester(b))` и `BureauReq(af_class)` проекта бюро.
- `ProcessView.UnitCostMult` — `Industry.Profile(a, [один PrcSetItem], Month+1).UnitCostMult`. [Предп.] Для набора из одного процесса это и есть `ucm` процесса. developer проверяет тестом против `IndustryMath`; если не так — пробел §12.6.
- Отражения и `InternalsVisibleTo` для Lab не нужны. `IPolicyProducts` и `GiveUpAllowed` (приватные, читались в T6eHarness) в поверхность не входят: печать охраны v0.21 формат v0.3 не требует.

### 12.3 `test_api_version` и тест поверхности
- `SliceSession.TestApiVersion = 1` (публичная константа).
- Тест в `Singularity.Session.Tests` строит канонический текст поверхности. Через отражение берутся все публичные типы сборки `Singularity.Session` и их публичные члены (имя, сигнатура, тип) в порядке Ordinal. Текст сравнивается с эталоном `tests/Singularity.Session.Tests/Api/session-surface.v1.txt`. Расхождение без нового файла эталона с номером, равным `TestApiVersion`, валит CI (ADR-0014 «Проверка»).
- Любое изменение views или `SliceRunConfig` после этого — повышение версии и новый эталон, ревью systems-architect.

### 12.4 Переключатели серий в `SliceRunConfig`
Правило для всех: **значение по умолчанию = поведение main**. Колонки `run.*` добавляются **только при значении не по умолчанию**, как у `run.tooling`. Поэтому `SchemaVersion`, отпечаток схемы и golden-хеши main не меняются.
```csharp
public LayerControl Layer { get; init; }             // Full
public LkTables? LkTables { get; init; }             // обязателен ровно при Layer = Lk
public GridCell Grid { get; init; }                  // default = (Mid, Mid, Mid) = main
public EffortShareArm EffortShare { get; init; }     // X1
public SliceViewMode ViewMode { get; init; }         // Full; опция исполнения (§12.1)

public enum LayerControl { Full = 0, Lk = 1, Lk0 = 2, Lc0 = 3 }      // Lc, Lp — §12.5
public enum GridStep { Mid = 0, Lo = 1, Hi = 2 }                      // ×1, ×0,5, ×2
public readonly record struct GridCell(GridStep KCatch, GridStep KDiff, GridStep HMax);
public enum EffortShareArm { X1 = 0, X0 = 1, X05 = 2, X2 = 3, X4 = 4 }
```
| Переключатель | Семантика (спека v0.24) | Сборка в `SliceWorld` (только вызовы Core) | `run.*` |
|---|---|---|---|
| `Layer = Lk` | §6.1 `L_K`: стохастическое расписание `known` с моментами полной модели; `K_d`, `K_ind`, `prog` не обновляются | `KnowledgeSchedule.BuildLk(Graph, kStart, LkTables, Streams.Know)` → `new KnowledgeSystem(..., KnowledgeMode.ScheduleLk, schedule)` | `run.layer` = 1; `run.layer.tables` = первые 8 байт SHA-256 канонической записи `LkTables` (`PCens.Raw`, `Mean`, `HalfWidth`, `Delta`, little-endian, по порядку) |
| `Layer = Lk0` | §6.1 старый контроль `L_K0` (печать, seeds 1…400): лаги 0/0/24/36/48 от середины окна | `KnowledgeSchedule.BuildLk0(Graph, kStart)` → `KnowledgeMode.ScheduleLk0`. Старт сценария 1945-09 совпадает с `Lk0WindowMid`; при другой дате старта сборка бросает исключение (середины окон — данные сценария, их пока нет) | `run.layer` = 2 |
| `Layer = Lc0` | §6.1 `L_C0` (печать): `yield = 1`, `unit_cost_mult = 1`, `q = 1` | `new IndustrySystem(..., IndustryMode.ConstLc0)` | `run.layer` = 3 |
| `Grid` | §7 P5, §8.2 п. 4а: оси `k_catch`, `h_max` × {0,5; 1; 2} к замороженному значению, `k_diff` — через `r = 1 − k_diff` (#190); 27 точек; `mid_mid_mid` = main | после `ApplyChannels`: `IndustryParams.KCatch`, `KnowledgeParams.KDiff`, `KnowledgeParams.HMax`. `k_catch`, `h_max`: `Lo` = `raw / 2` (усечение), `Hi` = `checked(raw * 2)`. `k_diff` (#190, спека v0.25 §7): ось по `r = 1 − k_diff` — `KDiff′ = One − Step(One − KDiff)`, т. е. `Lo` → 0,75, `Hi` → 0. На текущих значениях (25 000; 500 000; 80 000) деление точное | `run.grid` = `9·kc + 3·kd + hm` (значения `GridStep`) |
| `EffortShare` | §6.8 `EFFORT_SHARE`: `share_D′ = x·share_D`, x ∈ {0; 0,5; 2; 4}; прочие доли × `(1 − share_D′)/(1 − share_D)`, сумма = 1 | статический `SliceWorld.ApplyEffortShare(arm, PolicyParams)` рядом с `ApplySaleArm` (построение параметров, не формула мира): ко всем `SharesByProfile` и `MassProgrammeShares`. `D′` — точные raw-операции. `R′`, `cap′`, `QP′` = `⌊raw·(1−D′)/(1−D)⌋` (`Int128`). Остаток округления → `Procurement′ = 1 − D′ − R′ − cap′ − QP′`. Результат проверяется `IsValid`. [Предл.] Правило округления спека не задаёт | `run.effort_share` = значение enum |
| `COMP_V010` | §6.8 v0.12: «по умолчанию пропускается». [Факт: путей v0.10 в Core нет] | **не делается**; серия `arm/COMP_V010` из формата убирается (v0.4) | — |

Проверки (developer, тестами):
- каждый переключатель в значении по умолчанию даёт golden-хеш main 60/384 мес. и прежний отпечаток схемы;
- не по умолчанию хеш отличается, и `run.*` его фиксирует;
- `Lk` без `LkTables` и `LkTables` без `Lk` — ошибка сборки;
- `Lk`, `Lk0`: I5 на всём прогоне, узлы стартового знания не трогаются; таблицы в тесте синтетические (настоящие придут из T12);
- `EffortShare`: сумма долей = 1 точно у всех профилей и у программы качества; ×1 возвращает тот же объект параметров;
- все 4 плеча проходят `PolicyLiveness.Validate` на сценарии (Р-12-2).

### 12.5 Что не исполнимо без правки Core
- **`L_C`** (`IndustryMode.CurveLc`) и **`L_P`** (`ProductsMode.CanonicalLp`). Обоим нужен `t_known` — месяц merge, в котором узел стал известен (`IIndustryInputs.KnownSince`, `IProductsInputs.AppKnownSince`). [Факт: код] В Core этого состояния нет: `know.known.N` хранит только 0/1, адаптеры возвращают −1. Вести `t_known` в Session — это состояние вне таблиц или колонки без системы-владельца (ADR-0013 п. 4, §3). Поэтому нужна задача Core.
  - [Предл.] Решение: `KnowledgeSystem` с опцией `trackKnownSince` пишет колонки `know.since.N` только в этих режимах, так что хеш main не меняется. Отдельно определить `t_known` узлов, известных на старте (в CSV это −1, а в адаптерах −1 = «неизвестен»).
  - Таблицы `Y_k`, `U_k`, `T_design`, `ρ` дадут только T12.
  - Для `L_P` ещё открыто: выключается ли продуктовый ИИ (`ProductAi = false`) и правила парка — game-designer/simulation-engineer.
- **Абляции §6.1 по существу** (не по коду) ждут таблиц контролей из калибровочной сборки (§8.2 п. 8). До T12 серии `layer/*` прогоняются только на синтетических таблицах тестов.

### 12.6 Формат v0.3: что даёт поверхность, а чего нет
| Таблица (формат) | Из views без новых систем | Пробел (чего в Core нет) → предложение |
|---|---|---|
| `summary` | профили; `lead`/`lead_all`/`lead_field` из `Key` каждого месяца; `hash_final`, `hash_years` из `StateHash` | — |
| `actor_year` | `Key`, `FleetValue`, `Budget` (Inputs), `KD`, `eng_short_months` по `BureauView`; `fv_fleet` в Lab = Σ `ProductsMath.FleetValue(InService, Readiness, Perf/1, γ)` по проектам | `volume` в формате не определён — уточнить в v0.4 (simulation-engineer) |
| `whynot` | `SliceSession.WhyNot` / `CurrentRequirement` (есть) | — |
| `node_times`, `prc_year`, `prc_month` | `Known` (смена между снимками = `t_known`), `ByLicence`, `ProcessView` | `ucm` — [Предп.] §12.2 |
| `gen_times` | `t_req` из `DesignCommands` (compose, `Target = g`), `BuiltAny`, `FieldedGen`, `UnitCost`; `cost_u100` — Lab по накопленному выпуску | — |
| `requirements` | `req`, `actor`, `gen`, `t`, выбранный состав | `n_variants`, `fv_tech`, `fv_super`, `chosen`: варианты пресетов живут только внутри `ProductPresets.Compose`. Повторить их в Session — дублировать логику ИИ. → Core: чистая `ProductPresets.Variants(...)` (≈ 20 строк) или убрать колонки в v0.4 |
| `events` | `DIS` (`Disruption`), `LIC_OFFER` (`OfferMask`), `LIC_BUY`/`LIC_END` (`Licence`), `LIC_REFUSE_MONEY` (`Refusals`, [Предп.]: есть ли в `BadLicence` денежная причина — проверить), `TRAINING`, `QP_START` (профиль) | `QP_START.other` «причина звена QC» не хранится → в v0.4 только профиль |
| `a40` | О: `DesignCommands` (Cancel) + `GiveUp` из Inputs; С: `Closes` при `Inputs.Designs.IdleMonths ≥ TAb`; `t_stage_o`, `rep_before`, `preset` = f(профиль) | класс С `COEXIST_MONEY` требует `left` (ниже) |
| `a40_c_months` | `YMin`, `ArgP`, `AlloySupply`, `AlloyDeficit`, `t_a` = `Tooling.TrainingBudget` | **`left`** (деньги закупок после содержания и `W`) — локальная величина шага 4.4d в `ProductsSystem`, публично не отдаётся → Core: колонка/метод снимка, либо класс С без `COEXIST_MONEY` |
| `tooling_ledger` | все колонки, кроме разбиения по проходам | `invest_p1`/`invest_p2`: `ToolingPlanResult` даёт только сумму → v0.4: одна колонка `invest` (инварианты §4.11 используют только сумму) |
| `lines` | `y_min`, `argp`, `idle`, `rate`, `unit_cost`, `start`, `close` | `close_rule` (YPROD/REPLACE/A40C/F17B): ИИ хранит только факт закрытия → Core: код причины в `pol.close` или колонка вне формата. `left_share` — нет `left`. `role` W/N/SOLE — Lab выводит из стадий и `PlatformGen` [Предп.: проверить по A41 F17b] |
| `fleet_cmd` | `Fleet`, `Gen`, `Bind`, `Inputs.GFront` | `price` у `SELL`: `Quote` — функция снимка t, а команда видна только после шага → цена = прирост `SaleInYear` в t+1 [Предп.] или убрать |
| `licences` | `Licence`, `Flow`, профиль | — |
| `calib_*`, `actor_month` | всё (`Calibration` без `Key`/`FV`/`why_not`) | — |
| параметры §3 | `FIX_*`, `START`, `PROFILE_CELL`, `CHANNELS`, `DERIV`, `FORCE` (`ForceProxy`), `FLEET_*`, `SALE_ARM`, `LINE_CLOSE`, `LIC_*`, `T6D_OFF`, `TOOLING_*`, `POOL_ORDER` уже есть; новые — `LAYER`, `CONTROL_TABLES`, `GRID`, `EFFORT_SHARE` | `fv_budget` не по умолчанию: `FvBudget` — вход пресета `adaptive`, т. е. параметр мира (нужен `run.*`), а не печати → пока только значение по умолчанию |

### 12.7 Риски
- **Р-12-1.** Неверная фаза `Inputs`: если собирать их после `RunTick` или до `ApplyDeals`, план и класс О в месяц сделки разойдутся с compute. Защита: тест — `Inputs.Tooling` равен `IndustrySystem.LastTooling`/`LastRest` того же шага на прогоне с лицензиями.
- **Р-12-2.** `EFFORT_SHARE ×4` может не пройти `PolicyLiveness` (L-C1…L-C3: доля закупок падает до 0,52 от прежней у P2). Тогда сборка громко падает. Решать simulation-engineer/Orchestrator, правило не ослаблять.
- **Р-12-3.** Объём поверхности: `TestApiVersion` будет расти с каждой новой печатью. Защита: правило ADR-0014 п. 6 — новые поля только под колонку формата.
- **Р-12-4.** Узкие места §12.6 (`left`, `close_rule`, варианты `Requirement`) — правки Core. TASK-0046 их не делает (условие «если нужна правка Core — остановиться»). Без них T10 пишет таблицы без этих колонок только после правки формата v0.4. Заглушки (пустые или нулевые колонки) запрещены.

## 13. Наблюдаемые величины среза (r4, TASK-0047) [Предл.]
Источник требований — формат `docs/simulation/slice-csv-format.md` v0.4 §4.15 (цитаты спеки v0.24 там). [Факт: код HEAD 094224e] `StateHash` хеширует **все** колонки `Table` по порядку регистрации (`Kernel/StateHash.cs`); исключить колонку из хеша нельзя. Значит, «не хешировать» = «не колонка».

### 13.1 Общее правило: наблюдение шага — след, а не состояние
- **Наблюдение** — величина, которую считает compute (или merge) месяца t и которую **никакой** compute/merge потом не читает. Такая величина хранится как **след последнего шага** (scratch вне `Table`), по образцу, который уже есть: `KnowledgeSystem.LastDiscoveries`, `LastSpent`, `IndustrySystem.LastTooling` («Diagnostics only»).
- Требования к следу (проверка — ревью и тесты §13.8):
  1. пишется только в compute/merge своей системы; строка актора `a` — только чанком, которому строка принадлежит (как колонки `Next`), поэтому merge не нужен, и значение не зависит от числа потоков;
  2. **полностью перезаписывается в каждом шаге** для каждой строки (очистка строки в начале её compute). Значит, след шага t — функция снимка t−1 и входов шага t; он не переносит информацию между тиками и не нарушает ADR-0013 п. 4;
  3. не хешируется и не сохраняется. После загрузки (T8) следа нет до первого месячного шага — это нормально, потому что view отдаёт его только для тика с месячным шагом (§13.7);
  4. симуляция его не читает. Члены-аксессоры помечаются атрибутом `Singularity.Core.ObservationAttribute` (как `SingleLayerStandAttribute`); архитектурный тест (`IlScanner`) запрещает вызов помеченных членов из кода `Singularity.Core`, кроме самого класса-владельца.
- **Состояние** — величина, которую читает симуляция в следующем тике. Только она идёт в колонку (и, значит, в хеш). Из пяти величин TASK-0047 это только `t_known` в режимах `L_C`/`L_P`.
- Рассмотренная альтернатива — колонки для всех величин. Отклонена: меняет golden-хеши и отпечаток схемы main ради печати; сохранение растёт; накопительный счётчик в колонке ничего не даёт Lab, который и так читает каждый месяц. Альтернатива «вывести в Lab по снимку» отклонена для `left` и причины закрытия: это повтор правил шага 7 A38 и F17b вне Core (то, что запрещает §2 и ADR-0014 п. 3), а при нескольких условиях в одном месяце — неоднозначно (формат §4.15).

### 13.2 `t_known` (спека §6.1 «Момент открытия»)
- **Решение: колонки `know.since.N` (long, по узлам в порядке `graph.Order`) только при `trackKnownSince = true`; Session включает флаг ровно при `Layer ∈ {Lc, Lp}`.** Колонки регистрируются после `know.K_ind.*`.
- Значения: `−1` — неизвестен (совпадает с контрактом `IIndustryInputs.KnownSince`/`IProductsInputs.AppKnownSince`); `t ≥ 0` — индекс месяца, в merge которого узел стал известен любым путём (открытие, расписание `L_K`, `lic_knows`); узлы стартового знания — значение `startKnownSince`. (r6) [Факт: код 4ade4ab, спека §6.1 v0.24 r2] Параметр конструктора необязательный: по умолчанию `KnownSinceMarks.StartKnown = −H = −384`, значения больше −H отклоняются (`ArgumentOutOfRangeException`). Так τ стартового узла не меньше горизонта (О-13-1 закрыт, §13.9).
- **Кто пишет:** `KnowledgeSystem.Merge` месяца t — в той же ветке, где `_known[r].Next[a] = 1` (`_since[r].Next[a] = t`, `t = MonthOf(context)`). Merge последовательный; запись — один раз на (a, r) (проверка `Next != 0` уже есть), значение — `t` при любом порядке чанков. Детерминизм L1/L2 сохраняется без новых правил порядка.
- **Чтение:** `KnowledgeSystem.KnownSince(actorRow, nodeRow)` по `Current` — `InvalidOperationException`, если флаг выключен (громкая ошибка вместо тихого −1). Адаптеры: `IndustryInputsAdapter.KnownSince` → узел процесса владельца site; `ProductsInputsAdapter.AppKnownSince` → `GenApp[g]`. Сборка: `IndustryMode.CurveLc` или `ProductsMode.CanonicalLp` без флага — исключение в `SliceWorld`.
- **Хеш:** в `L_C`/`L_P` колонки хешируются — это состояние, его читает compute t+1 (кривая `Y_k(t − t_known)`). Обоснование хеширования: без него сохранение `L_C` продолжилось бы с другим `yield`. В main колонок нет: хеш и отпечаток схемы main не меняются. `run.layer` = 4 (`Lc`), 5 (`Lp`).
- **«Всегда» против «только в `L_C`/`L_P`» и правило §12.4 «по умолчанию = main»:**
  | | Всегда | Только `L_C`/`L_P` (выбрано) |
  |---|---|---|
  | golden main 60/384 | пересоздаются (61 узел × 5 акторов новых колонок) | не меняются |
  | правило §12.4 | нарушает: состояние main растёт ради режима не по умолчанию | выполняет: колонки появляются только при значении не по умолчанию, как `run.tooling` |
  | польза для main | нет: в main `t_known` никто не читает; Lab получает его из смены `NodeView.Known` между снимками (формат §9), стартовые узлы — «освоено на старте», исключаются (спека C2) | — |
  | цена | сохранение и хеш больше; `Merge` пишет всегда | два пути сборки; защита — исключения выше и тест «в `Lc` с флагом хеш отличается, в main колонок нет» |
  Вывод: «всегда» имело бы смысл, только если `t_known` нужен самой симуляции main. Сейчас не нужен. Если позже понадобится (например, игроку), это новая задача с причиной пересоздания golden.
- **View:** `NodeView.KnownSince : long?` — значение колонки, `null` при выключенном флаге. Lab в main берёт `t_known` из смены `Known`, в `L_C`/`L_P` — из поля (тест: совпадают в месяцы открытий).
- **Не входит:** таблицы `Y_k`, `U_k`, `T_design`, `ρ` (T12) и вопрос `ProductAi` в `L_P` (§12.5) — без них серии `layer/L_C`, `layer/L_P` по-прежнему прогоняются только на синтетических таблицах тестов.

### 13.3 `left` и `left_share` (A40 «Печать причины», «v0.20: две линии»; A41 F17b «Печать»)
- **Решение: след шага 7 A38 в `ProductsSystem`.** В `Purchases` для строки `a`:
  - `LeftStart(a)` — `left` в начале шага 7 (после содержания и доводки);
  - по каждой линии `d` в стадии С, в момент, когда шаг 7 до неё дошёл (по возрастанию `design_id`): `LeftIn(a, d)` — общие деньги до её закупки (до проверки `Admitted`), `FromLeft(a, d)` — взято из общих, `Training(a, d)` — `TrainingBudget(a, d, t)` входа, `FromTraining(a, d)` — взято из него (`pay − fromLeft`).
  - Проекты не в С и режим `L_P` (`StepCanonical`) — след пустой (`HasMoneyTrace = false` для строки).
  - (r6) ~~`Training` читается только у допущенной линии~~ — заменено после второго ревью Codex (high): `Training` = фактический `TrainingBudget` входа у каждой линии стадии С; у недопущенной `FromLeft = FromTraining = 0`, `LeftIn` записан. Адаптер читает только снимок, динамика и golden не меняются.
- **Хеш:** нет (§13.1). Существующие `prd.spent`, `prd.tbSpent` остаются как есть.
- **Определения для формата v0.5 (simulation-engineer):** `left` строки `a40_c_months` = `LeftIn(N)` — деньги, которые видела линия; при обычном порядке `design_id(W) < design_id(N)` это и есть «`left` после `W`» (спека A41 F17b п.3). `COEXIST_MONEY` = в каждом из `T_ab` месяцев выпуск `N` = 0, `LeftIn(N) = 0` и `Training(N) = 0`. (r6) [Предл.] Условие с `Training(N) = 0` не отличает «нет денег» от «нет допуска». Поэтому формату v0.5 предлагается определять `COEXIST_MONEY` так: в каждом из `T_ab` месяцев выпуск `N` = 0 и `DesignView.Bind == Money` (`LineBind`, договор r6 §4.4d шаг 7). У недопущенной линии `Bind = None`. `left_share` = `FromLeft(N) / LeftStart` (при `LeftStart = 0` — пусто). [Предп.] Случай `design_id(W) > design_id(N)` (спека F17b п.2, «X с `g_p(X) < g_p(W)`») редкий; Lab помечает его, а не пересчитывает.
- **View:** `SliceView.Step.Actors[a].Money` (§13.7).

### 13.4 Причина закрытия линии (`close_rule`)
- **Решение: след решения в `PolicySystem`** — `LastCloseReason(row, design) : LineCloseReason`, пишется в `Decide` в момент `dec.Closes.Add(d)`; одна причина на проект в месяц (первая добавленная: `!dec.Closes.Contains` уже обеспечивает один `close_line`, спека A40 «Двойная команда»). Очистка строки — в начале `ComputeProducts`.
  ```csharp
  public enum LineCloseReason { None = 0, A40C = 1, YProd = 2, LaunchAll = 3, LaunchCandidate = 4 }
  ```
  `A40C` — ветка С A40; `YProd` — F17b п.2; `LaunchAll` — F17b п.1 при `y_min(X) ≥ y_prod` или `LINE_CLOSE = ADMIT`; `LaunchCandidate` — F17b п.1 ниже `y_prod`, заменён кандидат. Разделение по роли (`A40C_N`/`A40C_W`) и «горизонт» — в Lab (роль — по стадиям, формат §4.12). [Предл.] формат v0.5 отображает `LaunchAll` и `LaunchCandidate` в `REPLACE (п.1)`; Core отдаёт более точный код, чтобы не терять различие.
- **Хеш:** нет. Альтернатива «код вместо стамп-значения в `pol.close.{d}.stamp`» отклонена: меняет хешируемые значения main.
- **Детерминизм:** `Decide` — compute строки; след индексируется строкой.
- **View:** `PolicyDecisionView.Closes : ImmutableArray<CloseView>` вместо `ImmutableArray<int>` (`CloseView(int Design, LineCloseReason Reason)`); причина берётся из следа только в тике с месячным шагом, иначе — `None` (§13.7).

### 13.5 Варианты `Requirement` (§5.5, §7 S1)
- **Решение: чистая функция снимка, хранения нет.**
  ```csharp
  public readonly record struct PresetVariants(int Gen, int Count,              // 0 — superior не собирается; 1 — только superior (или producible = superior, F-PC3); 2
      ImmutableArray<int> Superior, ImmutableArray<int> Producible,             // Producible пуст при Count < 2
      Fx6 FvSuperior, Fx6? FvProducible);                                       // CandidateValue(..., p.FvBudget, month)
  public static PresetVariants ProductPresets.Variants(IPolicyProducts products, PolicyParams p, int actor, int gen, long month);
  public PresetVariants PolicySystem.RequirementVariants(int row, long month);  // gen = g* (NewestGeneration); Count = 0 при g* = 0
  ```
  `Compose` переписывается через общий закрытый шаг построения кандидатов (тот же, что у `Variants`) — логика выбора профиля одна, дубля нет. `Variants` считает `FV` обоих кандидатов при любом профиле (у `Compose` `Quality`/`Mass` их не считают); это чистые вызовы, на состояние не влияют.
- **Хеш:** нет и не может измениться, если рефакторинг `Compose` эквивалентен; защита — тест §13.8 и неизменный golden.
- **Где Session вызывает:** при сборке `Inputs` месячного шага с `Month mod 12 = 0` (включая 0: снимок перед шагом 0 — состояние после `Initialize`, `Initialize` пишет только `pol.*`, а `Variants` их не читает [Предп.; проверка — тест]). Поле `ActorInputsView.Requirement : RequirementView?` — `null` вне годовых месяцев и в `ViewMode = Calibration` (там `FV` запрещён, §12.1). Lab печатает строку `requirements` только при compose-команде ИИ того же шага (`Decisions.DesignCommands`, `Target = Gen`).

### 13.6 Отказы ИИ от лицензии по деньгам (§6.11 «Печать», A43.3 (б))
- **Решение: след решения в `PolicySystem.ComputeLicence`** — по (покупатель `row`, продавец `s`): отказ, когда профиль хочет предложение (`accept` до проверки денег), а `room < price`. Хранится `price`, `gen` и `room` (raw) в массиве `[row·n + s]`; −1 — нет отказа. Очистка строки — в начале её compute. Аксессор `LastMoneyRefusals(row)` → список по возрастанию `s` (порядок перебора продавцов в коде).
- **Хеш:** нет. Накопительный счётчик в колонке отклонён: меняет хеш main; Lab суммирует по месяцам сам и получает строку события на отказ (формат: `LIC_REFUSE_MONEY` с продавцом, поколением, ценой). `BadLicenceCount(Money)` (отказы внешних команд) не меняется — это другая величина.
- **View:** `SliceView.Step.Actors[a].LicenceMoneyRefusals` (§13.7).

### 13.7 Поверхность Session, `TestApiVersion = 2`
Изменения против §12.2 (остальное без изменений):
```csharp
public const int TestApiVersion = 2;                 // эталон tests/Singularity.Session.Tests/Api/session-surface.v2.txt

public sealed record SliceView(long Tick, CivilDate Date, string StateHash, long Month,
    ImmutableArray<ActorView> Actors, MonthInputsView? Inputs,
    MonthStepView? Step);                            // НОВОЕ: не null ровно когда не null Inputs (тик с месячным шагом)

public sealed record NodeView(int Number, bool Known, bool ByLicence, ProcessView? Process,
    long? KnownSince);                               // НОВОЕ: know.since.N; null без trackKnownSince (§13.2)

public sealed record PolicyDecisionView(ImmutableArray<DesignCommandView> DesignCommands, ImmutableArray<StartView> Starts,
    ImmutableArray<CloseView> Closes,                // ИЗМЕНЕНО: было ImmutableArray<int>
    ImmutableArray<FleetCommand> Fleet, ImmutableArray<int> Repeats);
public readonly record struct CloseView(int Design, LineCloseReason Reason);   // Reason = None в снимке без месячного шага

public sealed record ActorInputsView(int Actor, Fx6 Budget, BudgetShares Shares, Fx6 ProcurementBudget,
    ToolingLedgerView Tooling, CapabilityView Capability, ImmutableArray<DesignInputsView> Designs,
    RequirementView? Requirement);                   // НОВОЕ: §13.5
public sealed record RequirementView(int Gen, int Count, ImmutableArray<int> Superior, ImmutableArray<int> Producible,
    Fx6 FvSuperior, Fx6? FvProducible);

// НОВОЕ: результаты месячного шага Month (след, §13.1); собираются после RunTick того же тика
public sealed record MonthStepView(long Month, ImmutableArray<ActorStepView> Actors);
public sealed record ActorStepView(int Actor,
    MoneyStepView? Money,                            // null в L_P (§13.3)
    ImmutableArray<LicenceRefusalView> LicenceMoneyRefusals);
public sealed record MoneyStepView(Fx6 LeftStart, ImmutableArray<LineMoneyView> Lines);   // линии стадии С по возрастанию design
public readonly record struct LineMoneyView(int Design, Fx6 LeftIn, Fx6 FromLeft, Fx6 Training, Fx6 FromTraining);
public readonly record struct LicenceRefusalView(int Seller, int Gen, Fx6 Price, Fx6 Room);
```
- `Step` собирается после `Runner.RunTick()` и до публикации, только если в тике был месячный шаг; в пустом и командном тике — `null` (след прошлого шага не переиздаётся). Начальный снимок (`Create`) — `null`.
- `ViewMode = Calibration`: `Requirement = null`; `Step` собирается (запрета спеки §8.2 п. 2 на деньги и причины нет; калибровочные таблицы их не пишут).
- Новый эталон поверхности v2 и ревью systems-architect (§12.3). Lab меняет чтение `Closes` (тип) — правка T10/T11.
- (r5) **Nullable-аннотации в тексте поверхности v2.** [Факт: `session-surface.v1.txt`] Текст v1 строится по типам отражения и теряет аннотации ссылочных типов: `SliceView.Inputs` записан как `MonthInputsView` без `?`. Значит, правка `X?` → `X` проходит без смены версии. В v2 тест поверхности (TASK-0047) пишет nullability каждого параметра, свойства и возвращаемого значения через `System.Reflection.NullabilityInfoContext` (например, `MonthInputsView?`). Значимые `Nullable<T>` остаются как есть. Эталон v1 не меняется.
- (r5) **Lab читает views по имени свойств**, а не позиционным `Deconstruct` и не позиционными шаблонами. Новые позиционные параметры (`Step`, `KnownSince`, `Requirement`) меняют `Deconstruct`, а при чтении по имени Lab при смене версии затрагивают только реально изменённые поля (здесь — `Closes`). Это правило кода T10/T11; проверка — ревью reviewer.

- (r6) **`TestApiVersion = 3`**: `LicenceView.Active` (`LicenceActive(a)`) — для `LIC_END` без вывода по `LicenceShare` (slice-csv-format §11 п. 7). Эталон `session-surface.v3.txt`; состояние, хеши и golden не меняются.

### 13.8 Проверки (developer, test-engineer)
- golden main 60/384 мес. и отпечаток схемы main **не меняются** (главный критерий: всё, кроме `t_known` в `L_C`/`L_P`, — не состояние).
- L2: на 1/4/8 потоках `Step` и `KnownSince` всех снимков совпадают (каноническая запись view, как хеш).
- Архитектура: члены с `[Observation]` не вызываются из `Singularity.Core` вне класса-владельца (`IlScanner`); `KnowledgeSystem.KnownSince` без флага бросает; `CurveLc`/`CanonicalLp` без флага — ошибка сборки.
- `t_known`: в `Lc` с флагом — `KnownSince` = месяц смены `Known` для открытых узлов, `startKnownSince` для стартовых; лицензионные узлы — месяц merge гранта; хеш `Lc` ≠ хеш main.
- `left`: инварианты `Σ FromLeft ≤ LeftStart`, `LeftIn(d) = LeftStart − Σ_{d' < d, С} FromLeft(d')`, `FromLeft + FromTraining = Rate·UnitCost` линии (в коде это одно `pay = r·cost`, равенство точное), `Σ FromTraining = prd.tbSpent`.
- Причина: фикстуры на каждую из четырёх веток (`A40C`, `YProd`, `LaunchAll`, `LaunchCandidate`, плюс ADMIT); каждая закрытая `CloseView` в тике с шагом имеет `Reason ≠ None`.
- Варианты: для каждого профиля и каждого годового месяца связанного прогона выбор профиля по `Variants` (`Quality` → superior; `Mass` → producible при `Count = 2`; `Adaptive` → больший `FV`, ничья → superior) равен составу compose-команды ИИ того же шага.
- Отказы: фикстура с `room < price` у `Mass` — один `LicenceRefusalView`, `BadLicenceCount(Money)` не меняется; у `Quality` отказов по деньгам нет.

### 13.9 Риски и открытые вопросы
- **О-13-1 — закрыт (r6):** `startKnownSince = −H = −384` (спека §6.1 v0.24 r2, код 4ade4ab). Исходная формулировка: `t_known` узлов стартового знания спека не определяет. Сейчас адаптер даёт −1, и в `L_C` стартовые процессы USA/GBR получили бы `yield = Y_k(·)` «неизвестного» = 0 (`IndustrySystem.Yield`: `since < 0 ? 0`) — линии t0 встали бы. Варианты: `startKnownSince = −τ_max` (зрелый хвост кривой), `0`, или значение из данных сценария. Архитектура принимает любое как параметр сборки; выбор — erratum спеки §6.1.
- **Р-13-1.** След читается в Session после `RunTick`; если кто-то начнёт собирать `Step` в начале следующего тика или после загрузки — получит пустой/чужой след. Защита: `Step` только в той же ветке, что `Inputs`; тест «после командного тика `Step = null`».
- **Р-13-2.** Рефакторинг `Compose` через общий шаг может изменить порядок вызовов `CandidateValue` — функция чистая, но любое расхождение выбора сразу видно по golden. Защита: golden + тест эквивалентности §13.8.
- **Р-13-3.** Смена типа `PolicyDecisionView.Closes` ломает Lab-код T10 этапа 1, если он уже читает `Closes`. Альтернатива без поломки — отдельное поле `CloseReasons`; выбрана замена, потому что поверхность ещё не заморожена потребителем (T10 этап 1 — draft).
- **Р-13-4.** `left` при `design_id(W) > design_id(N)`: «после `W`» спеки и «до `N`» следа расходятся. Lab помечает такие месяцы; спека — уточнение, если случай встретится в печати.
- Нового ADR не нужно: след шага — уже применяемый в Core приём («Diagnostics only»), здесь он только формализован (§13.1) и закреплён IL-тестом. [Предп.] Orchestrator подтверждает, что это толкование ADR-0013 п. 4 и ADR-0014 п. 5, а не их изменение.
