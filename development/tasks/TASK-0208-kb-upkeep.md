---
id: TASK-0208
title: "Knowledge base upkeep: fact drift is an error, decision-to-facts check at landing, freshness, /fact, facts in kb_db and mandatory facts block for code/data agents, agent DB access read-only (#597)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: tooling                # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0202]
related: []
approved_by: owner
approved_at: 2026-11-26
approval_ref: "DECISIONS_LOG #597"
created: 2026-11-26
---

# TASK-0208 — Knowledge base upkeep: fact drift is an error, decision-to-facts check at landing, freshness, /fact, facts in kb_db and mandatory facts block for code/data agents, agent DB access read-only (#597)

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
#597: база знаний (development/FACTS.yaml + kb_check) не устаревает молча, и её получают все исполнители. Разбор: docs/reviews/platform-kb-access-2026-11-26.md.

## Объём (tools/development, .claude/hooks, skills — skill /fact пишет Orchestrator)
1. kb_check: дрейф зонда факта — ошибка (exit 1); факт со ссылкой на решение, помеченное SUPERSEDED, — ошибка.
2. Свежесть: поле `reviewed: YYYY-MM-DD` у факта; файл-источник менялся позже (git log) — предупреждение «проверить»; старше 30 дней — список для /handoff (`kb_check.py stale`).
3. Решение → факты: kb_guard после записи DECISIONS_LOG печатает связанные факты новой строки; land.py отказывает, если дифф добавляет строку решения, а связанные факты не изменены и в сообщении коммита нет `facts-ok: #NNN`.
4. kb_db: таблица facts (id, statement, source, keywords, reviewed, drift) и `q facts text~…` (O-112).
5. guard_dispatch: для агентов developer, simulation-engineer, test-engineer, 3d-modeler — в задании должен быть блок «Факты:» (печать `kb_check.py related` по теме); иначе отказ с подсказкой команды.
6. guard_bash (агенты): прямые `psql` и `redis-cli` закрыты, кроме тестов на временной базе (переменная SINGULARITY_KB_TEST); чтение — через kb_db.py и metrics.py.
7. Тесты на всё.
