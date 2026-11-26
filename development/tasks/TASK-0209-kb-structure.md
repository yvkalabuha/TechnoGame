---
id: TASK-0209
title: "Knowledge base structure: tag vocabulary (<=50, 3 facets), docs catalogue and chains in kb_db, monthly decisions log, archives, shorter hooks (#598, #599)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
tags: [task, knowledge-base, platform, active]
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
**developer, 2026-11-26** (ветка worktree-agent-a434bdfdf3baaf49a; коммиты 7f8bd242, 5fdea005, fb800f6a, f0c1acd5 и этот).

- п.1 [Факт] `development/TAGS.yaml`: 41 тег (вид 15, область 20, состояние 6), описание и пример у каждого. kb_check: незнакомый тег, не 3–6 тегов, нет вида или области, не один тег состояния — ошибка; документ без тегов — предупреждение.
- п.2 [Факт] Теги у 639 документов: черновик DeepSeek flash (16 вызовов, 96,7 тыс. токенов входа, 23,4 тыс. выхода, $0,0571 по ledger), проверка и правка Claude (вид по папке, состояние по статусу реестра, живые документы — вручную). 28 ACCEPTED ADR и `docs/simulation/risk19-yield-channel.md` (хеш в контракте llm_worker) — через `overrides`.
- п.3, п.9 [Факт] kb_db: таблицы `docs` (путь, заголовок, «Краткий вывод»/«Цель», теги text[] с GIN, дата, байты, ≈токены, over_limit; полнотекст russian+simple) и `doclinks` (related/supersedes/implements из шапки, depends_on задач); пересборка только изменённых файлов (mtime+размер); `q chain ID [depth=N]`, `pack ID --budget N` (блок «Факты:» для /dispatch), `q docs over_limit`, `q docs tags=ui`. kb_check проверяет цели ссылок.
- п.4 [Факт] Выбор: DECISIONS_LOG.md остаётся местом, куда добавляют новые строки (так написано в CLAUDE.md, skills /handoff и /new, их правит только оркестратор), а все инструменты читают все файлы журнала. `kblog.py rollover` (повторяемый) переносит строки: #1–#400 → `docs/decisions/log/archive-0001-0400.md`, остальные — в `log/ГГГГ-ММ.md`, в главном файле — шапка, индекс файлов и последние 12 строк. Закрытые долги → `development/archive/TECH_DEBT-closed.md`. Читатель — kb_check (kblog его переэкспортирует): kb_check, kb_guard, kb_db (колонка file), metrics, ворота фактов land.py. Строк: 606 до и 606 после (тест); долгов 125 = 90 + 35.
- п.5 [Факт] kb_guard после вопросов: не больше 5 фактов/решений, без повторов в сессии (`.claude/.state/kb_guard/<session>.json`); новые строки в месячных файлах тоже дают «подтвердить факты». SessionStart: две последние записи «Сводки», «Фокус», проверки, число активных задач.
- п.6 [Факт] Базовая линия (metrics.py report --compare, неделя 2026-11-19..2026-11-26): основных сессий 6; токенов на основную сессию 1,82·10⁸ (почти всё — чтение кеша); выход агентов на задачу 205 тыс.; названных чтений файлов 13 415. Замер «после» — 2026-12-03..2026-12-10 тем же отчётом.
- п.8 [Факт] Пределы (#600) в kb_check: всегда загружаемые 6 КБ, живые 30 КБ, месячные журналы 60 КБ; до 2026-12-03 — предупреждение, затем ошибка. Сейчас выше предела: CLAUDE.md 6,4; CURRENT_STATE 10,4; PLAN 11,7; M_PLAY_LOOP 36; UI_DESIGN_SYSTEM 103,5; `log/2026-11.md` 66,3 КБ.
- п.10 [Факт] metrics: таблица file_reads (только пути: Read, Grep, Glob; пути в командах Bash и коде REPL), отчёт «самые дорогие файлы» (чтения × размер). [Предп.] Путь, собранный из частей в скрипте, не виден; названный путь не всегда прочитан.
- п.7 Тесты: `tools/development/test_kb_structure.py` (в verify.py), правка `test_kb_db.py`. verify.py не запускался (по заданию).

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
