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
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
