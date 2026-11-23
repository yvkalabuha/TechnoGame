---
id: TASK-0127
title: "Open Codex findings after TASK-0125 landing (#432)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M05
agent: test-engineer
category: core
priority: medium
needs_owner: false
depends_on: [TASK-0125]
related: [TASK-0125]
approved_by: orchestrator
approved_at: 2026-11-22
approval_ref: "#432 (light)"
created: 2026-11-22
---

# TASK-0127 — Open Codex findings after TASK-0125 landing (#432)

## Цель
По решению владельца #432 задача TASK-0125 слита после круга, на котором ревьюер дал APPROVE; открытые замечания второго ревью Codex перенесены сюда, чтобы не потеряться.

## Описание
Замечания из `tools/llm_worker/runs/review-TASK-0125-265ec51e-codex.json`:
- [high] `tests/Singularity.Architecture.Tests/NoMutableStaticsTests.cs:178` — The fresh-array exemption lets a writable static buffer pass both architecture scans.
- [medium] `tests/Singularity.Session.Tests/TwoWorldsOnOnePoolTests.cs:116` — The concurrent test can pass without overlapping the tick operations that use the shared buffer.
- Ревьюер (круг 7): добавить `Marshal` и `GCHandle` в запрещённые классы; поправить причину для `ViewBuilder.Inputs` («захвачен только лямбдой `Parallel.For`, не переживающей вызов»).
- Классификация по #432: находка «свежий массив» касается полноты страж-теста, а не дефекта детерминизма в коде ядра; поэтому перенесена, а не исправлена до слияния.

Каждое замечание: исправить или отклонить с причиной (в «Разногласия»). Высокие замечания по детерминизму и сохранению по #432 не переносятся — их в этом списке нет.

## Критерии приёмки
- [ ] Каждое замечание закрыто: исправлено с тестом или отклонено с причиной.
- [ ] reviewer APPROVE; второе ревью по правилу #432.
