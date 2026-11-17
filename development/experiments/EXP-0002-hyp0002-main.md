---
id: EXP-0002
title: "EXP-0002: основной эксперимент HYP-0002 (срез «реактивная авиация»)"
status: DONE          # PLANNED | RUNNING | DONE | FAILED
hypothesis: HYP-0002
kind: scenario           # batch | scenario | sweep | monte-carlo | regression | balance | benchmark
related: []
# Воспроизводимость — обязательно для DONE. Lab записывает сама.
core_commit: 550b5d56179e6d4577bb63ff9ca267348b9d59e8   # binary build; frozen build_commit 792aaa2 (ancestor, empty code diff); #265
data_hash: efcd0984e9d8a6a8e7bf06cba7a5e89b82747e1cfacdf67b7d7d8b2f889efa9f
manifest_hash: c3c2d471cfb3bdaff6cb2128cf6a550c8cd50c885f6732f08de99aa8c15f9f14
manifest: lab/manifests/EXP-0002.json
seeds: "main 1-1000; full manifest 93 points, 21 080 runs; det 60 runs"
threads: "1 (main); 1/4/8 (det)"
golden_hash: 183e47b9fc81957ccd6a53f93f5d09f4d10a2ca094b0be119680bd1cf42ab02d   # golden_set_hash from runs.csv (#265); det 824f4ded07216828ee6354f95f6bd942dcefd6059f70fdff24fbf9f9ab91ea5b
baseline:                # контрольная версия (commit или EXP-NNNN)
outcome: refutes
created: 2026-11-04
---

# EXP-0002 — EXP-0002: основной эксперимент HYP-0002 (срез «реактивная авиация»)

## Вопрос
Основной эксперимент HYP-0002 по спеке `docs/simulation/slice-jet-aviation.md` (§6, §7, §11 T10–T12). Номер зарезервирован: спека ссылается на EXP-0002. Манифест — после калибровки T12.

## Заморозка (2026-11-17, решение владельца #258)
- Манифест `lab/manifests/EXP-0002.json`, **`manifest_hash` `c3c2d471cfb3bdaff6cb2128cf6a550c8cd50c885f6732f08de99aa8c15f9f14`**: 93 точки, 21 080 прогонов, threads 1. Серия det — `lab/manifests/EXP-0002-det.json` (`286d5379…`).
- Сборка `792aaa2` (код `8f7b3de`), SDK 10.0.403, `data_hash` (provenance) `efcd0984…`, `control_tables_hash` `4e10abb5…`, спека v0.34 r1 (`85574961…`).
- Калибровка: `L` = 0 (C5 `undefined`, C4 — печать). Отступления — в `meta.deviations` и в `docs/simulation/t12-calibration-report.md` §7.7–§7.14.
- Прогоны ещё не запускались. Поля `core_commit`, `data_hash`, `manifest_hash`, `seeds`, `threads`, `golden_hash`, `outcome` заполняются после прогона.


## Результат (2026-11-17)
- Исход по пре-регистрированному правилу §7: `refutes`. P1, P3 (`L_K`, `L_C`) и P4 провалены; P2 и P5 пройдены. Подробно: `docs/simulation/exp0002-result.md`.
- Отступления: #264 (потоковый загрузчик анализа, эквивалентность доказана), #265 (исключение по происхождению прогонов).
- Gate: critic, advocate, DeepSeek — `docs/reviews/exp0002-result-*.md`, `tools/llm_worker/runs/gate-exp0002-result-db31dc8d-third.json`. Решение владельца: #266.
