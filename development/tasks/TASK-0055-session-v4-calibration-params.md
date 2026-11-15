---
id: TASK-0055
title: "Session v4: параметры калибровки «К» и таблицы L_C/L_P в SliceRunConfig (блокер T12)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false
depends_on: [TASK-0054]
related: [HYP-0002, EXP-0002, TASK-0034]
approved_by: orchestrator
approved_at: 2026-11-15
approval_ref: "ADR-0014 п.5 (Session отдаёт то, что нужно Lab); TASK-0054 «Блокирует T12»; спека среза §8.2, §6.1"
created: 2026-11-15
---

# TASK-0055 — Session v4: параметры калибровки и таблицы L_C/L_P

## Цель
Сделать точки калибровочной сетки T12 исполнимыми: Session принимает параметры класса «К» и таблицы контролей L_C/L_P через `SliceRunConfig`, не меняя поведения при значениях по умолчанию.

## Описание
По TASK-0054 («Блокирует T12»):
1. `SliceRunConfig` получает параметры «К» спеки среза §8.2: делитель `k` для `CostByType` (`c_n`-множитель, #177), `ThetaStart`, `HMax`, `KappaQ10`, `Alpha`, `XHalfByTier`, `DeltaH`, `CM` — по умолчанию текущие значения §8.1. Найти, где каждое значение сейчас задаётся (данные `mods/core` или константы Core), и передать через конфигурацию без дублирования логики.
2. Таблицы L_C/L_P: `ImmutableArray<CapabilityCurve>` (кривые `Y_k`/`U_k`), `T_design`, `ρ` — для `IndustryMode.CurveLc` и `ProductsMode.CanonicalLp` (уже есть в Core или нет — проверить; если режимов нет в Core, остановиться и описать).
3. `TestApiVersion` → 4, закреплённый `session-surface.v4.txt`; колонки `run.*` в Lab — только значения не по умолчанию.
4. Lab (`SliceParameters`, `ControlTables`): приём параметров сетки и `LAYER = LC/LP` из файла `CONTROL_TABLES` (формат v0.5 r8 §3.1).
5. Тесты: значения по умолчанию дают golden main без изменений (L1/L2); каждый параметр «К» меняет траекторию; L_C/L_P-прогон по таблицам `t11-calib` проходит; отказ при неверной таблице.
6. Закрыть `[Fact]`-строки «slice: control tables», «slice: design capacity», «slice: licence track», «slice: switches» в `tests/Singularity.Lab.Tests/SliceModelTests.cs` (DEBT-0034, часть Lab).

## Правила
- Golden main и поведение по умолчанию не меняются. Если меняются — остановиться и описать.
- Правила кода — `.claude/rules/csharp-core.md`; детерминизм — ADR-0002.

## Критерии приёмки
- [ ] Все точки черновика `lab/manifests/EXP-0002-calib.json` исполнимы (малый прогон по одной точке каждого этапа).
- [ ] Полный `dotnet test` зелёный, golden без изменений; L1/L2.
- [ ] Второе ревью Codex — accept.

## REVIEW
- [ ] Code review (reviewer)
- [ ] Второе ревью (codex)
- [ ] Детерминизм (test-engineer)

## Результат
**Сделано (TASK-0055, developer; не закоммичено).** `TestApiVersion` = 4, поверхность закреплена `tests/Singularity.Session.Tests/Api/session-surface.v4.txt`; `JetSliceModelFactory.SurfaceVersion` = 4. Core не менялся: режимы `IndustryMode.CurveLc`, `ProductsMode.CanonicalLp`, `LpTDesign/LpRho` уже были; все «К» — поля записей параметров Core.

**Параметры `SliceRunConfig` (по умолчанию = §8.1, колонка `run.*` только не по умолчанию) и место подключения (`SliceWorld.ApplyCalibration`, до каналов и сетки P5):**
- `CostDivisor` (k) → `KnowledgeParams.CostByType / k` (`run.k.c_n_div`); `ThetaStart`, `HMax` → `KnowledgeParams` (`run.k.theta_start`, `run.k.h_max`; ось `GRID` HMax масштабирует уже откалиброванное значение);
- `KappaQ10` → `ProductsParams.KappaQ10` (`run.k.kappa_q10`); `Alpha`, `XHalfByTier`, `DeltaH`, `CM` → `IndustryParams` (`run.k.alpha`, `run.k.x_half.{3,4,5}`, `run.k.delta_h`, `run.k.c_m`; канал `ALPHA` по-прежнему обнуляет α);
- `CapabilityTables` (`CapabilityControlTables`: `Curves` ×3, `TDesign`, `Rho`) обязательны ровно при `Layer = Lc/Lp`: Lc → `IndustrySystem(CurveLc, curves)`, Lp → `ProductsSystem(CanonicalLp)` + `LpTDesign/LpRho` (`run.layer.capability` — хеш таблиц). Проверки: форма набора, диапазоны «К»; отказ при неверных таблицах/значениях.
- Lab (`SliceParameters`): ключи `K_DIV`, `THETA_START`, `H_MAX`, `KAPPA_Q10`, `ALPHA`, `X_SCALE` (множитель к X½ 200/800/2500, отношения сохраняются), `DELTA_H`, `C_M`; `LAYER = LC/LP` с `CONTROL_TABLES` (файл §3.1) → `CapabilityTables` в `JetSliceModelFactory`; `LC/LP` без файла и файл без LK/LC/LP — ошибка параметра.

**Файлы:** src/Singularity.Session/Slice/SliceRunConfig.cs, SliceWorld.cs, SliceSession.cs; src/Singularity.Lab/Models/Slice/SliceParameters.cs, JetSliceModel.cs, ControlTables.cs (комментарий), SliceChecks.cs; tests/Singularity.Session.Tests/CalibrationParamsTests.cs (новый), Api/ApiSurfaceTests.cs, Api/session-surface.v4.txt; tests/Singularity.Lab.Tests/SliceModelTests.cs (+5 `[Fact]`: licence track, control tables, design capacity, calibration points, switches — DEBT-0034, часть Lab).

**Проверено:** default (явные значения по умолчанию) = хеш main seed 1 мес. 149, схема и отпечаток колонок без изменений; каждый из 8 параметров меняет хеш и пишет `run.k.*`; Lc и Lp: L1 (два прогона) и L2 (threads 1/4, chunk 3) — один хеш каждый из 60 месяцев, отличаются от main; таблицы входят в хеш; отказы. Lab: «slice: calibration points» — по одной точке C1 (`X_SCALE`, `ALPHA`), C3+C4 фаз А и Б, C2 (`C_M`, `DELTA_H`) и слои LC/LP через `JetSliceCalib` на 60 мес. — все исполнимы, golden различны. Полный `dotnet test`: Architecture 97, Kernel 316 (1 skip), Core 1035, Lab 18, Session 327 + 2 пропущено — зелёный (устаревший тест отказа LC/LP заменён на «отказ без таблиц»).

**Не сделано / вне зоны:** документы (slice-csv-format §3, §3.1: «LC/LP отклоняются», ключи «К»; манифест `EXP-0002-calib.json` — сетки точек как `params`) — правка simulation-engineer/Orchestrator. Строки закрытия DEBT-0034 — Orchestrator.
- Codex r1 (medium): тест «К» сравнивал `LastHash`, куда входят колонки `run.*`; теперь сравнивается дайджест таблиц без `run.*` (`SimulationDigest`), для 8 параметров и Lc/Lp — траектория действительно меняется.
### simulation-engineer, 2026-11-15 — находки Codex (high ×2) в зоне simulation-engineer, TASK-0055
**(1) `c1-stand` без параметров «К» — исправлено.**
- `c1-stand --out DIR [--manifest M [--point P]]`. Параметры точки разбираются тем же `SliceParameters.Parse` (calibration = true), что и в `JetSliceCalib` (`Smoke/StandPoint.cs`).
- Значения «К» из `SliceRunConfig` кладутся в `IndustryParams` стенда: `ALPHA`, `X_SCALE` (→ `XHalfByTier`), `DELTA_H`, `C_M`. Так же делает `SliceWorld.ApplyCalibration`, но тот internal — его повтор в 10 строк, Session не трогал.
- `C1Stand.ReferenceLine` и `DevSite` берут эти `IndustryParams` (раньше было `new()`).
- Новая команда `c3-stand --manifest M [--point P] [--seeds N]`: статистика C3 точки этапа C3+C4 — `K_DIV`, `THETA_START`, `H_MAX` на seeds точки, медиана CV по 11 ★ и `d_C3` (`SmokeT4s.RunC3Point`, стенд `C3Stand` без изменений).

**(2) Сетки → исполняемые точки.**
- `lab/manifests/EXP-0002-calib.json` — 55 точек, `manifest_hash` `b11e9a66…`:
  - `base` — значения §8.1;
  - C3+C4 фаза А: 5 серий `C3C4-A/k=*` с `sweep {H_MAX, THETA_START}`, точки 1…45. Порядок файла = объявленный порядок обхода: k внешний, `H_MAX`, `THETA_START` 0,6/0,3/0,9;
  - C2: 3 серии `C2/c_M=*` с `sweep DELTA_H`, точки 46…54.
- Фазу Б (`κ`) и перенос замороженных значений между этапами заранее не перечислить. Правило записано в `meta.stage_protocol`: серии добавляются по правилу §8.2 п.4 до оценки, это ревизия манифеста с записью в `journal`.
- `lab/manifests/EXP-0002-c1.json` — этап C1, 25 точек (`X_SCALE` внешний × `ALPHA`), `manifest_hash` `8544290a…`. Запускается через `c1-stand --manifest`, затем `exp0002.py --c1` на каждую точку — `lab-cli.md`, раздел «Точки калибровки T12».

**Проверка: по одной точке на этап, из самих манифестов** (`lab/results/t12prep-0055`):
- C1, точка 0 (§8.1): `c1_stand.csv` побайтно равен стенду v0.27; `L_C1` = 0.
- C1, точка 7 (`X_SCALE` 0,75, `ALPHA` 0,02): C1a 11,5%, C1b 80,15% (`d` 0,005), C1d 2,1%, C1e 3,6%. Параметры доходят до стенда.
- C3+C4, точка 10 (k = 1,5): `run --point 10 --seed 10001` — ok (golden `333d7b99…`); `c3-stand --point 10 --seeds 10` — медиана CV 0,371, `d_C3` = 0.
- C3+C4, точка 1 (= §8.1): медиана CV 0,447, `d_C3` = 0. Траектория побайтно равна точке `base` (`node_times`, `gen_times`, `a40`), а golden другой (`ac21242a…` против `1027cc75…`): заданный ключ пишет `run.*`.
- C2, точка 46: `run --point 46 --seed 10001` — ok.

**Документы:**
- `slice-csv-format.md` r9: §3 — `LAYER` с `LC`/`LP`, ключи «К» с областями и значениями по умолчанию; §3.1 — снято «Session не принимает».
- `lab-cli.md`: раздел «Точки калибровки T12».

**Тесты:** Lab selftest 35/35, Architecture.Tests 97/97 (стенды помечены `SingleLayerStand`), Lab.Tests 18/18. Session.Tests не запускал: их правит developer. Python не менялся. zone_check: вне зоны только `CURRENT_STATE.md` (правка Orchestrator).
### simulation-engineer, 2026-11-15 — Codex r2 (high ×2), TASK-0055

**(1) Протокол заморозки — этапы разведены по своим манифестам** (из двух вариантов этот проще).
- `EXP-0002-c3c4.json`: фаза А, точки 0…44 (`manifest_hash` `4289e0cf…`). Фаза Б описана в `meta.phase_B`: серии `C3C4-B/rank=r` со `sweep KAPPA_Q10`.
- `EXP-0002-c2.json`: точки 0…8 (`a37d9a02…`).
- `EXP-0002-c1.json`: без изменений (`8544290a…`).
- `EXP-0002-calib.json`: только базовая точка §8.1 и объявление калибровки в `meta` (`680676e8…`); в `meta.stage_manifests` и `meta.stage_protocol` — ссылки на манифесты этапов и правило заморозки.
- Правило: выбранные значения этапа пишутся в общий `params` только более поздних манифестов (C1 → c3c4, c2; C3+C4 → c2) и `EXP-0002-calib.json` (прогон таблиц §8.2 п.8). Каждый манифест перебирает только свои ключи, поэтому замороженный ключ не может стать одновременно параметром и осью (`Manifest.ReadPoints`).
- [Факт] Копии с «замороженными» ключами разбираются:
  - c2 + ALPHA, X_SCALE, K_DIV, THETA_START, H_MAX, KAPPA_Q10 — 9 точек, прогон точки 4 seed 10001 ok;
  - c3c4 + ALPHA, X_SCALE + серия фазы Б — 49 точек.
- Проверка `SliceChecks` «slice: stage manifests stay valid after freezing…» замораживает ключи в настоящих манифестах этапов и разбирает их заново.

**(2) Каталог одной точки на 100 seeds и разбор одной точки.**
- Lab: `matrix --manifest M --point P [--seeds N]` (`Manifest.Select`). Индекс точки, `manifest_hash` и run id остаются как у полного манифеста; выбор записывается в `matrix.json.selection`.
- `exp0002.py --calib DIR --point P` берёт только прогоны точки P (колонка `point` в `runs.csv`); строки других точек в `a40`/леджере пропускаются, а не считаются ошибкой. Значит, годится и общий каталог `matrix`. Несуществующая точка → ошибка ввода.
- Документировано: `lab-cli.md`, раздел «Точки калибровки T12»; формат r9.
- [Факт: `lab/results/t12prep-0055r2`]
  - `matrix --manifest EXP-0002-c2.json --point 3 --seeds 4` → 4 прогона, `selection` «point=3 seeds=4»; `--calib --point 3` → seeds 4, `d_C2` 1, `d_order` 1, C4 G4 0,75.
  - Общий каталог (`--seeds 3`, 27 прогонов): `--point 3` и `--point 0` дают по 3 seeds и разные `control_tables_hash` (`d2791124…` / `3bba2592…`).
  - golden точки 3 в обоих каталогах совпадают.

**Тесты:** Python `CalibPoint`: фильтр точки, чужие строки, ошибка для несуществующей точки. Новая проверка `SliceChecks` нашла дефект `matrix --point`: ширина run id зависела от числа отобранных точек (`p3` вместо `p03` у 45 точек). Исправлено — `Manifest.FullPointCount`, `RunExecutor.MakeRunId` берёт ширину по полному манифесту; `matrix --manifest EXP-0002-c3c4.json --point 3 --seeds 2` → `p03-s10001-t1`, `p03-s10002-t1`, `--calib --point 3` ok. Lab selftest 36/36, Lab.Tests 18/18, Architecture 97/97, Python `test_exp0002` + `--selftest` 40/40 — зелёные. [Fact]-строка новой проверки в `tests/Singularity.Lab.Tests` — вне зоны (developer).
