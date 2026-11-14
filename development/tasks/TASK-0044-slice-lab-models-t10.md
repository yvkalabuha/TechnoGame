---
id: TASK-0044
title: "T10: Lab-модели JetSlice и JetSliceCalib, CSV v0.24, манифесты, печати A39/A40"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: simulation-engineer
category: lab
priority: high
needs_owner: false
depends_on: [TASK-0040, TASK-0042]
related: [HYP-0002, EXP-0002]
approved_by: orchestrator
approved_at: 2026-11-13
approval_ref: "Спека среза v0.24 §11, строка T10; DECISIONS_LOG #182"
created: 2026-11-13
---

# TASK-0044 — T10: Lab-модели среза

## Цель
Срез запускается из Lab сериями по манифесту. Выходные CSV пригодны для анализа T11 (`lab/analysis/exp0002.py`) и калибровки T12.

## Описание
Источник: спека `docs/simulation/slice-jet-aviation.md` v0.24, §11 строка T10 (grep «T10»: 9 мест — печати A39, A40 и §6.x); §8.2 п. 2 (калибровочная сборка без колонок лидерства); §9 (детерминизм, хеши, манифест); `docs/simulation/slice-csv-format.md` v0.2. Формат v0.2 писался по спеке v0.6 и устарел.
Сборка мира и прогон уже есть в `Singularity.Session` (T6a–T6d; `tests/Singularity.Session.Tests` — `LinkedRunTests`, `ProductAiSessionTests`). Lab: `src/Singularity.Lab/` (`ModelRegistry`, `MatrixRunner`, `RunExecutor`, `Manifest`, `Csv`; ADR-0008).

**Этап 1 — формат.** `slice-csv-format.md` → v0.3 под спеку v0.24:
- таблицы и колонки печатей §6.x, леджера A39, `cancel_design`, срабатываний A40 О/С с классом причины;
- `tool_unmet`, `why_not`;
- C1 по двум постановкам — если печать идёт из Lab;
- запрещённый список для `JetSliceCalib`.
Этап 1 сверяется с `exp0002.py`. **Чтение новых таблиц и колонок v0.5 в `exp0002.py` (в том числе `requirements.csv`) — объём T11, не этой задачи** и коротко отражается в «Результат».

**Этап 2 — код.**
- `JetSlice` и `JetSliceCalib` в `ModelRegistry` поверх Session, без дублирования логики Session.
- Параметры из `ParamSet` манифеста: seeds, `T6D_OFF`, `LIC_OFF`, `FIX_*`, `START_PERT`, `START_SYM`, `TOOLING_TARGET`, `POOL_ORDER` — какие есть в Session.
- Манифест калибровки и шаблон манифеста EXP-0002 — в `lab/manifests/`. Манифест EXP-0002 не замораживается: это делает T12.

**Тесты** (в `tests/`):
- заголовок CSV `JetSliceCalib` не содержит запрещённых колонок и файлов;
- L1: два прогона дают одинаковые CSV и хеш;
- L2 на малой матрице;
- прогон через `MatrixRunner` даёт все таблицы у всех прогонов.

**Печать** на seeds 1…20 (как TASK-0026, информационно): ветки О и С по акторам и классам — для сверки с ожиданиями A40.

## Критерии приёмки
- [ ] `slice-csv-format.md` v0.3 согласован со спекой v0.24.
- [ ] Обе модели зарегистрированы; `dotnet run --project src/Singularity.Lab -- matrix --manifest <manifest> --out DIR` (CLI по `docs/simulation/lab-cli.md`) выполняет малую матрицу.
- [ ] Тесты: заголовок калибровочной сборки, L1, L2, полнота таблиц; `dotnet test` всего решения зелёный.
- [ ] Печать A40 на seeds 1…20 и её сверка с ожиданиями — в «Результат».
- [ ] `src/Singularity.{Kernel,Core,Session}` не меняются; если нужно изменить — остановиться и описать.

## REVIEW
- [ ] Code review (reviewer)
- [ ] Второе ревью (codex) — до чистого accept (#181)
- [ ] Тесты (test-engineer)

## Результат
### simulation-engineer, 2026-11-13 — этап 1 сделан, этап 2 остановлен (нужна правка Session)
**Этап 1.** `docs/simulation/slice-csv-format.md` переведён на v0.3 под спеку v0.24. В нём: серии и параметры манифеста (`ParamSet` → `SliceRunConfig`, §3). Новые таблицы `JetSlice`:
- `a40.csv` — ветки О и С с классом: `PROCESSES`/`DEFECTS` и `CULTURE`/`COEXIST_MONEY`/`OTHER`; `cancel_design` = строки `branch = O`;
- `a40_c_months.csv`;
- `tooling_ledger.csv` — леджер A39 и `tool_unmet` по кластерам;
- `lines.csv` (F17b), `fleet_cmd.csv`, `licences.csv`.

`whynot.csv` получила колонки `cluster`/`order`/`unmet`. Запрещённый список `JetSliceCalib` дополнен: параметр `TOOLING_M_LEAD` содержит подстроку `lead`, поэтому в калибровочный манифест его не передают. Стенд C1 в двух постановках — `c1_stand.csv` (§6). Сверка с `exp0002.py` (§10): конфликтов колонок нет; новые таблицы и серии добавит T11.

**Этап 2 — остановлен.** [Факт: код] Lab не может прочитать мир:
- `SliceWorld` и адаптеры — `internal`, `InternalsVisibleTo` есть только у `Session.Tests`;
- `SliceSession` публично отдаёт только хеш, тик, дату, `why_not` и `CurrentRequirement`.

ADR-0014 п.5 и `SLICE_COMPOSITION.md` (строка 38) требуют read views и прямо говорят сделать их «вместе с первым потребителем (T10)». Это правка `src/Singularity.Session`, которую эта задача запрещает. Нужный набор описан в формате, §9: `ActorView`, `NodeView`, `DesignView`, `PolicyDecisionView`, `ToolingLedgerView`, `CapabilityView`. Это обёртки над уже публичными методами Core, новой логики в них нет. Модели не зарегистрированы, код Lab и тесты не писались: без чтения мира они были бы заглушками.

**Манифесты (черновики, не заморожены):**
- `lab/manifests/EXP-0002.json` — 53 точки, 12 880 прогонов, `hash` проходит;
- `lab/manifests/EXP-0002-calib.json` — seeds 10001…10100.

Модели в них ещё не зарегистрированы, поэтому команда `run` сейчас падает с «Unknown model».

**Печать A40, seeds 1…20** (информационно, как TASK-0026). Прогон через `lab/runners/t6e/run_t6e.sh` (T6eHarness, архив HEAD 58d23a4, плечо V5, main, 384 мес.; golden pass), разбор — `lab/analysis/t10_a40_print.py`.

| Актор | С: seeds с ≥ 1 срабатыванием | О `PROCESSES`, всего (на seed) | О `DEFECTS` | Ожидание A40 (б) v0.16: С; О |
|---|---|---|---|---|
| USA | 0 | 5 (0,25) — **ниже полосы** | 0 | ≤ 0,16; 0,5–2,0 |
| GBR | 0 | 0 | 0 | ≤ 0,16; ≤ 0,15 |
| SUN | 0 | 4 (0,20) — на нижней границе | 0 | ≤ 0,16; 0,2–1,0 |
| FRA | 0 | 0 — **ниже полосы** | 0 | ≤ 0,16; 0,05–0,6 |
| SWE | 0 | 0 | 0 | ≤ 0,16; ≤ 0,15 |

- `n_O` = 0,45 на seed при ожидании 2,0 (полоса 1,0–3,0) — ниже полосы.
- `n_C` = 0 при ожидании ≤ 0,2.
- L-O0 (`DEFECTS` = 0) и L-C0 (срабатываний С нет) выполнены. Наибольший `idle_c` линии — 1 мес.
- Все 9 срабатываний О приходятся на мес. 323–381: проекты G3 USA (P1 ×4, P3 ×1) и G1/G2 SUN (P1, P3).
- Охрана v0.21 не сработала ни разу.

Как читать: полосы v0.16 объявлены для сборки T6c2 (`LINE_CLOSE = ADMIT`, без T6d, F17b и V5). Спека (A40 v0.20) требует объявить их заново до первого прогона main. Поэтому расхождение — материал для переобъявления, а не дефект. [Предп.] Ветка О стала реже, потому что V5 (A39) и F17b быстрее доводят процессы до допуска. FRA не доходит до проекта в стадии О ни в одном seed.

### simulation-engineer, 2026-11-14 — ревью Codex r3 (base 2d3bee9, head 2459bdc): обе находки приняты
1. **`a40.force` всегда 0 (medium) — принято.** [Факт: код] `ForceProxy` шлёт `StartSeries(..., Force: true)`, Core ставит `series_forced`, а `SliceObserver` печатал `"0"`. Флага нет во view (`DesignView`), Session не трогаем. Исправление: `JetSliceModel` после `force.Issue(t)` (если `Sent` вырос) читает `SliceSession.CommandLog` и сообщает наблюдателю `ForcedStart(actor, design, t)`; `force = 1`, если `t_launch = f + 1` (команда принята). Отказ и поздний запуск ИИ дают 0. Правило — в формате §9 (`a40.force`). [Факт: прогон] FORCE=X1, seeds 1–3, 384 мес. — по одной строке `C 1 CULTURE`; FORCE=OFF seed 1 — только `force = 0`.
2. **Кэш `SliceData` по каталогу (medium) — принято.** [Факт: код] `SliceChecks.DataProvenance` заполнял кэш, менял `nodes.yaml`, и прогон брал старые данные при новом `data_hash`. Исправление:
   - кэш хранит SHA-256 каждого прочитанного файла и используется, только пока байты те же; иначе загрузка заново;
   - загрузка проверяется повторной загрузкой: `DataHash` и хеши файлов совпадают;
   - `SessionModelSetup.RecordedDataFiles` (`RunExecutor` передаёт `provenance.DataFiles`): `SliceData.Load` сверяет SHA-256 загруженных файлов с записанными; при расхождении прогон `failed`. Пустой список (selftest) не проверяется.
- Тесты (`SliceChecks` + `SliceModelTests`):
  - `Changed_mod_file_is_reloaded_and_stale_data_files_fail_the_run` — правка веса в `nodes.yaml` меняет `GameData.DataHash` через `Load`; прогон со старым provenance — `failed`; со свежим — `run.json.data_hash` и `model_info.data_hash` новые.
  - `A40_force_marks_FORCE_series_and_never_the_AI` — FORCE=X1 seed 1 даёт строку С с `force = 1`; при OFF все `force = 0`.
- `dotnet test Singularity.sln` зелёный: Architecture 93, Kernel 315 (+1 skip), Core 1029, Lab 11, Session 308 (+2 skip). Golden не менялись (`Lab_path_gives_Session_golden_seed1_month149` проходит). [Факт: прогон шёл на рабочем дереве с незакоммиченными правками TASK-0049 в Core и тестах.]

### simulation-engineer, 2026-11-14 — ревью Codex r4 (head 829867c)
1. **`COEXIST_MONEY` только по `Bind = Money` (high) — принято.** [Факт: спец. A40 «v0.20: две линии» — «`N` с выпуском 0 … при `left` после `W` = 0 и без `T_a`»] `Bind = Money` выполняется и при `left > 0`, которого не хватает на единицу, и при `T_a > 0` у линии. Такие срабатывания уходили из L-C0.
   - Исправление: `SliceObserver.CoexistMoney` требует в каждом месяце `TwoLines`, `N`, `rate = 0`, `LeftIn = 0`, `Training(d) = 0` (`LineMoneyView`, `T_a` у линии) и `Bind = Money`.
   - `Bind = Money` оставлено, чтобы недопущенная линия шла в `OTHER`. Это строже буквы спец.: L-C0 только строже.
   - `T_a` взят у линии, а не из плана актора: план может иметь `T_a > 0`, который ушёл на другую линию или не потрачен (спец. A41: учебные деньги — только линии цели).
   - В `a40_c_months` добавлена колонка `training`, чтобы анализ проверял класс.
   - Формат v0.5 r4: §4.10, §4.16 (б).
   - Тест: `Coexist_money_needs_zero_left_and_no_training_in_every_month`. Плохой месяц — последний из трёх; проверены `left > 0`, `training > 0`, нет строки шага 7, `Bind = None`, роль `W`, одна линия.
2. **Истечение лицензии не видно без доли (medium) — находка верна, полное исправление требует правки Session (остановлено).** [Факт: код] `LicenceActive(b) = lic_gen > 0 ∧ g_b ≤ lic_gen`, `g_b` — `AltEngGen` базового слота проектов в С/В (`SliceWorld.EngineGen`).
   - Альтернатив проекта во view нет. `DesignView.Gen = min(g_p, eng + 1)` даёт только нижнюю границу `eng`.
   - Восстанавливать альтернативы по командам ИИ значит повторить логику Core в Lab (ADR-0014 п. 5–6).
   - Сделано в Lab: счётчик `model_info.lic_end_unobservable` — лицензии без единой доли: открытые на конце и заменённые, у которых `t_end` — только верхняя граница. Пустой `t_end` в формате теперь значит «не наблюдается».
   - Формат §9.1, §11 п. 7 (обновлён).
   - Тест: `Licence_that_never_gave_a_share_is_counted_unobservable`.
   - [Предл.] Session v3: `LicenceView.Active` = `LicenceActive(a)` снимка, `test_api_version` 3. Решает systems-architect, делает developer.
- `dotnet test Singularity.sln` зелёный: Architecture 93, Kernel 315 (+1 skip), Core 1029, Lab 13, Session 308 (+2 skip). Golden не изменились. [Факт: рабочее дерево с незакоммиченными правками TASK-0049 в Core и тестах]
