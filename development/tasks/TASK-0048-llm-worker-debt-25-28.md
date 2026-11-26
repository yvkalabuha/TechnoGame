---
id: TASK-0048
title: "llm_worker: DEBT-0025 (ledger repair) и DEBT-0028 (записи ревью)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
tags: [task, platform, external-llm, done]
---

# TASK-0048 — llm_worker: DEBT-0025 (ledger repair) и DEBT-0028 (записи ревью)

## Цель
Закрыть DEBT-0025 и DEBT-0028 (`development/TECH_DEBT.md`).

## Описание
- DEBT-0025: команда `python3 -m tools.llm_worker ledger repair` — пересчёт `ledger.head` по проверенной цепочке записей под `flock`. Повреждённый хвост не удаляется, а отчитывается; запуск только явной командой. Тест на сбой между записью строки и обновлением head.
- DEBT-0028: запись ревью получает `created_at` (UTC) и `round`; повторное ревью того же head не перезаписывает прежнюю запись. Команда отметки подтверждённых находок (`review-confirm <record> --finding N --confirmed|--rejected`) пишет поле `confirmed`; метрики берут раунды и подтверждения из этих полей. Сначала проверяется `bwrap:`, потом лимит (`quota` в строке bwrap — не `codex_limit`). `sandbox_unavailable` пишется в `codex_attempts.jsonl`. `related: true` не даёт id. `_review_rounds` устойчив к нестандартным `findings`.

## Критерии приёмки
- [x] Тест на каждый пункт; все тесты `llm_worker` зелёные.
- [x] Второе ревью Codex — до чистого accept (#181).

## REVIEW
- [x] Code review — заменено двумя раундами Codex (tools/** — второе ревью обязательно); отдельный reviewer не запускался
- [x] Второе ревью (codex, head 8deb17cb) — accept: 2 medium → DEBT-0030; report sha256 37363fdae70773d67c7c8ebfd451df5ca7e49bab1db665768d52c9e21d3f5144

## Результат
developer, 2026-11-14 (записал Orchestrator): `ledger repair` (проверка цепочки, `*.bak-<UTC>`, отказ без изменений при сомнении); записи ревью с `created_at`/`round`, без перезаписи (`-n2`); `review-confirm` → sidecar `<record>.confirm.json` (sha256 записи неизменен); метрики по (`round`, `created_at`, имя); `bwrap:` раньше лимита; `sandbox_unavailable` в журнале попыток; `related: bool` отклоняется. 276 тестов OK.
