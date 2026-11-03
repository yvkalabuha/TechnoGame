---
id: TASK-0022
title: "T6a: система политики ИИ и сборка мира среза (Session)"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0020, TASK-0021]
related: [HYP-0002]
approved_by: orchestrator
approved_at: 2026-11-03
approval_ref: "DECISIONS_LOG #123–#126; спека v0.12 §11"
created: 2026-11-03
---

# TASK-0022 — T6a: система политики ИИ и сборка мира среза (Session)

## Цель
Собрать мир среза «реактивная авиация» в одной точке (`Singularity.Session`, ADR-0014) и добавить систему политики ИИ без правил способности и изделия (T6a спеки v0.12). Закрыть часть DEBT-0016 (composition root, `Planned`/`PlannedVolume`, журнал команд, проверка повтора фокуса).

## Описание
- Сборка мира по `docs/architecture/SLICE_COMPOSITION.md` (решения [Предл.] приняты Orchestrator как локальные; П-1 — #126): сборка `Singularity.Session`, `SliceWorld`, `SliceSession` (`Submit`, `Step`, применение команд на границе тика, `commands.log`), порядок систем Knowledge → Industry → Products → Events → Policy.
- `PolicySystem` (A41): профили P1–P3 из потока `AI`, доли и `share_D`, `prio`/`j`, фаза инициализации A8; годовой ритм решений. Правила оснастки, учебной серии, программы качества, `Requirement`, запуска, лицензии и перебоя — **не** в этой задаче (T6b–T6d); на их месте нет заглушек, выдающих себя за правило.
- Потоки RNG §9.1, `FIX_*`, `START_PERT`/`START_SYM`, факторные ячейки §6.6, переключатели каналов §6.2.
- Журнал команд: колонки `cmd.*`, ключ `(apply_tick, source_class, source_id, local_n)`, проверка при `Submit` (повтор фокуса наладки, `force` не от Ai, сумма долей = 1). Решения ИИ — колонки `pol.*`, без поля `force`.
- `Planned`/`PlannedVolume` из `ProductsSystem`; убрать переходные значения по умолчанию в `IIndustryInputs`/`IKnowledgeInputs`.
- Golden 60 мес. перенести в `tests/Singularity.Session.Tests` (новый хеш, причина «composition root T6a» в сообщении коммита); удалить `ProductsLinkedRunTests` (V23).
- `IndustrySystem._lastRest` вне таблицы: проверить против ADR-0013 п.4; если нарушает — перенести в колонку.

## Критерии приёмки
- [ ] Всё из спеки v0.12 §11 «T6a» (ключи потоков, `FIX_ALL`, `FIX_A`, доли, `START_PERT`, `START_SYM`, номер ячейки `9u + 3g + s`, A8, переключатели §6.2).
- [ ] Общие пункты §11 T6a–T6d: `dotnet test` зелёный, ADR-0003 R1–R3 зелёный, нет `CounterRng` в `Core.Industry`/`Core.Products` и их адаптерах, L1 и L2 (threads 1/4/8, seeds 1…20) на сборке среза.
- [ ] Мир среза собирается только через Session (архитектурный тест); `SmokeT4s.LineStand` помечен как стенд одного слоя.
- [ ] Журнал команд пишется и читается одним кодеком; отказ `Submit` даёт причину, а не только счётчик.

## Тесты
Тесты по §11 T6a; архитектурные тесты сборок; golden-хеш Session; L1/L2.

## Связанные документы
`docs/simulation/slice-jet-aviation.md` v0.12 (§4, A8, A41, §6.2–§6.6, §9.1, §11); `docs/architecture/SLICE_COMPOSITION.md`; ADR-0004, ADR-0006, ADR-0013, ADR-0014; `.claude/rules/csharp-core.md`; DEBT-0016.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

### Ревью test-engineer
[Факт] Добавлен `tests/Singularity.Session.Tests/AssembledSliceProperties.cs`: 18 тестов (13 фактов + теория 8 комбинаций FIX), +~3 с. Весь набор зелёный (Session 90, Core 779, Kernel 284+1, Architecture 77); src не менялся, багов в src не найдено.
- Журнал: один и тот же `commands.log` и хеш при любом чередовании источников (случайные сценарии, 14 тиков, смесь месячных и нечётных тиков); ключи строго растут, `local_n` растёт по источнику, ИИ никогда не форсирует, кодек круглый на каждой строке, число исходов = число строк журнала.
- Replay: тот же хеш на threads 1/4/8 и разных chunk; префикс журнала даёт хеш «сохранения» на середине; повтор replay — неподвижная точка; другой seed хеш не воспроизводит. L2 скриптового прогона 120 тиков на 1/4/8.
- START_PERT: множители в [0,8; 1,2] на 300 seed × 60 индексов, детерминизм, среднее ≈ 1; загруженный сценарий не мутируется; под `FIX_ALL` возмущённый старт всё равно следует seed. START_SYM: сценарий не зависит от seed, равенство USA/GBR/SUN в мире (бюджет, знание) и после 24 мес.
- FIX_*: все 8 комбинаций дают ровно свои seed потоков; FIX_K/FIX_E не трогают профили; FIX_A даёт один набор профилей на 30 seed.
- Пробелы: L2 уже были в `LinkedRunTests`; нет теста сохранения мира в файл (формата save в T6a нет); `StateHash` по-прежнему включает тик (п. 7 отклонений); экономика 384 мес. не проверяется до T6b; FIX_E не имеет поведенческого эффекта (правил событий нет), проверены только seed потока.

## Результат
**Сделано** (7 коммитов на main, не запушено; developer, 2026-11-03).
- `Singularity.Session` (новая сборка, ссылки только на Core и Kernel): `SliceWorld` (internal; таблица «строка = актор», порядок систем Knowledge → Industry → Products → Policy, потоки RNG с `FIX_*`, `START_PERT`/`START_SYM`, ячейка факторного плана, переключатели §6.2, фаза инициализации A8, колонки `run.*`), адаптеры входов (`KnowledgeInputsAdapter`, `IndustryInputsAdapter`, `ProductsInputsAdapter`; только вызовы Core), `SliceSession` (`Submit` с причиной отказа, `Step`, применение команд на границе тика в порядке ключа, `commands.log`, `Replay`, хеш только на непустом тике).
- `Core.Policy`: `PolicySystem` (профиль P1–P3 из потока `AI`, доли R/cap/D/proc/QP по таблице §4, `pol.budget` с ростом по A12, годовой шаг с `t mod 12 = 0, t > 0`, счётчик `pol.round`), `BudgetMath`, `PolicyRules` (`prio`, ячейка `9u+3g+s`), `PolicyRng` (ключи `AI`, `START`). `Core.Commands`: типизированные команды (`set_budget_shares`, `set_effort_focus`, `compose_design`, `derive_design`, `start_series`, `close_line`), `CommandKey`, `CommandValidator` (форма и право при `Submit`, знание процесса при применении), `CommandState` (колонки `cmd.*`, слоты со `stamp`), `CommandCodec` (JSON Lines, Fx6 — строкой). `Core.Events.EventRng` (ключи `EVT_DIS`, `EVT_LIC`; правил событий нет). `ScenarioData.Perturbed/Symmetrized`.
- Убраны переходные значения по умолчанию в `IIndustryInputs`/`IKnowledgeInputs` (перегрузка `ToolingInvest` без `rest_a` удалена); Lab `LineStand`/`C3Stand` и фейки тестов реализуют входы явно (поведение то же). `LineStand` и `C3Stand` помечены `[SingleLayerStand]`.
- Golden 60 мес. перенесён в `tests/Singularity.Session.Tests` (новый хеш `e495c5ab…`, причина «composition root T6a»); `ProductsLinkedRunTests` (V23) и `ProductsCbLinkedRunTests` удалены.
- `IndustrySystem._lastRest`: проверено по ADR-0013 п. 4 — только диагностика, compute/merge/post его не читают, на следующий тик не влияет; оставлено вне таблицы, комментарий добавлен.

**Критерии приёмки.** Все четыре выполнены: (1) §11 T6a — ключи потоков (golden-тесты `StreamKeyTests`), `FIX_ALL` seeds 1…5 (одна траектория), `FIX_A` (профили не зависят от seed, остальное зависит), доли = таблица §4 и сумма = 1, `START_PERT` ([0,8; 1,2], выведенные поля от возмущённых, известные узлы не меняются), `START_SYM`, ячейка `9u+3g+s` (FRA/SWE как в main), A8, переключатели §6.2 (каждый меняет ровно одну величину); (2) общие пункты: `dotnet test` зелёный, ADR-0003 R1–R3 зелёный (в т.ч. для Session), нет `CounterRng` в `Core.Industry`/`Core.Products` и в их адаптерах, L1 и L2 (threads 1/4/8, seeds 1…20, 48 мес.; плюс 60 мес. на 4 конфигурациях и 384 мес. L1); (3) мир собирается только через Session (архитектурный тест IL-сканом по конструкторам систем), стенды помечены; (4) журнал пишется и читается одним кодеком, отказ `Submit` даёт код и текст.

**Тесты (итог `dotnet test`):** Kernel 284 + 1 пропущен (как было), Core 779, Architecture 77, Session 72 (новая сборка). Core было 784: −5 удалённых тестов связанных прогонов (V23, CbLinked), перенесённых в Session.

**Отклонения от спеки/дизайна (выбор ближе к спеке).**
1. Правила T6b–T6d не реализованы, как требует задача. Четыре входа Industry возвращают константу с комментарием, какая задача её заменит: `ToolingInvest = 0` (план A39/A30 — T6b), `QualityProgramme = false` (T6b), `LicenceShare = 0`, `AlloyDeficit = 0` (события, T6d); `LicensedEngines` пуст, `KnownSince`/`AppKnownSince` = −1 (только контроли L_C/L_P). **Следствие:** без поддержки оснастки (A30) `M` изнашивается, и около 25–45-го месяца выпуск линий останавливается (профиль не допущен, сплав → 0); в 384-мес. прогоне серии стоят. Это ожидаемо по A30 («6–8 лет»), но 384-мес. прогон до T6b не показывает экономику; golden-60 упражняет путь выпуска только первые ~2 месяца серии (игрок запускает серию форсированно, `force` ему разрешён).
2. `ProductsParams.Validate` теперь допускает `q10_b = 0` (было `> 0`): иначе переключатель `b = 0` §6.2 нельзя выразить. Одно условие в Core.Products, тест в `PolicyAndConfigTests`.
3. Переключатель `E ≡ 0` реализован как `E_full = 0` (все `E_useful` = 0, вся статья D остаётся в `rest_a`); проверен тестом (Σ E = 0, rest = D). Других способов выключить распределение без правки Industry нет.
4. `START_PERT`: пары (актор, поле) нумеруются по актору и по имени поля (порядок ordinal) среди `estimated` (провенанс на весь сценарий или список актора); поля `known`/`mastered` (наборы узлов), `tooling_m`, `hcap` (выводятся: сдвиг от возмущённой культуры) своего множителя не получают; `fleet` — целые `⌊count·m⌋`. Спека v0.12 даёт «канонический порядок записи сценария» — взят порядок полей по имени, как в `ScenarioData.Estimated`. `START_SYM`: выведенные поля — от средней культуры со сдвигами USA (правило R-M без особой добавки SUN); `trophy_engineers` и `gdp_year` усредняются как числовые.
5. `run.*` хранит эффективные seed потоков (`run.seed.know/events/ai/start`), а не seed прогона: под `FIX_ALL` все seeds дают один хеш (требование «одна траектория»). Отпечаток параметров (SLICE_COMPOSITION §3) не записан: параметры задаёт манифест (T10/T12), в T6a их нет в колонках; в `run.*` — флаги `FIX`, старт, ячейка, переключатели, хеш сценария (после возмущения).
6. `SetLineRate` не введён (у игрока — [Предл.], у ИИ — колонки `pol.*`, T6c); `accept_licence` — T6d. Точка расширения: `ProductsInputsAdapter` (слияние `cmd.*` и `pol.*` по ключу).
7. Версия схемы мира `SchemaVersion = 1` и отпечаток колонок закреплены тестом (SLICE_COMPOSITION §8). `StateHash` по-прежнему хеширует тик (шаг ADR-0013 «Последствия» — отдельная задача Kernel, не сделан).
8. Повтор `local_n` на другом тике — `LocalNotMonotonic`; `DuplicateKey` — только тот же полный ключ (тот же `apply_tick`).

**Открыто.** (а) Что делать с экономикой 384 мес. до T6b — ничего не нужно, если T6b следующая; (б) `E_ref`/`SkilledPerEngineer` вынесены в `PolicyParams` как [Предп.] §1.1 (не калибруются); (в) архитектурное правило «нет изменяемых полей систем кроме `[Scratch]`» (ADR-0013 п. 4) в коде не реализовано (атрибута `[Scratch]` нет) — не в этой задаче.
