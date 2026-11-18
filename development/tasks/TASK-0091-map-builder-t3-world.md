---
id: TASK-0091
title: "Map builder T3: world beyond Europe, regions split by event lines (#350), territories.yaml"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: developer
category: data
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0011, ADR-0020, TASK-0075, RR-0020]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "ADR-0011 rev 3, ADR-0020 ACCEPTED (#354), #350; light step of approved map builder"
created: 2026-11-18
---

# TASK-0091 — Map builder T3: world beyond Europe, regions split by event lines (#350), territories.yaml

## Цель
Сборщик карты T3: мир за пределами Европы и нарезка регионов строго по линиям событий (#350).

## Описание
1. Расширить `tools/map` (TASK-0075) на весь мир: NE admin-1 и admin-0 map units, OHM-снимки там, где нужны линии 1945 (CC0/CC BY), закрепить в `sources.lock.yaml`.
2. Разрезы и границы регионов по линиям событий RR-0020 (ядро: Германия — зоны и Берлин 4 региона, Австрия — Вена 5 регионов, Корея по 38-й параллели, Пенджаб/Бенгалия по линии Рэдклиффа, Палестина, Триест A/B) — по ADR-0020 «строгое выравнивание»; CI-проверка: каждая линия события совпадает с границами регионов.
3. `map/territories.yaml`: владение без геометрии для мелких территорий (ADR-0020).
4. Число регионов печатается; если > 250 — предупреждение (вопрос владельцу по ADR-0011).
5. Детерминизм: две сборки побайтово равны.

## Критерии приёмки
- [ ] Валидатор зелёный для мира; `check_determinism.sh` OK; тесты MapBuilder зелёные.
- [ ] Отчёт: число провинций и регионов, список линий событий, которые не удалось провести (с причиной).

## Результат
