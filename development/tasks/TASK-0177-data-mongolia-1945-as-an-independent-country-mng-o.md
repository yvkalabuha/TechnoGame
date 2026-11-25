---
id: TASK-0177
title: "Data: Mongolia 1945 as an independent country (MNG owner and controller), #550"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: data
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#550"
review_ref: "reviewer: APPROVE (9af89b2, 27be18c); land.py verify OK, merge 5435cbf"
created: 2026-11-25
---

# TASK-0177 — Data: Mongolia 1945 as an independent country (MNG owner and controller), #550

## Цель
Монголия в 1945 показана самостоятельной страной (#550, A9), а не «оккупированным Китаем».

## Описание
В `mods/core/scenarios/1945/owners.yaml` регионы Монголии имеют owner ROC, controller MNG. Сделать owner MNG для всех регионов собственно Монголии (не Внутренней Монголии — она остаётся у ROC). Источник: де-факто независимость МНР, признание ROC в январе 1946; дописать в `docs/research/` записку owners-1945-world.md (строка K6/C34). Проверить по фактическим данным: список регионов MNG, цвет на глобусе, подпись страны.

## Критерии приёмки
- [ ] Все регионы собственно Монголии: owner MNG, controller MNG; Внутренняя Монголия не тронута
- [ ] Источник в записке, URL + дата
- [ ] Тесты карты и сценария зелёные; эталоны меняются только с объяснением
- [ ] reviewer APPROVE

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
Регионы mongolia_govi_altay и mongolia_mn_govi: owner MNG, controller MNG (owners.yaml). Других регионов Внешней Монголии нет (regions.yaml: только эти два); china_inner_mongol не тронут. Добавлен тест (Ulaanbaatar -> MNG core) в WorldMapTests; без правки данных он падает (проверено). MapBuilder.Tests: 101/101. Штриховка Occupied включается только при owner != controller, поэтому для MNG её нет. Записка docs/research/owners-1945-world.md (строки mongolia_*) обновлена Оркестратором (27be18c), источник FRUS 1946 vol. X doc. 961. Влито 5435cbf. Событие mongolia_recognised (C34) в tools/map/spec/events/world.yaml оставлено, оно CI-проверка границ.
