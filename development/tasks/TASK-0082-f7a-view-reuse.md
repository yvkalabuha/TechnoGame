---
id: TASK-0082
title: "F7a: reuse the view on probe-only days, cheaper daily snapshot"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0017, ADR-0016, EXP-0007]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "ADR-0017 «Вне ADR» F7a (light); smoke of TASK-0064"
created: 2026-11-18
---

# TASK-0082 — F7a: reuse the view on probe-only days, cheaper daily snapshot

## Цель
Убрать цену ежедневного `ViewBuilder.Snapshot` на днях только с зондом до официального замера EXP-0007. Смоук TASK-0064: дневной тик ≈ 200 мс и ≈ 120 МБ аллокаций при выключенном хеше.

## Описание
По `docs/architecture/notes/daily-hash-and-view.md` «F7a» и `docs/simulation/tick-profile-m0.md` F7:
1. Переиспользование view: условие `!monthly && !applied` и все активные системы view-нейтральны (явный список в `SliceWorld`). Новый view = `prev with { Tick, Date, StateHash, Inputs = null, Step = null, Actors = actorsMid }`; `actorsMid` строится один раз за месяц с `Closes[].Reason = None`.
2. Дешёвая часть F7: `IndustryLayout.RowOf` без поиска (F1) в `ViewBuilder.Nodes`, лицензионные гранты — один проход в `bool[]` на актора.
3. Сначала профиль: подтвердить, что цену даёт снимок (dotnet-counters или Stopwatch по фазам), записать до/после.

## Критерии приёмки
- [ ] Тест: в прогоне с зондом JSON переиспользованного view на каждом дне == `ViewBuilder.Snapshot(...)` заново; тик с командой строит view заново.
- [ ] Поверхность Session не меняется (F7b вне задачи).
- [ ] Golden и `lab selftest` без изменений; L2 view-тест TASK-0079 зелёный.
- [ ] Замер до/после дневного тика (200, 2000, t8) через `lab bench --mode process` (справочно).

## REVIEW
- [ ] Code review (reviewer)

## Результат

**Профиль (до).** [Факт] `lab bench --mode process --actors 200 --nodes 2000 --warmup 1 --measure 2 --precheck-months 0`, t8, Release: месячный прогон p50 558.8 мс/мес, дневной (зонд, хеш выключен) p50 11811 мс/мес ≈ 390 мс/день; снимок `S_ms` p50 ≈ 171 мс. Цена дня — `ViewBuilder.Snapshot` на каждом тике, подтверждено.

**Сделано.**
- `SliceWorld.DailyViewNeutral` — явный список: все ежедневные системы только `DailyProbeSystem`.
- `SliceSession.StepOne`: при `!monthly && !applied && DailyViewNeutral` view = `prev with { Tick, Date, StateHash, Inputs = null, Step = null, Actors = actorsMid }`; `actorsMid` (`ViewBuilder.WithoutCloseReasons`) строится один раз после каждого полного снимка и сбрасывается при нём.
- `ViewBuilder.Nodes`: лицензионные гранты одним проходом в `bool[]` на актора (вместо прохода по списку на каждый узел). `IndustryLayout.RowOf` уже O(1) по таблице (F1 сделан ранее), здесь менять нечего.
- Поверхность Session и `TestApiVersion` не менялись (F7b вне задачи).

**После.** [Факт, справочно] тот же замер: дневной прогон p50 791.6 мс/мес (было 11811), ≈ в 15 раз. Машина была занята (loadavg 23 при втором замере против ~2 при первом), поэтому месячный прогон тоже стал медленнее (1026 против 559 мс/мес); абсолютные числа неточны, порядок ускорения надёжен. V2 хеш месячного и дневного прогона равен. Поля `decomposition` во втором замере искажены шумом (отрицательные k), официальным замером не считать.

**Тесты** (`tests/Singularity.Session.Tests/DailyTickProbeTests.cs`): `ReusedView_EqualsAFreshSnapshot_OnEveryProbeDay` (JSON с `IncludeFields` на каждом немесячном дне == свежий `Snapshot`, `HashAssert` на хеш, актёры реально переиспользуются > 40 дней); `ATickWithACommand_RebuildsTheView`. `dotnet test Singularity.sln`: зелёный (Session 367/2 skip, Core 1060, Lab 21 и др.). `lab selftest`: 48/48, golden без изменений.

**Файлы.** src/Singularity.Session/SliceSession.cs, Slice/SliceWorld.cs, Views/ViewBuilder.cs, tests/Singularity.Session.Tests/DailyTickProbeTests.cs, этот файл.

