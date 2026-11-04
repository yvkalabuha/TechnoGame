---
id: TASK-0029
title: "D2: правило старта R-H-MS′, валидатор S-2, переключатель цели оснастки TOOLING_TARGET"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
