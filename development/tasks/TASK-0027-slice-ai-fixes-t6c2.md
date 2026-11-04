---
id: TASK-0027
title: "T6c2: стартовые линии (E1), запуск при допуске (F17), выбор двигателя (#140), why_not W1"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0026]
related: [HYP-0002]
approved_by: owner
approved_at: 2026-11-04
approval_ref: "DECISIONS_LOG #137, #138, #140, #144; спека v0.15 §11 T6c2"
created: 2026-11-04
---

# TASK-0027 — T6c2: стартовые линии (E1), запуск при допуске (F17), выбор двигателя (#140), why_not W1

## Цель
Исправить поведение среза, найденное в диагностике D1 (`docs/simulation/d1-output-diagnosis.md`): стартовые парки с линией (E1, #137), запуск серии только при допуске всех процессов (F17, #138), выбор двигателя пресетом (#140), «ближайший узел» в `why_not` (W1).

## Описание
По спеке `docs/simulation/slice-jet-aviation.md` v0.15 §11 строка «T6c2», A32 (E1), A35, A40 (L-O0, классы срабатываний), A41 (F17), §1.4 (#140), §5.6 (W1), §6.9 «Дополнения v0.15». Контракт §6 для W1 правит systems-architect параллельно (F19); исходить из текста спеки §5.6.
Не входит: списание и продажа парка (#141, #142), T6d.

## Критерии приёмки
- [ ] Всё из §11 «T6c2».
- [ ] Общие пункты T6 (тесты, ADR-0003, нет RNG в Industry/Products, L1/L2); golden перезаписаны с причиной «T6c2».
- [ ] В «Результат» — информационная печать 384 мес. ИИ на seeds 1…20: выпуск по окнам 48 мес. по акторам, доля содержания в закупках `u` по профилям, срабатывания A40 по классам — вход для объявления чисел списания (#141).

## Тесты
По §11 T6c2.

## Связанные документы
Спека v0.15; `docs/simulation/d1-output-diagnosis.md`; контракт v2.4 r4; DECISIONS_LOG #137, #138, #140, #144.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
