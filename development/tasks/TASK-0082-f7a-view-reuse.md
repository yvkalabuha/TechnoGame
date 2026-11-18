---
id: TASK-0082
title: "F7a: reuse the view on probe-only days, cheaper daily snapshot"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
