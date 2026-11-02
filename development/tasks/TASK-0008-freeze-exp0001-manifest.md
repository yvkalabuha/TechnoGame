---
id: TASK-0008
title: "Заморозка манифеста EXP-0001 и переключение Lab на интерфейс Core"
status: IN_PROGRESS
level: light             # light | gate | gate+research
milestone: MS-M0
agent: simulation-engineer
category: lab
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0003, TASK-0005]
related: [EXP-0001, TASK-0004]
approved_by: orchestrator
approved_at: 2026-11-02
approval_ref: light, DECISIONS_LOG #16–#17
created: 2026-11-02
---

# TASK-0008 — Заморозка манифеста EXP-0001 и переключение Lab на интерфейс Core

## Цель
Официальный манифест EXP-0001, который понимает Lab, с правилами оценки по решению #17; затем — Lab использует модель из Core.

## Описание
Этап 1 (сейчас):
1. `exp0001.py`: правило п.5 — случай необъяснён, только если NOISE ≥ `c5.noise_unexplained_share`·gap (умолчание 0,3); тест. П.2 — argmax Power 1950 (проверить, что это умолчание).
2. Привести `lab/manifests/EXP-0001.json` к схеме Lab 1 (`series`/`sweep`, остальное — в `meta`), без изменения смысла; контроль C: L=120, ψ=0,45.
3. Перегенерировать манифест; `--dump-config` = `analysis` в манифесте; sha256 — в ответ.
Этап 2 (после TASK-0004): Lab ссылается на Core, использует интерфейс из `Singularity.Core.Simulation`, регистрирует `ToyFunnel` A/B/C; `selftest` проходит.

## Критерии приёмки
- [ ] `Singularity.Lab hash lab/manifests/EXP-0001.json` стабилен; прототип и Lab читают один манифест.
- [ ] Тесты анализа зелёные.
- [ ] Этап 2: `selftest` + прогон 1 точки EXP-0001 через Lab.

## Результат
Этап 1 (simulation-engineer): п.5 `noise_unexplained_share`=0.3, п.2 `final_leader: raw`; манифест в Lab schema 1 (серии main/A, main/B/nu*, main/C, grid; `seeds` у серии); `manifest_hash` = `93427d5497533d1d1d5f3bd55f89daaa184720c4724f929a421fd6901c45ac15`, sha256 файла `e64b2cc7…2adb`; прототип: по-прежнему `refutes`. Исправлены 8 замечаний ревью Lab (TASK-0005). Тесты анализа 44/44, Lab selftest 15/15.
