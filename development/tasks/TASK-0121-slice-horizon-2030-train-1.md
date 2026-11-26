---
id: TASK-0121
title: "Slice horizon to 2030, train 1: Years = 86, q mask in 62-bit blocks, horizon fixture"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M05
agent: developer         # ответственный агент
category: core           # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: high           # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0115]
related: []
approved_by: orchestrator
approved_at: 2026-11-19
approval_ref: "#423"
review_ref: "reviewer: APPROVE 27c2463 (+lows fixed 30288a6); Codex accept 30288a6c; fixture verified on base 3d730e8 by Orchestrator; landed d63289e"
created: 2026-11-19
tags: [task, core, determinism, done]
---

# TASK-0121 — Slice horizon to 2030, train 1: Years = 86, q mask in 62-bit blocks, horizon fixture

## Цель
Срез считает до 2030 года без изменения траекторий до 1977: `SliceWorld.Years = 86`, маска `q` блоками по 62 бита. Первый шаг решения владельца #423.

## Описание
Проект: `docs/architecture/notes/slice-horizon-2030.md` §2 (H1, H3, H8) и §5 «Поезд 1» (шаги T-H0 и T-H1). TASK-0115 уже слит со `SchemaVersion` 11, поэтому эта задача поднимает версию до 12 своим «поездом схемы» (не раньше 2026-11-20, вместе с правками TASK-0118, если они готовы).

Порядок:
1. T-H0 до любой правки: фикстура проекционных хешей текущего `main` — seeds 1–3; конфиги Main, ProductAi, T6dOff, FleetArm NoRetire; месяцы 149, 265, 383, 387.
2. T-H1: H1 + H3 + страж в `ProductsLayout`; тесты 1–7 из §5 записки.
3. Проекции новой сборки по старым именам равны T-H0 во всех точках; только после этого `SchemaVersion` 12 и одна перезапись golden с построчным объяснением.

## Критерии приёмки
- [ ] Тесты 1–3 из §5 падают на старом коде и проходят на новом.
- [ ] Проекции равны T-H0 во всех точках (старые колонки не изменились).
- [ ] Дым 1020 месяцев, seeds 1–3: без исключений; L1 и L2 равны.
- [ ] Замер: мс на месяц и пиковая память на 388 и 1020 месяцев; не хуже ×1,5 на месяц.
- [ ] Ревьюер APPROVE, Codex без открытых высоких замечаний.

## Тесты
`python3 tools/development/verify.py`; долгие прогоны — в Lab (`SliceChecks`) или с отметкой slow.

## Связанные документы
`docs/architecture/notes/slice-horizon-2030.md`, `docs/simulation/m-play-horizon-2030.md`, DEBT-0061.

## REVIEW

## Результат

Ветка worktree-agent-abe3fd84f580ef583 (developer, 2026-11-19).

**Сделано.** `SliceWorld.Years = 86`; маска `q` блоками по 62 бита (`ProductsLayout.MaskBits/MaxCohortYears/MaskBlocks`, блок 0 = `prd.d{d}.q`, блок 1 = `prd.d{d}.q.1`); страж `years ≤ 124` в `ProductsLayout.FromData`; `SchemaVersion` 11 → 12, один перезапис golden (комментарий у каждого). Новых колонок 648 (636 когорт 1978…2030 + 12 `q.1`): 1903 → 2551.

**Проверка.** Фикстура `Fixtures/horizon-baseline.txt` (отдельный коммит без правок src) — 48 точек; `HorizonTests.OldColumns_…` проходит: старые колонки не изменились. Тесты 1–3 красные на старом коде (проверено откатом src). Дым 1020 мес. seeds 1–3, L1, L2 (мес. 387 и 1019) — `HorizonTests`, зелёные.

**Замер** (seed 1, threads 1, мс/мес.): 388 мес. старый 4,18, новый 5,06–5,31 (×1,21–1,27); 1020 мес. новый 2,73–3,08. Пик RSS 111 → 112 МБ (388), 115–116 МБ (1020).

**Lab (simulation-engineer, коммиты 27c2463 и следующий).** [Факт] `Golden150` = `a0b7ab02…869e` записан после прогона пути Lab (seed 1, месяц 149, threads 2): хеш совпал с `MainSeed1Month149` Session. Страж H8: `LAYER` ∈ {LK, LK0, LC0, LC, LP} при `ticks` > 11688 (384 мес.) → `ParamException` в `JetSliceModelFactory.Create` и в `calib-print`, сравнение за O(1) (огромные `ticks` отклоняются сразу); FULL не ограничен. Проверки selftest/`SliceModelTests` «slice: H8» (красная без каждой точки вызова — проверено) и «slice: full run to 2030-12» (FULL, 1024 мес., seed 1, status ok, `months` = 1024, последняя строка `lines` t = 1023, хеш threads 1 = 2). Прогон до 2030-12: 4,6 с, пик RSS 85 МБ, 1,3 МБ CSV.
[Факт] `ticks = 31046` из §5 п. 5 дизайна — это 1020 мес. (1945-09 … 2030-08); до 2030-12 включительно нужно `ticks = 31168` (1024 мес.).
