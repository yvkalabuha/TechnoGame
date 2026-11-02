---
id: TASK-0005
title: "Simulation Lab: CLI, манифест, раннер матрицы"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: simulation-engineer
category: lab
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0001]
related: [HYP-0001, EXP-0001]
approved_by: owner
approved_at: 2026-11-02
approval_ref: DECISIONS_LOG #12–#15; EXP-0001 «Ревью»
created: 2026-11-02
---

# TASK-0005 — Simulation Lab: CLI, манифест, раннер матрицы

## Цель
`src/Singularity.Lab`: консольный запуск прогонов по манифесту, независимо от конкретной модели.

## Описание
- `run --manifest <json> --seed N --threads T --out <dir>`; `matrix --manifest <json> --out <dir>` — по процессу на прогон, параллельно.
- Выход: `run.json` (core_commit, manifest_hash, data_hash, seed, threads, golden_hash, время), CSV метрик.
- Модель подключается через интерфейс; до TASK-0004 — тестовая модель из Kernel-тестов или простая заглушка, **явно помеченная как тестовая**.
- JSON-манифест вместо TOML — отклонение от ADR-0008 для пилота (EXP-0001 «Ревью» п. 11).

## Критерии приёмки
- [ ] `manifest_hash` стабилен (канонизация JSON).
- [ ] Повторный `run` с тем же seed даёт тот же `golden_hash`.
- [ ] `matrix` из 100 прогонов тестовой модели завершается без ошибок; `lab/results/` в `.gitignore`.

## REVIEW
- [ ] Code review (reviewer)
- [ ] Тесты (test-engineer)
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
