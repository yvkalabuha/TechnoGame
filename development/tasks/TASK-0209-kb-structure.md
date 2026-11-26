---
id: TASK-0209
title: "Knowledge base structure: tag vocabulary (<=50, 3 facets), docs catalogue and chains in kb_db, monthly decisions log, archives, shorter hooks (#598, #599)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: tooling                # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0208]
related: []
approved_by: owner
approved_at: 2026-11-26
approval_ref: "DECISIONS_LOG #598, #599"
created: 2026-11-26
---

# TASK-0209 — Knowledge base structure: tag vocabulary (<=50, 3 facets), docs catalogue and chains in kb_db, monthly decisions log, archives, shorter hooks (#598, #599)

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
#598, #599: быстрый поиск без чтения лишнего, меньше токенов.

## Объём
1. development/TAGS.yaml: до 50 тегов в трёх гранях (вид, область, состояние) с описанием и примерами; поле `tags:` в шапке документа; kb_check — незнакомый тег ошибка, у документа 3–6 тегов.
2. Первичная разметка 647 документов: черновик — один проход DeepSeek (llm_worker, без секретов), проверка Claude; правки — отдельным коммитом.
3. Ссылки в шапке: `related:`, `supersedes:`, `implements:`; kb_db строит граф; `kb_db.py q chain <id>` — упорядоченный список путей с краткими выводами; таблица docs (путь, заголовок, «Краткий вывод», теги, дата).
4. Журнал решений — по месяцам (docs/decisions/log/2026-11.md …) с общим индексом; DECISIONS_LOG.md — короткая шапка со ссылками; все инструменты (kb_check, kb_guard, kb_db, registry, hooks) читают новую раскладку; строки #1–#400 и закрытые долги — в архивные файлы.
5. Хуки короче: kb_guard — не больше 5 близких фактов/решений без повторов за сессию; SessionStart — две последние записи CURRENT_STATE и «Фокус».
6. Замер: токены на сессию и задачу (metrics.py) неделю до/после.
7. Тесты; verify.py.
8. Пределы размера (#600): загружаемые всегда ≤ 6 КБ, живые документы ≤ 30 КБ, журналы по месяцам ≤ 60 КБ; kb_check — предупреждение, с 2026-12-03 — ошибка.
9. База (O-112): таблицы docs (байты, ≈токены, over_limit), links/chain; пересборка только изменённых файлов; GIN-индекс тегов, полнотекст russian+simple; `kb_db.py pack <id> --budget N` для /dispatch; `q docs over_limit`.
10. metrics: частота чтения файлов (Read/grep по file_path в журналах сессий, только пути) × размер — отчёт «самые дорогие файлы».
