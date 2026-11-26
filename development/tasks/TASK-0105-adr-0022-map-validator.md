---
id: TASK-0105
title: "ADR-0022 map validator: region limit 150-400, great-power core quota, single status per region"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: developer         # ответственный агент
category: tooling
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0022, TASK-0096]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "ADR-0022 (#410), mandatory follow-up (1)"
created: 2026-11-18
tags: [task, map, testing, done]
---

# TASK-0105 — ADR-0022 map validator: region limit 150-400, great-power core quota, single status per region

## Goal
Implement ADR-0022 amendment p.6 (ACCEPTED, #410) in `tools/map/MapBuilder/Validator.cs`, with tests.

## Description
- Region count: Info "(world target 150-400, ADR-0022)"; warning > 400 ("exceed the ADR-0022 provisional limit of 400 (report to the owner)") and < 150.
- Core quota: count only `Status == "core"`, only for actors in `great_powers.yaml`; warnings > 15 and < 5; named exception SUN ≤ 35 with the reason in the data (#406).
- Info line per great power: `actor GBR: core 2, colony 85, protectorate N, mandate N, leased N`.
- New error: more than one `status` within one region, listing the provinces.
- Unknown id in `great_powers.yaml`: error.

## Acceptance criteria
- [x] Tests per ADR-0022 "Проверка": 400 regions without a warning, 401 with one; 150 without, 149 with; a power with 16 core warns; 4 core warns "< 5"; 10 core + 80 colony gives no warning plus the exact Info line; a non-listed actor with 20 core gives no quota warning; an unknown id is an error; a region with two statuses is an error; SUN 31 gives no warning, 36 warns.
- [ ] The 1945 map build gives exactly the quota warnings of ADR-0022 table p.2.7; MapBuilder tests and `check_determinism.sh` pass.

## Результат
- Сделано: `Validator.cs` по ADR-0022 п. 6: диапазон регионов 150-400 (`CheckRegionCount`, предупреждения), квота только для `great_powers` по `status: core` (> 15 и < 5, исключение SUN ≤ 35), Info по статусам, ошибка нескольких `status` в регионе, ошибка неизвестного id. Новый файл `tools/map/spec/great_powers.yaml` (список 1945, исключение SUN с причиной #406); `SpecSet` читает его и не считает правилом.
- Файлы: `tools/map/MapBuilder/{Validator,Scenario,Spec}.cs`, `tools/map/spec/great_powers.yaml`, `tests/Singularity.MapBuilder.Tests/MapBuilderTests.cs` (8 новых тестов).
- Проверка: `dotnet test` 64/64, `check_packages.sh` OK, `check_determinism.sh` OK. Сборка 1945: 374 региона, предупреждений о лимите и квоте нет: по текущему `owners.yaml` core USA 7, SUN 31, GBR 5, FRA 6, ROC 7 (в норме). Предупреждения GBR/FRA < 5 появятся после правок `status` (T4, ADR-0022 п. 2.6); до них критерий «ровно п. 2.7» не достижим.
- Критерий 2 («ровно предупреждения п. 2.7») достижим только после правок `status` (T4, ADR-0022 п. 2.6): сейчас квотных предупреждений нет. В данных ROC core 7, в ADR «сейчас» 6 — проверить в T4.
- Доработка по ревью: ошибки в `great_powers.yaml` (исключение вне `ids`, дубликат, нет/≤15 `max_core`, дубль id), единый `status` покрывает территории, группировка по регионам один раз, тест для не-мирового режима (67 тестов).
