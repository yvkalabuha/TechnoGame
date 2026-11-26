---
id: TASK-0133
title: "G5/G6 data in mods on the train-2 branch and calibration probe (O-038)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M05
agent: developer
category: data
priority: high
needs_owner: false
depends_on: [TASK-0128, TASK-0129, TASK-0131]
related: [TASK-0128, TASK-0129, TASK-0131]
approved_by: orchestrator
approved_at: 2026-11-23
approval_ref: "O-038 (data on a branch, probe before owner approval)"
review_ref: "reviewer: APPROVE train 2 (4d37a33); codex: waived #432 after 3rd return (f9ae0774), remainder DEBT-0091; landed 88fa41b"
created: 2026-11-23
tags: [task, data, simulation-lab, done]
---

# TASK-0133 — Данные G5/G6 в mods на ветке поезда 2 и пробный прогон

## Цель
Порядок O-038: данные G5/G6 пишутся на ветке, пробный прогон с заранее записанными порогами показывает, убирает ли контент простой 1978–2030, и только потом владелец утверждает контент (TASK-0128). На `main` данные не попадают до утверждения.

## Описание
1. developer: ветка от `main` с влитой веткой TASK-0129 (старение парка); узлы 62–81, альтернативы, `af_class` 5–6, `pu_ranks`, якоря — строго по `docs/research/slice-g5-g6-content.md` ред. 2; дисциплины существующие; `max_designs` 12. Отклонения — в «Разногласия».
2. simulation-engineer: пре-регистрация (`lab/` манифест) и пробный прогон 1945–2030, seeds 1–10 (сначала пилот на 1 seed по правилу 15 минут). Пороги O-038: последний новый узел лидера ≥ 2005; G5 в строю у ≥ 2 стран к 2030; узкая метрика простоя 1978–2030 < 0,8; годы с G = 0 по странам; доля провалов проверки «в строю» 1990–2030; L1/L2 на 1020 мес. (DEBT-0079). Калибровка `COST_MULT_G5/G6` под целевые годы открытия ред. 2 — не более 3 точек на множитель.
3. Итог — раздел в `docs/research/slice-g5-g6-content.md` или отдельная записка `docs/simulation/g5-g6-probe.md` с цифрами по порогам.

## Критерии приёмки
- [ ] Данные загружаются, `verify.py` зелёный на ветке (golden перезаписаны один раз с объяснением).
- [ ] Пробный прогон: таблица по каждому порогу O-038, `VALIDITY: OK`.
- [ ] Ревьюер APPROVE на данные; сверка данных с документом ред. 2 (правило §4 оркестрации).

## Связанные документы
`docs/research/slice-g5-g6-content.md`, `docs/reviews/slice-g5-g6-content-*.md`, `docs/game-design/notes/fleet-without-replacement.md`.


## Разногласия (шаг 1, данные; developer)
Отклонения данных от `docs/research/slice-g5-g6-content.md` ред. 2: каждое вызвано правилом валидатора Core, который менять нельзя в этой задаче. Решение по каждому нужно от Orchestrator / research-scientist.
1. **`informs`: оставлено 6 рёбер из ~58 документа.** Валидатор (`NodeTable.Validation.cs`) допускает ребро только FND → ENG/PRC той же дисциплины. Документ ред. 2 даёт рёбра от PRN/ENG/CMP/PRC, к APP/CMP/FND и между разными дисциплинами. Записаны: 62→67 0,6; 62→78 0,6; 62→70 0,3; 63→70 0,3; transonic_aerodynamics→67 0,3; transonic_aerodynamics→78 0,3 (62→68 и 62→71 отброшены: другая дисциплина или тип). Остальные не записаны. Следствие: новые узлы дороже, чем в расчёте документа (меньше скидка `Σ w_i`) — это влияет на калибровку. Нужно: либо расширить правило валидатора (Core, D1), либо принять урезанный набор.
2. **Узел 74 (tier 4): `quality_control` 4,5 вместо 5.** Валидатор требует `capability_req` в [tier−1; tier+0,5] = [3; 4,5].
3. **Ранги планера 6, 7, 8 вместо 6, 6, 7.** Core запрещает повтор ранга в слоте («duplicate rank 6 in slot airframe»). `af_fbw` ранг 6, `af_composite` ранг 7, `af_lo_composite` ранг 8; `platform_min_rank` G5 = 6 (оба пути проходят), G6 = 8 (вместо 7). Смысл «вход в G6 только через малозаметный композит» сохранён.
4. **`perf_mult` af_fbw 1,75 < af_mach2 1,8** при более высоком ранге — как в документе; отмечено, не менял.
5. **Источник якорей** — поле `source` в формате `G56-S-A…N` (коды источников документа), в G1–G4 там коды `S32`, `RULE-MID`.
6. **`c_n` G5/G6:** в данных поля нет; Core (TASK-0131) держит умолчание 6,4/10,2 (`KnowledgeParams`), документ предлагает стартовые 7,0/12,0 через `COST_MULT_G5/G6` манифеста. Умолчание не менял (код); 7,0/12,0 задаются манифестом зонда.
7. **`proc_scale`, `h_first`, … для классов 5, 6 и `pu_ranks` 5–7** — ровно стартовые числа документа (1,33 / 1,33 / 2,14), не расчёт по R-PS2m.
8. **Названия и тексты локализации** (en, ru, 20 узлов + 8 альтернатив, `_sources.tsv`) в документе не даны: написаны мной, переводы на ru без вычитки носителем; `.short` ≤ 12 знаков.

## Результат
**Ветка:** worktree-agent-aee00c98dea970eae (от main + влитая ветка TASK-0129, SchemaVersion 13; конфликтов при слиянии не было).

**Сделано.** `mods/core/nodes.yaml`: узлы 62–81 и 6 рёбер `informs`; `mods/core/product_classes.yaml`: слот engine +3 (eng_g5, eng_g6, eng_g6_adaptive), hot_section +2 (hot_sc_sc, hot_sc_cmc), airframe +3 (af_fbw, af_composite, af_lo_composite), 4 процесса, `pu_ranks` 5–7, `af_classes` 5–6, `generations` 5–6; локаль en/ru и `_sources.tsv`. `max_designs` 12 не менялся; `scenario_1945.yaml` не менялся (хэш сценария прежний).

**Узлы 62–81** (id без префикса; поколение; дисциплина; hazard; requires; якорь):
| № | id | G | дисц. | hazard | requires | год |
|---|---|---|---|---|---|---|
| 62 | electronic_flight_control_theory | 5 | aero | hz | transonic_aerodynamics | 1972 |
| 63 | edge_wave_diffraction | 5 | aero | hz | compressible_flow | 1962 |
| 64 | single_crystal_growth | 5 | metal | hz | ds_casting | 1974 |
| 65 | fibre_composite_laminate_design | 5 | chem | hz | aluminium_alloy_sheet | 1970 |
| 66 | low_observable_shaping | 6 | aero | hz | edge_wave_diffraction | 1975 |
| 67 | fly_by_wire_relaxed_stability | 5 | aero | none | electronic_flight_control_theory + all_moving_tail | 1974 |
| 68 | turbofan_g5 | 5 | prop | key | turbofan_g4 + electronic_flight_control_theory | 1981 |
| 69 | composite_airframe | 5 | aero | none | fibre_composite_laminate_design + mach2_airframe_g4 | 1978 |
| 70 | low_observable_airframe | 6 | aero | none | low_observable_shaping + fly_by_wire_relaxed_stability | 1977 |
| 71 | thrust_vectoring_supercruise_engine | 6 | prop | key | turbofan_g5 + turbine_blade_single_crystal | 1990 |
| 72 | turbine_blade_single_crystal | 5 | prop | key | single_crystal_growth + turbine_blade_cooled | 1982 |
| 73 | single_crystal_casting | 5 | metal | key | single_crystal_growth + ds_casting | 1982 |
| 74 | composite_layup_cure | 5 | aero | none | fibre_composite_laminate_design | 1978 |
| 75 | low_observable_finishing | 6 | chem | none | low_observable_shaping | 1981 |
| 76 | jet_fighter_g5 | 5 | aero | none | fly_by_wire_relaxed_stability + (composite_airframe | turbofan_g5) | 1984 |
| 77 | jet_fighter_g6 | 6 | aero | none | thrust_vectoring_supercruise_engine + low_observable_airframe + aesa_sensor_fusion | 2005 |
| 78 | aesa_sensor_fusion | 6 | aero | none | electronic_flight_control_theory | 1997 |
| 79 | ceramic_matrix_composite | 6 | chem | key | turbine_blade_single_crystal + ceramic_shell_moulding | 2016 |
| 80 | cmc_fabrication | 6 | chem | none | ceramic_matrix_composite + ceramic_shell_moulding | 2016 |
| 81 | adaptive_cycle_engine | 6 | prop | key | thrust_vectoring_supercruise_engine + ceramic_matrix_composite | 2021 |

**Проверка порядка якорей:** ни одна предпосылка узла 62–81 не имеет якоря позже узла (тест `SixGenerationDataTests.NewNodes_AnchorsNeverPrecedeTheirPrerequisites`, зелёный). Для узлов 1–61 правило не применяется (у Brayton 1945 «до», предпосылка gas_turbine 1937).

**Тесты данных.** `CoreNodesTests.Core_Has81NodesWithTypeCounts` (81 узел, типы 10/11/20/10/24/6); `SixGenerationDataTests` (af_class 6 есть, 6 поколений, анкеры); `G5G6DataTests` (мир строится, широкая маска лицензий включена, на старых данных узкая).

**Данные 61 узла как фикстура.** `tests/Singularity.Session.Tests/Fixtures/core-g4/` — копия mods/core до задачи; `SliceTestData.LegacyData/CreateLegacy`. На ней идут: тесты «тождественно сборке до…» (Train2Identity, HorizonTests.OldColumns, GoldenProjectionAgainstMain ×3, T6dOff, ToolingUnmet, FleetAblation) — иначе они не могут выполняться при новых данных; контрольные слои L_K/L_K0/L_P (по O-038 отклоняют gen > 4); тесты механизмов с настроенными месяцами (Observables walks, DailyTickProbe, WhyNot, SlotReuseL2). Это отход от «данные + тесты данных»: объём правок тестов ~40 файлов.

**Перезаписанные golden (один раз, комментарий «Re-recorded in TASK-0133 (train 2: G5/G6 data + fleet ageing)»)**; причина у всех одна: узлы 62–81 и альтернативы G5/G6 меняют траекторию и размеры колонок (SchemaVersion остаётся 13):
- `DataHashTests.CoreDataHash_IsFrozen` → d9dce0a2…e796 (хэш данных ядра); хэш сценария не менялся.
- `SeriesSwitchTests/CalibrationParamsTests/ViewTests` MainSeed1Month149 → 0dc571a6bffaef40e6922578270f39f48f7917e48a862c38af918ce5cceb78a1.
- `F17bTests` AdmitGolden ×5, YProdGolden ×5; `LinkedRunTests.Golden60` → c3956dab…385f; `ProductAiSessionTests.Golden150` → 0f682e21…2e21; `TwoWorldsOnOnePoolTests.Pinned` (6 seeds); `SchemaTests.PinnedFingerprint` → 7f54f88a…d1fb.
- `Fixtures/train2-baseline.txt` — перезаписан на данных core-g4 с влитой TASK-0129 (хэши базового коммита после TASK-0129 на месяцах 387, 1019 невозможны).

**Правки существующих тестов под новые данные** (счётчики 61→81, 20→24 процесса, 18→26 альтернатив, c_max 49→58 и `LaunchMonthMax` 33→37, G_max 1..6, массивы L_P `T_design`/ρ до 6 элементов, `TestMods.DropG4Class` убирает альтернативы и процессы G5/G6, SixGenerationDataTests переписан на реальные данные, `KnowledgeSystemTests.RunSlice` 24→48 месяцев: на новых данных за 24 месяца при фиксированном бюджете открытий нет — причину не исследовал).

**Новое значение Lab golden (Golden150, `SliceChecks.cs`, вне зоны): `0dc571a6bffaef40e6922578270f39f48f7917e48a862c38af918ce5cceb78a1`** (seed 1, месяц 149).

**verify.py:** build, Core 1113/1113, Session 506 (4 пропуска как раньше), Localization 131, Architecture 147, Kernel, Audio — зелёные. Красные только Lab (код вне зоны): Lab.Tests 5 из 213 и selftest 59/66. 7 проверок selftest: Golden150; BUDGET_RULE (тот же golden); control tables, calibration points L_P, lk-draws (LK/LP отклоняют 6 поколений — проверки должны идти на данных core-g4, а не mods/core); «G5/G6 … 4 на текущих данных» (ждёт 4, теперь 6); «synth: overlay of 5 actors × 61 nodes» (генератор требует ≥ 81). Всё это — правки `src/Singularity.Lab` (simulation-engineer).

**Не проверено.** Данные не сверены с первоисточниками (якоря, hazard — как в документе); пробный прогон 1945–2030 и пороги O-038 — шаг 2 (simulation-engineer), не делался; L1/L2 на 1020 месяцев — идут тесты Session (Train2Identity на старых данных до 1019; TwoWorlds, LinkedRun 384 мес. на новых), отдельного прогона 1020 месяцев на новых данных нет; клиент (дуги G1–G4, локали) не проверялся; ревьюер не смотрел.
