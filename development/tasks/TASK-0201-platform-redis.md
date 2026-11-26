---
id: TASK-0201
title: "Redis for agent heartbeat, locks and expiring permits (#590), after redis-server is installed"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: tooling                # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0200]
related: []
approved_by: owner
approved_at: 2026-11-26
approval_ref: "DECISIONS_LOG #590"
created: 2026-11-26
---

# TASK-0201 — Redis for agent heartbeat, locks and expiring permits (#590), after redis-server is installed

## Цель

## Описание

## Критерии приёмки
- [ ]

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->

## Цель
Решение #590: Redis для пульса агентов, блокировок и разрешений со сроком.

## Условие старта
Redis на машине не установлен (проверено 2026-11-26: нет redis-server и redis-cli). Нужна установка владельцем: `sudo apt install redis-server` (слушать только 127.0.0.1, пароль в ~/.config, не в репозитории).

## Объём (после установки)
1. Пульс: хук PostToolUse для агентов пишет `SET agent:<id>:beat <t> EX 600`; watch.py --actions считает молчащим агента без пульса > N мин (вместо 2 ч по журналу).
2. Блокировки land/test/godot через SET NX EX с запасным путём на flock, если Redis недоступен.
3. client_permit со сроком через EX; запасной путь — нынешний файл.
4. Тесты с поддельным клиентом; без Redis всё работает как сейчас.
