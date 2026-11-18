---
id: TASK-0096
title: "Owners 1945 data per decisions 397, 399-402"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M1
agent: developer
category: data
priority: medium
needs_owner: false
depends_on: []
related: [TASK-0091, TASK-0095]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "#397, #399-#402"
created: 2026-11-18
---

# TASK-0096 — Owners 1945 data per decisions #397, #399–#402

## Цель
Привести данные владения 1945 и карту к решениям владельца.

## Описание
- #397 Данциг: owner POL с t0 (сейчас DEU без события передачи).
- #399 протектораты: owner = держава-протектор — проверить, что так во всех случаях (`docs/research/owners-1945-world.md`).
- #400 Берлин: два региона (Запад, Восток) по границам округов из разрешённых источников (PD/CC0/CC BY, ADR-0009); dl-de/by-2.0 не использовать.
- #401 спорные: Тибет owner/controller TIB (до события 1950); Огаден GBR; Спратли без владельца (если формат не допускает — записать как отклонение и предложить представление).
- #402 Тайвань: #313 без изменений (owner JPN, controller ROC), добавить комментарий-условность в данных.
Файлы: `mods/core/scenarios/1945/owners.yaml`, `tools/map/spec/*`, при необходимости пересборка карты (`tools/map/check_determinism.sh`).

## Критерии приёмки
- [ ] Данные соответствуют #397, #399–#402; события передачи, ссылающиеся на изменённые строки, согласованы (RR-0020, `scripted-events-conditions.md`).
- [ ] Карта пересобрана, детерминизм (две сборки побайтно равны) и тесты MapBuilder зелёные; `dotnet test Singularity.sln` зелёный.
