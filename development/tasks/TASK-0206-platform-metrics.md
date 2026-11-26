---
id: TASK-0206
title: "Platform metrics in PostgreSQL: sessions, agents, LLM calls, landings, task flow, owner waits, errors; kb_db report (#593)"
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
approval_ref: "DECISIONS_LOG #593"
created: 2026-11-26
---

# TASK-0206 — Platform metrics in PostgreSQL: sessions, agents, LLM calls, landings, task flow, owner waits, errors; kb_db report (#593)

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
- tools/development/metrics.py: collect (идемпотентно, ключи по id сообщения/файла/ledger/коммита) и report (период, --compare A..B C..D). Таблицы sessions, llm_calls, landings, task_flow, owner_waits в схеме checkout; deleted_at; errors и agent_runs берутся из kb_db. В базу идут только числа, id, даты, короткие метки.
- tools/development/land.py: каждая посадка дописывает строку в .claude/.state/landings.jsonl (задача, попытка, код, время шагов review и verify).
- tools/development/test_metrics.py: 10 тестов (парсеры на фикстурах, журнал посадок, база с пропуском без PostgreSQL). Прогон вместе с test_kb_db: 39 OK.
- Проверка на реальных данных: collect дал 1080 сессий, 176 вызовов LLM, report за 2026-11-19..27 строится.
- Не проверено: компаратор --compare на реальных данных; landings и owner_waits пусты до первых посадок с новым land.py и до появления дат "Закрыт" в OPEN_QUESTIONS.

## Цель
#593: метрики работы платформы для поиска узких мест и оценки изменений. План: docs/reviews/platform-metrics-release-2026-11-26.md §1.

## Объём (tools/development; без src/)
1. `tools/development/metrics.py collect|report` поверх kb_db (PostgreSQL, схема main): журнальные таблицы только с добавлением и мягким удалением — sessions, agent_runs, llm_calls, landings, task_flow, owner_waits; errors берутся из каталога.
2. Источники: журналы Claude Code ~/.claude/projects/-var-www-gameDev/*.jsonl и вложенные журналы агентов (модель, время, usage: input/output/cache tokens) — терпимый парсер, только числа и id, никаких текстов промптов/ответов; .claude/.state/agents.jsonl; tools/llm_worker/runs/*.json и ledger (провайдер, вид, задача, токены, стоимость, вердикт, число находок); land.py — добавить строку в .claude/.state/landings.jsonl (задача, попытка, код, время шагов verify) на каждый запуск; git log файлов задач — переходы status; OPEN_QUESTIONS/DECISIONS — время от вопроса до ответа (по датам, где есть).
3. Идемпотентный сбор (повторный collect не дублирует: ключи по id сообщения/записи).
4. `report` за период: токены и время на задачу и тип агента; круги reviewer/Codex на задачу; топ ошибок по исполнителю; где задачи стоят дольше (ожидание ревью, Codex, владельца, CPU); сравнение двух периодов (`--compare 2026-11-19..2026-11-26 2026-11-26..2026-12-03`) — для #586 (Fable) и #592.
5. Тесты на фикстурах; тесты базы пропускаются без PostgreSQL.
