---
id: TASK-0214
title: "CI fix: capacity file-source test samples the real clock (MAX_AGE 3 h)"
tags: [task, platform, testing, done]   # kind, area (edit), state (draft → active → done); development/TAGS.yaml
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: orchestrator
category: tooling          # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: high         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: orchestrator
approved_at: 2026-11-26
approval_ref: "red CI on main (run 45906448513), orchestration.md §2"
review_ref: "reviewer: APPROVE 7dff6449 (code read; test run by orchestrator in worktree: 22 OK)"
created: 2026-11-26
---

# TASK-0214 — CI fix: capacity file-source test samples the real clock (MAX_AGE 3 h)

## Цель
Вернуть зелёный CI на `main`.

## Описание
`CapacityTest.test_file_source_shows_out` запускает `capacity.py` на реальных часах, а образец лимитов в тесте помечен 2026-11-26T19:30Z. После 22:30Z образец старше `llm_limits.MAX_AGE` (3 ч), состояние становится `unknown`, тест падает. Исправление: образец в этом тесте получает текущее время.

## Критерии приёмки
- [ ] `python3 -m unittest tools.llm_worker.tests.test_llm_limits` проходит в любой момент времени.
- [ ] CI на `main` зелёный.

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
