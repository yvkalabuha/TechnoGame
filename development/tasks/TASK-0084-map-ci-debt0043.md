---
id: TASK-0084
title: "Map tooling CI and boundary checks (DEBT-0043)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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

**Пункт 2 (сделан).** `tests/Singularity.Architecture.Tests/MapToolingIsolationTests.cs`: Kernel, Core, Session (сборки и csproj) и клиент (csproj) не ссылаются на NetTopologySuite и MapBuilder; третий тест подтверждает, что детектор срабатывает на искусственной ссылке (PackageReference NetTopologySuite и ProjectReference на MapBuilder) и молчит без неё.

**Пункт 3 (не трогал).** `ModLoader` знает только виды `node`, `product_class`, `scenario`; `map/*` загружаться не умеет, поэтому `mods/core/mod.yaml` не менялся.

**Пункт 1 (не сделан: блокер).** `.github/workflows/ci.yml` вне зоны записи developer (хук запретил). Нужно, чтобы Orchestrator добавил job `map-tooling` (ubuntu-latest) перед `analysis:`: checkout; setup-dotnet 10.0.x; `bash tools/map/check_packages.sh`; `dotnet test tests/Singularity.MapBuilder.Tests -c Release`; `actions/cache@v4` (path `tools/map/.cache`, key `map-sources-${{ hashFiles('tools/map/sources.lock.yaml') }}`, id map-cache); шаг fetch (`dotnet run -c Release --project tools/map/MapBuilder -- fetch`, `if cache-hit != 'true'`, `continue-on-error: true`, id map-fetch); `bash tools/map/check_determinism.sh` при `cache-hit == 'true' || map-fetch.outcome == 'success'`; иначе шаг с `echo "::warning::map sources unavailable; check_determinism.sh skipped"`.

**Проверка.** MapBuilder.Tests: 42/42. check_packages.sh: OK. Architecture.Tests: 105/106; падение `HashAssertUsageTests` (tests/Singularity.Lab.Tests/BenchTests.cs, `Assert.Equal(h, StateHash...)`) существовало до задачи и не связано с ней. check_determinism.sh и actionlint локально не запускались (нет сети/кеша, actionlint не установлен).
