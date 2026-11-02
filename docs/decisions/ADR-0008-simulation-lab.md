---
id: ADR-0008
title: "Simulation Lab: прогоны, хранение, анализ"
status: PROPOSED
date: 2026-11-02
supersedes:
superseded_by:
related: [RR-0002]
approved_by:
approved_at:
approval_ref:
---

# ADR-0008 — Simulation Lab: прогоны, хранение, анализ

## Решение (предложение)
- Один прогон = один процесс `Singularity.Lab`. Параллелизм прогонов — на уровне процессов.
- Манифест эксперимента — TOML в `lab/`: версия ядра, версия данных, параметры, sweep, seeds, число потоков.
- Метрики — Parquet; каталог прогонов — DuckDB; анализ — Python (polars, DuckDB).
- Регрессия: golden-run по хешам таблиц + сравнение распределений Monte Carlo (квантили, KS-тест, bootstrap).
- `lab/results/` не коммитится; в git — манифесты, golden-хеши, выводы в EXP-файлах.

## Проверка
`registry.py check` требует полей воспроизводимости у DONE-экспериментов.
