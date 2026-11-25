---
id: TASK-0183
title: "Pace tuning G3-G4 after owner play session: ahead cost on on-time nodes, epoch budget weighting (#558)"
status: PROPOSED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M0
agent: simulation-engineer
category: lab
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0180]
approved_by:             # owner | orchestrator (orchestrator — только level: light)
approved_at:
approval_ref:
created: 2026-11-25
---

# TASK-0183 — Pace tuning G3-G4 after owner play session: ahead cost on on-time nodes, epoch budget weighting (#558)

## Цель
Вернуть G3–G4 ближе к окнам C4 после партии владельца (#558).

## Описание
Остаток опоздания после TASK-0180 (`docs/simulation/slice-pace-regression.md`, раздел «TASK-0180 замер»): ≈50 мес. — цена опережения (#458, #465) бьёт и по узлам, изучаемым в свой срок; ≈79 мес. — узлы G5/G6 (63, 91, 62, 83, 90, затем 82, 93) открываются в 1954–1961 и делят бюджет. Варианты: доля бюджета по эпохе узла; цена опережения только для узлов раньше их якоря. Проверка — инструмент темпа (#555).

## Критерии приёмки
- [ ] Предрегистрация, замер, gate; решение владельца по варианту
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
