---
id: TASK-0084
title: "Map tooling CI and boundary checks (DEBT-0043)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: developer         # ответственный агент
category: tooling
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0075, ADR-0011]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "DEBT-0043 CI part, light"
created: 2026-11-18
---

# TASK-0084 — Map tooling CI and boundary checks (DEBT-0043)

## Цель
Закрыть CI-часть DEBT-0043 для сборщика карты (TASK-0075).

## Описание
1. CI-шаги для `tools/map`: `check_packages.sh`, `dotnet test tests/Singularity.MapBuilder.Tests`, `check_determinism.sh` (кэш источников — через actions/cache по SHA из `sources.lock.yaml`; если сеть в CI недоступна, шаг помечается и пропускается с понятным сообщением).
2. Архитектурный тест: Core, Kernel, Session и клиент не ссылаются на NetTopologySuite.
3. `mods/core/mod.yaml`: включить `map/*` только если ModLoader их уже знает; иначе — запись в «Результате», не трогать.
Данные (Данциг, Калининград, имена регионов) — не в этой задаче (T3/T4 по ADR-0020 и #350).

## Критерии приёмки
- [ ] Workflow-файл проходит `actionlint` (если есть) или проверен глазами; локально все шаги зелёные.
- [ ] Архитектурный тест падает на искусственной ссылке и зелёный без неё.

## Результат
