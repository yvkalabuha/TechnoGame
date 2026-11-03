---
id: TASK-0005
title: "Simulation Lab: CLI, манифест, раннер матрицы"
status: DONE 
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
Lab CLI (simulation-engineer): команды run/matrix/hash/models/selftest, `ILabModelFactory`, тестовая модель `test.random-walk` (`model_is_test: true`), `docs/simulation/lab-cli.md`. selftest 13/13; критерии 1–3 выполнены (проверено Orchestrator: build 0 warnings, selftest 13/13). Открыто: интерфейс модели переносится в Core (TASK-0004); манифест EXP-0001 привести к схеме Lab 1.
После ревью (changes-requested): все 8 замечаний исправлены в TASK-0008 этап 1 (simulation-engineer), selftest 15/15 → 17/17 после этапа 2; закрыто Orchestrator 2026-11-03.
