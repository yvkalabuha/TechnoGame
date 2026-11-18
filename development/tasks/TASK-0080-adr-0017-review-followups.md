---
id: TASK-0080
title: "ADR-0017 review follow-ups (DEBT-0045 low items)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0017, TASK-0078]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "DEBT-0045, light follow-up of TASK-0078 review"
created: 2026-11-18
---

# TASK-0080 — ADR-0017 review follow-ups (DEBT-0045 low items)

## Цель
Закрыть низкие замечания ревью TASK-0078 из DEBT-0045.

## Описание
1. `Column.Load` внутри тика бросает `InvalidOperationException` (ADR-0017 п. 12: запись вне compute — только `Load` и только вне тика).
2. Приватный объект блокировки вместо `lock (this)` в `Column`.
3. Проверка потока-владельца в `Step` (ADR-0014 п. 5), а не только в `LastHash`.
4. `HashEveryTick` хеширует каждый непустой тик (ADR-0017 п. 4), а не только тики, публикующие view.
5. Комментарий в `HashAssertUsageTests` об ограничении лексического поиска.

## Критерии приёмки
- [ ] Пункты 1–5 сделаны, на 1, 3, 4 есть тесты.
- [ ] `dotnet test Singularity.sln` зелёный, `lab selftest` golden без изменений.

## REVIEW
- [ ] Code review (reviewer)

## Результат
