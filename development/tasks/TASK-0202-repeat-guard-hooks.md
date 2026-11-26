---
id: TASK-0202
title: "Hooks against repeats: Agent dispatch needs committed task in its status, shot launch checks branch has new model, kb_check stale errors cleaned (#591)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: general-purpose         # ответственный агент
category: tooling                # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-26
approval_ref: "DECISIONS_LOG #591"
review_ref: "reviewer: APPROVE f7deb1cd; codex: 3 rounds, waived per owner #592 (DEBT-0122)"
created: 2026-11-26
---

# TASK-0202 — Hooks against repeats: Agent dispatch needs committed task in its status, shot launch checks branch has new model, kb_check stale errors cleaned (#591)

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
#591: машина ловит повторяющиеся ошибки ERR-0001, ERR-0002, ERR-0005.

## Объём (.claude/hooks, .claude/settings.json, tools/development/test_hooks.py)
1. `guard_dispatch.py` (PreToolUse на Agent): если промпт называет TASK-NNNN и задача в HEAD main имеет status не APPROVED/IN_PROGRESS/REVIEW, или файл задачи в рабочем дереве отличается от HEAD (не закоммичен) — отказ с понятным текстом. Агенты только чтения (reviewer, critic, advocate, claude-code-guide, Explore) — пропуск.
2. `guard_shots.py`: перед запуском клиента с --shots/--map-frames — проверить, что текущая ветка/каталог содержит f12684f (git merge-base --is-ancestor) и не отстаёт от main по src/Singularity.Client.Spike; иначе отказ.
3. Чистка kb_check: пять старых ссылок (на отчёт RR-15 в DECISIONS_LOG строки 214/266/316/452 и на долг DEBT-39 в DECISIONS_LOG 330 и TECH_DEBT 44 — `kb_check.py check` их печатает) — исправить ссылки на правильные id или пометить [устар.] так, чтобы kb_check их принимал.
4. Тесты в test_hooks.py; запись профилактики в errors.yaml (errors.py close ERR-000N --prevention hook:...) после посадки — делает Orchestrator.
