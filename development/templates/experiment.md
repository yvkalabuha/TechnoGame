---
id: {{ID}}
title: {{TITLE_YAML}}
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
