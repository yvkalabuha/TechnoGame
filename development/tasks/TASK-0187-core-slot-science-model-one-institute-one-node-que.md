---
id: TASK-0187
title: "Core: slot science model (one institute = one node, queue 5, focus, directions, research_speed) — golden batch (#576)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0171]
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#576"
created: 2026-11-25
---

# TASK-0187 — Core: slot science model (one institute = one node, queue 5, focus, directions, research_speed) — golden batch (#576)

## Цель
Ядро модели «по слотам» по утверждённому `docs/game-design/SCIENCE_SLOTS.md` (#561–#576).

## Описание
Режим KnowledgeMode.Slots рядом со старым (Shares ≡ LEGACY побайтно, S11). Узел: цена ×0,25 (#573), T_n по пилоту TASK-0185 (6·c_type·(1+0,5·gen) мес.), направления (данные — отдельная задача), ставка r = C·M·s^0,6/T_eff (O-089), M ≤ 2,5 (#568). Слот = институт, очередь активный + 5 (#567), прогресс у узла, первый доступный элемент очереди, свободный институт берёт неначатый узел из чужой очереди. Фокус BuyFocus (O-088) уровни из пилота. Опыт ∝ прогресс·вес, новое направление уровня 1 (#572). Узел-шанс освобождает слот у потолка прогресса (O-092). Излишек науки — в бюджет (O-089). Потолок институтов — по содержанию (#574). Диффузия и кража сокращают и время (O-089). ИИ: PATH на свободный слот + запасные ступени, институт с наибольшим пересечением (O-092), фокус по отношению воздушной мощи, стройка. Session: поля и команды §12 SCIENCE_SLOTS. FleetValue в виде каждый месяц, MPI (#565). Инварианты S1–S14.

## Критерии приёмки
- [ ] Тесты на инварианты S1–S14, каждый новый тест падает без изменения
- [ ] Эталоны перезаписаны один раз с объяснением
- [ ] Прогон живости и полосы ±8–10 лет (#566); pace_check — справка
- [ ] reviewer APPROVE, второе ревью

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
**Сделано (ветка worktree-agent-a810348052da178f2, коммиты по шагам):**
- Режим слотов в ядре: `SlotParams`/`SlotMath` (T_n = 6·c_тип·(1+0,5·gen), цена ×0,25, M ≤ 2,5, ставка C·M·s^0,6/T_eff, u общий), `KnowledgeSystem.Slots` (прогресс только у узла в слоте, излишек науки возвращается в бюджет, диффузия и кража сокращают и время, выпуск слота рисковым узлом у потолка прогресса с пониженным шансом), `InstituteSystem.Slots` (Resolve: очередь 1+5, первый доступный, заимствование нетронутого узла из чужой очереди, ИИ: PATH, затем любой узел фронта, институт с наибольшим пересечением, фокус по отношению воздушной мощи; опыт ∝ прогресс·вес по направлениям, новое направление на уровне 1, потолок по содержанию).
- Команды AssignNode/Unassign/ReorderQueue/SetFocus (+ SetPriorityNode как «активный узел»), поля Session (SlotScienceView, поля InstituteView), FleetValue/MPI каждый месяц, TestApiVersion 28.
- Доп. объём координатора (founded_month): founded_month в institutes.yaml (CEA: 1945-10, кандидат; уровни CEA chem 2 + metal 1 — лимит малой страны 3), направления в хэшах DataHash и InstituteTable.Hash; ядро читает направления данных (TASK-0189, слита) с запасным вариантом «дисциплина = направление».
- Режим Shares и Legacy: побайтно прежние (эталоны Legacy не менялись); эталоны конфигурации по умолчанию перезаписаны один раз (LinkedRun, ViewTests, TwoWorlds): режим слотов по умолчанию и FRA с 2 институтами.
- Тесты: tests/Singularity.Session.Tests/SlotScienceTests.cs (S1, S2, S3, S5, S6 L1/L2, S10, S11, живость, команды, фокус), tests/Singularity.Core.Tests/Institutes/SlotMathTests.cs.
- Часть 2: запросы FitInstitutes/FocusQuote/PathToGoal/PathToGeneration/SuggestQueues, команды CancelBuild/ApplyQueues, подтверждение невыгодного старта (Acknowledged), фокус из резерва, эффект research_speed (чтение из данных, данных пока нет), второй ярус пути ИИ (g*+1), общее правило стройки ИИ (при нынешнем дереве не срабатывает: путь уже слотов), уведомление «институт свободен» раз в год (S12), излишек науки идёт в статью закупок, пороги уровней покрывают 5.
- Не сделано в части 1 (кроме перечисленного в части 2): Lab SurfaceVersion 27→28 (зона simulation-engineer), запросы FitInstitutes/FocusQuote/PathToGoal/SuggestQueues, CancelBuild/ApplyQueues, оплата фокуса из резерва, общее правило стройки ИИ, ступень g*+1, research_speed R, пауза S12, свойство S13, полосы темпа через Lab.
