---
id: TASK-0208
title: "Knowledge base upkeep: fact drift is an error, decision-to-facts check at landing, freshness, /fact, facts in kb_db and mandatory facts block for code/data agents, agent DB access read-only (#597)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
review_ref: "reviewer: APPROVE db9ebd83; agy-sonnet second review accept (2 medium, 2 low -> DEBT-0125)"
created: 2026-11-26
tags: [task, knowledge-base, platform, done]
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
Ветка worktree-agent-a47665f49cf25f471 (developer, 2026-11-26).
1. kb_check: дрейф зонда и пропавший файл зонда — ошибка; факт, чей `source` ссылается на решение, помеченное SUPERSEDED в начале строки, — ошибка (частично отменённое — предупреждение); `statement` не проверяется (там законно упоминаются старые решения).
2. Поле `reviewed: YYYY-MM-DD` необязательное; файл-источник (зонд + пути в `source`, без журналов решений) менялся в git позже — предупреждение; `kb_check.py stale [--days 30]` — список для /handoff (без `reviewed` — «never reviewed»). В FACTS.yaml поле не добавлено: файл вне зоны developer (см. OPEN).
3. kb_guard после Edit/Write DECISIONS_LOG печатает связанные факты новых строк (`kb_check.py decision-facts N`); land.py до второго ревью вызывает `kb_check.py landing` и выходит с кодом 7, если новая строка решения имеет связанные факты, не изменённые на ветке, и нет `facts-ok: #N` в -m или в коммитах ветки. needs_second_review больше не оставляет корень в sys.path.
4. kb_db: таблица facts (id, statement, source, keywords, reviewed, drift, drift_text), `q facts text~…`, `drift=true`; SCHEMA 2; FACTS.yaml и файлы зондов в отпечатке.
5. guard_dispatch: developer, simulation-engineer, test-engineer, 3d-modeler — нужен блок «Факты:» со строками FACT/DECISION (вывод `kb_check.py related`) или «Факты: нет связанных»; при внутренней ошибке пропускает.
6. guard_bash --deny-db (TASK-0201) — проверено тестами GuardBashDenyDbTest, без изменений.
7. Тесты: test_hooks.py (KbCheckFactsTest, KbGuardDecisionTest, GuardDispatchTest), test_kb_db.py, tools/llm_worker/tests/test_land_lock.py.

OPEN: в development/FACTS.yaml добавить `reviewed: 2026-11-26` фактам с зондом без дрейфа (slice-actors, slice-horizon, slice-content, schema-version, api-version, map-regions, science-slots, institutes-count) и описание поля в шапке; связь по ключевым словам широкая (факт decision-levels совпадает почти с любым решением), так что почти каждая новая строка решения потребует `facts-ok`.

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

## Посадка (Orchestrator, 2026-11-26)
- reviewer: CHANGES (374eab56) → APPROVE (db9ebd83). Второе ревью agy-sonnet (Codex — лимит): accept, 2 medium + 2 low → DEBT-0125. Влито как a904fd4 по #604 (verify --full OK).
- reviewed: 2026-11-26 добавлено 8 фактам с зондом без дрейфа.
