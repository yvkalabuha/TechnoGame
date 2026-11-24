---
id: TASK-0139
title: "HUD time controls: centred pause and speed buttons, no speed text, speed gauge active frame alignment"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M05
agent: developer
category: client
priority: medium
needs_owner: false
depends_on: []
related: [TASK-0136, TASK-0117]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "owner in chat 2026-11-24"
created: 2026-11-24
---

# TASK-0139 — Кнопки времени в верхней полосе

## Цель
Владелец: кнопки паузы и ускорения не отцентрованы и некрасивы; скорость словами («1 день/с», «1 месяц/с») не нужна; у шкалы скорости рамка активного деления стоит не по центру. Gemini (Hydrogen) тоже отметил тонкие деления шкалы (A2).

## Описание
Шаг 1 — ui-art-director: спецификация (размеры в du, центровка значков по оптическому центру, состояние нажатия и фокуса, шкала скорости без подписи с числом в подсказке и для читалки экрана, рамка активного деления). Шаг 2 — developer: `TimeIcons.cs`, `TimeGlyphMetrics.cs`, разметка верхней полосы.

## Критерии приёмки
- [ ] Кадры 1920x1080 при 100 %, 125 %, 150 %: значки центрированы (отклонение ≤ 0,5 du, проверка в прогоне кадров), рамка активного деления симметрична.
- [ ] Скорость без подписи, но доступна (подсказка, A6–A7); пауза — пробел.
- [ ] ui-art-director PASS; ревьюер APPROVE.

## Результат
