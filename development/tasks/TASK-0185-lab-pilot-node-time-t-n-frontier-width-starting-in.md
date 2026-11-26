---
id: TASK-0185
title: "Lab pilot: node time T_n, frontier width, starting institutes and ceiling for the slot model (#561, #564)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: simulation-engineer
category: lab
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#563, #564"
review_ref: "train f12684f: reviewer APPROVE (a74ab0a, rounds to 8a4b96b7); codex 4 returns, waived by #432, DEBT-0115; pilot note in docs/simulation/slot-model-pilot.md"
created: 2026-11-25
---

# TASK-0185 — Lab pilot: node time T_n, frontier width, starting institutes and ceiling for the slot model (#561, #564)

## Цель
Числа для модели «по слотам» до проекта ядра: масштаб T_n по поколению, ширина фронта по годам, стартовые институты (#564) и потолок.

## Описание
Песочница Lab (как TASK-0176): режим «прогресс только у узла со слотом», поток цена/время, без старого деления бюджета. Серия «слоты × масштаб T_n» на 10 seeds. Мерить: год первой машины G2–G4 (полосы ±8–10 лет, #566), простой слотов, ширина фронта (сколько доступных узлов на слот) по годам, когда кончается дерево 99 узлов. Потолок Cmax = round(4 + 8·√b) — проверить.

## Критерии приёмки
- [ ] Записка docs/simulation/slot-model-pilot.md: рекомендуемые T_n, стартовые институты и потолок с цифрами
- [ ] Предрегистрация; серии ≤ 15 мин

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
