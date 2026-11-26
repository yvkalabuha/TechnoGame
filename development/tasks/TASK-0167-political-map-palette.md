---
id: TASK-0167
title: "Globe colours by political-map traditions (#527): fixed tones for great powers, colony = metropolis tint"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M05
agent: developer         # ответственный агент
category: client
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#527"
created: 2026-11-25
---

# TASK-0167 — Globe colours by political-map traditions (#527): fixed tones for great powers, colony = metropolis tint

## Цель
Перекраска глобуса по привычной схеме политических карт (#527): `docs/ui/specs/political-map-palette.md`, п. 5. Делается в ветке TASK-0163 (те же файлы), кадры — одной пачкой с подписями после разрешения владельца.

## Описание

## Критерии приёмки
- [ ]

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
Ветка общая с TASK-0163 (round 4 слита). Цвет выбирается чистыми функциями `CountryPalette.Assign` (фиксированные тона держав, остальные жадно, соседи не из `Forbidden`) и `CountryPalette.ColonyFill` (тон метрополии + 18 % белого; камень без смеси). Монголия (MNG, TASK-0177) своя держава: жадный тон, на реальном графе 1945 соседи (SUN, ROC) не в запретных парах (тест `WorldMapTests.Palette_is_deterministic...`).

Файлы: src/Singularity.Client.Spike/CountryPalette.cs; tests/Singularity.MapBuilder.Tests/PoliticalPaletteTests.cs, PoliticalPaletteTests2.cs, WorldMapTests.cs.

Проверка: после `python3 tools/map/ensure_geo.py` модульные тесты MapBuilder (Palette|WorldMap|Hatch|Globe) 48/48 (nice -n 10). Серый кадр (A5): отличие держав несёт толстая граница и штриховка, не цвет; не проверено на кадрах.

Список кадров одной пачкой (после разрешения владельца), `--only`:
map1945_world_1920x1080,map1945_europe_1920x1080,map1945_africa_1920x1080,map1945_asia_1920x1080,map1945_oceania_india_1920x1080,map1945_namerica_1920x1080
(серые двойники `*_grey` снимаются автоматически; смотреть: Канада против Индии, Бельгия против Франции, Монголия между SUN и ROC.)

Не проверено: кадры и клиент (не запускался); тест на падение без изменения (изменение пришло в ветке раньше, на старом Fixed не гонял); ΔE USA/CAN.

