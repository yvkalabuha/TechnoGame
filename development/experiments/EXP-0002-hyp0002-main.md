---
id: EXP-0002
title: "EXP-0002: основной эксперимент HYP-0002 (срез «реактивная авиация»)"
status: PLANNED          # PLANNED | RUNNING | DONE | FAILED
hypothesis: HYP-0002
kind: scenario           # batch | scenario | sweep | monte-carlo | regression | balance | benchmark
related: []
# Воспроизводимость — обязательно для DONE. Lab записывает сама.
core_commit:
data_hash:
manifest_hash:
manifest:                # путь к манифесту в lab/
seeds:
threads:
golden_hash:
baseline:                # контрольная версия (commit или EXP-NNNN)
outcome:                 # supports | refutes | inconclusive
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

