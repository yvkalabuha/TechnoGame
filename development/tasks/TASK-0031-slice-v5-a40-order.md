---
id: TASK-0031
title: "V5 и порядок A40: вариант цели оснастки V5, охрана снятия при условии запуска"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0030]
related: [HYP-0002, EXP-0004]
approved_by: owner
approved_at: 2026-11-04
approval_ref: "DECISIONS_LOG #162; спека v0.21 §11 «V5 + A40 order»"
created: 2026-11-04
---

# TASK-0031 — V5 и порядок A40: вариант цели оснастки V5, охрана снятия при условии запуска

## Цель
Добавить вариант цели оснастки V5 (все измерения, где оснастка мешает) и закрыть гонку A40 (снятие в месяц, когда проект уже можно запустить) — подготовка к EXP T6e3 (#162).

## Описание
По спеке `docs/simulation/slice-jet-aviation.md` v0.21: §11 «T6e» таблица и блок «V5 — условие участия», A40 ветка О (охрана: `cancel_design` не выдаётся при выполненном условии запуска по снимку t), §11 строка «V5 + A40 order» с приёмкой (5 пунктов). Ответ с Результатом — Orchestrator (хук запрещает developer писать в development/tasks).

## Критерии приёмки
- [ ] Все 5 пунктов §11 «V5 + A40 order».
- [ ] Общие пункты T6; golden-хеши меняются только в месяцах срабатывания охраны — перечислить.

## Тесты
По §11.

## Связанные документы
Спека v0.21; `docs/simulation/exp-t6e3-tooling-target.md`; EXP-0004; DECISIONS_LOG #162.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
