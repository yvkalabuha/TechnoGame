---
id: TASK-0023
title: "T6b: ИИ способности — план оснастки A39, учебная серия, программа качества"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0022]
related: [HYP-0002]
approved_by: orchestrator
approved_at: 2026-11-03
approval_ref: "DECISIONS_LOG #125; спека v0.12 §11 T6b; контракт v2.4 r3"
created: 2026-11-03
---

# TASK-0023 — T6b: ИИ способности — план оснастки A39, учебная серия, программа качества

## Цель
Правила ИИ и системы для слоя способности среза (T6b спеки v0.12): план оснастки A39 с поддержкой A30 (закрыть DEBT-0015), деньги учебной серии через `TrainingBudget` (F15, #125), программа качества. Снять DEBT-0018: длинные прогоны должны показывать экономику.

## Описание
- `ToolingPlan(a, t, rest_a)` по A39: один пул денег (сначала `rest_a`, затем `B_cap`), проход 1 — поддержка A30, проход 2 — узкие места `M`; порядок site → MET, CHEM, MECH, AERO → precision, purity, qc; водопад в raw `Fx6`; потолок ячейки `(M_cap − M_dec)·cost_M`; невзятое из `rest_a` возвращается (#118); остаток `T_a` — в учебную серию. Подставить вместо константы `ToolingInvest=0` из T6a.
- `TrainingBudget(actor, design, t)` в `IProductsInputs` и шаг 7 A38 по контракту v2.4 r3 §4.4d (`from_left`, `tb_spent`, колонка Σ `tb_spent`); при `tb ≡ 0` поведение прежнее.
- Программа качества: состояние, `reserve_workforce`, `QualityProgramme`, триггер P2 (A41; 0,05 из закупок на 84 мес., F13). Триггеры P1/P3 по `why_not` — не здесь (T6d).
- После правил: перезаписать golden-60 и L1 384 мес. с работающей оснасткой, повторить L2 (DEBT-0018); причина в сообщении коммита.

## Критерии приёмки
- [ ] Всё из спеки v0.12 §11 «T6b» (инварианты A39, регрессия DEBT-0015 на фикстуре «2 site», план одинаков во всех вызовах месяца, `M ≤ M_cap`, A30 держит `p*` 120 мес., баланс `Σ взято + неизрасходованное = rest_a + B_cap`, программа качества).
- [ ] Инварианты контракта v2.4 r3 §4.4d для `TrainingBudget`.
- [ ] Общие пункты §11 T6a–T6d (тесты, ADR-0003 R1–R3, нет `CounterRng` в Industry/Products, L1/L2).
- [ ] На 384 мес. main выпуск не падает в 0 из-за износа оснастки (печать по акторам в тесте или отчёте задачи).

## Тесты
По §11 T6b; фикстура «2 site»; golden и L1/L2 на сборке Session.

## Связанные документы
Спека `docs/simulation/slice-jet-aviation.md` v0.12 (§3.4, §3.5 A22, A30, A37–A39, A41, §4, §11); `docs/architecture/LAYER_CONTRACT.md` v2.4 r3 (§4.1b, §4.4d, §5); `docs/architecture/SLICE_COMPOSITION.md` r1; `docs/game-design/PROCESS_DEVELOPMENT.md`; DEBT-0015, DEBT-0018; TASK-0022 «Результат».

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

### Ревью reviewer (2026-11-03, вставлено Orchestrator)
Вердикт: approve. `dotnet test` зелёный (Kernel 284 +1 skip, Core 798, Architecture 77, Session 96). A39, одинаковость плана в месяце, `TrainingBudget` (§4.4d), программа качества (A41/F13), правка `PlannedVolume` + `proc_v` (A37), детерминизм, `SchemaVersion` 1→2 — соответствуют. Промежуточные коммиты 64597de, 2633d99 не собираются поодиночке (мешает только bisect).
- **medium** `ToolingPlan.cs:57-64` — ничья A30 по номеру процесса решается по номеру строки; порядок строк не гарантирован (`IndustryLayout.FromData` — порядок таблицы узлов, по номеру только `l.Order`); в main ничья реальна (USA/GBR: 48 и 49, MECH tier 4). Fix: `foreach (int r in l.Order)`, юнит-тест с непорядковыми строками.
- low `T6bAiCapabilityTests.cs:171,176` — тавтология и слабая проверка `tb_spent`. Fix: `tb_spent ≤ TrainingBudget` того же месяца на собранном мире или оставить юнит-тесты шага 7.
- low `PolicyInputsAdapter.cs:27` — строка актора вместо строки site в `Limiter` (верно только при A27 site = актор). Fix: site явно или комментарий.
- low `ToolingPlan.cs:~66,~133` — план пересчитывается в 12 вызовах на site; оставить, кэш при нужде.
- info `SLICE_COMPOSITION.md` §2, §4 устарели (`PlannedVolume` и `proc_v`, имя `ToolingRule`) — systems-architect.
- OPEN: SUN не производит — пробел спеки (A23, A37, A39, C-B `eligible`: плавка вне `prc_set` с `yield` 0 — замкнутый тупик), не ошибка кода; разбор у simulation-engineer.

## Результат
**Сделано** (developer, 2026-11-03; коммиты на main, не запушено).
- `Core.Policy.ToolingPlan` — план оснастки A39 как чистая функция `Compute(industry, inputs, m_keep, actor, t, rest_a, B_cap)`: пул `rest_a` → `B_cap`, проход 1 (поддержка A30 для `p*`), проход 2 (узкие места `M`), водопад site → кластер (порядок раскладки) → измерение, потолок ячейки `(M_cap − M_dec)·cost_M`, raw `Fx6`, без деления. Результат: `Invest(site,c,d)`, `TakenRest`, `TakenCap`, `RestReturned`, `TrainingBudget = B_cap − taken_cap`. Без кэша: каждый вызов месяца пересчитывает тот же план. `IndustrySystem` получил `Parameters` и `RestActor(actor, t)` (тот же `rest_a`, что идёт в `ToolingInvest`; общий `Allocation`).
- `IProductsInputs.TrainingBudget` (F15) и шаг 7 A38 (`(left + tb)/unit_cost`, `from_left`, `tb_spent`), колонка `prd.tbSpent` и `ProductsSystem.TrainingSpent`; при `tb ≡ 0` формула прежняя.
- `PolicySystem`: состояние программы качества (`pol.qp.active/start/count`), `reserve_workforce` через почтовый ящик Industry (`QP_REQ` = id над всеми КБ/линиями/бригадами; слой требований +1), триггер P2 (24 мес. подряд серия с `q < 0,7`, старт в годовой шаг, доли 0,20/0,18/0,12/0,45/0,05 на 84 мес., затем прежние), P1 стартует на t0; цель учебной серии `pol.train` (P1: новейшая серия, узкое место из `H`, limiter ∈ {VOLUME, LAB_CEILING}). `IPolicyInputs` + `PolicyInputsAdapter` в Session. Доли записываются каждый месяц (значения те же).
- Session: `IndustryInputsAdapter.ToolingInvest` = ячейка плана (`B_cap = share_cap·budget`), `QualityProgramme` = активна ∧ `granted(QP_REQ) ≥ qp_engineers` (`⌊0,02·⌊E_ref·b0⌋⌋`), `ProductsInputsAdapter.TrainingBudget`. `SchemaVersion` 1 → 2.
- **Исправлен дефект T6a:** `IndustryInputsAdapter.PlannedVolume` не добавлял `proc_v` снимка (спека A37: `v_exp = proc_v + проекты П/О`). Без него у серийной линии `v_exp = 0`: ни наладки, ни узкого места `M` не получали денег, и линия умирала независимо от плана оснастки.
- Тест-скрипт игрока: старая линия закрывается, только когда новая линия поколения уже выпускает (раньше закрывал работающую линию ради линии с нулевым выходом).

**Файлы.** src: `Core/Policy/{ToolingPlan,IPolicyInputs,PolicySystem,PolicyProfile}.cs`, `Core/Industry/IndustrySystem.cs`, `Core/Products/{IProductsInputs,ProductsSystem}.cs`, `Session/Slice/{SliceWorld,IndustryInputsAdapter,ProductsInputsAdapter,PolicyInputsAdapter}.cs`. tests: `Core.Tests/Policy/{ToolingPlanTests,QualityProgrammeTests}.cs`, `Core.Tests/Products/{TrainingBudgetTests,ProductsHarness,ProductsDeterminismAuditTests}.cs`, `Session.Tests/{T6bAiCapabilityTests,LinkedRunTests,SchemaTests,PlayerScript}.cs`, `Architecture.Tests/SessionArchitectureTests.cs`.

**Тесты (`dotnet test`, всё зелёное):** Kernel 284 + 1 пропущен, Core 798 (было 779: +8 ToolingPlan, +6 QualityProgramme, +5 TrainingBudget), Architecture 77, Session 96 (было 90: +6 T6b). Покрыто: инварианты A39 (баланс `Σ взято + неизрасходованное = rest_a + B_cap` на 300 случайных пулах, `taken_rest ≤ rest_a`, ячейка ≤ `room`, ячейка с `M_dec ≥ M_cap` = 0, водопад, поддержка A30 до узких мест), регрессия DEBT-0015 на фикстуре «2 site» (при `B_cap = 0` общий вклад = `rest_a`, а не `2·rest_a`), одинаковый план во всех вызовах месяца и `M ≤ M_cap` на 40 мес. при потоках 1 и 4 (одинаковый хеш), A30 держит `p*` допущенным 120 мес. (контроль без A30 теряет допуск на 60–120 мес.; и на собранном мире 120 мес. без игрока), `TrainingBudget` (в т.ч. `tb ≡ 0`, чужая линия, `tb < 0` — исключение), программа качества (P1 на t0, 84 мес., КБ раньше `QP_REQ` при дефиците, неполный `granted` виден, P2: 24 мес. подряд → годовой шаг → доли 0,45/0,05 → возврат через 84 мес., сброс счётчика), цель учебной серии. ADR-0003 R1–R3 зелёные, `CounterRng` нет в Industry/Products и адаптерах (в т.ч. `PolicyInputsAdapter`). L1 (golden-60, 384 мес. на потоках 1/8) и L2 (seeds 1…20 на 48 мес., 60 мес. на 4 конфигурациях) повторены и зелёные.

**Golden-60** перезаписан: `e495c5ab…` → `d9e11cf5…`. Причина: оснастка работает (план A39/A30), колонки программы/учебной серии (схема v2, лишний requester), `v_exp` с `proc_v`, правка скрипта игрока.

**384 месяца, seed 3, скрипт игрока: выпуск (шт.) по 48-мес. окнам.** USA(P2): 690 / 1440 / 1440 / 1440 / 1470 / 1440 / 1440 / 1440; GBR(P2): 276 / 561 / 672 / 751 / 792 / 1025 / 1128 / 1180; SUN, FRA, SWE: 0 во всех окнах. До T6b (после T6a) выпуск USA/GBR падал в 0 к 60-му месяцу. Выпуск USA/GBR не падает; в тесте это утверждается (окна 2…8 > 0).

**Отклонения и находки.**
1. P3 (учебная серия по `eta` `why_not`) и триггеры программы P1/P3 по `why_not` — не реализованы (T6d, `why_not` нет, T7 параллельно). Следствие: у P1 программа качества живёт 84 мес. от t0 и больше не перезапускается; у P3 программы и учебной серии нет. Учебная серия работает только у P1.
2. Деньги учебной серии на практике не тратятся: темп линии задаёт игрок (30 шт./мес.) и общих денег хватает; `LineRate` с `(B_proc + B_cap)/floor` — T6c. Путь `TrainingBudget` проверен юнит-тестами шага 7 и тем, что цель выбирается (P1, cell 0, 384 мес.); трат `tb_spent` в собранном мире пока 0.
3. SUN, FRA, SWE не производят и без T6b: у FRA/SWE нет проекта (знаний мало), у SUN серия есть, но плавка 39 (`yield` 0, узкое место `Purity`/`M`) никогда не допускается, поэтому сплава нет. Плавки не входят в `prc_set` проекта, `v_exp` = 0, а A39 проход 2 берёт только процессы с `v_exp > 0` — оснастка плавки не поддерживается. **[Предл.]** simulation-engineer/game-designer: нужно ли правилу оснастки покрывать плавки сплава (спека A39/A30); код следует тексту спеки.
4. `qp_engineers` считается от `engineer_base` на старте (`⌊E_ref·b0⌋`), не от текущего бюджета (спека: «`engineer_base_a`», USA 400 — сходится).
5. Доли пишутся каждый месяц (идемпотентно), а не только в годовой шаг: старт/конец программы может не совпасть с годовым шагом (P1 t0 → конец на t = 83).
6. Ключи программы читаются как `granted` из снимка (`Industry.Granted(site, 0, QP_REQ)`): эффект с месяца 1 после старта.
7. `PolicySystem` принимает `IPolicyInputs`, почтовый ящик и `qpRequesterId` (конструктор изменён).
8. Коммиты промежуточные не все собираются поодиночке (контракт `IProductsInputs` требует адаптера Session): сборка и тесты зелёные на последнем коммите.

### Исправления по ревью (developer, 2026-11-03; поверх main с T7)
1. medium: ничья A30 в `ToolingPlan` решается обходом `l.Order` (по номеру процесса), а не по строке; тест `A30_TieOnTier_GoesToTheLowerProcessNumber_NotTheLowerRow` (строки: процесс 7, затем 6).
2. low: слабая проверка заменена: до шага месяца считается Σ `TrainingBudget` по линиям, после шага `TrainingSpent ≤` этой суммы (собранный мир, cell 0, 384 мес.); для не-цели `tb = 0`, для цели равен `TrainingBudget` плана.
3. low: `PolicyInputsAdapter` передаёт в `Limiter` явную строку site (`= actorRow`, комментарий про допущение A27).
4. T7: `WhyNotInputsAdapter.ToolingRate` = чистый рост `M` за месяц при плане A39 (`invest/cost_M(k) − δ_M·M`, не меньше 0). В `IWhyNotInputs` добавлен `TrainingBudget(actor, design, month)`; звено ECONOMY сравнивает стоимость с `ProcurementBudget + TrainingBudget` линии (шаг 7 платит из `left + tb`); бюджет закупок — общий `SliceWorld.ProcurementBudget`. `SliceWorld.ProductsInputs` вынесен в свойство. Тест `WhyNotToolingRate_...` (рост положителен в части ячеек за 60 мес.). Хеши и golden не менялись.
Тесты: Core 799, Architecture 80, Session 119, Kernel 284 (+1 пропуск) — зелёные.
