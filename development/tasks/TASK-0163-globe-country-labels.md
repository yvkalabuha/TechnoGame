---
id: TASK-0163
title: "Globe country names: no plate, size by country extent with a minimum, hide when not fitting until zoomed (#523)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M05
agent: developer         # ответственный агент
category: client
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#523"
created: 2026-11-25
tags: [task, map, ui, active]
---

# TASK-0163 — Globe country names: no plate, size by country extent with a minimum, hide when not fitting until zoomed (#523)

## Цель
Подписи стран на глобусе без белой подложки; размер шрифта по размеру страны на экране с нижним порогом; не помещается в страну — показывается только при приближении (#523).

## Описание
Читаемость без подложки — тонкий ореол/обводка цвета фона (контраст ≥ 4,5:1 к тону страны, A1–A3); подпись внутри контура страны (полюс недоступности или центр самой большой части); выноски малых стран (TASK-0142) — без подложки. Только клиент.

## Критерии приёмки
- [ ] Ни одна подпись не выходит за границу своей страны на кадрах 1920x1080 100 % (общий вид и 2 шага зума) и серый
- [ ] ui-art-director PASS с мнением Gemini; reviewer APPROVE

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
