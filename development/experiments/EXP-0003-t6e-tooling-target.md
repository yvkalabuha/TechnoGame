---
id: EXP-0003
title: "Цель оснастки A39: варианты V0–V4 против эталона VINF (T6e)"
status: PLANNED          # PLANNED | RUNNING | DONE | FAILED
hypothesis: HYP-0002
kind: sweep
related: [TASK-0029]
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

# EXP-0003 — Цель оснастки A39: варианты V0–V4 против эталона VINF (T6e)

## Вопрос
Какой вариант цели оснастки A39 снимает замок «культура ↔ оснастка» (`docs/simulation/fielded-empty-diagnosis.md`) дешевле всех за единицу роста культуры. Решения #129, #156. Дизайн, критерии K1–K5, правило выбора — `docs/simulation/exp-t6e-tooling-target.md` и спека v0.19 r1 §11 «EXP T6e» (объявлены до прогона, ревью critic учтено). Выбор варианта — владелец.

## Начальные условия и параметры
Сборка после TASK-0029 (b203293): данные R-H-MS′, переключатель `TOOLING_TARGET` ∈ {V0, V1, V2, V3, V3b, V4, VINF}; seeds 1…20 main + 3 факторные ячейки × seeds 1…10; 350 прогонов по 384 мес.

## Метрики
M1–M4 и K1–K5 по `exp-t6e-tooling-target.md`; парный bootstrap 95% CI против V0 и VINF.

## Результаты
