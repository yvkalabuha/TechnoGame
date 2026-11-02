---
id: TASK-0006
title: "CI: сборка, тесты и проверка детерминизма L1/L2"
status: REVIEW 
level: light             # light | gate | gate+research
milestone: MS-M0
agent: orchestrator
category: tooling
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0004, TASK-0005]
related: [HYP-0001, EXP-0001]
approved_by: orchestrator
approved_at: 2026-11-02
approval_ref: light, owner-approved pilot plan (#12)
created: 2026-11-02
---

# TASK-0006 — CI: сборка, тесты и проверка детерминизма L1/L2

## Цель
Job в `.github/workflows/ci.yml`: `dotnet build/test` на ubuntu, windows, macos; L1/L2 на 20 seeds на ubuntu.

## Описание
`.github/` вне зон агентов — ведёт Orchestrator.

## Критерии приёмки
- [ ] CI падает при расхождении хешей L1 или L2 (проверено намеренной поломкой в ветке).

## REVIEW
- [ ] Code review (reviewer)
- [ ] Тесты (test-engineer)
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
CI: jobs `registry`, `dotnet` (ubuntu/windows/macos: build, test, Lab selftest), `analysis`. Хеш-расхождение L1/L2 роняет тесты Core/Kernel. Проверка на GitHub — при push PHASE E.
