---
id: TASK-0184
title: "Data: owners 1945 corrections from TASK-0166 check (Brunei, Ogaden, confidence marks; Tibet/Taiwan/Danzig wait for owner)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M1
agent: developer
category: data
priority: low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0166]
approved_by: orchestrator
approved_at: 2026-11-25
approval_ref: "O-086"
review_ref: "reviewer: APPROVE (3 low fixed by orchestrator); land verify OK; merge e3435e2"
created: 2026-11-25
---

# TASK-0184 — Data: owners 1945 corrections from TASK-0166 check (Brunei, Ogaden, confidence marks; Tibet/Taiwan/Danzig wait for owner)

## Цель
Применить к `mods/core/scenarios/1945/owners.yaml` правки из сверки TASK-0166, кроме вопросов к владельцу (A17).

## Описание
Источник: `docs/research/notes/owners-1945-check.md`. Применить: brunei controller GBR→AUS (британская военная администрация Борнео 1945-09-12…1946-07-01), ethiopia_ogaden owner GBR→ETH (англо-эфиопское соглашение 1945), уверенность: taiwan H→M, bratislava_bridgehead H→M, spratly_is controller → null если схема позволяет (иначе оставить и записать). Не трогать: Тибет, Тайвань (controller), Гданьск, tenda_brig, china_xinjiang — ждут владельца или первичных источников. Обновить строки записки owners-1945-world.md.

## Критерии приёмки
- [ ] Правки с источником в поле source; тесты карты зелёные; данные проверены по региону
- [ ] reviewer APPROVE
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
Сделано в `owners.yaml`: brunei controller GBR→AUS (M); ethiopia_ogaden owner GBR→ETH, controller GBR; taiwan H→M; bratislava_bridgehead H→M. AUS и ETH есть в polities.yaml. spratly_is: controller null невозможен (Validator требует политию в controller), оставлен JPN, L. Тесты: WorldMapTests.Corrected_1945_owner_and_controller (2 случая; без правки данных оба падают, проверено), MapBuilder.Tests 128/128. Не сделано (вне зоны developer): строки owners-1945-world.md (brunei, taiwan, bratislava_bridgehead, spratly_is) возвращены Orchestrator.
