---
id: TASK-0024
title: "T7: why_not по контракту §6 и 4 сценария «первый слой»"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0021]
related: [HYP-0002]
approved_by: orchestrator
approved_at: 2026-11-03
approval_ref: "спека v0.12 §11 T7; контракт v2.4 r3 §6"
created: 2026-11-03
---

# TASK-0024 — T7: why_not по контракту §6 и 4 сценария «первый слой»

## Цель
Реализовать `why_not(actor, target)` по контракту слоёв §6 и 4 сценария, где первым звеном по очереди оказывается каждый слой (T7 спеки). Это вход для правил ИИ T6d и прокси читаемости §5.6.

## Описание
- Чистая функция чтения состояния слоёв знания, способности и изделия (без записи, без RNG); формат звеньев, `eta`, `limiter`, рычаги — по контракту §6 (в т.ч. v2.4: снятый проект не даёт звеньев; F10 — `limiter` процесса без разрыва).
- Место в коде — по `docs/architecture/SLICE_COMPOSITION.md` (чтение снимка; Session отдаёт результат наружу). Не встраивать в compute слоёв.
- Задача идёт параллельно с T6b в отдельном worktree: не менять `PolicySystem`, `ToolingInvest`, `IProductsInputs`.

## Критерии приёмки
- [ ] Контракт §6 исполнен полностью; каждое расхождение с ним записано в «Результат».
- [ ] 4 сценария «первый слой» (спека §6.4) — тесты.
- [ ] Валидатор «рычаги соответствуют сборке» (§5.6 d).
- [ ] Детерминизм: результат не зависит от порядка обхода и числа потоков; нет float.

## Тесты
Юнит-тесты звеньев; 4 сценария; тест на `ENGINEERS` при `granted/n < 0,9` (§5.6 b); архитектурные тесты.

## Связанные документы
`docs/architecture/LAYER_CONTRACT.md` v2.4 r3 §6; спека v0.12 §5.6, §6.4, §11 T7; `docs/game-design/PROCESS_DEVELOPMENT.md` §10 (слова `limiter`), `docs/game-design/STATE_WORDS.md` (если есть).

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
