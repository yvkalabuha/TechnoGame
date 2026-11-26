---
id: TASK-0201
title: "Redis for agent heartbeat, locks and expiring permits (#590), after redis-server is installed"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
review_ref: "reviewer: APPROVE c709b559; agy-sonnet second review accept (3 low, DEBT-0124)"
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

## Результат (2026-11-26, остановлено по слову владельца)
- Ветка worktree-agent-ab6db97a841ce0eb5 (e7f66ea0, d56d978e): redis_kv.py, хук пульса agent_beat.py, watch.py по пульсу, запись держателя land-блокировки, разрешение на клиент с EX (Redis только запрещает), guard_bash --deny-db для агентов (#597). 220 тестов OK.
- reviewer: CHANGES (medium): ложные отказы агентам после обёрток — `nice -n 10 grep psql x`, `timeout 60 rg redis-cli docs`, `xargs grep psql`, `env A=1 git grep psql`; low: heredoc-строки, начинающиеся с psql; SINGULARITY_KB_TEST не ограничен базой kbtest_*; пульс молчит во время длинного вызова — порог --silent-min 20 мин мал.
- Следующий шаг: исправить medium + тесты на четыре примера, поднять --silent-min (или считать «последний вызов начат»), повторная проверка reviewer, land. После посадки разрешение на запуск клиента выдать заново.

## Результат (посадка 2026-11-26)
- Правки по ревью (исключение владельца #601): 32ab468f, c709b559 — обёртки nice/timeout/xargs/env и др. снимаются, проверяется настоящее слово команды; heredoc; SINGULARITY_KB_TEST только для kbtest_*; watch --silent-min 45.
- reviewer recheck: APPROVE (c709b559), 87 тестов OK. Второе ревью agy-sonnet (Codex — лимит): accept, 3 low → DEBT-0124.
- Влито по #602 как 3388720 (land.py, verify OK). Разрешение на запуск клиента выдать заново при следующей съёмке.
