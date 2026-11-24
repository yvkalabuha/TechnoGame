---
id: TASK-0144
title: "Muted sound character (#455): soften attack and low-pass all routine cues"
status: IN_PROGRESS
level: light
milestone: MS-M05
agent: sound-director
category: client
priority: medium
needs_owner: false
depends_on: []
related: [TASK-0140]
approved_by: orchestrator
approved_at: 2026-11-24
approval_ref: "owner preference #455; light task approved by Orchestrator"
created: 2026-11-24
---

# TASK-0144 — Глухой характер звуков (#455)

## Цель
Владелец предпочитает глухие звуки. Перевести рутинные звуки интерфейса и событий в глухой характер: мягкая атака, срез верхов, без звонких щелчков и колокольчиков.

## Описание
Шаг 1 — sound-director: по каждому звуку каталога (`AudioLogic.cs`, `docs/audio/EVENT_SOUNDS_MAP.md`) — оставить, обработать (low-pass, атака, длина) или заменить источником PD/CC0/CC BY (ADR-0009); параметры; тревоги P0 остаются заметными. Шаг 2 — developer по таблице.

## Критерии приёмки
- [ ] Таблица звуков с решением и параметрами; P0 различим.
- [ ] Ревьюер APPROVE; sound-director PASS; владелец слушает.
