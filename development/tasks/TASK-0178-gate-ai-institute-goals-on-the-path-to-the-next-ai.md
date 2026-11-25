---
id: TASK-0178
title: "Gate: AI institute goals on the path to the next aircraft generation, Lab measurement with kappa 1.5 (#552)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M0
agent: simulation-engineer
category: lab
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0176]
related: [TASK-0171, TASK-0179]
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#552"
created: 2026-11-25
---

# TASK-0178 — Gate: AI institute goals on the path to the next aircraft generation, Lab measurement with kappa 1.5 (#552)

## Цель
Множитель «деньги в первую очередь» κ = 1,5 (#487) остаётся, а страны ИИ выбирают цели институтов так, что история 1945–1977 проходит допуск §8-d (#552, #554).

## Описание
TASK-0176 показал: ИИ заполняет все слоты целей самыми тяжёлыми научными узлами, самолёты и процессы вне целей теряют деньги (`docs/simulation/institutes-recalibration.md`). Предложить правило выбора целей ИИ: узлы на пути к следующему поколению изделия (requires-цепочка ближайшего G), с учётом процессов; записка с формулой, затем замер в Lab-песочнице (ключи LEVERS из TASK-0176) с κ = 1,5. Gate: critic + advocate + DeepSeek по записке до реализации в Session (задача developer после gate).

## Критерии приёмки
- [ ] Записка `docs/simulation/ai-institute-goals.md`: правило, почему оно правдоподобно, чем отличается от игрока
- [ ] Замер: институты (κ = 1,5) с новым правилом проходят §8-d на 10 seeds; P-7 PASS
- [ ] Gate-ревью записано в задаче, «Разногласия» не скрыты

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
