---
id: TASK-0198
title: "Platform error catalogue: errors.yaml, errors.py add/list/stats, auto capture from land, hooks, reviewer, CI; weekly DeepSeek digest (#584)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: tooling                # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: orchestrator
approved_at: 2026-11-26
approval_ref: "DECISIONS_LOG #584"
created: 2026-11-26
---

# TASK-0198 — Platform error catalogue: errors.yaml, errors.py add/list/stats, auto capture from land, hooks, reviewer, CI; weekly DeepSeek digest (#584)

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
Решение #584 (вариант C): каталог ошибок платформы, чтобы не повторять уже сделанные ошибки.

## Объём (tools/development, .claude/hooks, development/errors.yaml; без src/)
1. `development/errors.yaml`: записи ERR-NNNN с полями id, date, executor (orchestrator | agent:<type> | tool:<name> | model:<name>), kind (process | code | data | ui | comms | tooling | determinism), area, symptom, cause, found_by (owner | reviewer | codex | ci | hook | self), cost (low|medium|high), fix, prevention {kind: hook|test|skill|rule|memory, ref}, repeats (список дат), status (open | prevented | wontfix).
2. `tools/development/errors.py add|list|stats|repeat ERR-N|close ERR-N --prevention ...`: stats печатает топ повторов по kind/executor за 7 и 30 дней и долю повторов.
3. Автозапись (черновик со status open, без дублей по ключу): land.py exit 1/6, отказы guard_* хуков (короткая строка в .claude/.state/hook_refusals.jsonl, сводка в errors.py import), красная CI на main (через gh в watch), CHANGES reviewer (команда `errors.py add --from-review <file>`).
4. `errors.py digest`: еженедельный разбор повторов через `python3 -m tools.llm_worker` (DeepSeek, без секретов — gitleaks как у gate) в docs/reviews/errors-digest-YYYY-WW.md.
5. kb_check/registry: проверка схемы errors.yaml; SessionStart печатает число открытых.
6. Засеять 6 записей этой сессии: старая версия на кадрах (2 повтора), агент до коммита задачи (2), Codex input > 1 МБ, AskUserQuestion 2 ч таймаут → список в чате, набор кадров без проверки ветки.

## Критерии приёмки
- verify.py --full зелёный; тесты на add/stats/import/дубли; digest имеет --dry-run.
