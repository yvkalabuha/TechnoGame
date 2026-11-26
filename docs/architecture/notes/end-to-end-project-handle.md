---
tags: [note, core, institutions, draft]
---
# Сквозной handle проекта `(slot, generation)` — DEBT-0052

Автор: systems-architect, 2026-11-19. Статус: проектная записка [Предл.], не решение. Основание: ADR-0005 п. 3 (ACCEPTED, #388), ADR-0014 п. 2, ADR-0013 пп. 7, 9, TASK-0099 «Результат».

## Краткий вывод
- [Факт, код] Handle проверяется только внутри `ProductsSystem` и только если вызывающий передал `Generation ≥ 0`. Все реальные источники передают −1 или вообще не имеют поля: `SliceCommand` (Derive/Cancel/StartSeries/CloseLine/Retire/Sell), `CommandState` (`cmd.*`), `PolicySystem` (`pol.*`), `ProductsInputsAdapter`, `ForceProxy`, клиент (`ActorPanel`), `WhyNotTarget.Design`. Проверка фактически выключена везде.
- [Предп., чтение кода, не воспроизведено] Окно устаревшего handle реально: (1) клиент показывает вид месяца m, а поток симуляции уже ушёл вперёд — `cancel_design(d)` попадёт в новый проект в слоте d; (2) ИИ решает в compute месяца t, применяется в t+1 после внешних design-команд того же шага: внешний `cancel(d)` в t → внешний `compose` в t+1 занимает d → `Cancel(d)` ИИ в t+1 снимает **новый** проект; (3) `ForceProxy._sent` ключуется `(actor, design)`, поэтому новый проект в переиспользованном слоте никогда не получит FORCE.
- [Предл.] Ввести `DesignHandle(Slot, Generation)` и протащить его через все слои; поле обязательное, значения «не передан» нет. Журнал `commands.log` получает версию 2 (строка-заголовок); журнал v1 отклоняется громкой ошибкой, без миграции.
- [Предл.] Нового ADR не нужно: это light-задача внутри ADR-0005 п. 3. Формат строк журнала ADR-0014 не фиксирует (он в `SLICE_COMPOSITION.md` §7.5 с пометкой [Предл.] и в `CommandCodec`); правится §7.5. ADR-0013 п. 9 уже говорит, что replay — артефакт сборки, а не сохранение.
- [Предл.] Ожидаемое влияние на golden: меняется только схема (новые колонки `*.gen`, `SchemaVersion` 10 → 11), траектории без устаревших handle не меняются.

## 1. Тип handle
- `Singularity.Core.Products.DesignHandle` — `readonly record struct (int Slot, int Generation)`. `Generation ≥ 1` у любого занятого слота (TASK-0099: стартовый слот = 1, свободный = 0). Значения −1 нет.
- Единая функция разрешения: `ProductsSystem.Resolves(int actor, DesignHandle h)` (Current) и `IsCurrent(int actor, DesignHandle h)` (Next) поверх существующего `HandleHolds`. Других проверок пары в коде нет (ADR-0005 п. 3).
- Имя поля в C# — `Design`/`Parent` типа `DesignHandle`; не `Generation`, чтобы не путать с поколением техники `g*` (`ComposeDesign.Generation`, DEBT-0055).

## 2. Где несётся handle, по слоям

| Слой | Сейчас | Должно быть |
|---|---|---|
| Клиент / UI (`ActorPanel`, будущие экраны) | `new CloseLine(a, d.Design)`, `new CancelDesign(a, d.Design)` | handle из `DesignView`: `new DesignHandle(d.Design, d.Generation)`. UI не вычисляет generation сам. |
| Read views (`DesignView`) | `Generation` уже есть (TestApiVersion 10) | без изменений; [Предл.] удобное свойство `Handle` |
| `SliceCommand` | `DeriveDesign.Parent`, `StartSeries/CloseLine/CancelDesign/RetireCohort/SellCohort.Design` — `int` | `DesignHandle`. `ComposeDesign` handle не несёт (создаёт сущность). |
| `CommandValidator.CheckForm` (Submit) | диапазон слота | диапазон слота **и** `Generation ≥ 1`; иначе отказ `CommandRefusal.BadHandle` [новый код]. Только форма: проверка «устарел» — не при Submit (см. §3.3). |
| `CommandCodec` | `"design":d` | `"design":d,"design_gen":g`; для derive `"parent":p,"parent_gen":g`; заголовок журнала (§3) |
| `CommandState` (`cmd.*`, хешируется) | target/design без gen | колонки `cmd.dsn.{k}.gen`, `cmd.flt.{k}.gen`, `cmd.series.{d}.gen`, `cmd.close.{d}.gen` |
| `PolicySystem` (`pol.*`, хешируется) | `pol.dsn.{k}.target`, `pol.flt.{k}.design`, `pol.start.{d}`, `pol.close.{d}` без gen | колонки `pol.dsn.{k}.gen`, `pol.flt.{k}.gen`, `pol.start.{d}.gen`, `pol.close.{d}.gen`; generation берётся из снимка в момент решения (`products.Generation(a, d)` на Current) |
| `IProductsInputs` | `DesignCommand.Generation = -1`, `FleetCommand.Generation = -1`, `SeriesStart(Rate, Force)`, `bool CloseLine(...)` | `DesignCommand` и `FleetCommand` несут `DesignHandle` без значения по умолчанию; `StartSeries`/`CloseLine` возвращают кандидатов в порядке ключа (§2.1) |
| `ProductsInputsAdapter` | склеивает внешние и ИИ, проверок нет | только переносит handle; **никакой проверки в Session** (проверка — Core) |
| `ProductsSystem` | `cmd.Generation >= 0 && !IsCurrent(...)` | безусловно `!IsCurrent(a, handle) → StaleHandle`, для Derive, Cancel, Retire, Sell, StartSeries, CloseLine |
| `ForceProxy` (harness) | `_sent` по `(a, d)` | `_sent` по `DesignHandle`; команды с handle |
| `WhyNotTarget.Design` | голый слот | handle; устаревший → ответ «сущности нет» (одна ссылка-причина `StaleHandle`), без чтения данных чужого проекта |
| `PolicySystem.CancelPending` | сравнивает target | сравнивает handle |

### 2.1. `start_series` и `close_line`: внешний и ИИ в одном месяце
[Факт, код] Сейчас адаптер отдаёт внешнюю команду, если она есть, иначе ИИ (`ProductsInputsAdapter.StartSeries/CloseLine`). Если внешняя устарела, а ИИ — нет, простая передача «первой» потеряет решение ИИ.
[Предл.] `IProductsInputs.StartSeries` → `SeriesStarts(actor, design, month)`: до двух `SeriesStart(DesignHandle, Rate, Force)` в порядке ключа (внешняя, затем ИИ); `CloseLine` → `LineCloses(...)`: до двух `DesignHandle`. Core применяет **первую разрешимую**, каждую устаревшую считает `StaleHandle`. Без устаревших handle поведение совпадает с текущим (внешняя побеждает), поэтому траектории golden не меняются.

## 3. Журнал команд: версия 2

### 3.1. Формат
- Первая строка `commands.log` — заголовок `{"commands_log":2}`. Далее строки команд, как сейчас, с новыми полями `design_gen` / `parent_gen` сразу после `design` / `parent` (канонический порядок ключей сохраняется).
- `CommandCodec.EncodeLog` пишет заголовок; `Encode(single)` заголовка не пишет (очередь в сейве ADR-0013 п. 5 получит версию в заголовке сейва — `save_format_version`; сейвов в коде пока нет [Факт: grep `CreateSave` по `src/` пуст]).
- `CommandCodec.LogVersion = 2` — константа рядом с кодеком.

### 3.2. Старые журналы
- Журнал без заголовка = v1 → `DecodeLog` бросает `FormatException("commands.log v1 has no design handles (ADR-0005 p. 3); re-record the run")`. Строка v2 без `design_gen` у команды с handle → `FormatException`. Версия больше известной → `FormatException` (как ADR-0013 п. 7).
- Миграции v1 → v2 нет. Основания: [Факт] в репозитории нет сохранённых журналов (журналы живут в памяти тестов и в `artifacts/runs/`, вне git; `JetSliceModel` читает `s.CommandLog` той же сессии); [Факт] ADR-0013 п. 9 и `TESTING_ARCHITECTURE_PROPOSAL.md` (вариант C): replay ломается при любой смене кода, это артефакт для воспроизведения дефектов. Отвергнутая альтернатива — «дозаполнить gen при replay из текущего состояния»: проверка всегда проходит, это и есть голый индекс.

### 3.3. Детерминизм replay
- Generation — обычная колонка (`prd.d{d}.generation`) в хеше и снимке. Журнал хранит generation, который видел отправитель; при replay то же состояние → то же разрешение → тот же исход (принята / `StaleHandle`) → тот же хеш.
- Устаревание проверяется **только при применении** (compute месяца), не при `Submit`: состояние в момент Submit и в момент применения различается (очередь до границы тика), а отказ при Submit не попадает в журнал. Команда с устаревшим handle принимается в журнал и при replay снова отклоняется по состоянию (как остальные отказы по состоянию, `SLICE_COMPOSITION.md` §7.5).
- Отказ `StaleHandle` не меняет состояние, кроме счётчика `prd.rejected.StaleHandle` (он в хеше, одинаков в обоих прогонах).
- L2: проверка — в compute по строке своего актора, читает только Current/Next своей строки; от числа потоков не зависит.

## 4. Нужен ли ADR
[Предл.] Нет. ADR-0005 п. 3 уже требует handle «во всех ссылках (колонки, команды, `why_not`, UI)» — задача исполняет ACCEPTED-решение (light, DF-001 §6). ADR-0014 задаёт состав журнала (только внешние команды, ключ, `apply_tick`), но не формат строки и не версию; версия 2 журнала не меняет ни одно положение ADR-0014. Нужны правки неадровых документов: `SLICE_COMPOSITION.md` §7.5 (версия журнала, заголовок, отказ v1) и `LAYER_CONTRACT.md` (§ команд: поля handle). Если владелец захочет миграцию журналов v1 — тогда gate (формат данных).

## 5. Миграция тестов
- `CommandTests.Codec_RoundTripsEveryCommand_WithOneFormat`: команды с handle; плюс проверка заголовка и отказа v1.
- Все `new StartSeries/CloseLine/CancelDesign/DeriveDesign/RetireCohort/SellCohort(...)` [Факт: 51 вызов в 10 файлах, из них тесты: `CommandTests` 18, `FleetPlayerCommandTests` 12, `ProductAiSessionTests` 4, `AssembledSliceProperties` 2, `NodeBlockReuseTests` 2, `PlayerScript` 2, `DailyTickProbeTests` 1]. Тестовый помощник `Handle(session, actor, slot)` читает generation из `View` — тесты не должны подставлять константу 1 вслепую.
- `ProductsSlotGenerationTests`: тесты, передающие −1 «чтобы пропустить проверку», переписать на явный handle; удалить сценарии «без handle».
- Golden: `SchemaVersion` 10 → 11 и fingerprint схемы меняются из-за колонок `cmd.*.gen`/`pol.*.gen`; полные хеши перезаписываются **с объяснением в задаче**. Критерий «траектория не изменилась»: проекции без новых колонок (как в TASK-0099) совпадают до последнего golden-месяца.
- `TestApiVersion` 12 → 13 (в записке было «11 → 12»; на момент слияния версия на `main` — 12) (меняются типы аргументов `Submit`, ADR-0014 п. 6), новый `session-surface.v13.txt`; `JetSliceModel.SurfaceVersion`.
- `lab selftest`: `SliceObserver`/`SliceChecks`, где они строят команды или читают `FleetCommand.Generation`.

## 6. Разбиение для developer (light, после утверждения Orchestrator)
**H1. Тип и Core.** `DesignHandle`; `Resolves/IsCurrent(actor, DesignHandle)`; `DesignCommand`/`FleetCommand` с обязательным handle; `SeriesStarts`/`LineCloses` (§2.1); удалить ветку «−1 = не передан». Тесты Core:
- stale Cancel/Derive/Retire/Sell/StartSeries/CloseLine → `StaleHandle`, хеш состояния, кроме счётчика отказов, равен прогону без команды;
- внешний stale + ИИ валидный в одном месяце → применена команда ИИ, один `StaleHandle`.

**H2. Команды и журнал.** `SliceCommand` с `DesignHandle`; `CheckForm` (`BadHandle` при `Generation < 1`); `CommandState` колонки `*.gen`; `CommandCodec` v2 с заголовком. Тесты:
- round-trip всех видов; заголовок обязателен; v1 и `design_gen` отсутствует → `FormatException`; версия 3 → `FormatException`;
- **stale из replay:** запись прогона, где игрок шлёт `cancel_design` по handle, устаревшему к моменту применения; `Replay` даёт тот же `StateHash`, тот же исход `rejected: StaleHandle` в `Outcomes`; ручная правка `design_gen` в строке журнала → replay детерминированно отклоняет команду (два replay → одинаковый хеш).

**H3. ИИ.** `pol.*.gen`; generation из снимка в момент решения; `CancelPending` по handle. Тесты:
- **stale от ИИ:** сценарий §Краткий вывод (2): внешний cancel(d) в t, внешний compose в t+1 занимает d, ИИ-cancel(d) решён в t → новый проект жив, ИИ получает `StaleHandle`. До исправления тест должен падать (проверить на `main`);
- L2: та же последовательность при 1 и N потоках → одинаковые handle и хеш.

**H4. Session, harness, UI.** `ProductsInputsAdapter` переносит handle; `ForceProxy._sent` по handle (тест: проект в переиспользованном слоте получает FORCE); `ActorPanel` берёт handle из `DesignView`. Тесты:
- **stale из UI:** вид снят в месяце m, сессия прошла до m+k, слот переиспользован; `Submit(CancelDesign(handle из старого вида))` принят в журнал, применение → `StaleHandle`, новый проект не тронут;
- `Submit` с `Generation = 0` → `BadHandle`, в журнал не попадает.

**H5. why_not.** `WhyNotTarget.Design(DesignHandle)`; устаревший → одна ссылка `StaleHandle`. Тест: why_not по устаревшему handle не читает поля нового проекта (ровно одна ссылка `StaleHandle`).

**Порядок:** H1 → H2 → H3 → H4 → H5; H1–H2 одним PR допустимо (иначе не компилируется). Reviewer на каждый diff; test-engineer — property-тест «случайные последовательности cancel/compose/start/close с handle из случайного прошлого вида: ни одна команда не меняет проект другой generation».

## 7. Бюджет
[Предп.] Новые колонки: `cmd` 4 + 8 + 12 + 12 = 36, `pol` 6 + F + 12 + 12 (F — `pol.flt` слоты) — порядка 70 `int`-колонок на строку актора. На фоне ≈ 8800 колонок полного масштаба (ADR-0017) это < 1 % CoW/хеша; проверить `K_d` бенчмарком TASK-0107 до и после (ADR-0020 п. 20).

## 8. Вне объёма
- DEBT-0053 (`PolicySystem.Fleet.Candidate` по индексу слота) — отдельная задача; handle её не решает, но даёт `Generation` для порядка.
- DEBT-0055 (имена `Gen`/`Generation` в `DesignView`) — можно закрыть заодно в H4, раз `TestApiVersion` всё равно растёт.
