---
id: TASK-0184
title: "Data: owners 1945 corrections from TASK-0166 check (Brunei, Ogaden, confidence marks; Tibet/Taiwan/Danzig wait for owner)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
