---
id: TASK-0193
title: "Research + data: knowledge tree 1990-2030 (avionics, stealth, composites, computing, space, missiles) so leaders' institutes do not idle after 1990"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: research-scientist
category: data
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: orchestrator
approved_at: 2026-11-26
approval_ref: "O-100, #575"
created: 2026-11-26
---

# TASK-0193 — Research + data: knowledge tree 1990-2030 (avionics, stealth, composites, computing, space, missiles) so leaders' institutes do not idle after 1990

## Цель
После 1990 у США и СССР ~65 % институтов простаивают: дерево кончается (TASK-0187, slot-model-pilot.md §7.6–7.8). Добавить темы 1990–2030.

## Описание
Как TASK-0188: записка (research-scientist, сбор и проверка фактов через `llm_worker research`, #577) → данные (developer) → замер простоя и возврата денег после 1990 (simulation-engineer). Области: авионика и сенсоры, малозаметность, композиты, вычисления (ускорители, #568), космос, ракеты. 40–60 тем.

## Критерии приёмки
- [ ] Простой слотов лидеров после 1990 < 20 %; возврат денег < 20 % науки
- [ ] Источники; reviewer APPROVE
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
Шаг 2 (данные, developer, 2026-11-26), ветка worktree-agent-aa98cff22496f48dc.
- [Факт] В `mods/core/nodes.yaml` внесены 55 узлов 160–214 из записки `tree-expansion-1990-2030.md` ред. 2 (номера 157–159 не заняты): RU/EN имена, краткие названия, якоря и истории (`locale/*/data.ftl`, `_sources.tsv` перештампован), `node_history.yaml` (9 узлов O-102 — `unconfirmed: true`, `estimated: true`), направления, `mil_value`, 7 эффектов `research_speed` по 0,005 (сумма aero 0,245 ≤ 0,25 [ЗАГЛУШКА]). Из имён убраны «первая/first» (J/APG-1, Starship).
- [Предп.] Правки к записке, нужные загрузчику: кластеры SPACE→AERO, COMP→ELEC (в загрузчике нет таких), FinFET — MECH (процесс требует кластер площадки); у 3 процессов (AFP, FSW, FinFET) tier/capability_req/labour_p выбраны мной по аналогам (4/4,5·4·4,5/7000; 4/то же/4000; 5/5,5/4500) — нужна сверка economy-analyst; рёбра informs в узлы s8.app.* (14 штук) удалены (загрузчик запрещает); gen узлов Link 16, J/APG-1, CEC поднят с 5 до 6 (предпосылки существующие G6 со старым годом); у UHTC убрана предпосылка ceramic_matrix_composite (год 2016 позже 1997).
- Тесты: новый `Expanded1990TreeTests` (6 тестов: номера, ацикличность по году/поколению, направления, якоря, O-102, имена, research_speed, space); обновлены счётчики Core/Localization/Session и эталоны (одним пакетом, объяснения в коммитах): DataHash, схема (fingerprint, SchemaVersion 15), main seed 1 m149, ADMIT/YPROD, TwoWorlds, AI-run, mastery-baseline.
- Приёмка Lab (`slot_core.py` на TASK-0187-core, 10 seeds, `lab/results/t0193/core`): простой после 1990 USA 0,0 %, SUN 0,2 % (было ≈65 %) — цель <20 % выполнена. Возврат денег после 1990 USA 90,1 %, SUN 91,0 % (было 93 %) — цель <20 % НЕ выполнена. Полосы G2/G3/G4 1953,7/1959,0/1969,8, LIVE0, CEIL, LEAD77 — PASS; GAP36 1947–1990 FAIL (USA 43, SUN 60, GBR 46, FRA 44, SWE 101).
- Не проверено: Lab-тесты (`tests/Singularity.Lab.Tests`: SynthWorld.NumberStride=200 < 214 и эталоны в `lab/` — зона simulation-engineer), L2 на новых данных, первичные источники узлов.
