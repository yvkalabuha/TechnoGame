---
id: TASK-0207
title: "Release model: release.py rc|check|approve|release|rollback, RC report, /release skill, client build (#594)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: tooling                # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0200]
related: []
approved_by: owner
approved_at: 2026-11-26
approval_ref: "DECISIONS_LOG #594"
created: 2026-11-26
---

# TASK-0207 — Release model: release.py rc|check|approve|release|rollback, RC report, /release skill, client build (#594)

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
Сделано: tools/development/release.py (rc, check, approve, release, reject, rollback), tools/development/test_release.py (17 тестов на временном git-репозитории, зелёные). Тег rc/v создаётся локально, без push и без переписывания истории; клиент не запускается (кадры — печать команды shot_batch).
Не сделано (вне зоны developer): run-client.sh и skill /release — заготовка скрипта: tools/development/release_run-client.sh.proposed (Orchestrator кладёт в корень как run-client.sh).
Допущения: ошибки пишутся через errors.py add (флаги по полям TASK-0198; на main файла не нашёл, проверено только заглушкой в тестах); отказ errors.py — ошибка релиза. Клиентского export_presets.cfg нет — релиз запускается из снимка исходников тега (git archive).
Проверка: nice -n 10 python3 -m unittest tools/development/test_release.py. check/approve с реальными verify, gh, Godot не запускались.

## Цель
#594: релиз перед каждой партией владельца и не реже раза в неделю. План: docs/reviews/platform-metrics-release-2026-11-26.md §2.

## Объём (tools/development, .claude/skills/release — skill пишет Orchestrator)
1. `tools/development/release.py rc`: тег rc-YYYY-MM-DD-N на main (без переписывания истории), список задач DONE с прошлого тега v* (из реестра и git), открытые долги периода → docs/releases/rc-….md (шаблон).
2. `release.py check`: verify.py --full; статус CI трёх ОС на этом коммите (gh, запуск workflow_dispatch при отсутствии); Lab-регрессия (pace_check, slot_core gates); L2; сборка клиента Godot (экспорт, если шаблоны есть; иначе build) — результаты в отчёт RC; пачка кадров — только печать команды shot_batch (запуск с разрешения владельца делает Orchestrator).
3. `release.py approve <rc>` (после ответа владельца) → тег v0.N на том же коммите, заметки к релизу простым русским языком (docs/releases/v0.N.md), сборка клиента в ~/singularity-releases/v0.N (вне git), ./run-client.sh --release v0.N запускает её.
4. `release.py rollback`: запуск предыдущего v-тега; для исправления на main — список merge-коммитов задач RC для git revert (печать, не выполняет).
5. Каждый отклонённый RC/откат — запись в каталог ошибок (errors.py add).
6. Тесты на временном git-репозитории.
