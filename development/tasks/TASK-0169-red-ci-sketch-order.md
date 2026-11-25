---
id: TASK-0169
title: "Red CI: sketch validates task id and count before opening the sandbox Box"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: tooling        # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: high           # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: orchestrator # owner | orchestrator (orchestrator — только level: light)
approved_at: 2026-11-25
approval_ref: red CI on main (orchestration §2)
created: 2026-11-25
---

# TASK-0169 — Red CI: sketch validates task id and count before opening the sandbox Box

## Цель
CI на `main` красный с 2026-11-25 02:34: `test_sketch.test_prompt_must_be_in_repo_and_count_bounded` получает `sandbox_unavailable` вместо `bad_task` на раннере без bwrap.

## Описание

## Критерии приёмки
- [x] `run_sketch` проверяет номер задачи и число картинок до открытия Box; песочница не ослаблена.
- [x] Новый тест `test_task_and_count_checked_before_sandbox` падает без исправления.
- [ ] CI на `main` зелёный.

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
Ветка `worktree-agent-ab3ee819a5c84310d`, коммит a5c34e2: `_check_task_count` в `tools/llm_worker/sketch.py`; `test_sketch.py` 69/69.
