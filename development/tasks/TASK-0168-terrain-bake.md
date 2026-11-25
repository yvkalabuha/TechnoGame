---
id: TASK-0168
title: "Terrain M1: download sources, bake per-province terrain and rivers, 1945 corrections (#528, #533-#536)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M1
agent: developer         # ответственный агент
category: data
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#528, #533-#536"
review_ref: "reviewer: APPROVE (f459d31, 4 rounds); codex: 3 returns, waived by #432, leftovers DEBT-0108; merge 6402e5f"
created: 2026-11-25
---

# TASK-0168 — Terrain M1: download sources, bake per-province terrain and rivers, 1945 corrections (#528, #533-#536)

## Цель
Рельеф для М1 по `docs/research/notes/terrain-research.md`: скачать GMTED2010, GLWD v2, ESA WorldCover в `~/.cache/singularity/map` (#536), запечь класс и доли местности по провинции (`terrain.yaml`), реки Natural Earth атрибутом границ (#534), ≈30 правок под 1945 с источниками (#535). Механики (стройка, добыча, пахота, население) — отдельной задачей М1 (#533).

## Описание

## Критерии приёмки
- [ ]

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
- Сделано: команда `MapBuilder terrain` (GMTED2010 с плитками 70S, GLWD v2, ESA WorldCover; SHA-256 в sources.lock.yaml) пишет `mods/core/map/terrain.yaml` (3124 провинции, промилле, доли `urban` и `farm` раздельно). Болота: GLWD main_class_50pct, вес по area_pct ячейки. Реки: ранг реки, реально идущей вдоль границы, третьим числом пар в `adjacency.yaml`; судоходные реки в `rivers.yaml`. 27 правок 1945 в `tools/map/spec/terrain_1945.yaml` (источник и дата у каждой; разбивка по провинциям [Предп.]). Острова без данных WorldCover берутся из `no_data` в `spec/terrain.yaml` с источником. Один кэш `~/.cache/singularity/map` для fetch, fetch --terrain, build, terrain.
- Проверка: тесты MapBuilder 126/126; `TERRAIN_CHECK=1 bash tools/map/check_determinism.sh` OK (две сборки карты и два запекания рельефа совпадают с закоммиченными таблицами).
- Не проверено: тесты на падение без правки не запускались; отдельного юнит-теста выбора ранга реки нет; Core/Client/verify.py/CI не запускались. Долг: Зёйдерзее, Месопотамские болота, Амазония не поправлены; Зюйд-Холланд classed swamp (GLWD).
