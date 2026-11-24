---
id: TASK-0134
title: "Hcap growth and production mastery in Core: series gate, candidate close, foresight, mastery programme (#438-#440)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M05
agent: developer
category: core
priority: high
needs_owner: false
depends_on: [TASK-0133, TASK-0129]
related: [TASK-0128, TASK-0133]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "#438, #439, #440 (mechanic approved by owner; gate docs/reviews/production-mastery-*.md)"
created: 2026-11-24
---

# TASK-0134 — Рост потолка кадров и освоение производства в Core (#438–#440)

## Цель
Зонд G5/G6 (TASK-0133) показал: процессы уровня 5 никто не осваивает, ИИ годами держит линии на неосваиваемых проектах. Владелец решил: потолок кадров растёт (#438), ИИ не запускает неосваиваемую серию и есть механика освоения (#439), платная программа освоения — сразу (#440), для игрока — «обучение персонала» (за границей при наличии донора или свой опытный участок).

## Описание
Спецификации: `docs/simulation/notes/hcap-growth.md` (правило `Hcap(t)`, тесты), `docs/game-design/notes/production-mastery.md` ред. 3 (ворота старта серии, закрытие линии-кандидата, статическое предвидение с гистерезисом, программа освоения, команды `StartMasteryProgramme`/`CancelMasteryProgramme`). Работа идёт на ветке поезда 2 поверх TASK-0133 (данные G5/G6 и старение парка). Одно повышение `SchemaVersion` в поезде (колонка `prog_state`).

Порядок: (1) simulation-engineer — пилот на 1 seed: почему процессы 43/73 стоят при ненулевом `GrowthNominal` (диагноз «Б»); (2) developer — `Hcap(t)`, ворота и закрытие, предвидение; (3) developer — программа и команды; (4) simulation-engineer — пре-регистрация и зонд по веткам B0–B3, пороги O-038 и записок.

## Критерии приёмки
- [ ] Тесты, падающие без правки: нет старта серии при `y_min < y_start`; нет линии-кандидата ниже `y_prod` дольше `T_Ab`; `Hcap` до 1977-12 равен сценарию побитно; программа выводит процесс к «готов к серии».
- [ ] 1945–1977 без изменений (проекция хешей на месяц 387).
- [ ] L1/L2 на 1020 мес.; без аллокаций на тике; без float.
- [ ] Зонд: таблица по порогам, `VALIDITY: OK`; страница для владельца.
- [ ] Ревьюер APPROVE; второе ревью по #432.

## Связанные документы
`docs/simulation/g5-g6-probe.md` (ветка TASK-0133), `docs/reviews/production-mastery-*.md`, `docs/decisions/DECISIONS_LOG.md` #438–#440.

## Ход работы
- 2026-11-24, developer, ветка `worktree-agent-a94cedd4bf9d686b2` от `main` + слияние ветки поезда 2 (`worktree-agent-aee00c98dea970eae`, без конфликтов).
- Шаг (2): `Hcap(t)` (производная величина, `IndustryParams.HcapGrowth`, адаптер Session), ворота старта серии, закрытие кандидата, статическое предвидение с гистерезисом в `Decide` и `RequirementVariants`.
- Шаг (3): программа освоения (состояние `prd.prog.p*`, `SchemaVersion` 13 → 14), команды `StartMasteryProgramme`/`CancelMasteryProgramme` с кодами `why_not`, приоритет и содержание в распределении `D`, ИИ запускает программы тем же чекером.
- Параметры из `SliceRunConfig` (колонки `run.k.*` только при явном значении): `HcapGrowthPerYear`, `HcapGrowthMax`, `MasteryXProg`, `MasteryUpkeepMult`, `MasteryRules` (флаги `Gate|CandidateClose|Foresight|Programme` для веток B0–B3). Поверхность сессии 17 → 18.
- Умолчания [Предп.], до зонда шага (4): `k` = 0,10/год, `ΔHmax` = 1,0 (пилотная точка hcap-growth.md §6), `upkeep_mult` = 1,5, `x_prog` = 1 (O-045), `T_look` = `T_max` = 144, `S_max` = 3.

## Разногласия
1. **«Готов к серии» и ворота измеряются по культурному выходу (без рампы опыта), не по `yield`.** Опыт `X` растёт только от отчётов о выпуске, а рампа без выпуска держит процесс на λ0 = 0,3 < `y_start` = 0,5 (`IndustryMath.Ramp`). По спецификации буквально (`y_min` = `yield`) ни программа, ни Prototype не могли бы вывести процесс G5 к воротам: тупик. Введено `ProcessYield.CultureYield` = Π g_d (рампа исключена); ворота старта и конец программы читают его. Закрытие кандидата (б) читает обычный `yield` (линия уже выпускает). Нужно решение game-designer/Orchestrator, если нужен иной смысл.
2. Предвидение проверяет только `Hcap(t + T_look) > r_d` по активным измерениям; **достижимость оснастки (`M ≥ r_d`) не проверяется** (в записке «оснастка достижима по плану A39» без формулы). В отчёте как не сделанное.
3. Порядок очереди программ: по месяцу старта, затем по номеру процесса (не «больший зазор `r_d − H` первым»); ИИ запускает не больше одной программы в месяц, процессы по возрастанию номера.
4. Старт программы с `SaturationWait` принимается командой игрока (очередь без расходов), ИИ его не запускает. Программа без пользы (`AlreadyReady`, `CellLedBy`, `NotLabCeiling`) отклоняется.
5. `MasteryProgrammeView` (карточка, вытесненный фокус) и расчёт ETA «до/после через 6 и 12 мес.» не сделаны: в критериях задачи их нет, `why_not` отдаётся кодом `CommandVerdict.Mastery`.
6. Lab не менялся (зона simulation-engineer). Требуемые правки Lab — в отчёте оркестратору.

## Результат
**Что сделано.** `Hcap(t)` = сценарий + `min(ΔHmax, k·max(0, m − 388)/12)` (целочисленно, вниз; `m_on` = 1978-01 = месяц 388 для сценария 1945-09); побитно равен сценарию до месяца 387. Правила мастерства действуют с месяца 388 и только для процессов tier ≥ 5: ворота старта серии (`CultureYield` ≥ `y_prod` по всем процессам `prc_set`), закрытие линии-кандидата после `T_Ab` ниже `y_prod` (колонка `pol.below.d`, причина `LineCloseReason.CandidateBelow`), предвидение `Hcap(t + 144) > r_d` в `Decide` и `RequirementVariants`, удержание проекта в Prototype, пока предвидение держит; программа освоения (вход `V_prog` в `GrowthNominal`/`Planned` как max с номиналом проекта, приоритет и `(upkeep_mult − 1)·E` в `EffortAllocator`, режимы «за границей» через существующий `catchUp` и «дома» без него).

**Файлы (основные).** Core: `Products/MasteryParams.cs`, `Industry/ProgrammeAssessment.cs`, `Industry/CbCultureRule.cs` (`Assess`), `EffortAllocator.cs`, `IndustryParams.cs`, `Products/ProductsSystem.cs`, `Policy/PolicySystem.Products.cs`, `Commands/*`. Session: `Slice/MasteryChecks.cs`, адаптеры, `SliceRunConfig`, `SliceWorld`, `SliceSession`. Тесты: `MasteryCoreTests`, `MasteryRulesTests`, `MasteryCommandTests`, `MasteryIdentityTests`, `MasteryRunSupport`, фикстура `mastery-baseline.txt`.

**Проверки.** Тождество 1945–1977: хеш всех колонок, кроме новых, на месяце 387 (сиды 1–3, текущие данные) равен записанному на сборке поезда 2 до правки (`mastery-baseline.txt`), на данных `core-g4` — тоже (`Train2IdentityTests`). `verify.py` в копии с двумя правками Lab (ниже): все шаги, кроме 1 теста Lab и 1 selftest-строки (`CandidateBelow` в `SliceObserver`).

**Эталоны, перезаписанные один раз в поезде 2 («Re-recorded in TASK-0134 (train 2: Hcap(t), mastery)»).** Причина общая: схема v14, новые колонки (нули до 1978-01) меняют хеш с месяца 0; старые колонки до месяца 387 не меняются (проекционные тесты). Файлы тестов: `CalibrationParamsTests`, `SeriesSwitchTests`, `ViewTests` (MainSeed1Month149), `F17bTests` (10 эталонов), `LinkedRunTests` (Golden60), `ProductAiSessionTests` (Golden150), `TwoWorldsOnOnePoolTests` (6 дайджестов), `SchemaTests` (версия и отпечаток), `Train2IdentityTests` (строки месяца 1019 меняются из-за `Hcap(t)` и правил), `session-surface.v18.txt`.
