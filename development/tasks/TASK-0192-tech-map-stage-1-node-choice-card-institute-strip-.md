---
id: TASK-0192
title: "Tech map stage 1: node choice card, institute strip, queue, path to goal — Gemini design, then client (#561, #578)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: client
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0187]
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#561, #563, #576, #578"
created: 2026-11-25
---

# TASK-0192 — Tech map stage 1: node choice card, institute strip, queue, path to goal — Gemini design, then client (#561, #578)

## Цель
Первый этап карты технологий новой модели (`docs/ui/specs/pivot-561-tech-map.md` стадии 0–1): игрок выбирает тему вручную и видит цену, срок, направления, институты, очередь и путь к цели.

## Описание
Шаг 1: бриф для Gemini (ui-art-director) → эскизы Gemini (`llm_worker sketch`, agy) по кольцу «год от центра» + карточка выбора + полоса институтов внизу + очередь (активная + 5, #567) + путь к цели; Gemini — ведущий (#578), Claude-проверяющий сверяет с §12. Шаг 2: developer реализует по утверждённому эскизу на Session-запросах TASK-0187 (FitInstitutes, FocusQuote, PathToGoal, SuggestQueues, команды AssignNode/ApplyQueues/BuyFocus/SetFocus). Остатки DEBT-0112 (TASK-0161) и материалы веток TASK-0156/TASK-0132 (O-097) — вход.

## Критерии приёмки
- [ ] Эскиз утверждён (вердикт Gemini + проверка Claude); владелец видит эскиз
- [ ] Клиент: выбор темы, назначение в институт, очередь, ускорение, путь к цели; кадры 1920x1080 на новой модели; вердикт Gemini и проверка; reviewer APPROVE
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
Шаг 2 (клиент), ветка worktree-agent-a1cad8ee202670e8e. Сделано: (1) DEBT-0115: ReorderQueue принимает опубликованную очередь (без активного узла), ApplyQueues получил Acknowledged и проверку 60 % (O-096), тесты SlotClientSupportTests; (2) ScienceMapLogic (чистые правила, 9 тестов); (3) SimHost: AskScience, ScienceSnap, FreeNoticeThrottle (S12), автозаполнение советником выключено по умолчанию (O-099); (4) TechTree.Science.cs: полоса институтов, карточка узла, панель очереди, путь к цели с окном «Разложить», RU/EN. Не проверено: отрисовка (клиент не запускался), кадры, серые двойники, 125 %. Не сделано: займствование свободным институтом без проверки 60 % (изменение симуляции), номера 1…N на карте, Alt+↑/↓, сравнение А/Б, кольцо лет по спецификации.
