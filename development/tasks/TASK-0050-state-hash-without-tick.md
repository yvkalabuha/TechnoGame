---
id: TASK-0050
title: "DEBT-0009: хеш состояния только по таблицам, tick отдельно (ADR-0006/0013)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: core
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0049]
related: []
approved_by: orchestrator
approved_at: 2026-11-14
approval_ref: "ADR-0006, ADR-0013 (ACCEPTED, #66); DEBT-0009; владелец 2026-11-14: «долги закончи»"
created: 2026-11-14
---

# TASK-0050 — DEBT-0009: хеш состояния только по таблицам, tick отдельно (ADR-0006/0013)

## Цель
Привести хеш состояния к ADR-0006/0013: хеш только по таблицам, номер тика — отдельное поле (DEBT-0009).

## Описание
- `StateHash.Compute` не включает tick; `run.json`/views/Session отдают tick отдельно.
- Все эталонные (golden) хеши пересчитываются **один раз** этой задачей. Причина каждого изменения — ADR-0006/0013, а не подбор. Новые значения сверяются: L1 (повтор) и L2 (потоки 1/4/8) дают одинаковые новые хеши; старые и новые хеши — в таблице в «Результат».
- Обновить места, где хеш документирован (спеки, EXP-записи — только ссылкой «хеши до TASK-0050»).

## Критерии приёмки
- [ ] Хеш не зависит от tick (тест: одинаковые таблицы при разном tick → один хеш).
- [ ] Все golden пересчитаны с L1/L2; полный `dotnet test` зелёный.
- [ ] Второе ревью Codex — до чистого accept (#181).

## REVIEW
- [ ] Code review (reviewer)
- [ ] Второе ревью (codex)
- [ ] Детерминизм L1/L2 (test-engineer)

## Результат
