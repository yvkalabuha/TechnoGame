---
id: TASK-0174
title: "Institutes follow-up: iterative group walk (deep graph), built institute capacity by level, cooldown on same goal, no build charge after completion"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core           # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: high           # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0151]
related: []
approved_by: orchestrator # owner | orchestrator (orchestrator — только level: light)
approved_at: 2026-11-25
approval_ref: DEBT-0107
created: 2026-11-25
---

# TASK-0174 — Institutes follow-up: iterative group walk (deep graph), built institute capacity by level, cooldown on same goal, no build charge after completion

## Цель
Закрыть ошибки кода из DEBT-0107 до включения флагов (TASK-0171); игровые свойства меряет TASK-0173.

## Описание
1. `KnowledgeSystem.MarkPath`/`BuildGroups`: итеративный обход вместо рекурсии (Codex р.5 #1, high: падение по стеку на глубоком графе мода); тест на цепочке глубиной ≥ 10 000.
2. Страна, которой нет в сценарии, не ломает загрузку институтов (р.5 #2).
3. Тест потоков и повтора с успешной стройкой игрока (р.5 #3).
4. Своё имя системы институтов в диагностике записи (р.5 #4).
5. Мощность и содержание построенного института растут с уровнями (при слиянии #2); повторный приказ той же цели не перезапускает срок смены (#3); построенный институт не показывает плату стройки (#4).
6. Допуск n·1e-6 для пола 25 % в инварианте (р.3 #4).

## Критерии приёмки
- [ ] Каждый пункт с тестом, который падает без исправления
- [ ] Флаг выключен — эталоны без изменений
- [ ] reviewer APPROVE, второе ревью

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
