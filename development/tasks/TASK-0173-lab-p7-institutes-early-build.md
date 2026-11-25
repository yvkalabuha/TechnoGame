---
id: TASK-0173
title: "Lab: P-7 build-everything-early does not dominate (years of key nodes) and project limit check, before flags on"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: simulation-engineer         # ответственный агент
category: lab            # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: high           # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0151]
related: []
approved_by: orchestrator # owner | orchestrator (orchestrator — только level: light)
approved_at: 2026-11-25
approval_ref: O-081
created: 2026-11-25
---

# TASK-0173 — Lab: P-7 build-everything-early does not dominate (years of key nodes) and project limit check, before flags on

## Цель
Проверить P-7 из `docs/game-design/civilization/research-institutes-priorities.md` §161 на коде TASK-0151 до включения флагов (TASK-0171).

## Описание
Lab-проба: игрок, строящий все институты при первой возможности (ref−10), против «в исторический год ±5». Мера — средний год ключевых узлов (список game-designer). Проходит, если выигрыш раннего пути ≤ 1 года. Заодно: число одновременных проектов стройки не ограничено (второе ревью TASK-0151 #2) — показать, не даёт ли это перекос; если даёт, предложить лимит. Пилот на 1 seed, затем серия ≤ 15 мин (orchestration §6).

## Критерии приёмки
- [ ] P-7 измерен, вердикт PASS/FAIL с цифрами
- [ ] Вывод по лимиту проектов

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
