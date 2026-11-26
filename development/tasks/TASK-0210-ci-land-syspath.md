---
id: TASK-0210
title: "CI fix: land.py imports redis_kv without putting tools/development first on sys.path (llm_worker test_rejections collision)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category:                # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: high         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: orchestrator
approved_at: 2026-11-26
approval_ref: "red CI on main (run 45873694378), orchestration.md §2"
created: 2026-11-26
---

# TASK-0210 — CI fix: land.py imports redis_kv without putting tools/development first on sys.path (llm_worker test_rejections collision)

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
CI на main красный после посадки TASK-0201 (3388720): tools/development/land.py делает sys.path.insert(0, tools/development) при импорте; тесты llm_worker (test_land_lock импортирует land) после этого находят tools/development/test_rejections.py вместо tools/llm_worker/tests/test_rejections.py — ImportError при discover.

## Объём
- land.py импортирует redis_kv без постоянной вставки tools/development в начало sys.path (importlib по пути файла или убрать вставку после импорта).
- Тест: `python3 -m unittest discover -s tools/llm_worker/tests` проходит; test_hooks и тесты land проходят.
