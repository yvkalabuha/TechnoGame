---
id: ADR-0009
title: "Политика лицензий внешних данных"
status: ACCEPTED
date: 2026-11-02
supersedes:
superseded_by:
related: [RR-0003]
approved_by: owner
approved_at: 2026-11-02
approval_ref: docs/research/PHASE_B_SUMMARY.md §2
---

# ADR-0009 — Политика лицензий внешних данных

> ACCEPTED: текст не меняется. Изменение — новый ADR с `supersedes`.

## Контекст
Репозиторий публичный; коммерческий релиз в будущем возможен. RR-0003.

## Решение
- Разрешено: Public Domain, CC0, CC BY (с атрибуцией в `mods/core/ATTRIBUTION.md`).
- Запрещено: некоммерческие лицензии (например, CShapes CC BY-NC-SA), данные без права распространения (GADM), copyleft (GPL, ODbL).
- GPL-карты (historical-basemaps) — только визуальный ориентир, без копирования геометрии.
- Границы 1910 года рисуются самостоятельно поверх Natural Earth.
- Каждый импорт данных указывает источник, лицензию и дату.

## Проверка
Правило `data.md`; ревью каждого изменения в `mods/`.
