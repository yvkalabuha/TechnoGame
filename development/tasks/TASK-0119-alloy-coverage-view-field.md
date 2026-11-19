---
id: TASK-0119
title: "Alloy coverage field in the Session view for the Production flask"
status: PROPOSED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M05
agent: developer         # ответственный агент
category: core           # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: low            # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0111, TASK-0115]
related: [TASK-0111]
approved_by:             # owner | orchestrator (orchestrator — только level: light)
approved_at:
approval_ref:
created: 2026-11-19
---

# TASK-0119 — Alloy coverage field in the Session view for the Production flask

## Цель
Вернуть колбу сплава в панель «Производство» с одним источником данных: поле покрытия сплавом в представлении сессии. Клиент не выводит это состояние сам (O-018).

## Описание
В TASK-0111 клиент определял состояние сплава по цепочке причин (`why_not`) одной цели. Codex трижды нашёл случаи, где колба показывала неверное: другая действующая линия ограничена сплавом, а колба «хватает»; один процент с разными знаменателями.

1. simulation-engineer или systems-architect: определить величину «покрытие сплавом» для актора (предложение: минимум по действующим линиям отношения поставки к потребности; отдельно — сбой снабжения политикой и отсутствие плавки) и где она считается в ядре без новых аллокаций на тике.
2. developer: поле в представлении сессии (смена версии тестового API — вместе с «поездом схемы»), подключить `InstrumentLogic.AlloyFlask`, `AlloyCaptionKey`, `FlaskPercent` (остались в коде с тестами) и вернуть ряд в панель.

## Критерии приёмки
- [ ] Колба читает только поле представления; состояния «хватает», «N % потребности», «N % от обычного», «сплава нет» покрыты тестами на данных сессии.
- [ ] Кадры 1920x1080 при 100 %: каждое из четырёх состояний.
- [ ] Ревьюер APPROVE, Codex без открытых высоких замечаний, ui-art-director PASS (#417).

## Тесты
Тесты сессии и клиента; `python3 tools/development/verify.py`.

## Связанные документы
`docs/decisions/ORCH_DECISIONS.md` (O-016, O-018), `docs/ui/UI_INSTRUMENTS.md`.

## REVIEW

## Результат
