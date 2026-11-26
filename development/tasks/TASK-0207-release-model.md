---
id: TASK-0207
title: "Release model: release.py rc|check|approve|release|rollback, RC report, /release skill, client build (#594)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
review_ref: "reviewer: APPROVE e8fdad45; codex: 3 rounds, waived per owner #592 after destructive/permit fixes (DEBT-0121)"
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
Сделано: tools/development/release.py (rc, check, approve, release, run, reject, rollback) и tools/development/test_release.py (36 тестов на временном git-репозитории с настоящим errors.py; зелёные). Теги создаются локально, без push и без переписывания истории; клиент сам не запускается.
- check в временном worktree на коммите RC: verify --full, CI трёх ОС (gh), pace_check --strict (WARN блокирует), серии Lab TASK-0187 (core, player, L2 threads 1+2) с воротами slot_core; результаты Lab принимаются только если у каждого прогона core_commit = коммит RC и дерево не грязное; коммит записан в отчёт («Проверено на коммите»). Сборка клиента. Кадры — печать команды (git worktree add ... <rc>, затем разрешение владельца).
- approve: нужны все шесть обязательных строк PASS на этом коммите и статус отчёта «НА ПРОВЕРКЕ»; сначала сборка клиента в ~/singularity-releases/vX.N (через временную папку и rename; существующая или symlink-папка отказ, ничего не удаляется), только после успеха тег vX.N, заметки и статус. Сбой сборки: ни тега, ни заметок, запись в каталог ошибок.
- Запуск релиза: python3 tools/development/release.py run vX.N (печатает команду снимка), с --exec запускает; без правки корневого скрипта запуска.
- Ошибки пишутся через настоящий errors.py add (причина в symptom, RC в cause, id ERR в отчёте RC); отчёт RC содержит метрики периода из metrics.py report, если он есть (иначе пометка).
Первый релиз без v-тега: `check --pace-base self` (или тег/sha самого RC) — RC сам себе база, pace PASS по определению, в отчёте помечено. CI: PASS только при success всех трёх ОС на коммите RC. approve отказывает RC старше последнего v-тега; документы пишутся во временные файлы до сборки и тега. Экспортированный клиент запускается с `--main-pack` своего pck. run-client.sh намеренно заменён `release.py run`.
Проверка: nice -n 10 python3 -m unittest tools/development/test_release.py (один прогон). Новые тесты на прежнем release.py (cae5e4d4): 27 из 36 падают.
Не проверено: реальные verify --full, gh, Godot, pace_check и серии Lab из check (в тестах подставлены); формат generated-манифеста L2 (RC-L2.json: threads [1,2], seeds 2, серия SLOTS) и флаги errors.py в реальной работе кроме тестов; запуск metrics.py report с PostgreSQL.

## Цель
#594: релиз перед каждой партией владельца и не реже раза в неделю. План: docs/reviews/platform-metrics-release-2026-11-26.md §2.

## Объём (tools/development, .claude/skills/release — skill пишет Orchestrator)
1. `tools/development/release.py rc`: тег rc-YYYY-MM-DD-N на main (без переписывания истории), список задач DONE с прошлого тега v* (из реестра и git), открытые долги периода → docs/releases/rc-….md (шаблон).
2. `release.py check`: verify.py --full; статус CI трёх ОС на этом коммите (gh, запуск workflow_dispatch при отсутствии); Lab-регрессия (pace_check, slot_core gates); L2; сборка клиента Godot (экспорт, если шаблоны есть; иначе build) — результаты в отчёт RC; пачка кадров — только печать команды shot_batch (запуск с разрешения владельца делает Orchestrator).
3. `release.py approve <rc>` (после ответа владельца) → тег v0.N на том же коммите, заметки к релизу простым русским языком (docs/releases/v0.N.md), сборка клиента в ~/singularity-releases/v0.N (вне git), ./run-client.sh --release v0.N запускает её.
4. `release.py rollback`: запуск предыдущего v-тега; для исправления на main — список merge-коммитов задач RC для git revert (печать, не выполняет).
5. Каждый отклонённый RC/откат — запись в каталог ошибок (errors.py add).
6. Тесты на временном git-репозитории.
