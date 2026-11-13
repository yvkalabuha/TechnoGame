# Формат CSV и манифестов EXP-0002 (срез «реактивная авиация»; `lab/analysis/exp0002.py`)

> Статус: черновик **v0.3** (simulation-engineer, 2026-11-13, TASK-0044 этап 1) под спецификацию `slice-jet-aviation.md` **v0.24** (дальше «спец. §N»). v0.2 (2026-11-03, по спец. v0.6) — в истории git.
> Соглашения унаследованы от `exp0001-csv-format.md` и Lab (ADR-0008, `src/Singularity.Lab/RunExecutor.cs`, `MatrixRunner.cs`).
> C#-моделей `JetSlice` и `JetSliceCalib` ещё нет: этап 2 T10 ждёт read views Session (§9). Формат проверен только синтетическими данными (`exp0002.py --selftest`) и сверкой с печатью T6eHarness (§9).

## Изменения v0.3 (против v0.2)
- Серии и параметры манифеста — по спец. v0.24 (§6.2 `E ≡ 0`, `k_tr`, `k_qp`, R0; §6.8 плечи; §6.10 парк; §6.11 `LIC_OFF`/`T6D_OFF`; A39 `TOOLING_TARGET`/`POOL_ORDER`; A41 `LINE_CLOSE`); имена параметров ↔ `SliceRunConfig` (§3).
- Новые таблицы `JetSlice`: `a40.csv` и `a40_c_months.csv` (A40 О/С с классом причины, `cancel_design`), `tooling_ledger.csv` (леджер A39 + `tool_unmet`), `lines.csv` (печать A41 F17b), `fleet_cmd.csv` (§6.10), `licences.csv` (§6.11). `whynot.csv` — колонки `cluster`, `order`, `unmet` (причина `TOOLING_POOL_EXHAUSTED`, спец. §5.6 v0.22).
- `JetSliceCalib` — те же A40 и леджер (это P0, не лидерство); запрещённый список уточнён: параметр `TOOLING_M_LEAD` содержит `lead` и в калибровочный манифест не передаётся (§5).
- Стенд C1 по двум постановкам (спец. §6.4, §8.2 п.1 v0.24) — `c1_stand.csv` (§6).
- Источники колонок и read views Session, без которых модели не пишутся (§9).

## Краткий вывод
- [Предл.] Lab пишет `runs.csv` (свои колонки + параметры точки) и по одной таблице на файл; первая колонка — `run`. Lab склеивает CSV прогонов и **требует одинаковый набор файлов и заголовков у всех прогонов**: модель всегда создаёт все свои таблицы, «тяжёлые» в `detail = lite` остаются с одним заголовком.
- [Предл.] Время — индекс месяца `t` 0…383 (спец. §2). Годовые строки — `t mod 12 = 0`. Значение строки «месяца t» — состояние **после** месячного шага t (post), если не сказано «снимок t» (то, что читает compute t).
- [Предл.] `Fx6` пишется **целым raw** (`MetricFormat.Int(raw)`, не `MetricFormat.Fx6`). Пусто — «не наступило к 383»; `−1` — «уже на старте».
- [Предл.] `JetSlice` — 14 таблиц, `JetSliceCalib` — 8 (§4, §5). Запрет спец. §8.2 п.2 проверяют тест T10 (заголовок) и `exp0002.py --calib` по одному списку файлов и подстрок.
- [Предл.] A40 печатается каждым срабатыванием, с классом: ветка О — `PROCESSES` / `DEFECTS` (L-O0), ветка С — `CULTURE` / `COEXIST_MONEY` / `OTHER` (L-C0). `cancel_design` = строки `branch = O`.
- [Факт: `SliceSession` API, `SLICE_COMPOSITION.md` §38, ADR-0014 п.5] Публично Session отдаёт только хеш, тик, дату, `why_not` и `CurrentRequirement`; всё прочее в таблицах требует read views (§9). Без них этап 2 T10 не выполним без правки `src/Singularity.Session` или обхода ADR-0014.

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
| `layer/L_K`, `layer/L_C`, `layer/L_P` | абляции слоёв §6.1 — **в Session не реализованы** (OPEN 3) | 1…1000 |
| `old/L_K0`, `old/L_C0` | старые контроли, печать (так же не реализованы) | 1…400 |
| `channel/alpha0`, `ramp1`, `b0`, `eta0`, `kcatch0`, `sigma0`, `kdiff0`, `E0`, `ktr0`, `kqp0`, `R0` | каналы §6.2 (`CHANNELS`) | 1…200 |
| `source/FIX_K`, `FIX_E`, `FIX_A`; `FIX_ALL` | источники §6.3 | 1…400 |
| `grid/<kc>_<kd>_<hm>` ∈ {`lo`,`mid`,`hi`} | сетка P5 (×{0,5; 1; 2} к замороженному `k_catch`, `k_diff`, `h_max`) — нужны параметры-множители в Session (OPEN 4) | 1…100 |
| `start/PERT`, `start/SYM` | старт §6.5 | 1…400 |
| `prof/<USA>_<GBR>_<SUN>` | факторный план §6.6 (`PROFILE_CELL` = `9u + 3g + s`) | 1…40 |
| `lic/knows` | §6.7 | 1…400 |
| `arm/EFFORT_SHARE_x0`, `_x05`, `_x2`, `_x4`; `arm/FORCE_x1`, `arm/FORCE_q`; `arm/DERIV`; `arm/COMP_V010` | плечи §6.8 (`EFFORT_SHARE`, `COMP_V010` — в Session нет, OPEN 4) | 200 / 400 |
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
| `detail` | `full`/`lite` | — (только Lab) | `full` |
| `fv_budget` | число (индекс бюджета) | — (только `JetSlice`, спец. §5.5) | 24 медианных месячных бюджета закупок великих на t0 |
`SkipStartValidation` из манифеста не задаётся (только печать F32). Данные — `mods/core` через `GameData`; `data_hash` и `scenario_hash` — в `run.json` (Lab `Provenance`).

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
`t`, `actor`, `key_G`, `key_T`, `key_N` (int), `key_P` (Fx6), `fv` (Fx6, спец. §5.5), `fv_fleet` (Fx6, §6.10 печать «Σ по когортам»), `volume` (Fx6, печать), `budget` (Fx6), `eng_short_months` (int 0…12, P4 (b)), `kd_aero`, `kd_prop`, `kd_metal`, `kd_chem`, `kd_mech` (Fx6).

### 4.3 `whynot.csv` — годовой `why_not` (full)
Строка на звено для цели спец. §5.6 (`CurrentRequirement`); актор-год без цели строк не пишет.
`t`, `actor`, `target`, `rank` (≥ 1), `layer` (`KNOWLEDGE`/`CAPABILITY`/`PRODUCT`/`ECONOMY`), `cause` (имя `WhyNotCause` в UPPER_SNAKE: `ENGINEERS`, `TOOLING_M`, `TOOLING_POOL_EXHAUSTED`, `ENGINE_GEN_LIMIT`, …), `subject`, `eta` (int / пусто = `null`), `lever`, `lever_valid` (0/1), `closed_t` (int / пусто), **`cluster`** (у `TOOLING_POOL_EXHAUSTED` — кластер, иначе пусто), **`order`** (0…4, у прочих пусто), **`unmet`** (Fx6 `tool_unmet` звена; 0 у прочих).
`closed_t` — первый `t' > t`, когда в `why_not(actor, target)` нет звена с тем же (`layer`, `subject`); модель вызывает `WhyNot` каждый месяц для открытых звеньев (чистая функция снимка). `TOOLING_POOL_EXHAUSTED` и `TOOLING_M` для `closed_t` — одно звено (тот же `subject`).

### 4.4 `node_times.csv` (full; в `calib` — всегда)
Строка на (актор, узел): `actor`, `node`, `t_known`, `t_first`, `t_07`, `t_mastered` (int / −1 / пусто; у не-PRC последние три пусты), `lic` (0/1, `lic/knows`).

### 4.5 `gen_times.csv` (full; в `calib` — всегда)
`actor`, `gen` (1…4), `t_req`, `t_first_unit`, `t_fielded` (int / −1 / пусто), `cost_u100` (Fx6 / пусто).

### 4.6 `requirements.csv` (full)
`req`, `actor`, `gen`, `t`, `n_variants`, `fv_tech`, `fv_super` (Fx6), `chosen` (`TECH`/`SUPER`).

### 4.7 `events.csv` (всегда)
`t`, `kind`, `actor`, `other`, `value`. `kind`: `DIS` (перебой сплава; `value` — длительность), `LIC_OFFER`, `LIC_BUY`, `LIC_REFUSE_MONEY` (A43.3 (б)), `LIC_END` (A43.4; `value` — срок, мес.) — у `LIC_*` `other` = продавец, `value` у предложения и покупки — цена Fx6; `QP_START` (программа качества, `other` = `P1`/`P3` + причина звена QC), `TRAINING` (учебная серия P3, `other` = `design`).

### 4.8 `prc_year.csv` (full)
`t` (`t mod 12 = 0`), `actor`, `node` (все PRC), `known`, `admitted` (0/1), `yield`, `ucm` (Fx6), `licence_share` (Fx6, `LicenceShare` §6.11).

### 4.9 `a40.csv` — срабатывания A40 (всегда) [Предл.]
Строка на команду ИИ по A40: ветка О — `cancel_design(d)`, ветка С — `close_line(d)` с `idle_c ≥ T_ab`. Месяц `t` — месяц compute (снимок t), команда действует с t+1.
| Колонка | Тип | Смысл |
|---|---|---|
| `t`, `actor`, `design`, `g_p` | int, str, int, int | ключ |
| `profile` | int 1…3 | профиль актора |
| `preset` | str | `SUPERIOR`/`PRODUCIBLE`/`ADAPTIVE` проекта |
| `branch` | `O`/`C` | ветка |
| `cls` | str | О: `PROCESSES` (`gate_open`, условие доктрины выполнены, `admitted` = false), иначе `DEFECTS` (`PolicySystem.ClassifyGiveUp`); С: `CULTURE` / `COEXIST_MONEY` / `OTHER` (ниже) |
| `t_stage_o` | int | месяц входа в О |
| `t_launch` | int / пусто | месяц запуска серии (С) |
| `force` | 0/1 | серия запущена с `force` (С; у ИИ всегда 0) |
| `idle` | int | `idle_c` снимка (С) |
| `rep_before` | 0/1 | повтор `Requirement` по (актор, `g_p`) уже был |
Класс С (спец. A40 «Печать причины», v0.20): `CULTURE` — в каждом из `T_ab` месяцев до команды наименьший `yield` по `prc_set` = 0, или сплав = 0 при `yield` всех плавок (39, 40, 41) = 0; `COEXIST_MONEY` — линия `N` при двух линиях, в каждом из `T_ab` месяцев выпуск 0 при `left` после `W` = 0 и без `T_a`; иначе `OTHER`. **L-O0:** `DEFECTS` в main = 0; **L-C0:** `OTHER` в main = 0 — иначе EXP не запускается (спец. §6.4).
`cancel_design` по сериям = число строк `branch = O` (спец. A40 «§7 не меняется»).

### 4.10 `a40_c_months.csv` — месяцы до срабатывания ветки С (всегда)
Строка на (срабатывание С, k = 0…`T_ab`−1): `t_fire`, `actor`, `design`, `t` (= `t_fire − T_ab + 1 + k`), `min_yield` (Fx6), `argp` (узел наименьшего `yield`), `alloy_supply` (Fx6), `deficit` (0/1), `left` (Fx6, деньги закупок после содержания и `W`), `t_a` (Fx6).

### 4.11 `tooling_ledger.csv` — леджер A39 (full; помесячно, строка на актора)
Чистая функция снимка, печать Lab (post), состояния нет (спец. A39 «Учёт»).
`t`, `actor`, `rest` (Fx6, `rest_a`), `b_cap` (Fx6), `invest_p1`, `invest_p2` (Fx6, Σ вложений проходов 1 и 2), `taken_rest`, `taken_cap` (Fx6), `rest_unspent` (= `rest_a − taken_rest`), `t_a` (= `B_cap − taken_cap`), `rest_returned` (Fx6), `n_part` (ячеек в проходе 2), `tool_unmet_MET`, `tool_unmet_CHEM`, `tool_unmet_MECH`, `tool_unmet_AERO` (Fx6, Σ по site и измерениям), `pool_exh` (0/1, `Σ tool_unmet > 0`).
Инварианты T8 (анализ проверяет): `invest_p1 + invest_p2 ≤ rest + b_cap`, `taken_rest ≤ rest`, `t_a ≥ 0`.

### 4.12 `lines.csv` — линии ИИ, печать A41 F17b (full; помесячно, строка на линию в С по снимку t)
`t`, `actor`, `design`, `g_p`, `role` (`W`/`N`/`SOLE`), `y_min` (Fx6), `argp`, `rate` (Fx6, выпуск месяца), `unit_cost` (Fx6), `left_share` (Fx6, доля `left`, взятая линией), `idle`, `close` (0/1, `close_line` с t+1), `close_rule` (`YPROD`/`REPLACE`/`A40C`/`F17B`/пусто), `start` (0/1). Сосуществования, их длительность, причины конца и запуски с `g_p(X) ≤ g_p(W)` анализ собирает из этой таблицы (спец. A41 F17b «Печать»; сверка seed 1 — §11 T6e2 п.10).

### 4.13 `fleet_cmd.csv` — команды парка ИИ (всегда; §6.10)
`t`, `actor`, `kind` (`RETIRE`/`SELL`), `design`, `gen_d`, `gen_line` (`gen(L)`), `count` (Fx6), `price` (Fx6, у `SELL`), `g_front` (int, мировой фронт), `line_bind` (`LineBind`). Печать #146 (продажи «отстающими своего фронта») и E-gen считаются из неё.

### 4.14 `licences.csv` — лицензии (всегда; §6.11)
`t`, `seller`, `buyer`, `gen`, `profile` (покупателя), `price` (Fx6), `t_end` (int / пусто). Дублирует `events.csv` `LIC_BUY`/`LIC_END` в удобной для печати форме [Предл.; если T11 сочтёт лишним — убрать в v0.4].

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
- ни одна колонка ни одного CSV каталога (включая параметры в `runs.csv`) не содержит подстрок `lead`, `chg`, `fv`, `why`, `key_`, `volume` (без учёта регистра). Следствия: `fv_budget` и `TOOLING_M_LEAD` в калибровочный манифест не передаются (у `m_lead` — значение по умолчанию §8.1); `lines.csv`, `fleet_cmd.csv`, `licences.csv` в калибровочной сборке не пишутся (не нужны C2/C4 и контролям §6.1).
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
- Определения v0.2 (засчитанная смена, tenure, `small_lead`, P1–P5, исход, D1, D2, таблицы контролей) не меняются; спец. v0.24 §7 их не меняла.
- **A40 (печать, P0):** по сериям — число `cancel_design` (`branch = O`), по (актор, класс) — среднее на seed, доля seeds с ≥ 1 срабатыванием С; L-O0 и L-C0 — флаги P0 в отчёте; сверка с ожиданиями A40 (б) (объявляются заново до первого прогона main, спец. A40 v0.20).
- **A39:** доли `pool_exh` по актору; инварианты леджера §4.11 — нарушение = дефект (P0).
- **F17b:** из `lines.csv` — сосуществования по (актор, seed), длительность p50/p90/max, причины конца, класс `COEXIST_MONEY`.
- **Лицензии, парк** — печати §6.10–6.11 из `licences.csv`, `fleet_cmd.csv`, `events.csv`.

## 8. Манифесты
**`lab/manifests/EXP-0002.json`** (схема Lab 1; `model: "JetSlice"`, `ticks: 11688`, `threads: 1`, серии §2) и **`EXP-0002-det.json`** (`threads: [1, 4, 8]`, серия `det`). Поле `meta` (хешируется в `manifest_hash`): `slice_spec` (v0.24), `csv_format` (v0.3), `hypothesis`, `decision_rule`, `scenario_hash`, `control_tables_hash`, `calibration_manifest`, `analysis` (`script`, `script_sha256`, `config` с `p0_passed`), `rng` (спец. §9.1, `0x5EED`), `after_review`, `seed_offset`, `freeze` — как v0.2. Шаблон не замораживается: заморозку делает T12 (спец. §8.2 п.8).
**`lab/manifests/EXP-0002-calib.json`** (`model: "JetSliceCalib"`, серия `calib`, seeds 10001…10100): в `meta` — `scenario_hash`, сетки «К» по этапам **C1 → (C3+C4) → C2** (v0.23, #177) с порядком обхода и бюджетом (≤ 40 / 60 / 30), функция потерь (C1a, C1b, C1d, C1e; C3; C4,G2…G4; C2), журнал точек со значениями `d_k` (коммитится).
DONE-эксперимент записывает `core_commit`, `data_hash`, `manifest_hash`, seeds, threads, `golden_hash` main (sha256 строк «`seed golden_hash`\n» по возрастанию seed), `outcome`.

## 9. Источники колонок и read views Session (блокер этапа 2 T10)
[Факт: код] `SliceWorld` и адаптеры — `internal` в `Singularity.Session` (`InternalsVisibleTo` только `Singularity.Session.Tests`); `SliceSession` публично даёт `Tick`, `Date`, `LastHash`, `HashComputations`, `CommandLog`, `Outcomes`, `WhyNot`, `CurrentRequirement`, `Step*`, `Submit`, `Replay`. Lab по ADR-0014 п.5 и `SLICE_COMPOSITION.md` (строки 38, 150) не читает `Table` и не строит `TickRunner` мимо Session; read views «делают вместе с первым потребителем (T10 Lab)». `ILabModel` Lab требует `Table` + `Systems` и сам ведёт `TickRunner` — для Session нужен второй путь исполнения в `RunExecutor` (это Lab, правка в зоне T10).
[Предл.] Минимальный набор узких неизменяемых снимков, публикуемых после непустого тика (месячного шага), по таблицам:
| Read view (Session) | Поля | Таблицы |
|---|---|---|
| `ActorView` | профиль, `budget`, доли бюджета, `proc_budget`, `TechKey` (`KeyOf`), `FieldedGen`, `G_front`, `K_d` по дисциплинам, `eng_short` (КБ `granted/n`), `FV` (`MaxFleetValue` при `fv_budget`), `FV_fleet`, `volume` | summary, actor_year, actor_month |
| `NodeView` | `known(a, n)`, `yield`, `admitted`, `ucm`, `v`, `mastered`, `LicenceShare`, по лицензии ли узел | node_times, prc_year, prc_month |
| `DesignView` | стадия, `g_p`, `gen`, пресет, `stage_o_month`, `gate_open`, `D_hidden`, `admitted`, `idle_c`, `rate`, `unit_cost`, `prc_set`, `y_min`, `built_any`, `cancelled`, роль линии | gen_times, requirements, lines, a40 |
| `PolicyDecisionView` | команды ИИ месяца: `cancel_design` (с классом `ClassifyGiveUp` снимка), `close_line` (с правилом), `start_series`, `retire`/`sell`, `Requirement` (с `FV` вариантов), повторы, лицензии, программа качества, учебные серии | a40, requirements, fleet_cmd, licences, events |
| `ToolingLedgerView` | результат `ToolingPlan.Compute` месяца по актору (`Rest`, `CapBudget`, `Invest`, `Participates`, `Unmet`, `Total`, `RestReturned`, `TrainingBudget`) — тот же план, что видели Industry/Products | tooling_ledger, a40_c_months (`t_a`) |
| `CapabilityView` | `alloy_supply`, `deficit`, `left` закупок после содержания и `W` | a40_c_months |
Всё перечисленное уже есть в публичных методах систем Core (`IndustrySystem`, `ProductsSystem`, `PolicySystem`, `KnowledgeSystem`, `ToolingPlan`) — их читает `lab/runners/t6e/T6eHarness.cs` из тестовой сборки Session через `internal`-доступ и отражение (два приватных члена `PolicySystem`: `_prod`, `GiveUpAllowed`). Read views — тонкая обёртка без новой логики; решение о поверхности — systems-architect (ADR-0014 п.6: `test_api_version`, тест снимка поверхности).

## 10. Сверка с `exp0002.py` (этап 1 T10; доработка — T11)
- Совпадает: `runs.csv`, `summary.csv` (лишние `hash_*` анализ игнорирует), `actor_year.csv` (лишняя `fv_fleet`), `whynot.csv` (лишние `cluster`, `order`, `unmet`), `node_times`, `gen_times`, `requirements`, `events` (новые `kind` анализ не трактует), `prc_year` (лишняя `licence_share`), калибровочные `calib_summary`, `prc_month`, `actor_month`, списки запрета (`DEFAULTS.calib`).
- T11 добавляет: чтение `a40.csv`, `a40_c_months.csv`, `tooling_ledger.csv`, `lines.csv`, `fleet_cmd.csv`, `licences.csv` и печати §7; серии §2 v0.3 (`channel/E0` …, `arm/*`, `fleet/*`, `sale/*`, `lic/LIC_OFF`, `tool/*`, `line/*`); C1 по `c1_stand.csv` (§6) и функцию потерь T12; версии в docstring (спец. v0.24, формат v0.3).
- Конфликтов колонок нет: анализ v0.2 читает по именам и не требует отсутствия лишних колонок.

## 11. Открытые вопросы
1. **Read views Session (§9)** — без них `JetSlice`/`JetSliceCalib` не пишутся; решение и исполнитель — Orchestrator (systems-architect + developer, правка `src/Singularity.Session`).
2. CSV стендов C3, C5 и функция потерь — T12.
3. Абляции слоёв `L_K`, `L_C`, `L_P` и старые контроли в Session не реализованы — их серии не исполнимы до отдельной задачи (после таблиц контролей §6.1, которые сами требуют `JetSliceCalib`).
4. В `SliceRunConfig` нет множителей сетки P5 (`k_catch`, `k_diff`, `h_max` ×{0,5; 1; 2}), `EFFORT_SHARE` и `COMP_V010` — их параметры §3 не заведены.
5. Проверка «расписание `L_K` воспроизводит `m` ±3 мес. и `s` ±20%» (спец. §6.1) — T12.
