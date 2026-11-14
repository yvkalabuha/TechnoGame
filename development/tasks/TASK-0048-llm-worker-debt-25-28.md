---
id: TASK-0048
title: "llm_worker: DEBT-0025 (ledger repair) и DEBT-0028 (записи ревью)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: tooling
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0045]
related: []
approved_by: orchestrator
approved_at: 2026-11-14
approval_ref: "Владелец 2026-11-14: «долги закончи»; DEBT-0025, DEBT-0028"
created: 2026-11-14
---

# TASK-0048 — llm_worker: DEBT-0025 (ledger repair) и DEBT-0028 (записи ревью)

## Цель
Закрыть DEBT-0025 и DEBT-0028 (`development/TECH_DEBT.md`).

## Описание
- DEBT-0025: команда `python3 -m tools.llm_worker ledger repair` — пересчёт `ledger.head` по проверенной цепочке записей под `flock`. Повреждённый хвост не удаляется, а отчитывается; запуск только явной командой. Тест на сбой между записью строки и обновлением head.
- DEBT-0028: запись ревью получает `created_at` (UTC) и `round`; повторное ревью того же head не перезаписывает прежнюю запись. Команда отметки подтверждённых находок (`review-confirm <record> --finding N --confirmed|--rejected`) пишет поле `confirmed`; метрики берут раунды и подтверждения из этих полей. Сначала проверяется `bwrap:`, потом лимит (`quota` в строке bwrap — не `codex_limit`). `sandbox_unavailable` пишется в `codex_attempts.jsonl`. `related: true` не даёт id. `_review_rounds` устойчив к нестандартным `findings`.

## Критерии приёмки
- [ ] Тест на каждый пункт; все тесты `llm_worker` зелёные.
- [ ] Второе ревью Codex — до чистого accept (#181).

## REVIEW
- [ ] Code review (reviewer)
- [ ] Второе ревью (codex)

## Результат
