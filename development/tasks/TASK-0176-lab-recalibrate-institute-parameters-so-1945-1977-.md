---
id: TASK-0176
title: "Lab: recalibrate institute parameters so 1945-1977 history is within tolerance with all levers on (A16, #549)"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: simulation-engineer
category: lab
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0173]
related: [TASK-0171]
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#549"
created: 2026-11-25
---

# TASK-0176 — Lab: recalibrate institute parameters so 1945-1977 history is within tolerance with all levers on (A16, #549)

## Цель
Разблокировать TASK-0171: все механики волны рычагов 1 включены у всех стран (включая страны ИИ), а годы 1945–1977 в допусках калибровки (#549, A16).

## Описание
На ветке TASK-0171 (`worktree-agent-a5360ae99120f7be5`, коммит 66a4919, флаги включены) институты у стран ИИ задерживают первые машины: СССР — первая машина в мес. 343–378 и позже вместо 247–307; у США позже G2. Найти причину (поведение ИИ при стройке институтов, цены, сроки, P-8, расписание стройки) и подобрать параметры институтов, при которых все допуски 1945–1977 проходят. Пилот на 1 seed, затем серия ≤ 15 мин (orchestration §6). Правило бюджетов не менять (Q70 ждёт владельца).

## Критерии приёмки
- [ ] Причина задержки названа с цифрами
- [ ] Новые параметры (данные или конфиг, не исторические ряды) — все допуски 1945–1977 PASS с флагами по умолчанию
- [ ] P-7 (TASK-0173) остаётся PASS на новых параметрах
- [ ] Записка `docs/simulation/institutes-recalibration.md`; reviewer APPROVE

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
