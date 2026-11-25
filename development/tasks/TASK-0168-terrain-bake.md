---
id: TASK-0168
title: "Terrain M1: download sources, bake per-province terrain and rivers, 1945 corrections (#528, #533-#536)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
- Сделано: команда `MapBuilder terrain` (GMTED2010, GLWD v2, ESA WorldCover из кэша, SHA-256 в sources.lock.yaml) пишет `mods/core/map/terrain.yaml` (3124 провинции, целые числа, промилле). Реки Natural Earth: атрибут `border_river` (ранг 1-4) третьим числом пар в `adjacency.yaml` (220 пар), судоходные реки в `rivers.yaml` (5 рек). 27 правок 1945 в `tools/map/spec/terrain_1945.yaml` (Арал, Чад, водохранилища) с источником и датой; разбивка по провинциям и исходный класс земли помечены [Предп.].
- Проверка: `dotnet test tests/Singularity.MapBuilder.Tests` 120/120; `tools/map/check_determinism.sh` OK (terrain.yaml исключён: источники 4,3 ГБ вне CI).
- Не проверено: тест на падение без правки не запускался; Зёйдерзее, Месопотамские болота, Амазония не поправлены; Зюйд-Холланд классифицирован как swamp (GLWD включает торфяники).
