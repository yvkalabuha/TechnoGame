---
id: TASK-0175
title: "Institutes: stretched project pays the price of its actual opening year; overload safety rule speed x min(1, 1/sum shares)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core           # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: high           # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0174]
related: []
approved_by: orchestrator # owner | orchestrator (orchestrator — только level: light)
approved_at: 2026-11-25
approval_ref: TASK-0173 (docs/simulation/institutes-p7-measurement.md)
created: 2026-11-25
---

# TASK-0175 — Institutes: stretched project pays the price of its actual opening year; overload safety rule speed x min(1, 1/sum shares)

## Цель
Замер TASK-0173: недофинансированный (растянутый) проект открывается в исторический год, но всё время платит надбавку раннего открытия (DARPA при 35 % — ×1,98, итого 1,77× от проекта в срок). Ловушка для игрока.

## Описание
1. Надбавка (цена по годам до исторического, O-067) считается по ожидаемому году открытия при текущем финансировании и пересчитывается при его изменении, либо по фактическому году открытия с перерасчётом — выбрать простейший честный вариант, описать в задаче; игрок видит цену до клика (окно институтов TASK-0156 показывает её).
2. Страховка перегрузки: скорость проекта × min(1; 1/сумма долей проектов и содержания), даже если сейчас перегрузка не достигается (TASK-0173: максимум 13,4 %).

## Критерии приёмки
- [ ] Тест: растянутый проект с открытием в исторический год не платит надбавку раннего открытия
- [ ] Тест страховки перегрузки
- [ ] Флаг выключен — эталоны без изменений
- [ ] reviewer APPROVE, второе ревью

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
Выбран вариант «по ожидаемому году открытия при текущем финансировании, пересчёт каждый месяц»: `InstituteSystem.ExpectedOpening(h, month)` = месяц + ceil((остаток − прогресс)/финансирование) − 1; `BuildRate(h, month)` и `BudgetFactor(actor, month)` берут год от него (без финансирования — цена исторического года). Окно институтов (`InstituteView.BuildRate`) показывает ту же цену до клика. Страховка: `InstituteMath.OverloadScale(take)` = min(1; 1/take), скорость проекта в `StepProject` умножается на неё (`Take` — общая сумма долей проектов и содержания). Оговорка: цена считается по финансированию без множителя страховки (иначе цикл: ставка зависит от срока, срок от ставки); при take ≤ 1 различия нет.
Файлы: src/Singularity.Core/Institutes/InstituteSystem.cs, InstituteMath.cs; src/Singularity.Session/Slice/KnowledgeInputsAdapter.cs, Views/ViewBuilder.cs; tests/Singularity.Session.Tests/InstituteTests.cs. Публичная поверхность Session не менялась (TestApiVersion остаётся 26; Lab SurfaceVersion не трогать).
Проверка: Core 1199, Session 646 (4 пропущены), Architecture 147 — зелёные; новый тест растянутого проекта падает на старой формуле (проверено), тест страховки — чистая функция (без фикса не компилируется). Флаг выключен: код институтов не исполняется, эталоны не менялись (отдельный прогон golden-тестов — в составе Session/Core).

## Codex р.1
- #3 отклонено: цена считается без множителя перегрузки намеренно (иначе цикл «ставка ↔ срок»); перегрузка недостижима на данных (TASK-0173: максимум 13,4 %) — DEBT-0107.

## Codex р.2 (6b12e12f)
- #1 отклонено: проект при 35 % открывается в декабре 1957 — на год раньше исторического 1958, по правилу O-067 надбавка ×1,02 за этот год верна; допуск теста 1,02 это и отражает (старое правило давало ×1,77).
- #2 отклонено: колонка `premium` теперь — множитель фактического года открытия; уплаченное за стройку — колонка `paid` (`_paid`), она и есть мера замера TASK-0173.
