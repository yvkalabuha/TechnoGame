---
id: TASK-0200
title: "Platform DB index in PostgreSQL: kb_db.py build/query from registry, decisions, debt, questions, errors, agent log (#590)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: tooling                # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0198]
related: []
approved_by: owner
approved_at: 2026-11-26
approval_ref: "DECISIONS_LOG #590"
created: 2026-11-26
---

# TASK-0200 — Platform DB index in PostgreSQL: kb_db.py build/query from registry, decisions, debt, questions, errors, agent log (#590)

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
Сделано: `tools/development/kb_db.py` (build|q|sql|status), драйвер — команда psql (без новых зависимостей), база `singularity_kb` на сокете, peer-аутентификация. Таблицы: items, item_links, decisions (superseded_by по тексту «отменяет #N»), orch_decisions, debt, questions, errors (из development/errors.yaml, если есть), agent_runs, журналы hook_refusals и land_failures (не пересобираются, мягкое удаление deleted_at). Полнотекстовый поиск russian + simple, индексы по status/agent/type/priority/area/date. `q` пересобирает сам по отпечатку (имя, mtime, размер файлов). `registry.py index` вызывает build тихо. Тесты: `tools/development/test_kb_db.py` (парсеры и компилятор запросов без базы, 6 тестов на временной базе пропускаются без PostgreSQL), шаг kb_db в verify.py.
Проверка: полная пересборка 1,7 с (267 задач, 586 решений, 117 долгов); 18 тестов зелёные, с недоступным сокетом — 12 прошли, 6 пропущены. Не проверено: errors и agent_runs на реальных данных (errors.yaml и agents.jsonl нет в этой ветке/копии); автозаполнение журналов отказов хуков и посадок — нет источника (хуки и errors.py не трогались).

## Цель
Решение #590: быстрые выборки по записям платформы без чтения больших файлов. Источник правды — файлы в git; база — производный индекс.

## Объём (tools/development, без src/)
1. `tools/development/kb_db.py build|q|sql|status`: PostgreSQL 16 локально, сокет /var/run/postgresql, peer-аутентификация текущего пользователя, база `singularity_kb` (создать, если нет); никаких паролей в репозитории. Драйвер: psycopg если есть, иначе вызов `psql` (без новых зависимостей в CI).
2. Таблицы: items (id, type, status, level, milestone, agent, category, priority, needs_owner, approved_*, path, title, updated), item_links (from, to, kind: depends_on|related), decisions (num, date, text, source, ref, superseded_by), orch_decisions (O-NNN), debt (DEBT-NNNN, status, area, text), questions (id, text, blocks, added, open), errors (из errors.yaml, с repeats), agent_runs (из .claude/.state/agents.jsonl: id, type, description, tasks, start, stop), service journals (hook_refusals, land_failures) с deleted_at для мягкого удаления; индексы по status, agent, type, priority, area, date; полнотекстовый поиск (russian + simple конфигурации).
3. Пересборка: полная за ≤ 5 с; `q` пересобирает сам, если файлы новее (mtime/sha); `registry.py index` вызывает build (тихо, без ошибки, если PostgreSQL недоступен).
4. `q` — короткий язык фильтров: `kb_db.py q debt status=open area=Lab`, `q decisions text~наука since=2026-11-20`, `q items status=REVIEW agent=developer`, `q agents silent>2h`; вывод — одна строка на запись, `--json`.
5. Тесты: на временной базе (создать/удалить) или skip, если PostgreSQL нет; парсеры таблиц — без базы.

## Критерии приёмки
- verify.py зелёный (CI без PostgreSQL — тесты базы пропускаются, парсеры проверяются).
- Замер для отчёта через 2 недели: размер ответа `q` против grep по тем же вопросам (5 примеров в записке docs/reviews/platform-db-2026-11-26.md).
