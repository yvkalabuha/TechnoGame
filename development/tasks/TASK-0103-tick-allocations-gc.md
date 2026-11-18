---
id: TASK-0103
title: "Tick allocations and GC: 1.1 GB per month, gen2 collections (EXP-0007 P8)"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: simulation-engineer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [EXP-0007, TASK-0102]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "EXP-0007 P8 (light)"
created: 2026-11-18
---

# TASK-0103 — Tick allocations and GC: 1.1 GB per month, gen2 collections (EXP-0007 P8)

## Goal
EXP-0007 P8 (high): the tick allocates 1.1 GB per month against a 1 MB limit. There are 243–288 gen2 collections per window against a limit of 0. GC pauses take about 25 % of the time. Peak memory is 1.96 GB. Find and remove the sources before the re-registered series (TASK-0102).

## Description
1. simulation-engineer profiles allocations by system and method on point A (t8, 1800 months). Use dotnet-counters/dotnet-trace or the existing Lab sampler. Write the report into this task.
2. developer fixes the hot spots inside approved architecture (ADR-0017 columns, ADR-0003 rules). Reviewer checks every diff.

## Acceptance criteria
- [ ] The allocation report by source is in this task.
- [ ] Steady-state allocations per month are at or below the P8 limit, or the gap is explained with a follow-up item.
- [ ] Golden hashes are unchanged; `dotnet test`, `lab selftest` pass.

## Related
EXP-0007 (Results, P8), ADR-0016, ADR-0017, DEBT-0045.

## Отчёт профиля аллокаций (шаг 1, simulation-engineer, 2026-11-18)

### Краткий вывод
- [Факт] 76 % месячных аллокаций (≈ 806 из 1054 МиБ) — два массива `Fx6[]` по 1 041 600 Б в каждом вызове `EffortAllocator.Allocate`, размером `SiteCount × ProcessCount` (200 × 651) при том, что актор владеет одним сайтом. Это LOH; ~402 вызова в месяц. Отсюда же gen2: 107 из 125 сборок gen2 в трассе — `AllocLarge`.
- [Факт] Тот же расчёт делается дважды на одном снимке: в `IndustrySystem.Compute` и в `ViewBuilder.Inputs` (вид входов месяца). Остальное — `ToolingPlan.Compute` (8 %) и снимок видов `ViewBuilder.Snapshot` (16 %, два снимка в месяц).
- [Предп.] После исправлений F1–F4 останется ≈ 85–100 МиБ/мес. — это `NodeView` снимка (400 тыс. объектов). Довести до порога P8 (≤ 1 МБ) без изменения состава снимка нельзя: нужен follow-up по формату снимка видов (ниже, F6).

### Метод
- Ветка `main` @ `a722428`, Release (повтор `alloc` t8, 4 мес. на `87b8257` после слияния TASK-0098 — те же 1055 МиБ/мес. и те же пробы), точка A: 200 акторов, 2000 узлов, `cross`, seed 1. Меряются байты и счётчики, не время: машина занята другими агентами.
- `dotnet-trace collect --profile gc-verbose`: события `GCAllocationTick` (выборка ~100 КБ, со стеком) и `GCStart` (поколение, причина). Разбор — одноразовый парсер на TraceEvent в scratchpad, не коммитится. Стек относится к корзине по самому внутреннему кадру: система тика, `ViewBuilder.Inputs` / `Snapshot` / `Step`, команды, построение мира.
- Новый режим Lab `bench --mode alloc` (`src/Singularity.Lab/Bench/AllocProfile.cs`): точные байты `GC.GetTotalAllocatedBytes(true)` и gen0/1/2 по месяцам месячного прогона (команды как в П2). После последнего месяца — точные байты отдельных чистых чтений. Хеш до и после проб совпал.
- Сырые данные: `lab/results/TASK-0103/` (`alloc_t1*.json|log`, `alloc_t8.json`, `alloc12_report.txt`, `t1_report.txt`, `*.nettrace`), не коммитятся.
- Сверка с EXP-0007: `process` t1, 2+4 мес. → 1 106 285 920 Б/мес. (monthly) и 1 106 329 200 (daily), как в П8 [Факт: `base_t1.json`].

### Раскладка (точка A)
200 акторов, 200 сайтов (по одному на актора), 651 процесс, 4 кластера, 2000 узлов.

### Прогрев и установившийся режим [Факт: `alloc_t1.json`, 30 мес.]
| Месяц | МиБ | gen2 (t1) |
|---|---|---|
| 0 | 977,6 | 9 |
| 1 | 1122,1 (пик) | 10 |
| 2–29 | 1054,0…1055,0; месяцы 12 и 24 — 1058 (годовые `Requirement` / `MaxFleetValue`, +4 МиБ) | 7–12 |

- Утечки нет: объём плоский с месяца 2. Это оборот (churn), а не рост.
- gen2 в месяц: t1 ≈ 9–10, t8 ≈ 2 (`alloc_t8.json`). Это совпадает с EXP-0007: B 749–924, A 243–288 за 120 мес.
- Дневные тики почти не аллоцируют. Дневной прогон − месячный = 173 КБ за 4 мес., то есть ≈ 43 КБ/мес. [Факт: `base_t1.json`]. Весь объём — в месячном тике и в видах месяца.

### Источники в установившемся месяце (МиБ/мес., из 1054)
Точные числа — пробы `alloc`. Доли подтверждены трассой (`alloc12_report.txt`: 12 мес. + пробы; корзина Industry 5302 МиБ / 12 = 442 = проба).

| # | Источник (система → метод) | Вызовов/мес. | Байт/вызов | МиБ/мес. | Доля | Куча |
|---|---|---|---|---|---|---|
| 1 | `IndustrySystem.Compute` → `AllocateEffort` → `EffortAllocator.Allocate`: `eu`, `result` = `new Fx6[SiteCount·n]` | 200 (по сайту) | 2055,6 КБ | 401,5 | 38,1 % | LOH |
| 2 | `ViewBuilder.Inputs` → `ActorInputs` → `Industry.RestActor` → тот же `Allocate` (повтор п. 1 на том же снимке) | 200 | 2055,6 КБ | 401,5 | 38,1 % | LOH |
| 3 | `ViewBuilder.Snapshot` ×2 в месяц (тик применения команд + месячный тик): `Nodes` → `NodeView` (record class) 200×2000, `NodeView[]`-builder, `Process` → `Industry.Profile` (`ProfileItem[]` ×2: `IndustrySystem.Profile` + `IndustryMath.Profile` `ToArray`), `ProcessView` | 2 | 82,6 МиБ | ≈ 165 | 15,7 % | SOH |
| 4 | `ToolingPlan.Compute` (`IndustryInputs.Plan`): 8 массивов `SiteCount·4·3` (19 КБ), `yields[s] = new ProcessYield[n]`, `mCap`, `pStar`, `List` | 400 (Compute 200 + Inputs 200) | 204 КБ | ≈ 80 | 7,6 % | SOH |
| 5 | `ProductsSystem.Compute` → `ProductsInputsAdapter.TrainingBudget` → `Plan(RestActor)` (п. 1 + п. 4 ещё раз для акторов с серией обучения) | ≈ 2–3 | ≈ 2,3 МиБ | ≈ 6 | 0,6 % | LOH |
| 6 | `PolicySystem.Compute`: `FleetBook..ctor` `Fx6[]`, `PolicyProductsAdapter.GFront` `int[]`, `DecideFleet` `int[]` | — | — | ≈ 3,5 | 0,3 % | SOH |
| 7 | Команды харнесса: `CommandState.Apply`/`Set` `Int64[]`, строки `_outcomes` (200 `SetBudgetShares`) | 200 | ≈ 9 КБ | ≈ 2 | 0,2 % | SOH |
| | Сумма | | | ≈ 1060 | ≈ 100 % | |

- `EffortAllocator.Allocate` в целом: ≈ 402 вызова × 2,0 МиБ ≈ 806 МиБ (76 %). Из 1 041 600 Б массива `eu` используется 651 × 8 = 5208 Б (0,5 %): остальные 199 сайтов — нули.
- `SplitBySite` дополнительно выделяет `new Fx6[SiteCount]` на каждый процесс с `e > 0` (SOH, мелочь).

### Причина gen2 [Факт: трасса `t1.nettrace`, `GCStart`]
- 125 сборок gen2. Из них 107 — `AllocLarge` (BackgroundGC), 17 — `AllocSmall` (BackgroundGC), 1 — `AllocSmall` NonConcurrent.
- Причина — бюджет LOH: ≈ 800 объектов LOH по 1 МБ в месяц (п. 1, 2, 5); каждый запускает учёт LOH, а LOH собирается только в gen2.
- Других LOH-аллокаций в тике нет. `KnowledgeNodeStep[]`, `ProcessYield[]` в LOH и строки — только при создании сессии и построении мира.
- [Предп.] Пиковый working set A (1,96 ГБ при t8 против 0,41–0,46 ГБ при t1) даёт параллельный `Parallel.For` в `Inputs` и compute: до 8 потоков держат по 2 МБ LOH одновременно, фоновый GC отстаёт, LOH фрагментируется. Уйдёт вместе с п. 1–2.

### План исправлений для developer (по выигрышу; результаты не меняются, golden hashes те же)
Общие условия:
- Арифметика и порядок обхода (`l.Order`, `SitesOf`) не меняются; меняется только место хранения промежуточных значений.
- Скретч — per-chunk буфер системы (правило csharp-core «per-chunk buffers») или `[ThreadStatic]` для `ViewBuilder.Inputs`, который идёт через `Parallel.For` по акторам. Перед использованием буфер очищается.
- Проверка каждого шага: `dotnet test`, `lab selftest`, `bench --mode validity` (хеш t1/4/8/16 × c1/8/32 равен), `bench --mode alloc` до и после.

1. **F1 — `EffortAllocator.Allocate`, локальная раскладка по сайтам актора (−≈ 800 МиБ, −76 %, вся LOH → gen2 `AllocLarge` ≈ 0).**
   - `eu` и `result` — размером `SitesOf(actor).Length × n` с локальным индексом сайта `k`, а не `SiteCount × n`.
   - `EffortAllocation.E` → локальная раскладка (или метод `E(site, r)`). Единственный потребитель в Core — `IndustrySystem.AllocateEffort`: индекс `s * _n + r` → `k * _n + r`.
   - `SplitBySite.share` — тоже локальный.
   - Уже это даёт ≈ 10 КБ SOH на вызов (≈ 4 МиБ/мес.).
   - Второй шаг: `euActor`, `e`, `weight`, `share`, `cand`/`rest` (две `List<int>` на каждый проход) и `result` — из скретча вызывающего (перегрузка `Allocate(..., EffortScratch)`) → ≈ 0.
2. **F2 — снимок видов `ViewBuilder.Snapshot` (−≈ 67 МиБ сразу, −≈ 165 вместе с F6).**
   - (а) `Process` → `Industry.Profile(a, [one item])`: сделать неаллоцирующий путь для одного процесса (`ProfileSingle` без `ProfileItem[]` и без `ToArray`+`Sort` в `IndustryMath.Profile`; для одного элемента сортировка не нужна) → −≈ 67 МиБ.
   - (б) `Nodes`: поиск `w.IndustryLayout.RowOf(number)` и `w.Graph.RowOf` на каждый узел — CPU, не байты; `bool[] granted` на актора — 2 КБ, вынести в скретч.
   - Остаток (`NodeView`/`ProcessView`, ≈ 85–100 МиБ) — F6.
3. **F3 — `ToolingPlan.Compute`, локальная раскладка + скретч (−≈ 80 МиБ, −7,6 %).**
   - Массивы `SiteCount·clusters·3` и `yields[SiteCount][]` → локальные по `SitesOf(actor)`.
   - `ToolingPlanResult.Applied/Unmet/Participates(site, …)` переводит глобальный сайт в локальный индекс.
   - `room`, `mDec`, `cost`, `mCap`, `pStar`, `yields`, `sites`, `cells`, `wants` — скретч.
   - Результат (`invest`, `notional`, `goals`, `unmet`, `takes`) — на 1 сайт: 12 элементов вместо 2400.
4. **F4 — один расчёт `Allocate` + `Plan` на (актор, месяц) вместо 2–3 (−50 % остатка п. 1, 3, 5; главное — CPU).**
   - `ViewBuilder.Inputs` считает план на том же снимке, что compute месяца (комментарий в `SliceSession.StepOne`: «inputs of the step as its compute will see them»).
   - [Предл.] Мемо в `IndustryInputsAdapter` с ключом (actor, month, `Table.Version`; ADR-0017 п. 6): заполняется в `Inputs` до `RunTick`, в compute только читается.
   - Слоты по актору, без общей записи в compute. Чистые функции → тот же результат и хеш.
   - Риск: если мемо промахнётся (иная версия), compute обязан пересчитать, а не взять устаревшее. Нужен тест «мемо выкл. = мемо вкл.» по хешу.
   - После F1/F3 выигрыш по байтам мал (≈ 5 МиБ), поэтому F4 — четвёртым. Если узкое место — CPU, его можно поднять.
5. **F5 — мелочь ≈ 6 МиБ (п. 5–7):** `FleetBook` / `GFront` / `DecideFleet` — скретч или переиспользуемые буферы системы. `CommandState.Apply` `Int64[]` — запись в колонку без временного массива. Строки `_outcomes` — вне тика (харнесс).
6. **F6 — follow-up, вне светлой задачи: формат снимка видов.**
   - Полный `SliceView` с `NodeView` 200 × 2000 на каждый непустой тик с видом — сейчас ≈ 49 МиБ на снимок (`NodeView` 34 + `NodeView[]` 14), на struct-ах [Предп.] ≈ 15–20 МиБ — всё равно на порядки выше 1 МБ. Состав снимка задают ADR-0014 / ADR-0016 §6 («снимок видов включён» в порог 1 МБ).
   - Решения уровня architect [Предл.]:
     - (а) узлы — лениво или только для наблюдаемых акторов;
     - (б) переиспользуемые буферы видов с протоколом владения (double-buffer);
     - (в) дельта изменившихся узлов;
     - (г) пересмотреть порог P8 для снимка.
   - [Предп.] Без F6 итог после F1–F5 ≈ 85–100 МиБ/мес., gen2 ≈ 0 (LOH в тике не останется; проверить `alloc`).

### Ожидаемый итог (точка A, МиБ/мес.) [Предп.]
| После | МиБ/мес. | LOH в тике |
|---|---|---|
| сейчас | 1054 | ≈ 806 |
| F1 | ≈ 255 | 0 |
| + F2(а) | ≈ 190 | 0 |
| + F3 | ≈ 110 | 0 |
| + F4, F5 | ≈ 95 | 0 |
| + F6 | ≤ 1 (цель P8) | 0 |

## Review of step 1 (reviewer, commit 1c6904c): APPROVE with minors, fold into step 2
- MED: the `--mode alloc` JSON lacks `BenchEnv.Write` provenance/conditions, `WriteParams` and `WriteWorld` (AllocProfile.cs:64-68); add them (RepoDir from BenchCommand).
- LOW: re-hash after probes with `StateHash.ComputeHex(world.Table, SliceWorld.SchemaVersion)` instead of lazy `LastHash`; lab-cli.md:94 lacks the 6th probe (Step + Snapshot stepped); add a smoke test for `--mode alloc` in Singularity.Lab.Tests.
- Before F1: verify `EffortAllocation.E` has no Core consumer other than AllocateEffort.
- `ViewBuilder.Process` Profile(a, …): not a bug, a site is an actor by design (SliceWorld.cs:17, A27). F1's local layout must still work for `SitesOf(actor).Length > 1`; add a guard `SiteOwner[s] == s` in SliceWorld.

## Step 2 scope (Orchestrator, 2026-11-18)
- developer: F1, F3, F4, F5 from the plan. F2 is replaced by the snapshot design R1 + R2 in `docs/architecture/notes/views-snapshot-allocations.md`: compact per-actor blocks; `NodeView` becomes a readonly record struct inside arrays < 85 000 B; reuse of unchanged blocks via a Kernel column version counter (reviewer required for Kernel).
- The `--mode alloc` review fixes move to TASK-0106.
- Starts after TASK-0099 is merged (Products.TrainingBudget calls EffortAllocator).

## Step 2 results (developer, branch worktree-agent-ac55e57321ad2a374: 9ec04c9, 62e77f4, e6f208a)
Point A, t1, steady state (`lab bench --mode alloc`, raw data lab/results/TASK-0103s2/):

| Step | MiB/month | LOH in tick | gen2 |
|---|---|---|---|
| before | 1055.0 | ~806 | 8–9 per month |
| F1+F3+F4 | 171.6 | 0 | 1 per month |
| R1+R2 | 14.0 | 0 | 0–1 per month |
| F5 | 9.8 | 0 | 0 |

- t8, 14 months: 9.8–10.5 MiB/month, gen2 3 times in 14 months.
- Remaining: ~5 MiB is `ProcessView[651]` per actor in the fresh monthly snapshot; ~3 MiB is other view parts, Inputs and Step. The 1 MB limit is met only with the ADR-0023 split P8 or option A2.
- Golden hashes unchanged. Tests: Core 1005, Session 384 (2 skip), Lab 132, Kernel 336 (1 skip), Architecture 141. `lab selftest` 49/49; validity 3 seeds valid.
- Kernel: `Column.Version` (+1 in SwapIfOpen and Load); `SliceWorld` throws if `SiteOwner[s] != s`; API surface v11.
