---
id: TASK-0136
title: "Knowledge tree and menu clarity pass after Gemini design review: player text, legend grammar, node sizes, menu reasons, sheet depth"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M05
agent: developer
category: client
priority: medium
needs_owner: false
depends_on: []
related: [TASK-0132, TASK-0126, TASK-0135]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "owner in chat 2026-11-24 («делай»); #417, #426"
created: 2026-11-24
---

# TASK-0136 — Проход понятности и глубины по дереву знаний и меню

## Цель
Gemini (второе мнение, #441) и прежние обзоры нашли на кадрах дерева знаний и меню дефекты понятности и плоский вид. Владелец решил делать. Решение PASS/FAIL — за ui-art-director.

## Описание
Источники: `docs/ui/reviews/gemini-criteria-2026-11-24-flash.md`, `gemini-design-2026-11-24-flash.md`, `gemini-design-2026-11-24-hydrogen.md`. Шаг 1 — ui-art-director разбирает находки и предложения: что принять, что отклонить и почему, спецификация правок (`docs/ui/specs/task-0136-clarity-depth.md`). Шаг 2 — developer по спецификации. Ожидаемый объём: служебный текст разработчика убрать из интерфейса (C6); легенда 1:1 с символами планисферы и состояния узлов, «корень почему не» с неколоровым признаком (C2, A5); неизвестные узлы и номера узлов читаемы при 100 % (A1, A2); подписи-счётчики с ореолом (C5); причины недоступности в меню под своими кнопками (C4); артефакт на глобусе; тень и толщина листов по VISUAL_DIRECTION_V06 §3.1. Глобус со светом — отдельно, по выбору владельца между прототипами (#426).

## Критерии приёмки
- [ ] Спецификация арт-директора с решением по каждой находке Gemini.
- [ ] Кадры 1920x1080 при 100 % и серый кадр (A5); одна проверка на живом состоянии сессии (`.claude/rules/ui.md`).
- [ ] Повторный прогон Gemini (`visual --mode criteria`) — для сравнения, не для вердикта.
- [ ] Ревьюер APPROVE; ui-art-director PASS; второе ревью по #432.

## Результат
