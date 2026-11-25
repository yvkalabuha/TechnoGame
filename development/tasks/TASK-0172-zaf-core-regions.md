---
id: TASK-0172
title: "Data: South Africa regions status core of ZAF (O-076), checked per region"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: data           # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: orchestrator # owner | orchestrator (orchestrator — только level: light)
approved_at: 2026-11-25
approval_ref: O-076
created: 2026-11-25
---

# TASK-0172 — Data: South Africa regions status core of ZAF (O-076), checked per region

## Цель
Регионы Южно-Африканского Союза в 1945 — `status: core` у ZAF (доминион, суверенен), не `colony` (O-076). Юго-Западная Африка остаётся мандатом ZAF.

## Описание
Правка `mods/core/scenarios/1945/owners.yaml` по каждому региону `south_africa_*` со сверкой состава региона (провинции); сборка карты `--map`; кадр глобуса — в следующей пачке кадров.

## Критерии приёмки
- [ ] Все регионы ЮАС — core ZAF; ЮЗА — мандат
- [ ] Сверка по составу регионов
- [ ] reviewer APPROVE

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
