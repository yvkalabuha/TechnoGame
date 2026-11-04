---
id: TASK-0030
title: "F17b: передача линии по рабочему выходу годных (удержание r_keep)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0029]
related: [HYP-0002, EXP-0003]
approved_by: owner
approved_at: 2026-11-04
approval_ref: "DECISIONS_LOG #138, #160; спека v0.20 A41, §11 F17b"
created: 2026-11-04
---

# TASK-0030 — F17b: передача линии по рабочему выходу годных (удержание r_keep)

## Цель
ИИ закрывает рабочую линию только когда новая серия выходит на рабочий выход годных (#160); до этого рабочая линия держится на темпе `r_keep`, остальные деньги закупок — новой линии.

## Описание
По спеке `docs/simulation/slice-jet-aviation.md` v0.20: A41 (правило F17b, `LineRate(W) = min(r_AI, r_keep)`), A35, A37, A40, A42.4 п.1, §4, §11 строка «F17b» и «F17b — приёмка» (10 пунктов); переключатель `LINE_CLOSE` {`YPROD`, `ADMIT`} (по умолчанию YPROD). Не входит: эксперимент T6e2 (simulation-engineer после задачи).

## Критерии приёмки
- [ ] Все 10 пунктов «F17b — приёмка».
- [ ] Общие пункты T6 (тесты, ADR-0003, нет RNG в Industry/Products, L1/L2); golden с причиной «F17b».
- [ ] Результат (в ответе Orchestrator) — печать seeds 1…20: выпуск по окнам, сосуществование двух линий (месяцы), закрытия по `y_prod` и по A40.

## Тесты
По §11 F17b.

## Связанные документы
Спека v0.20; `development/experiments/EXP-0003-t6e-tooling-target.md`; DECISIONS_LOG #138, #160.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
