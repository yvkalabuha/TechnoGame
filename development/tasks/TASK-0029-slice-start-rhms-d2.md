---
id: TASK-0029
title: "D2: правило старта R-H-MS′, валидатор S-2, переключатель цели оснастки TOOLING_TARGET"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0028]
related: [HYP-0002]
approved_by: owner
approved_at: 2026-11-04
approval_ref: "DECISIONS_LOG #155, #156, #159; спека v0.19 §11 D2"
created: 2026-11-04
---

# TASK-0029 — D2: правило старта R-H-MS′, валидатор S-2, переключатель цели оснастки TOOLING_TARGET

## Цель
Сделать «освоено по сценарию» честным (yield ≥ 0,7 на t0) и подготовить сборку к эксперименту по цели оснастки (#156).

## Описание
По спеке `docs/simulation/slice-jet-aviation.md` v0.19: §1.1 R-H-MS′ (таблица 16 ячеек, столбец R-H-MS′ по #159, `M = max(M, H + 0,2)`), валидатор S-2 (ошибка загрузки, если `mastered` сценария даёт yield(t0) < 0,7), переключатель манифеста `TOOLING_TARGET` ∈ {V0…V4} (по умолчанию V0) с числами из §11/`docs/simulation/exp-t6e-tooling-target.md`, набор данных `RHMS_OFF` для печати «до/после» (F32), §11 строка «D2» с приёмкой. Данные мода — по `docs/research/data/jet-slice/scenario_1945.md` §2.1 с поправкой R-H-MS′.

## Критерии приёмки
- [ ] Всё из §11 «D2».
- [ ] Общие пункты T6 (тесты, ADR-0003, нет RNG в Industry/Products, L1/L2); golden с причиной «D2».
- [ ] В Результат (через ответ Orchestrator — хук запрещает developer писать в development/tasks): печать на seeds 1…20 при V0: первый `fielded`, `G_front` по окнам, yield 49/54 по окнам.

## Тесты
По §11 D2.

## Связанные документы
Спека v0.19; `docs/simulation/fielded-empty-diagnosis.md`; `docs/simulation/exp-t6e-tooling-target.md`; DECISIONS_LOG #155, #156, #159.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
**Сделано (developer, 8791c53; перенесено Orchestrator).** Данные: 16 ячеек R-H-MS′ в `mods/core/scenario_1945.yaml` (USA 5, GBR 5, SUN 4, FRA 2). Валидатор S-2 `Core/Industry/StartYieldValidator.cs` (через `IndustryMath.Yield`, `DataLoadException` со списком; вызывается при сборке `SliceWorld` до START_PERT). `TOOLING_TARGET` (`Policy/ToolingTarget.cs`): V0, V1, V2, V3, V3b, V4, VInf; числа ρ_max 0,021, h_lo 0,021, h_hi 0,063, h_hi(V3b) 0,20, m_lead 0,20; по умолчанию V0 (побитно прежнее поведение); числа в хеше прогона (`run.tooling.*` только при не-V0). VINF — по спеке v0.19 r1 (`NotionalTotal` = `invest_notional`; исключение инварианта названо в тесте). `RHMS_OFF` — мод `mods/rhms_off` + `SkipStartValidation`.
Golden («D2»): Golden60 c377f34e…, Golden150 951bcf96…, scenario_hash 71160e65…, DataHash 7cc1120c…. Тесты: Arch 80, Kernel 284 (+1), Core 925, Session 185.
**Отклонения.** (1) S-2 в сборке `SliceWorld`, не в `ModLoader` (нужны слои Industry). (2) Приёмка п.3: проверены PRC fielded-проектов; процессы без линии (47 у USA) теряют `mastered_run` по правилу модели. (3) `FleetAblationTests.NoRetire` идёт на `RHMS_OFF` (побитно). (4) Манифеста Lab для среза нет — манифест = `SliceRunConfig.ToolingTarget`; хеш манифеста и леджер Lab — в EXP T6e. (5) Печать — тест `Print_Seeds1To20` под `SINGULARITY_D2_PRINT=1`.
**Печать seeds 1…20, V0, 384 мес. (D2 / RHMS_OFF).** Первый fielded: USA и GBR мес. 0 у 20/20; SUN, FRA, SWE никогда; RHMS_OFF — ни у кого. Max `G_front` по окнам 1,1,1,2,2,2,2,2; G ≥ 2 хотя бы раз в 9/20 seeds. Yield (медиана): 49/USA 0,89 → 0,94, в w7 0,75 (seed 2: 0,92 → 0,02); 49/GBR 0,83 → 0,91; 54/USA 0,90 → 0,97, w7 0,89; 54/GBR 0,84 → 0,93. RHMS_OFF: 49/USA 0,14 → 0,35; 49/GBR 0,09 → 0,40; 54/USA 0,49–0,59; 54/GBR 0,25 → 0,53.
**Находка (medium).** У USA в части seeds yield 49/54 деградирует в поздних окнах при V0 — вход для EXP T6e.

