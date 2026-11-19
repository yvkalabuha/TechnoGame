---
id: TASK-0120
title: "Core emits a per-command verdict; commands keyed by handle"
status: PROPOSED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate              # light | gate | gate+research
milestone: MS-M05
agent: systems-architect        # ответственный агент
category: core           # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0115]
related: [TASK-0115, TASK-0118]
approved_by:             # owner | orchestrator (orchestrator — только level: light)
approved_at:
approval_ref:
created: 2026-11-19
---

# TASK-0120 — Core emits a per-command verdict; commands keyed by handle

## Цель
Вердикт по каждой внешней команде выдаёт ядро, а не сессия; команды хранятся по дескриптору проекта (слот и поколение), а не по слоту. Это закрывает замечания Codex, отложенные при слиянии TASK-0115 (O-019).

## Описание
После TASK-0115 сессия сама повторяет порядок фаз ядра, чтобы сообщить об отказе команды (`SliceSession.ReconcileOutcomes`), а `CommandState` хранит одну запись запуска серии и закрытия линии на (актор, слот, месяц). Отсюда отложенные случаи:
- A. Запись с большим поколением, которое не разрешается (подделанный или битый ввод), вытесняет действующую команду (`CommandState.cs:190`).
- B. Сессия пишет «принято» для команды парка, которую ядро отклоняет после закрытия линии в том же месяце (`SliceSession.cs:297`); то же для переноса линии родителя при запуске производного проекта (`ProductsSystem.cs:1434`).
- C. Два кандидата на закрытие одной линии дают лишний отказ `StaleHandle` (`ProductsSystem.cs:1346`).
- D. Проекционный тест сверяет только конечные точки прогонов.
- Вытесненная команда не входит в счётчик `prd.rejected.StaleHandle`.

Шаги:
1. systems-architect: записка — формат вердикта (колонка или массив на команду журнала, без аллокаций на тике), хранение команд по дескриптору, влияние на схему и хеш, порядок применения. Gate: critic, advocate, третий голос.
2. developer: реализация одним «поездом схемы»; `ReconcileOutcomes` удаляется.
3. test-engineer: тест счётчика для проекта в серии; сверка проекций по месяцам, а не только в конце.

## Критерии приёмки
- [ ] `Outcomes` в записи и в `Replay` строятся только из вердиктов ядра.
- [ ] Случаи A–C покрыты тестами, которые падают на коде TASK-0115.
- [ ] Ревьюер APPROVE, Codex без открытых высоких замечаний.

## Тесты
Тесты ядра и сессии; `python3 tools/development/verify.py`.

## Связанные документы
`docs/architecture/notes/end-to-end-project-handle.md`, `docs/decisions/ORCH_DECISIONS.md` (O-019), `tools/llm_worker/runs/review-TASK-0115-1c59e2a4-codex.json`.

## REVIEW

## Результат
