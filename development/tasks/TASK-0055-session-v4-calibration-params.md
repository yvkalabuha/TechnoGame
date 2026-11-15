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
