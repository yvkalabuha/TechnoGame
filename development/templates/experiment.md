---
id: {{ID}}
title: {{TITLE_YAML}}
tags: [experiment, simulation-lab, draft]   # kind, area (edit), state (draft → active → done); development/TAGS.yaml
status: PLANNED          # PLANNED | RUNNING | DONE | FAILED
hypothesis: HYP-0000     # проверяемая гипотеза
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
runner:                  # matrix | bench | other (ADR-0008); matrix ⇒ golden_set_hash обязателен для DONE
golden_set_hash:
analysis_seconds:        # время анализа (ADR-0008, порог Parquet #374)
baseline:                # контрольная версия (commit или EXP-NNNN)
outcome:                 # supports | refutes | inconclusive
created: {{DATE}}
---

# {{ID}} — {{TITLE}}

## Вопрос
<!-- Что проверяем и какой результат подтвердит или опровергнет гипотезу. -->

## Начальные условия и параметры

## Метрики

## Результаты
<!-- Распределения, разброс, крайние сценарии. Ссылки на файлы анализа. -->

## Проверка эталона
<!-- Кто и как сверил эталонную реализацию (прототип) со спецификацией; паритет «порт = прототип» этого не доказывает. -->

## Ошибки и аномалии

## Сравнение с контрольной версией

## Вывод
