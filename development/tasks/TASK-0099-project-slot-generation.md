---
id: TASK-0099
title: "Project slots reuse with generation (ADR-0005 #388)"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M1
agent: developer
category: core
priority: medium
needs_owner: false
depends_on: []
related: [ADR-0005, TASK-0098]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "#388, ADR-0005"
created: 2026-11-18
---

# TASK-0099 — Project slots reuse with generation (#388)

## Цель
Привести `ProductsLayout` и `ProductsSystem` к ADR-0005 п. 3 (ред. 3, ACCEPTED).

## Описание
Колонка `generation` (int, +1 при занятии слота); выбор слота — наименьший свободный индекс; handle = (slot, generation); устаревший handle — «сущности нет», команда с ним отклоняется с `why_not`; создание отклоняется только при 12 занятых. `generation` входит в StateHash и сейв. Выполнять после серии EXP-0007 (#355).

## Критерии приёмки
- [ ] Тесты: переиспользование слота, устаревший handle, отказ при 12 занятых, детерминизм L1/L2 (golden обновляются только с объяснением).
- [ ] `dotnet test Singularity.sln`, `lab selftest` зелёные.
