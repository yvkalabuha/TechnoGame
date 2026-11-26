---
id: TASK-0197
title: "Autopilot tooling: watch acts on silent and finished-unsubmitted agents, shared shot batch collector, autopilot stop flag (#583)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: tooling                # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: orchestrator
approved_at: 2026-11-26
approval_ref: "DECISIONS_LOG #583"
created: 2026-11-26
---

# TASK-0197 — Autopilot tooling: watch acts on silent and finished-unsubmitted agents, shared shot batch collector, autopilot stop flag (#583)

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
Сделано (кроме части в .claude/, вне зоны developer):
- `watch.py --actions` (+ `--ping-hours 2`, `--stop-hours 3`): ping / stop+redispatch / to-review, только чтение; чистая функция `agent_actions`.
- `shot_batch.py`: поле `shots: {branch, only}` в задаче; проверка f12684f и отставания по src/Singularity.Client.Spike; печатает команды, ничего не запускает.
- `autopilot.py on|stop|status`; `capacity.py` при stop печатает `free agent slots 0 (autopilot stopped)`.
- Тесты: `tools/development/test_hooks.py` (WatchActionsTest, AutopilotTest, ShotBatchTest).
- Не сделано (вне зоны): вывод состояния в `.claude/hooks/session_context.py`, правка SKILL.md. См. отчёт, OPEN.
- Проверка: test_hooks.py 20 тестов OK; verify.py см. отчёт.

## Цель
Решение #583: автопилот сам следит за агентами, собирает одну пачку кадров и умеет останавливаться.

## Объём (tools/development, .claude/hooks; без src/)
1. `watch.py --actions`: для каждого агента из .claude/.state/agents.jsonl печатать действие: SILENT > 2 ч без stop — `ping`; > 3 ч — `stop+redispatch`; агент со stop-записью, у чьей задачи статус IN_PROGRESS и ветка с коммитами не в main — `to-review`. Пороги параметрами. Только чтение.
2. `tools/development/shot_batch.py`: собрать по задачам в REVIEW/IN_PROGRESS категории client, которые ждут кадров (поле `shots:` в frontmatter задачи: ветка + `--only` маска), одну пачку: проверка, что каждая ветка содержит f12684f (новая модель) и не отстаёт от main по client; печать команд пачки. Ничего не запускает.
3. `tools/development/autopilot.py on|stop|status`: файл .claude/.state/autopilot.json; capacity.py при stop печатает `free agent slots 0 (autopilot stopped)`; session_context.py печатает состояние.
4. Тесты в tools/development/test_hooks.py (или рядом) на каждый пункт.

## Критерии приёмки
- `python3 tools/development/verify.py` зелёный; новые тесты падают без изменения.
- Документация в начале каждого скрипта.
