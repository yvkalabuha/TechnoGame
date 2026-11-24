---
id: TASK-0138
title: "Globe draws 1945 political borders and labels from the region map and owners, not modern Natural Earth countries"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M05
agent: developer
category: client
priority: high
needs_owner: false
depends_on: []
related: [TASK-0137, TASK-0091, TASK-0095]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "owner in chat 2026-11-24 («границы современные, а не на 45 год»); ADR-0011"
created: 2026-11-24
---

# TASK-0138 — Глобус с границами 1945 года

## Цель
Глобус рисует современные страны Natural Earth (Казахстан, Норвегия, Судан…): клиент грузит только `assets/data/ne_50m_admin_0_countries_min.json` (`Main.cs:126`, `GlobeData.cs:29`), подписи — 30 крупнейших современных стран. Карта регионов (379) и владельцы 1945 года уже собраны (`mods/core/map/`, `mods/core/scenarios/1945/owners.yaml`), но клиент их не читает. Владелец: в партии 1945 года нужны границы 1945 года.

## Описание
- Клиент строит ID-текстуру по регионам (`mods/core/map/geo/arcs.bin`, генерируется сборкой карты; ADR-0011 §… — проверка `data_binding` с громким отказом) и красит по владельцу из `owners.yaml` (статус: метрополия, колония, мандат, оккупация — отличие не только цветом, A5).
- Граница рисуется между разными владельцами (политическая), тонкая линия — между регионами одного владельца по желанию; подписи — по владельцам (имена держав 1945 года, EN/RU), колонии — по правилу арт-директора.
- Статичная карта на 1945-10-01; смены владельцев в ходе партии — позже (MS-M1 п. 1.5, #420). Это переносит часть п. 1.5 в M-play.
- Сборка карты: `SINGULARITY_MAP_CACHE=~/.cache/singularity/map`; `arcs.bin` в git не хранится — `play.sh` и `verify.py` должны его получать.

## Критерии приёмки
- [ ] Кадры 1920x1080 при 100 %: Европа, Африка, Азия 1945 года (СССР целиком, колонии Британии и Франции), серый кадр; сверка с `owners.yaml` по 10 регионам.
- [ ] Чистый клон: `./play.sh` строит или скачивает карту без ручных шагов.
- [ ] ui-art-director PASS; ревьюер APPROVE; второе ревью по #432.

## Результат
