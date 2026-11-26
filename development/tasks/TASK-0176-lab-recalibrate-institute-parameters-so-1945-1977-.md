---
id: TASK-0176
title: "Lab: recalibrate institute parameters so 1945-1977 history is within tolerance with all levers on (A16, #549)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
review_ref: "train f12684f: reviewer APPROVE (a74ab0a, rounds to 8a4b96b7); codex 4 returns, waived by #432, DEBT-0115; Lab keys landed with the train"
created: 2026-11-25
tags: [task, simulation-lab, science, done]
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

## Ревью (2026-11-25)
- reviewer: APPROVE коммита 318e867 (ключи Lab LEVERS и т. п., версия поверхности 27, записка). Lab.Tests 229/229.
- Критерии задачи выполнены не полностью: с флагами по умолчанию нет PASS (рынок лицензий: 13 нарушений §8-d), P-7 при κ = 1,02 проверен только в песочнице. Владелец выбрал дальнейший путь: #552 (правило целей ИИ, TASK-0178), #553 (рынок, TASK-0179), #554 (допуск §8-d).
- Ветка не вливается отдельно: в ней слияние непроверенного WIP TASK-0171 (66a4919). Вливается вместе с TASK-0171 после TASK-0178 и TASK-0179.
- Перед опорой на цифры повторить TASK-0176-inst и P-7 на чистой сборке (замечание reviewer); мелочи: ссылка §4→§5 в institutes_recal.py, описание манифеста плацебо, источник Nene/Vampire во Франции (передано в TASK-0179).
