---
id: TASK-0212
title: "CI fix: test_kb_structure LandRolloverTest sets git identity in its temporary repo"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: tooling          # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: high         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: orchestrator
approved_at: 2026-11-26
approval_ref: "red CI on main (run 45897427365), orchestration.md §2"
created: 2026-11-26
---

# TASK-0212 — CI fix: test_kb_structure LandRolloverTest sets git identity in its temporary repo

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
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->

## Цель
CI красный: test_kb_structure.LandRolloverTest.test_rollover_is_committed_after_landing падает на машине CI — во временном репозитории нет git user.name/user.email ("fatal: empty ident name"). Задать имя и почту во временном репозитории теста (git config user.name/user.email локально, или GIT_AUTHOR_*/GIT_COMMITTER_* в env вызова). Проверить, не коммитит ли так же land.py сам на машине без идентичности (rollover на main локально идентичность имеет — менять land.py только если нужно).
