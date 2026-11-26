---
id: TASK-0203
title: "Skills /shots, /recheck, /error for the Orchestrator (#591)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: orchestrator         # ответственный агент
category: tooling                # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-26
approval_ref: "DECISIONS_LOG #591"
created: 2026-11-26
---

# TASK-0203 — Skills /shots, /recheck, /error for the Orchestrator (#591)

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
#591: три skill для оркестратора.

## Объём (.claude/skills)
- `/shots`: shot_batch.py → проверка веток → один AskUserQuestion на пачку → client_permit grant → прогон --only по задачам → revoke → llm_worker visual (agy) → ui-art-director → /show.
- `/recheck`: повторная проверка тем же рецензентом (SendMessage, иначе новый reviewer) с перечнем изменений с прошлого вердикта и списком закрытых находок.
- `/error`: errors.py add/repeat по описанию; предложить профилактику (hook > test > skill > rule > memory).
