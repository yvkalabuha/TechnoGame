---
id: TASK-0093
title: "Lab matrix validity and size fields; registry golden_set_hash for matrix EXPs (ADR-0008, #374)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: simulation-engineer
category: lab
priority: low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0008]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "#374, ADR-0008 rev 3"
created: 2026-11-18
---

# TASK-0093 — Lab matrix validity and size fields; registry golden_set_hash for matrix EXPs (ADR-0008, #374)

## Цель
Поля валидности и объёма матриц Lab по ADR-0008 ред. 3 (#374).

## Описание
1. В `matrix.json`: `csv_bytes` (суммарный объём CSV), флаг `valid` (≥ 2 значений `threads`, `thread_mismatch = 0`, непустой `golden_set_hash`); длительность анализа — в журнал эксперимента (`analysis_seconds`).
2. Тесты Lab на новые поля (если тестовая папка вне зоны — список тестов в «Результат» для test-engineer).
`registry.py` (поле `golden_set_hash` в обязательных для матричных EXP) правит Orchestrator — опишите нужное правило в «Результат».

## Критерии приёмки
- [ ] Поля пишутся; selftest 48/48.

## Результат
- [Факт] `MatrixRunner` пишет в `matrix.json`: `csv_bytes` (размер `runs.csv` + объединённых CSV), `valid`, `invalid_reasons`; в лог — `matrix: valid=true|false (...)`. Условие — `MatrixRunner.InvalidReasons` (ADR-0008 п. 6: ≥ 2 разных `threads`, `thread_mismatch` = 0, `problems` пуст, нет `--point`/`--seeds`, `golden_set_hash` не null).
- [Факт] selftest 49/49 (добавлена проверка `matrix: valid flag`; было 48). Ручная проверка: матрица `threads [1]` → `valid: false`, `[1, 4]` → `valid: true`.
- `analysis_seconds`: печатает скрипт анализа, хранится в EXP-записи (поле журнала; в код Lab не входит). [Предл.] добавить строку `analysis_seconds:` в `development/templates/experiment.md` (зона Orchestrator).
- [Предл.] для test-engineer (`tests/Singularity.Lab.Tests`): тест `InvalidReasons` по каждому условию; интеграционный `matrix` с `threads [1]` → `valid=false`, `csv_bytes` = сумма размеров CSV.
- Правило `registry.py` (Orchestrator): EXP со `status: DONE` и `runner: matrix` во frontmatter обязан иметь непустой `golden_set_hash` (в дополнение к `REPRO_FIELDS`); если матрица невалидна — `golden_set_hash` не заполнять и outcome не DONE.
