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
