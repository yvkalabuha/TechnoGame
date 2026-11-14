# Формат CSV и манифестов EXP-0002 (срез «реактивная авиация»; `lab/analysis/exp0002.py`)

> Статус: черновик **v0.5 r5** (simulation-engineer, 2026-11-14, TASK-0044 этап 2; r5 — `eng_short_months` по одному КБ (ревью Codex r5); r4 — ревью Codex r4: `COEXIST_MONEY` по букве спец., `a40_c_months.training`, `lic_end_unobservable`; r1 — ось `k_diff` ключа `GRID`, решение #190; r2 — пара compose месяца 0, TASK-0047; r3 — `COEXIST_MONEY` по `Bind`, линии без строки следа денег, определения Lab) под спецификацию `slice-jet-aviation.md` **v0.25** (дальше «спец. §N») и поверхность read views `docs/architecture/SLICE_COMPOSITION.md` §12–§13 (r4, дальше «SC §12.N», «SC §13.N»; `TestApiVersion = 3`; r5 — `LIC_END` по `LicenceView.Active`). v0.4, v0.3 и v0.2 — в истории git.
> Соглашения унаследованы от `exp0001-csv-format.md` и Lab (ADR-0008, `src/Singularity.Lab/RunExecutor.cs`, `MatrixRunner.cs`).
> Модели `JetSlice` и `JetSliceCalib` — `src/Singularity.Lab/Models/Slice/` (T10 этап 2): читают только `SliceSession` и его views по имени свойств. Проверки — `Singularity.Lab selftest` (`SliceChecks`), малая матрица `lab/manifests/EXP-0002-t10-smoke.json`, серия `det` (`EXP-0002-det.json`).

## Изменения v0.5 r4 (ревью Codex r4, TASK-0044)
- **`COEXIST_MONEY`** (§4.16 (б)) — в каждом месяце `left = 0`, `training = 0` и `Bind = Money` (буква спец. A40 и признак допуска); одно `Bind = Money` r3 пускало `left > 0` и `T_a > 0`.
- `a40_c_months` + колонка `training` (§4.10).
- `model_info.lic_end_unobservable` (§9.1) — снят в r5 (см. ниже).

## Изменения v0.5 r5 (TASK-0044)
- Session `test_api_version` = 3: `LicenceView.Active` = `SliceWorld.LicenceActive(a)` (read view, не состояние; golden и хеши не меняются). `LIC_END` = переход `Active` 1 → 0 (§11 п. 7); счётчики `lic_end_lapses_inferred` и `lic_end_unobservable` удалены, `LapseTracker` удалён; пустой `t_end` снова значит «активна до горизонта».

## Изменения v0.5 r3 (TASK-0044 этап 2)
- **`COEXIST_MONEY`** (§4.16 (б)) — по предложению SC §13.3 r6: в каждом из `T_ab` месяцев у актора две линии, d — `N`, выпуск `rate(d) = 0` и `DesignView.Bind = Money` (шаг 7 ограничил линию деньгами). Прежнее условие `LeftIn = 0 ∧ Training = 0` не отличало «нет денег» от «нет допуска» (у недопущенной линии `Bind = None`).
- **Линии месяца** (§4.12, §4.16): в С по снимку t, кроме линий, которые ИИ закрыл в compute t−1. Линия снимка без строки в `Money.Lines` (её закрыла команда до шага 7: прокси `FORCE`, `derive`, переносящий родителя) **не** валит прогон: роль сохраняется (её видел ИИ), `left` и `left_share` пусты, счётчик `lines_without_step7_money` — в `run.json` (`model_info`). [Факт: малая матрица T10] Без этого `arm/FORCE_*` и `arm/DERIV` падали; в main таких линий 0. Если исключать такие линии из пары, `YPROD` в `arm/DERIV` попадает на `SOLE` (seed 1, GBR, мес. 337) — роль должна быть та, что видел ИИ.
- **Определения Lab** (§4.4, §4.5, §4.7, §4.9, §4.13, §5; [Предп.], не из спец. дословно) — §9.1. `run.json` модели содержит объект `model_info` (§9.1).
- `a40.preset` — пресет профиля актора (у проекта во view пресета нет, §4.9); `LIC_END` по истечению: новая покупка сбрасывает учёт доли (иначе ложный `LIC_END` замещённой лицензии); срабатывание С без `T_ab` записанных месяцев — прогон `failed`.
- `exp0002.py` читает каталоги `JetSlice` v0.5 r3 (smoke, det) без ошибок: `REQ_COLS` = `n_variants`, `fv_superior`, `fv_producible` (пустые — не в S1); новые колонки прочих таблиц пока не читает (полный разбор — T11, §10). Калибровочный разбор (`--calib`) читает `EXP-0002-calib` без ошибок.
- Стандартный `data_hash`/`data_files` в `run.json` и `matrix.json` покрывает файлы `mods/core`, которые загрузил срез (`lab-cli.md`).

## Изменения v0.5 r1 (решение #190, спец. v0.25 §7 P5)
- Ключ `GRID`, серия `grid/*`: ступень оси `k_diff` — множитель к доле цены `r = 1 − k_diff`, а не к `k_diff`: `LO` → `k_diff` 0,75, `MID` → 0,5, `HI` → 0 (при `k_diff*` = 0,5). Оси `k_catch`, `h_max` — по-прежнему множитель к самому значению. [Предл.] Отчёт P5 (`exp0002.py`) подписывает точку сетки обоими числами: `k_diff` и `r`. Колонки и прочие ключи не менялись; код `run.grid` (SC §12.4) тот же.

## Изменения v0.5 (против v0.4)
Основание — SC §13 (r4, решение #189): наблюдаемые величины среза как след последнего шага (`SliceView.Step`, `CloseView.Reason`, `ActorInputsView.Requirement`) и `NodeView.KnownSince`. Правило v0.4 сохраняется: колонка пишется, только если её значение даёт поверхность Session. Из §4.15 v0.4 перенесены ровно колонки, которые даёт SC §13; §4.15 пуст.
- `a40.cls` ветки С: `CULTURE` / `COEXIST_MONEY` / `OTHER` вместо `NOT_CULTURE` (§4.9, определения §4.16 (б), (г)); L-C0 проверяется полностью. Ветка С берётся из `CloseView.Reason = A40C`, а не из `IdleMonths ≥ T_ab`.
- `a40_c_months`: + `left` (`LeftIn` линии, §4.16 (а)), + `w_after_n` (оговорка (г)).
- `lines`: + `close_rule` (`YPROD`/`REPLACE`/`A40C_N`/`A40C_W`/`A40C_SOLE`; отображение `LineCloseReason` — §4.12), + `close_core` (код Core без потерь), + `left_share` (§4.16 (в)), + `w_after_n`. «Горизонт» — не колонка, а исход сосуществования в анализе (§7).
- `requirements.csv` снова пишется (§4.6): `n_variants`, `fv_superior`, `fv_producible` из `RequirementView` (SC §13.5).
- `events`: + `LIC_REFUSE_MONEY` (`LicenceRefusalView`, SC §13.6).
- `node_times.t_known`: в `L_C`/`L_P` — из `NodeView.KnownSince`; стартовый узел пишется `−1` (как в main), хотя в Core это `startKnownSince = −384` (спец. §6.1 v0.24 r2 «`t_known` стартового знания»).
- §2: `layer/L_C`, `layer/L_P` больше не ждут `t_known` (SC §13.2), но ждут таблиц `Y_k`, `U_k`, `T_design`, `ρ` (T12) и ключей `LAYER = LC/LP` — ключи появятся вместе с ними.
- §9: источники по SC §13.7, `test_api_version` = 3 (v2: тип `PolicyDecisionView.Closes` изменён; v3: `LicenceView.Active`).

## Изменения v0.4 (против v0.3; история)
Основание — разбор пробелов SC §12.6 (решения Orchestrator по TASK-0044 этап 1). Правило: каждая колонка v0.4 исполнима из SC §12 без правки Core; колонка, которую требует текст спец. v0.24, но которой в Core нет, вынесена в §4.15 «Отложено до правки Core» и в CSV **не пишется** (пустые и нулевые колонки-заглушки запрещены).
- Серия `arm/COMP_V010` убрана (спец. §6.8 v0.12: по умолчанию пропускается; путей v0.10 в Core нет).
- Новые ключи манифеста `LAYER`, `CONTROL_TABLES`, `GRID`, `EFFORT_SHARE` (§3) ↔ переключатели SC §12.4. Серии `layer/L_C`, `layer/L_P` помечены «ждут `t_known` в Core» (SC §12.5). Ключ `fv_budget` убран: значение не по умолчанию — параметр мира без `run.*` (SC §12.6), до этого — только значение спец. §5.5.
- `actor_year.volume` определён (§4.2, контракт §9.1 `volume_lead`).
- `tooling_ledger`: `invest_p1`, `invest_p2` → одна колонка `invest` (`ToolingLedgerView.Total`).
- `requirements`: `chosen` убран; `n_variants`, `fv_producible`, `fv_superior` (бывш. `fv_tech`, `fv_super`) отложены (§4.15) — таблица в v0.4 не пишется.
- `events`: `LIC_OFFER` убран (продавец непринятого предложения не хранится, спец. его не печатает); `LIC_REFUSE_MONEY` отложен (§4.15); у `QP_START` `other` = профиль + ранг звена QC, выводится из `why_not` снимка (§4.7).
- `a40`/`a40_c_months`: `left` отложен; класс С в v0.4 — `CULTURE` / `NOT_CULTURE` (без `left` `COEXIST_MONEY` и `OTHER` не различимы). L-O0 проверяется, L-C0 — только при нуле `NOT_CULTURE` (§4.9).
- `lines`: `close_rule`, `left_share` отложены; `role` выводится по стадиям (спец. A41 F17b «Пара линий»).
- `fleet_cmd`: `price` убран.
- §9 — источники колонок по SC §12.2 вместо черновика read views v0.3.

## Изменения v0.3 (против v0.2; история)
- Серии и параметры манифеста — по спец. v0.24 (§6.2 `E ≡ 0`, `k_tr`, `k_qp`, R0; §6.8 плечи; §6.10 парк; §6.11 `LIC_OFF`/`T6D_OFF`; A39 `TOOLING_TARGET`/`POOL_ORDER`; A41 `LINE_CLOSE`); имена параметров ↔ `SliceRunConfig` (§3).
- Новые таблицы `JetSlice`: `a40.csv` и `a40_c_months.csv` (A40 О/С с классом причины, `cancel_design`), `tooling_ledger.csv` (леджер A39 + `tool_unmet`), `lines.csv` (печать A41 F17b), `fleet_cmd.csv` (§6.10), `licences.csv` (§6.11). `whynot.csv` — колонки `cluster`, `order`, `unmet` (причина `TOOLING_POOL_EXHAUSTED`, спец. §5.6 v0.22).
- `JetSliceCalib` — те же A40 и леджер (это P0, не лидерство); запрещённый список уточнён: параметр `TOOLING_M_LEAD` содержит `lead` и в калибровочный манифест не передаётся (§5).
- Стенд C1 по двум постановкам (спец. §6.4, §8.2 п.1 v0.24) — `c1_stand.csv` (§6).
- Источники колонок и read views Session, без которых модели не пишутся (§9).

## Краткий вывод
- [Предл.] Lab пишет `runs.csv` (свои колонки + параметры точки) и по одной таблице на файл; первая колонка — `run`. Lab склеивает CSV прогонов и **требует одинаковый набор файлов и заголовков у всех прогонов**: модель всегда создаёт все свои таблицы, «тяжёлые» в `detail = lite` остаются с одним заголовком.
- [Предл.] Время — индекс месяца `t` 0…383 (спец. §2). Годовые строки — `t mod 12 = 0`. Значение строки «месяца t» — состояние **после** месячного шага t (post), если не сказано «снимок t» (то, что читает compute t).
- [Предл.] `Fx6` пишется **целым raw** (`MetricFormat.Int(raw)`, не `MetricFormat.Fx6`). Пусто — «не наступило к 383»; `−1` — «уже на старте».
- [Предл.] `JetSlice` — 14 таблиц (v0.5: `requirements.csv` вернулась, §4.6), `JetSliceCalib` — 8 (§4, §5). Запрет спец. §8.2 п.2 проверяют тест T10 (заголовок) и `exp0002.py --calib` по одному списку файлов и подстрок.
- [Предл.] A40 печатается каждым срабатыванием, с классом: ветка О — `PROCESSES` / `DEFECTS` (L-O0), ветка С — `CULTURE` / `COEXIST_MONEY` / `OTHER` (v0.5, §4.16), L-C0: `OTHER` в main = 0. `cancel_design` = строки `branch = O`.
- [Предл.] Все колонки v0.5 берутся из read views SC §12.2 и §13.7 (§9). `left`, причина закрытия линии, варианты `Requirement`, отказ ИИ по деньгам — след шага Core (SC §13), не состояние; Lab их не выводит заново. Причина закрытия в печати F17b — отображение кода Core + роль линии по стадиям (§4.12).

## 1. Общие правила
| Правило | Значение |
|---|---|
| Кодировка, разделитель | UTF-8 без BOM, `,`, `\n`; первая строка — заголовок; порядок строк и лишние колонки анализ не трактует |
| Акторы | id сценария `USA`, `GBR`, `SUN`, `FRA`, `SWE`; индекс 0…4 — порядок сценария (спец. §3.5 A7) |
| Великие державы | `USA`, `GBR`, `SUN` = индексы 0, 1, 2 в ленте `lead` |
| Узлы | номер 1…61 (спец. §1.3) |
| Проект | `design` — номер слота проекта актора 0…`Designs−1` (как `ProductsLayout`); `g_p` — поколение платформы, `gen` — `min(g_p, eng_gen + 1)` (спец. §5.1) |
| Время | `t` — индекс месяца; `t = 0` — 1945-09; год `y = 1945 + (8 + t) div 12` |
| `Fx6` | целое raw (`yield` 0,7 → `700000`) |
| Пусто / `−1` | не наступило к 383 / на старте |
| Профиль ИИ | `1`…`3` = P1 `Quality`, P2 `Mass`, P3 `Adaptive` (спец. §4) |
| Классы, перечисления | заглавные латинские слова из этого документа |
| Несколько каталогов | id прогона в анализе = `<номер каталога>:<run>` |

## 2. Серии (поле `series` манифеста, колонка `series` в `runs.csv`)
| Серия | Значение (параметры §3) | Seeds |
|---|---|---|
| `main` | полная модель | 1…1000 |
| `layer/L_K` | абляция §6.1 (`LAYER = LK`, `CONTROL_TABLES`); по существу — после таблиц контролей T12, до них только синтетические таблицы тестов (SC §12.5) | 1…1000 |
| `layer/L_C`, `layer/L_P` | абляции §6.1. `t_known` даёт Core (SC §13.2; стартовое знание — спец. §6.1 v0.24 r2); **ждут таблиц `Y_k`, `U_k`, `T_design`, `ρ` (T12)**, до них — только синтетические таблицы тестов; ключей манифеста нет (`LAYER = LC/LP` — вместе с таблицами) | 1…1000 |
| `old/L_K0`, `old/L_C0` | старые контроли, печать (`LAYER = LK0` / `LC0`) | 1…400 |
| `channel/alpha0`, `ramp1`, `b0`, `eta0`, `kcatch0`, `sigma0`, `kdiff0`, `E0`, `ktr0`, `kqp0`, `R0` | каналы §6.2 (`CHANNELS`) | 1…200 |
| `source/FIX_K`, `FIX_E`, `FIX_A`; `FIX_ALL` | источники §6.3 | 1…400 |
| `grid/<kc>_<kd>_<hm>` ∈ {`lo`,`mid`,`hi`} | сетка P5 (×{0,5; 1; 2} к замороженному `k_catch`, `h_max` и к доле цены `r = 1 − k_diff`, т.е. `k_diff` ∈ {0,75; 0,5; 0}, v0.5 r1; ключ `GRID`) | 1…100 |
| `start/PERT`, `start/SYM` | старт §6.5 | 1…400 |
| `prof/<USA>_<GBR>_<SUN>` | факторный план §6.6 (`PROFILE_CELL` = `9u + 3g + s`) | 1…40 |
| `lic/knows` | §6.7 | 1…400 |
| `arm/EFFORT_SHARE_x0`, `_x05`, `_x2`, `_x4`; `arm/FORCE_x1`, `arm/FORCE_q`; `arm/DERIV` | плечи §6.8 (`EFFORT_SHARE`, `FORCE`, `DERIV`). `COMP_V010` не исполняется (спец. §6.8 v0.12; путей v0.10 в Core нет) | 200 / 400 |
| `fleet/NO_RETIRE`, `fleet/NO_SALE`, `fleet/PURCHASE_CAP`; `sale/SIGMA_LO`, `PHI_FLAT`, `QUOTA_LO`, `QUOTA_HI`, `CHI_OFF` | §6.10 | 1…1000 / 1…200 |
| `lic/LIC_OFF`, `lic/T6D_OFF` | §6.11 | как main |
| `tool/V0`; `tool/POOL_<CAN|REV|PROP|SEV>` | A39: плечо абляции V0 (#163), порядок пула (`exp-pool-order.md`) | по пункту EXP |
| `line/ADMIT` | A41: правило закрытия до F17b (регрессия) | печать |
| `det` | main при threads 1 / 4 / 8 (отдельный манифест) | 1…20 |
| `calib` | `JetSliceCalib` | 10001…10100 |

- `grid/mid_mid_mid` совпадает с main; нет в данных — анализ берёт main на seeds сетки.
- Повторный EXP (#43, #46): те же имена серий, seeds со сдвигом +20000.

## 3. `runs.csv` и параметры манифеста
Колонки Lab: `run`, `series`, `point`, `seed`, `threads`, `status` (`ok`/`failed`), `golden_hash`, затем параметры точки (по алфавиту). Анализ использует `run`, `series`, `seed`, `threads`, `status`, `golden_hash`; прогоны `status ≠ ok` исключаются с предупреждением. `ticks` манифеста = 11 688 (384 мес.); `threads` и `chunk_size` — только исполнение (L2).

[Предл.] Параметры `ParamSet` → `SliceRunConfig` (Session, T6a–T6e). Неизвестный ключ или значение — ошибка загрузки; непрочитанный ключ — `unused_params` в `run.json`.
| Ключ | Тип | Поле `SliceRunConfig` | По умолчанию |
|---|---|---|---|
| `FIX_K`, `FIX_E`, `FIX_A` | bool | `FixK`, `FixE`, `FixA` | false |
| `START` | `SCENARIO`/`PERT`/`SYM` | `Start` | `SCENARIO` |
| `PROFILE_CELL` | int 0…26 | `ProfileCell` | нет (профили из потока `AI`) |
| `CHANNELS` | массив из `ALPHA`, `RAMP`, `B`, `ETA`, `KCATCH`, `SIGMA`, `KDIFF`, `E0`, `KTR`, `KQP`, `R0` | `Channels` | `[]` |
| `DERIV` | bool | `Derive` | false |
| `FORCE` | `OFF`/`X1`/`Q` | — (прокси `Session.Arms.ForceProxy`, `rateDivisor` 1 / 4) | `OFF` |
| `FLEET_ARM` | `MAIN`/`NO_RETIRE`/`NO_SALE`/`PURCHASE_CAP` | `FleetArm` | `MAIN` |
| `FLEET_LINE_COND` | `GEN`/`KAPPA`/`NONE` | `FleetLineCond` | `GEN` |
| `SALE_ARM` | `NONE`/`SIGMA_LO`/`PHI_FLAT`/`QUOTA_LO`/`QUOTA_HI`/`CHI_OFF` | `SaleArm` | `NONE` |
| `LINE_CLOSE` | `YPROD`/`ADMIT` | `LineClose` | `YPROD` |
| `LIC_OFF`, `T6D_OFF`, `LIC_KNOWS` | bool | `LicOff`, `T6dOff`, `LicKnows` | false |
| `TOOLING_TARGET` | `V0`…`V5`, `VINF` | `ToolingTarget.Variant` | `V5` (#163) |
| `TOOLING_RHO_MAX`, `TOOLING_H_LO`, `TOOLING_H_HI`, `TOOLING_M_LEAD`, `TOOLING_H_HI_CONTROL` | число | числа `ToolingTargetSpec` | значения спец. §8.1 |
| `POOL_ORDER` | `CAN`/`REV`/`PROP`/`SEV` | `ToolingTarget.PoolOrder` | `CAN` |
| `LAYER` | `FULL`/`LK`/`LK0`/`LC0` | `Layer` (`LayerControl`) | `FULL` |
| `CONTROL_TABLES` | строка: путь к файлу таблиц контролей §6.1 (относительно манифеста) | `LkTables` | нет; обязателен ровно при `LAYER = LK` (иначе — ошибка загрузки) |
| `GRID` | `<kc>_<kd>_<hm>`, каждое из `LO`/`MID`/`HI`; `LO`/`HI` = ×0,5/×2 к значению, для `kd` — к `r = 1 − k_diff` (`LO` → `k_diff` 0,75, `HI` → 0; #190) | `Grid` (`GridCell`: `KCatch`, `KDiff`, `HMax`) | `MID_MID_MID` |
| `EFFORT_SHARE` | `X1`/`X0`/`X05`/`X2`/`X4` | `EffortShare` (`EffortShareArm`) | `X1` |
| `detail` | `full`/`lite` | — (только Lab) | `full` |
`SkipStartValidation` из манифеста не задаётся (только печать F32). `ViewMode` — опция исполнения, не ключ: `JetSlice` = `Full`, `JetSliceCalib` = `Calibration` (SC §12.1). `fv_budget` — значение спец. §5.5 (24 медианных месячных бюджета закупок великих на t0, `SliceLayoutView.FvBudget`); ключа нет: другое значение меняет вход пресета `adaptive`, т. е. мир, и требует `run.*` (SC §12.6).
[Предл.] Ключи v0.4 в значении по умолчанию не пишут `run.*` и не меняют golden-хеш main (SC §12.4). `CONTROL_TABLES`: Lab читает файл, хеширует его (`meta.control_tables_hash`) и передаёт `LkTables`; Session пишет `run.layer.tables` (SC §12.4). Формат файла задаёт T12 вместе с таблицами. `EFFORT_SHARE`: правило остатка округления — спец. §6.8 (v0.24, [Предл.]): `D′` точно, прочие доли кроме закупок — floor, остаток — в закупки. Данные — `mods/core` через `GameData`; `data_hash` и `scenario_hash` — в `run.json` (Lab `Provenance`).

## 4. Таблицы `JetSlice`
`detail = full` — в `main`, `lic/*`, `channel/*`, `tool/*`, `line/*`, `det`; `lite` — в остальных. В `lite` только заголовок у `whynot`, `prc_year`, `node_times`, `gen_times`, `requirements`, `tooling_ledger`, `lines`.

### 4.1 `summary.csv` — строка на прогон (всегда)
| Колонка | Тип | Смысл |
|---|---|---|
| `prof_USA` … `prof_SWE` | int 1…3 | профиль ИИ |
| `lead` | str[384] | `tech_lead(t)` среди великих (ключ `TechKey`, контракт §9.1 v2.2, ничья — удержание), `'0'`…`'2'` |
| `lead_all` | str[384] | тот же ключ по 5 акторам, `'0'`…`'4'` |
| `lead_field` | str[384] | поле ключа, на котором лидер строго лучше лучшего из прочих великих: `G`/`T`/`N`/`P`; `=` — ничья |
| `hash_final` | hex | хеш состояния после тика 11 687 (= `golden_hash` Lab) |
| `hash_years` | str | 32 годовых хеша (после шага `t mod 12 = 11`), через `;` (спец. §9.3) |
Смены, `chg`, tenure, `small_lead` считает анализ (спец. §5.2–5.3).

### 4.2 `actor_year.csv` — `t mod 12 = 0` (всегда)
`t`, `actor`, `key_G`, `key_T`, `key_N` (int), `key_P` (Fx6), `fv` (Fx6, спец. §5.5), `fv_fleet` (Fx6, §6.10 печать «Σ по когортам»), `volume` (Fx6, печать `volume_lead`, определение ниже), `budget` (Fx6), `eng_short_months` (int 0…12, P4 (b): **наибольшее по КБ актора** число месяцев года с `granted/n < 0,9` у этого КБ (`Required > 0`, у КБ есть проект); v0.5 r5 — было «месяцы, когда не хватало хотя бы одному КБ»: 3 + 3 мес. у двух КБ давали 6, а спец. §7 #77 требует ≥ 6 мес. у одного КБ), `kd_aero`, `kd_prop`, `kd_metal`, `kd_chem`, `kd_mech` (Fx6).

**`volume` (v0.4) [Предл.].** Контракт §9.1: «`volume_lead` = Σ выпуска по tier ≥ 4 при `yield ≥ 0.7`» (спец. §5.3 — только печать). Выпуск по tier — величина процесса, поэтому:
`volume(a, t) = Σ_{m = t … min(t+11, 383)} Σ_{p: ProcessTier(p) ≥ 4, Yield_m(a, p) ≥ 700000} V_m(a, p)` — сумма raw `Fx6` (`checked`), где `Yield_m`, `V_m` — `ProcessView` (home-site, A27) снимка `Month = m` (post), `ProcessTier` — `SliceLayoutView`. Строка t покрывает год, который начинается в t (последняя — 11 мес. до 383), а не мгновенное значение: строка `t = 348` — ровно последний год окна 0…359, по которому `exp0002.py` считает `volume_lead`. Lab накапливает сумму и пишет строку после месяца `t + 11`. Порог — сравнение raw, равенство проходит. Единица — исполнения процессов (не машины): контракт говорит о tier, а tier есть только у процесса.

### 4.3 `whynot.csv` — годовой `why_not` (full)
Строка на звено для цели спец. §5.6 (`CurrentRequirement`); актор-год без цели строк не пишет.
`t`, `actor`, `target`, `rank` (≥ 1), `layer` (`KNOWLEDGE`/`CAPABILITY`/`PRODUCT`/`ECONOMY`), `cause` (имя `WhyNotCause` в UPPER_SNAKE: `ENGINEERS`, `TOOLING_M`, `TOOLING_POOL_EXHAUSTED`, `ENGINE_GEN_LIMIT`, …), `subject`, `eta` (int / пусто = `null`), `lever`, `lever_valid` (0/1), `closed_t` (int / пусто), **`cluster`** (у `TOOLING_POOL_EXHAUSTED` — кластер, иначе пусто), **`order`** (0…4, у прочих пусто), **`unmet`** (Fx6 `tool_unmet` звена; 0 у прочих).
`closed_t` — первый `t' > t`, когда в `why_not(actor, target)` нет звена с тем же (`layer`, `subject`); модель вызывает `WhyNot` каждый месяц для открытых звеньев (чистая функция снимка). `TOOLING_POOL_EXHAUSTED` и `TOOLING_M` для `closed_t` — одно звено (тот же `subject`).

### 4.4 `node_times.csv` (full; в `calib` — всегда)
Строка на (актор, узел): `actor`, `node`, `t_known`, `t_first`, `t_07`, `t_mastered` (int / −1 / пусто; у не-PRC последние три пусты), `lic` (0/1, `lic/knows`).
`t_known` (v0.5): в main и прочих сериях — месяц смены `NodeView.Known` между снимками (формат §9); в `L_C`/`L_P` — `NodeView.KnownSince`, и Lab проверяет, что оно совпадает со сменой `Known` (расхождение — прогон `failed`). Узел, известный на старте, — `−1` в любой серии: Core хранит для него `startKnownSince = −384` (спец. §6.1 v0.24 r2), значение −384 в CSV не пишется; анализ подставляет его сам при построении таблиц §6.1.

### 4.5 `gen_times.csv` (full; в `calib` — всегда)
`actor`, `gen` (1…4), `t_req`, `t_first_unit`, `t_fielded` (int / −1 / пусто), `cost_u100` (Fx6 / пусто).

### 4.6 `requirements.csv` (full; v0.5)
- (v0.5 r2, TASK-0047) **Месяц 0:** compose месяца 0 выдаёт `Initialize` до тика 0; он виден в начальном view (`Create`, `Month = −1`) как `Actors[a].Decisions`, а `Requirement` месяца 0 — в `Inputs` view после шага месяца 0. Пара для t = 0: `initial.Decisions` × `view0.Inputs.Requirement`; для t > 0 — оба из одного view. На данных ядра compose месяца 0 не возникает (у всех акторов с APP живёт G1, E1).
Строка на compose-команду ИИ годового месяца: `t mod 12 = 0`, в `Decisions.DesignCommands` того же шага есть compose с `Target = Gen` и `ActorInputsView.Requirement` не `null` (SC §13.5). Иначе строки нет.
`req` (порядковый номер строки в прогоне, 0…), `actor`, `gen` (`RequirementView.Gen` = `g*`), `t`, `n_variants` (`Count`: 0 — superior не собирается; 1 — только superior или producible = superior, F-PC3; 2), `fv_superior` (Fx6 / пусто при `Count = 0`), `fv_producible` (Fx6 / пусто при `Count < 2`).
- `FV` — `CandidateValue(..., FvBudget, month)` обоих кандидатов при любом профиле (SC §13.5): S1 сравнивает пресеты, а не выбор ИИ. `chosen` не возвращается.
- S1 (спец. §7): доля строк с `fv_producible > fv_superior` среди `n_variants = 2`; строки с `n_variants < 2` не считаются.
- Проверка T10 (SC §13.8): выбор профиля по вариантам (`Quality` → superior; `Mass` → producible при `Count = 2`; `Adaptive` → больший `FV`, ничья → superior) равен составу compose-команды; расхождение — прогон `failed`.
- В `JetSliceCalib` файла нет (§5; `Requirement = null` в `ViewMode = Calibration`).

### 4.7 `events.csv` (всегда)
`t`, `kind`, `actor`, `other`, `value`. `kind`: `DIS` (перебой сплава; `value` — длительность), `LIC_BUY`, `LIC_END` (A43.4; `value` — срок, мес.) — у `LIC_*` `other` = продавец, `value` у покупки — цена Fx6; `QP_START` (программа качества), `TRAINING` (учебная серия P3, `other` = `design`).
- **`QP_START` (v0.4).** `other` = профиль и ранг звена QC: `P1:T0` (A8), `P1:<rank>`, `P3:1`, `P2` (счётчик A41, без `why_not`). Звено QC — CAPABILITY `CultureH` по `quality_control` (спец. A43.7). [Предп.] Ранг берётся из `SliceSession.WhyNot` того же снимка, что видел годовой compute (Lab и так вызывает `WhyNot` каждый месяц до шага, §4.3); в годовой месяц `ApplyDeals` состояние не меняет (сделки — из годового шага, применяются с t+1). Проверка T10: у каждого `P1:<rank>` ранг ≤ 3, у `P3` — 1; иначе прогон — `failed` (не пустое значение).
- **Цена `LIC_BUY` [Предп.].** Цены в `LicenceView` нет; Lab выводит её из `Flow` месяца сделки: `Flow(b)` = Σ цен, полученных b от покупателей того же месяца с `Seller = b`, − цена(b); покупка месяца — смена `LicenceView` (`Gen`, `Seller`) между снимками t−1 и t. Покупатель берёт не больше одной лицензии в год и только у продавца старшего поколения двигателя, поэтому система решается снизу по поколению. Проверка T10: `Σ Flow` по акторам месяца = 0.
- **`LIC_REFUSE_MONEY` (v0.5; спец. §6.11 «Печать», A43.3 (б)).** Строка на `LicenceRefusalView` из `Step.Actors[a].LicenceMoneyRefusals` месяца `t` (SC §13.6): `actor` — покупатель, `other` = `<seller>:G<gen>` (например `GBR:G2`), `value` — цена (Fx6). Порядок строк месяца — по возрастанию продавца. `room` не пишется (печать спец. его не требует; есть в view для отладки). Это отказы ИИ (профиль хотел предложение, `room < price`); `BadLicenceCount(Money)` — отклонённые внешние команды, другая величина.
- Убран в v0.4: `LIC_OFFER` (непринятые предложения хранятся одной маской `OfferMask` без продавца; спец. §6.11 «Печать» предложения не печатает).

### 4.8 `prc_year.csv` (full)
`t` (`t mod 12 = 0`), `actor`, `node` (все PRC), `known`, `admitted` (0/1), `yield`, `ucm` (Fx6), `licence_share` (Fx6, `LicenceShare` §6.11).

### 4.9 `a40.csv` — срабатывания A40 (всегда) [Предл.]
Строка на команду ИИ по A40: ветка О — `cancel_design(d)`, ветка С — `close_line(d)` с `idle_c ≥ T_ab`. Месяц `t` — месяц compute (снимок t), команда действует с t+1.
| Колонка | Тип | Смысл |
|---|---|---|
| `t`, `actor`, `design`, `g_p` | int, str, int, int | ключ |
| `profile` | int 1…3 | профиль актора |
| `preset` | str | `SUPERIOR`/`PRODUCIBLE`/`ADAPTIVE` — пресет профиля актора (v0.5 r3): у проекта во view пресета нет (`DesignView`); профиль постоянен в прогоне, поэтому для проектов compose ИИ это и есть пресет проекта; у стартовых проектов и `derive` — пресет профиля, не пресет проекта |
| `branch` | `O`/`C` | ветка |
| `cls` | str | О: `PROCESSES` (`gate_open`, условие доктрины выполнены, `admitted` = false), иначе `DEFECTS` (`ClassifyGiveUp`, `DesignInputsView.GiveUp`); С: `CULTURE` / `COEXIST_MONEY` / `OTHER` (v0.5, ниже) |
| `t_stage_o` | int | месяц входа в О |
| `t_launch` | int / пусто | месяц запуска серии (С) |
| `force` | 0/1 | серия запущена с `force` (С; у ИИ всегда 0) |
| `idle` | int | `idle_c` снимка (С) |
| `rep_before` | 0/1 | повтор `Requirement` по (актор, `g_p`) уже был |
**Строки ветки С (v0.5):** закрытия с `CloseView.Reason = A40C` (SC §13.4). Проверка T10: у каждой такой строки `idle ≥ T_ab` (иначе прогон `failed`). Закрытия с `idle ≥ T_ab` и другой причиной печатаются счётчиком [Предп.: 0 — ветка С A40 стоит в шаге (3) A41 раньше F17b, а причина одна на проект в месяц — первая, SC §13.4].
**Класс С** (спец. A40 «Печать причины» и «v0.20: две линии»), по строкам `a40_c_months` этого срабатывания, в порядке проверки:
1. `CULTURE` — в каждом из `T_ab` месяцев наименьший `yield` по `prc_set` = 0, или сплав = 0 при `yield` всех плавок (39, 40, 41) = 0 (как v0.4);
2. `COEXIST_MONEY` — §4.16 (б);
3. `OTHER` — прочие.
**L-O0:** `DEFECTS` в main = 0. **L-C0:** `OTHER` в main = 0 (спец. §6.4); `COEXIST_MONEY` в L-C0 не входит (спец. A40 «v0.20: две линии»: следствие удержания F17b, ожидание [Предп.] 0 — печатается отдельно). Строки с `w_after_n = 1` в каком-либо из `T_ab` месяцев печатаются отдельным счётчиком (оговорка (г)).
`cancel_design` по сериям = число строк `branch = O` (спец. A40 «§7 не меняется»).

### 4.10 `a40_c_months.csv` — месяцы до срабатывания ветки С (всегда)
Строка на (срабатывание С, k = 0…`T_ab`−1): `t_fire`, `actor`, `design`, `t` (= `t_fire − T_ab + 1 + k`), `min_yield` (Fx6), `argp` (узел наименьшего `yield`), `alloy_supply` (Fx6), `deficit` (0/1), `t_a` (Fx6), **`left`** (Fx6, v0.5: `LeftIn` линии `design` в шаге 7 месяца `t`, §4.16 (а); пусто, если следа нет — `L_P`), **`w_after_n`** (0/1, v0.5: в месяце `t` у актора две линии и `design_id(W) > design_id(N)`, оговорка (г)), **`training`** (Fx6, v0.5 r4: `Training` линии `design` в шаге 7 месяца `t` — `T_a`, доступный этой линии; пусто, если следа нет). `t_a` — `T_a` плана актора (леджер), он не обязательно дошёл до линии.

### 4.11 `tooling_ledger.csv` — леджер A39 (full; помесячно, строка на актора)
Чистая функция снимка, печать Lab (post), состояния нет (спец. A39 «Учёт»).
`t`, `actor`, `rest` (Fx6, `rest_a`), `b_cap` (Fx6), `invest` (Fx6, Σ вложений проходов 1 и 2 = `ToolingLedgerView.Total`; разбиения по проходам `ToolingPlanResult` не даёт), `taken_rest`, `taken_cap` (Fx6), `rest_unspent` (= `rest_a − taken_rest`), `t_a` (= `B_cap − taken_cap`), `rest_returned` (Fx6), `n_part` (ячеек в проходе 2), `tool_unmet_MET`, `tool_unmet_CHEM`, `tool_unmet_MECH`, `tool_unmet_AERO` (Fx6, Σ по site и измерениям), `pool_exh` (0/1, `Σ tool_unmet > 0`).
Инварианты T8 (анализ проверяет): `invest ≤ rest + b_cap`, `taken_rest ≤ rest`, `t_a ≥ 0`.

### 4.12 `lines.csv` — линии ИИ, печать A41 F17b (full; помесячно, строка на линию в С по снимку t)
`t`, `actor`, `design`, `g_p`, `role` (`W`/`N`/`SOLE`), `y_min` (Fx6), `argp`, `rate` (Fx6, выпуск месяца), `unit_cost` (Fx6), `idle`, `close` (0/1, `close_line` с t+1), `start` (0/1), **`close_rule`**, **`close_core`** (str / пусто при `close = 0`; ниже), **`left_share`** (Fx6 / пусто; §4.16 (в)), **`w_after_n`** (0/1, оговорка (г)). `role` Lab выводит по стадиям: [Факт: спец. A41 F17b «Пара линий» — «Состояния нет: пара определяется по стадиям»] при двух линиях `N` — с большим `g_p` (ничья → больший `design`), `W` — другая; одна линия — `SOLE`. Роль берётся по снимку t — тому же, по которому ИИ решал закрытие. Линии месяца t (v0.5 r3) — проекты в С по снимку t без закрытых ИИ в compute t−1 (`Decisions.Closes` снимка); больше двух — прогон `failed`. `start` = 1, если в `Decisions.Starts` месяца t есть этот `design`; проверка «в месяце `LAUNCH_*` есть `start`» — по `Starts` актора (запускаемый `X` ещё не в С и строки не имеет).

**Причина закрытия (v0.5).** `close_core` — `CloseView.Reason` закрытия этой линии в `Decisions.Closes` месяца t (SC §13.4), в UPPER_SNAKE: `A40C`, `YPROD`, `LAUNCH_ALL`, `LAUNCH_CANDIDATE`. `close_rule` — код печати спец. A41 F17b «Печать» по `close_core` и `role`:
| `LineCloseReason` (Core) | Ветка правила | `role` закрытой линии | `close_rule` |
|---|---|---|---|
| `A40C` | ветка С A40 (`idle_c ≥ T_ab`) | `N` / `W` / `SOLE` | `A40C_N` / `A40C_W` / `A40C_SOLE` |
| `YProd` | F17b п.2: `y_min(N) ≥ y_prod` → закрыта `W` | `W` | `YPROD` |
| `LaunchAll` | F17b п.1, `y_min(X) ≥ y_prod`: закрыты все прочие линии; и `LINE_CLOSE = ADMIT` (правило v0.15–v0.19) | любая | `REPLACE` |
| `LaunchCandidate` | F17b п.1, `y_min(X) < y_prod`: закрыт кандидат `N`, его заменяет `X` | `N` | `REPLACE` |
| `None` | — | — | при `close = 1` недопустимо: прогон `failed` (SC §13.8) |
- `A40C_SOLE` — дополнение формата к списку спец. (закрытие единственной линии сосуществованием не является; печать A40 — `a40.csv`). `REPLACE` объединяет два кода Core, как «`REPLACE` (п.1)» спец.; различие сохраняет `close_core` (в `line/ADMIT` все замены — `LAUNCH_ALL`).
- Проверки T10 (нарушение — `failed`): `YPROD` только у `role = W` при двух линиях; `LAUNCH_CANDIDATE` только у `role = N`; в месяце `LAUNCH_*` у актора есть `start = 1`.
- **«Горизонт»** — не код закрытия: сосуществование (`W`, `N`), не закончившееся к месяцу 383, анализ помечает `HORIZON` (цензура 384, §7). Конец сосуществования — первый месяц, когда пара (`W`, `N`) перестаёт быть той же парой; его причина — `close_rule` закрытой линии этого месяца.

Сосуществования, их длительность, причины конца и запуски с `g_p(X) ≤ g_p(W)` анализ собирает из этой таблицы (спец. A41 F17b «Печать»; сверка seed 1 — §11 T6e2 п.10).

### 4.13 `fleet_cmd.csv` — команды парка ИИ (всегда; §6.10)
`t`, `actor`, `kind` (`RETIRE`/`SELL`), `design`, `gen_d`, `gen_line` (`gen(L)`), `count` (Fx6), `g_front` (int, мировой фронт), `line_bind` (`LineBind`). Печать #146 (продажи «отстающими своего фронта»: число, акторы, месяцы) и E-gen считаются из неё. `price` у `SELL` убран (v0.4): спец. §6.10 цену команды не печатает, а `Quote` — функция снимка t, команда видна только после шага.

### 4.14 `licences.csv` — лицензии (всегда; §6.11)
`t`, `seller`, `buyer`, `gen`, `profile` (покупателя), `price` (Fx6, как у `LIC_BUY`, §4.7), `t_end` (int / пусто). Дублирует `events.csv` `LIC_BUY`/`LIC_END` в форме печати спец. §6.11 («лицензии по парам (продавец, покупатель, поколение, месяц, профиль, цена)») [Предл.; T11 может убрать дубль].

### 4.15 Отложено до правки Core
В v0.5 пусто: все колонки §4.15 v0.4 (`left`, `COEXIST_MONEY`/`OTHER`, `left_share`, `close_rule`, `n_variants`/`fv_producible`/`fv_superior`, `LIC_REFUSE_MONEY`) даёт SC §13 и перенесены в §4.6, §4.7, §4.9, §4.10, §4.12. Правило v0.4 остаётся: колонки без источника в Session в CSV нет (не пустая и не 0).

### 4.16 Деньги шага 7 A38: `left`, `COEXIST_MONEY`, `left_share` (v0.5; SC §13.3)
Источник — `SliceView.Step.Actors[a].Money` месяца t (`MoneyStepView`: `LeftStart`; по каждой линии стадии С по возрастанию `design` — `LeftIn`, `FromLeft`, `Training`, `FromTraining`). Это след шага 7 A38 месяца t: деньги, на которые куплен выпуск `rate` того же месяца. `Money = null` (`L_P`) — колонки ниже пусты. Линия месяца (§4.12) без строки в `Money.Lines` — `left`, `left_share` пусты, счётчик `lines_without_step7_money` (v0.5 r3; было «прогон `failed`»).
- **(а) `left`** (строка `a40_c_months` месяца t для линии d) = `LeftIn(d)` — общие деньги закупок, которые увидела линия d до своей закупки (до проверки `Admitted`).
- **(б) `COEXIST_MONEY`** (v0.5 r3, SC §13.3 r6) — для срабатывания С линии d: в **каждом** из `T_ab` месяцев строк `a40_c_months` одновременно: у актора две линии и d — `N` (`role` по снимку месяца, §4.12); выпуск `rate(d) = 0` (`DesignView.Rate` post t); `left = LeftIn(d) = 0` (§4.16 (а)); `training = Training(d) = 0` (`T_a` у линии d: учебные деньги идут только на линию цели учебной серии, спец. A41); `DesignView.Bind(d) = Money` (post t: шаг 7 месяца t ограничил выпуск деньгами, договор r6 §4.4d). Это «`N` с выпуском 0 … при `left` после `W` = 0 и без `T_a`» спец. A40 «v0.20: две линии». Условие `Bind = Money` оставлено: у недопущенной линии `Bind = None`, такие месяцы — `OTHER`. Это строже буквы спец., L-C0 от этого только строже. **v0.5 r4** (ревью Codex r4 TASK-0044): в r3 было одно `Bind = Money`. Оно выполняется и при `left > 0`, которого не хватает на единицу, и при `T_a > 0` у линии; такие срабатывания ошибочно уходили из L-C0. Нет строки шага 7 (`left` пусто) — не `COEXIST_MONEY`. Проверяется после `CULTURE` (§4.9). ~~v0.5: `LeftIn(d) = 0` и `Training(d) = 0`~~ — заменено в r3 (не отличало «нет денег» от «нет допуска»).
- **(в) `left_share`** (строка `lines` линии d месяца t) = `FromLeft(d) / LeftStart` — доля общих денег начала шага 7, взятая линией d; raw `Fx6`: `⌊FromLeft·10⁶ / LeftStart⌋` в целых (`checked`), без `double`. Пусто при `LeftStart = 0` и при `Money = null`. Пишется для любой `role`; «доля `left`, взятая `N`» спец. A41 F17b «Печать» — строки `role = N`. Сумма по линиям актора месяца ≤ 10⁶ (инвариант SC §13.8 `Σ FromLeft ≤ LeftStart` с округлением вниз) — проверка анализа.
- **(г) Оговорка про `design_id`.** Шаг 7 тратит деньги по возрастанию `design_id`. «`left` после `W`» спец. равно `LeftIn(N)` только при `design_id(W) < design_id(N)` — обычный случай [Факт: спец. A41 F17b п.3 — «lowest free slot, never reused»]. При `design_id(W) > design_id(N)` (спец. F17b п.2: запуск `X` с `g_p(X) < g_p(W)`) `N` покупает раньше `W`, и `LeftIn(N)` — деньги **до** `W`, не после: условие (б) строже, чем в спец., а `left_share(N)` не учитывает трату `W`. Lab такие месяцы **не пересчитывает** — помечает `w_after_n = 1` (в `lines` у обеих линий пары, в `a40_c_months`) и печатает их число отдельно [Предп.: случай редкий; SC §13.9 Р-13-4]. Если в main такие месяцы есть у срабатываний С — вопрос к спец. (уточнение «после `W`»), а не правка формата.

## 5. Таблицы `JetSliceCalib` (серия `calib`)
| Файл | Колонки |
|---|---|
| `calib_summary.csv` | `run`, `prof_USA` … `prof_SWE`, `hash_final` |
| `node_times.csv` | как §4.4 |
| `gen_times.csv` | как §4.5 |
| `prc_month.csv` | `run`, `t`, `actor`, `node` (все PRC), `known`, `admitted`, `yield`, `ucm` (Fx6), `v` (Fx6) — `Y_k(τ)`, `U_k(τ)`, C2 |
| `actor_month.csv` | `run`, `t`, `actor`, `budget`, `proc_budget` (Fx6), `out_g1` … `out_g4` (int), `active_g1` … `active_g4` (0/1) — `ρ[g]` |
| `a40.csv`, `a40_c_months.csv` | как §4.9–4.10 (L-O0/L-C0 на калибровочных seeds — P0, не лидерство) |
| `tooling_ledger.csv` | как §4.11 (в `calib` — всегда) |

**Запрет (спец. §8.2 п.2), проверяется тестом T10 и `exp0002.py --calib`:**
- файлов `summary.csv`, `actor_year.csv`, `whynot.csv`, `requirements.csv` в каталоге калибровки нет;
- ни одна колонка ни одного CSV каталога (включая параметры в `runs.csv`) не содержит подстрок `lead`, `chg`, `fv`, `why`, `key_`, `volume` (без учёта регистра). Следствия: `TOOLING_M_LEAD` в калибровочный манифест не передаётся (у `m_lead` — значение по умолчанию §8.1); `lines.csv`, `fleet_cmd.csv`, `licences.csv` в калибровочной сборке не пишутся (не нужны C2/C4 и контролям §6.1).
- [Предл.] Модель `JetSliceCalib` не вызывает `TechKey`, `FV`, `why_not` вообще (а не только не пишет): тест T10 проверяет заголовки, ревью — код.

## 6. Стенд C1 по двум постановкам (`c1_stand.csv`; T12, спец. §6.4 п.2, §8.2 п.1 v0.24)
[Предл.] Отдельная модель Lab с одним актором (`SingleLayerStand`, как `LineStand`), детерминированная, без seeds. Строка на (постановка, плечо, удвоение):
| Колонка | Смысл |
|---|---|
| `setup` | `DEV` («освоение»: `X0 = 0`, `culture_H` эталонного GBR-подобного site t0, `g` по правилу наладки ИИ; удвоения от выпуска первого месяца `r_ref` при `yield ≥ 0,25`) / `MATURE` («зрелая»: `X0 = 6·X½`, `g = 1`, удвоения `line_E` с началом ≤ 400) / `MATURE_3X` (печать, `X0 = 3·X½`) |
| `arm` | `FULL`, `NO_YIELD` (`proc_X` заморожен), `NO_ASM` (`line_E` заморожен), `NONE`; у `DEV` ещё `FULL_E0` (`E ≡ 0`) и `FULL_G1` (`g ≡ 1`) |
| `k`, `n_from`, `n_to` | номер удвоения и его границы (шт.) |
| `dlog_cost` | `Δ log unit_cost` (double, анализ; `MetricFormat.Real`) |
| `dlog_yield`, `dlog_asm` | вклады каналов выхода и сборки (из плеч) |
| `asm_drop`, `asm_over_floor_drop` | падение полного `assembly_hours` и `assembly_hours − h_floor` |
Статистики (анализ, спец. §8.2 п.1): C1a = `1 − exp(−Σ dlog_cost/n)` по `DEV/FULL`, C1b = `Σ dlog_yield / Σ (dlog_yield + dlog_asm)` по `DEV`; C1d, C1e — то же по `MATURE`; пустой набор удвоений → `d` = 1. Падение `asm_over_floor_drop` в `MATURE` — проверка P0 (15–30%, #187), в потери не идёт. «Зрелая» уже покрыта тестом `ProductsChannelBudgetTests` (TASK-0042 п.4); «освоение» требует `culture_H`/`tooling_M` GBR t0 и правила наладки — из данных сценария и чистых функций Core, без Session [Предп.; проверить на T12].

## 7. Что считает анализ (дополнения v0.3; определения v0.2 §6 — в силе)
- Определения v0.2 (засчитанная смена, tenure, `small_lead`, P1–P5, исход, D1, D2, таблицы контролей) не меняются; спец. v0.24 §7 их не меняла; v0.25 (#190) поменяла только ось `k_diff` сетки P5.
- **A40 (печать, P0):** по сериям — число `cancel_design` (`branch = O`), по (актор, класс) — среднее на seed, доля seeds с ≥ 1 срабатыванием С; L-O0 и L-C0 — флаги P0 в отчёте; сверка с ожиданиями A40 (б) (объявляются заново до первого прогона main, спец. A40 v0.20).
- **A39:** доли `pool_exh` по актору; инварианты леджера §4.11 — нарушение = дефект (P0).
- **F17b:** из `lines.csv` — сосуществования по (актор, seed), длительность p50/p90/max (цензура 384); причина конца по `close_rule` {`YPROD`, `REPLACE`, `A40C_N`, `A40C_W`} или `HORIZON` (§4.12); распределение `left_share` строк `role = N`; число месяцев `w_after_n = 1`; класс `COEXIST_MONEY` из `a40.csv`.
- **S1:** из `requirements.csv` (§4.6).
- **Лицензии, парк** — печати §6.10–6.11 из `licences.csv`, `fleet_cmd.csv`, `events.csv`.

## 8. Манифесты
**`lab/manifests/EXP-0002.json`** (схема Lab 1; `model: "JetSlice"`, `ticks: 11688`, `threads: 1`, серии §2) и **`EXP-0002-det.json`** (`threads: [1, 4, 8]`, серия `det`). Поле `meta` (хешируется в `manifest_hash`): `slice_spec` (v0.25), `csv_format` (v0.5 r3), `hypothesis`, `decision_rule`, `scenario_hash`, `control_tables_hash`, `calibration_manifest`, `analysis` (`script`, `script_sha256`, `config` с `p0_passed`), `rng` (спец. §9.1, `0x5EED`), `after_review`, `seed_offset`, `freeze` — как v0.2. Шаблон не замораживается: заморозку делает T12 (спец. §8.2 п.8).
**`lab/manifests/EXP-0002-t10-smoke.json`** — малая матрица T10 (каждый ключ §3 один раз, seeds 1–2, threads 1/4), проверка пути Lab, не эксперимент.
**`lab/manifests/EXP-0002-calib.json`** (`model: "JetSliceCalib"`, серия `calib`, seeds 10001…10100): в `meta` — `scenario_hash`, сетки «К» по этапам **C1 → (C3+C4) → C2** (v0.23, #177) с порядком обхода и бюджетом (≤ 40 / 60 / 30), функция потерь (C1a, C1b, C1d, C1e; C3; C4,G2…G4; C2), журнал точек со значениями `d_k` (коммитится).
DONE-эксперимент записывает `core_commit`, `data_hash`, `manifest_hash`, seeds, threads, `golden_hash` main (sha256 строк «`seed golden_hash`\n» по возрастанию seed), `outcome`.

## 9. Источники колонок (read views SC §12.2)
[Предл.] Lab читает только `SliceSession.Layout`, `View` (после каждого месячного шага), `WhyNot`, `CurrentRequirement` (ADR-0014 п. 5–6; `test_api_version` = 3, SC §13.7). Строка «месяца t» — `View` с `Month = t`; «снимок t» — `View.Inputs` того же снимка; решения ИИ месяца t — `ActorView.Decisions` (SC §12.1); след шага t — `View.Step` того же `View` (не `null` ровно в тике с месячным шагом; в другом тике его не читать, SC §13.9 Р-13-1).
| Таблица | Источник |
|---|---|
| `summary` | `Profile`; `lead*` — `ActorView.Key` каждого месяца; `hash_*` — `StateHash` |
| `actor_year` | `Key`, `FleetValue` (только `Month mod 12 = 0`), `Inputs.Budget`, `KD`, `eng_short_months` — `BureauView` (`10·Granted < 9·Required`, счёт по `Bureau`, в колонке — максимум по КБ); `fv_fleet` — Lab, `ProductsMath.FleetValue` по `DesignView`; `volume` — `ProcessView` + `ProcessTier` (§4.2) |
| `whynot` | `WhyNot`, `CurrentRequirement` |
| `node_times`, `prc_year`, `prc_month` | `NodeView.Known` (смена между снимками = `t_known`; в `L_C`/`L_P` — `NodeView.KnownSince`, §4.4), `ByLicence`, `ProcessView`; `ucm` — `UnitCostMult` [Предп. SC §12.2, проверка developer] |
| `gen_times` | `t_req` — `DesignCommands` (compose, `Target = g`); `BuiltAny`, `FieldedGen`, `UnitCost`; `cost_u100` — Lab по накопленному выпуску |
| `requirements` | `Inputs.Requirement` (`RequirementView`), `DesignCommands` (compose) |
| `events` | `Disruption`, `Licence` (+ `Flow`, §4.7), `Quality`, профиль, `WhyNot` (ранг QC), `TRAINING` — `Tooling.TrainingBudget`; `LIC_REFUSE_MONEY` — `Step.Actors[a].LicenceMoneyRefusals` |
| `a40`, `a40_c_months` | `DesignCommands` (Cancel) + `Inputs.Designs.GiveUp`; `Closes` с `Reason = A40C`; `YMin`, `ArgP`, `Capability`, `Tooling.TrainingBudget`; `left`, класс С — `Step.Actors[a].Money` (§4.16) |
| `tooling_ledger` | `Inputs.Tooling` (`Rest`, `CapBudget`, `Total`, `TakenRest`, `TakenCap`, `RestReturned`, `TrainingBudget`, `Participating`, `UnmetByCluster`) |
| `lines` | `DesignView` (стадия, `PlatformGen`, `Rate`, `UnitCost`), `Inputs.Designs` (`YMin`, `ArgP`, `IdleMonths`), `Starts`, `Closes` (`CloseView.Reason`), `Step.Actors[a].Money` (§4.16) |
| `fleet_cmd`, `licences` | `Decisions.Fleet`, `DesignView.Gen`, `Bind`, `Inputs.GFront`; `LicenceView` |
| `calib_*`, `actor_month` | те же поля в `ViewMode = Calibration` (без `Key`, `FleetValue`, `WhyNot`) |
`layer/L_C`, `layer/L_P` — после таблиц T12 (§2).

### 9.1 Определения Lab (v0.5 r3) [Предп.]
Где спец. и §4–§5 не задают величину дословно, модели Lab считают так (код: `SliceObserver`). Это выводы из views без новых формул мира.
| Колонка | Определение |
|---|---|
| `node_times.t_known` | первый месяц t, когда `NodeView.Known` в post t; `−1` — известен в начальном view; при `KnownSince ≠ null` оно обязано равняться t (иначе `failed`) |
| `t_first`, `t_07`, `t_mastered` | первый post t с `Yield > 0 ∧ V > 0`; `Yield ≥ 700000`; `Mastered` (`ProcessView`); `−1` — уже в начальном view |
| `lic` | `ByLicence` хотя бы в одном view |
| `gen_times.t_req` | первая compose-команда ИИ с `Target = g` (`Decisions` начального view → 0, view t → t); `−1` — у актора на старте есть проект с `PlatformGen = g` |
| `t_first_unit` | первый post t с проектом `Gen = g` и `InService > 0`; `t_fielded` — первый post t с `FieldedGen ≥ g` (`FieldedGen` — наибольшее поколение в `fielded`) |
| `cost_u100` | первая серия поколения `Gen = g` (первый проект, вошедший в С после старта): `UnitCost` в месяце, когда Σ `Rate` с начала серии ≥ 100 ед.; пусто, если серия поколения была на старте (выпуск до t0 неизвестен) |
| `whynot.target` | `REQUIREMENT:<g*>` (`WhyNotTarget`); `subject` — `<process>:<DIMENSION>` у звена CAPABILITY с процессом, иначе `SubjectId`; `lever` — все рычаги звена через `;`; `lever_valid` — `LeverValidator.Check` по `LeverRegistry.Slice` пуст |
| `events DIS` | `t` = `Disruption.Start` нового перебоя, `value` = `End − Start + 1` (границы включительно) |
| `events QP_START` | `t` = `Quality.Start` (месяц, с которого действует программа; решение — годовой compute t−1); ранг QC — первое звено CAPABILITY `CultureH` с `Dim = QualityControl` в `why_not(actor, CurrentRequirement)` снимка того же годового месяца |
| `events TRAINING` | строка на месяц t, когда `Quality.TrainingTarget` снимка ≥ 0 и `Inputs.Tooling.TrainingBudget > 0`; `other` = проект, `value` = `T_a` |
| `events LIC_END`, `licences.t_end` | замена лицензии новой сделкой (`t_end` = t) или **истечение A43.4** — переход `LicenceView.Active` 1 → 0 в post t → `t_end` = t + 1 (§11 п. 7) |
| `a40.t_launch` | первый post t со `Stage = Series`; `−1` — серия на старте |
| `a40.force` | 1, если прокси FORCE отправил `start_series` с `force` для (актор, проект) после шага месяца f (по `SliceSession.CommandLog`; у прокси набор закрыт) и `t_launch = f + 1` (команда принята: проект в С с post f + 1). Отклонённая команда и поздний запуск ИИ — 0 (ИИ не форсирует, P0). [Предп.: эквивалентно `ProductsSystem.SeriesForced`, во view его нет; ревью Codex r3 TASK-0044] |
| `a40.rep_before` | `Decisions.Repeats[g_p − 1] > 0` снимка t |
| `fleet_cmd.gen_line`, `line_bind` | линия `L` — новейшая линия в С по снимку (наибольший `g_p`, ничья — больший `design`): её `Gen` по снимку и `Bind` post t; `count` = `N · 10⁶` (целые единицы как Fx6) |
| `actor_year.fv_fleet` | Σ по проектам с `InService > 0` `ProductsMath.FleetValue(⌊InService⌋, Readiness, Perf, γ)`, `γ` = `ProductsParams.GammaQ10` по умолчанию (каналы среза его не меняют) |
| `actor_month.out_g*` | `⌊Σ Rate⌋` проектов `Gen = g` (post t); `active_g*` — есть проект `Gen = g` в С (post t) |
| `tooling_ledger.t_a` | `ToolingLedgerView.TrainingBudget`; на seeds 1…20 main совпадает с `b_cap − taken_cap` во всех 115 200 строках |

`run.json` модели — объект `model_info`: `test_api_version`, `data_hash` (`GameData.DataHash`), `scenario_hash`, `months`, `force_sent` (при `FORCE`), счётчики `a40_close_idle_ge_tab_other_reason` (§4.9), `w_after_n_actor_months`, `closes_of_designs_not_lines`, `lines_without_step7_money`.

## 10. Сверка с `exp0002.py` (этап 1 T10; доработка — T11)
- Совпадает: `runs.csv`, `summary.csv`, `actor_year.csv` (лишняя `fv_fleet`), `whynot.csv` (лишние `cluster`, `order`, `unmet`), `node_times`, `gen_times`, `events` (новые `kind` анализ не трактует), `prc_year` (лишняя `licence_share`), калибровочные таблицы, списки запрета (`DEFAULTS.calib`).
- **Расходится с v0.4 (правит T11):** `REQ_COLS` и S1 читают `requirements.csv` (`n_variants`, `fv_tech`, `fv_super`, `chosen`) — в v0.4 файла нет, S1 = «нет данных» до v0.5 (имена — `fv_producible`, `fv_superior`); `volume_lead` по `actor_year.volume` — смысл по §4.2 (годовая сумма); леджер — `invest` вместо `invest_p1/p2`; класс С `NOT_CULTURE`; серии `arm/COMP_V010` нет.
- **v0.5 (правит T11):** ~~`REQ_COLS` → `req`, `actor`, `gen`, `t`, `n_variants`, `fv_superior`, `fv_producible` (без `chosen`; S1 по §4.6)~~ — сделано в r3 (ревью Codex TASK-0044); класс С `COEXIST_MONEY`/`OTHER` вместо `NOT_CULTURE`, L-C0 — по `OTHER`; новые колонки `lines` (`close_rule`, `close_core`, `left_share`, `w_after_n`) и `a40_c_months` (`left`, `w_after_n`); `kind = LIC_REFUSE_MONEY`.
- T11 добавляет чтение `a40.csv`, `a40_c_months.csv`, `tooling_ledger.csv`, `lines.csv`, `fleet_cmd.csv`, `licences.csv` и печати §7; серии §2; C1 по `c1_stand.csv` (§6); версии в docstring (спец. v0.24 r2, формат v0.5).

## 11. Открытые вопросы
1. ~~Реализация SC §13 (TASK-0047)~~ — сделана; модели T10 пишут все колонки v0.5 (TASK-0044 этап 2).
2. `t_known` стартового знания определён (спец. §6.1 v0.24 r2: −384); правка Core «неизвестен — ровно −1» и `max(t_known, 0)` в `L_P` — в той же задаче Core. `layer/L_C`, `layer/L_P` ждут таблиц T12.
3. Формат файла `CONTROL_TABLES` и таблицы контролей §6.1 — T12; до них `layer/L_K` только на синтетических таблицах.
4. CSV стендов C3, C5 и функция потерь — T12. Проверка «расписание `L_K` воспроизводит `m` ±3 мес. и `s` ±20%» (спец. §6.1) — T12.
5. [Предп.] Выводы в Lab (ранг QC у `QP_START`, цена `LIC_BUY` из `Flow`) проверяются тестами T10 (§4.7); если тест покажет расхождение — колонка уходит в §4.15.
6. [Предп.] `w_after_n = 1` в main — редкость (оговорка (г)); если такие месяцы попадут в срабатывания С или в сосуществования заметно, нужно уточнение спец. A40/F17b «`left` после `W`». [Факт: T10 малая матрица] seeds 1…20 main — 0 месяцев; `arm/DERIV` seed 1–2 — 344 актор-месяца.
7. **`LIC_END` по истечению A43.4** — переход `LicenceView.Active` 1 → 0 (Session v3, `Active` = `SliceWorld.LicenceActive(a)` = `lic_gen > 0 ∧ g_b ≤ lic_gen`); без вывода по `LicenceShare`. Закрыто в r5 (решение Orchestrator, ADR-0014 п. 5: read view вместе с первым потребителем T10). Замена лицензии новой покупкой — `t_end` = t.
