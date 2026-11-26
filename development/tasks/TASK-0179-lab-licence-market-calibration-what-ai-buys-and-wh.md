---
id: TASK-0179
title: "Lab: licence market calibration, what AI buys and why, prices and availability within tolerance (#553)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: simulation-engineer
category: lab
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0176]
related: [TASK-0171, TASK-0178]
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#553"
review_ref: "train f12684f: reviewer APPROVE (a74ab0a, rounds to 8a4b96b7); codex 4 returns, waived by #432, DEBT-0115; market M2 in core (#560)"
created: 2026-11-25
tags: [task, simulation-lab, economy, done]
---

# TASK-0179 — Lab: licence market calibration, what AI buys and why, prices and availability within tolerance (#553)

## Цель
Рынок лицензий включён у всех стран, история 1945–1977 в допуске §8-d (#553, #554).

## Описание
TASK-0176: один рынок лицензий даёт 13 нарушений §8-d — Франция берёт лицензию G2 и выпускает G2 на 17–20 лет раньше, у Швеции меняется ранг 1977 в 9 из 10 seeds. Выяснить, что и почему покупает ИИ (кто продаёт, когда, по какой цене), и подобрать параметры рынка (цены, доступность по году, условия продавца) по проекту `docs/game-design/economy/production-lines-and-licence-market.md`. Пилот на 1 seed, серии ≤ 15 мин. Плюс находка: рынок отказывается работать в синтетическом мире больше 64 каталогов — описать или исправить.

## Критерии приёмки
- [ ] Причина с цифрами: список покупок ИИ по годам
- [ ] Параметры рынка: §8-d PASS на 10 seeds при включённом рынке (институты выключены или с правилом TASK-0178, если оно готово)
- [ ] Записка `docs/simulation/licence-market-calibration.md`; reviewer APPROVE

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
