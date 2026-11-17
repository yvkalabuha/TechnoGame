---
id: TASK-0068
title: "Tick hot-spot fixes F1-F3, F5, F6, F8 (tick profile M0)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0016, EXP-0007]
approved_by: orchestrator
approved_at: 2026-11-17
approval_ref: "DECISIONS_LOG #283 (owner: fix hot spots before the benchmark); light, no behaviour change"
created: 2026-11-17
---


## Goal
Implement fixes F1, F2, F3, F5, F6 and F8 from docs/simulation/tick-profile-m0.md §4. They are pure performance changes: no simulation behaviour changes, StateHash identical, golden hashes unchanged.

F4 (parallel ViewBuilder.Inputs) and F7 (reuse the view on days without a monthly step) are out of scope: they go to systems-architect first. The daily-mode hash algorithm is out of scope too: it needs gate+research.

## Acceptance
- Every fix has an equivalence test against the old path (same outputs), as §4 specifies.
- All golden tests are unchanged; the full `dotnet test` is green; determinism L1/L2 holds.
- Lab `selftest` is green (the `synth:` checks).
- Measured before and after on the synthetic points (6, 2000) and (200, 500) for 1–2 months, using the profile's method. Record the numbers in «Результат».

## Результат
Сделано (F1, F2, F3, F5, F6, F8; поведение и StateHash не менялись):
- F1: `KnowledgeGraph.RowOf`, `IndustryLayout.RowOf` — плотная таблица номер→строка (ImmutableArray, O(1)).
- F6: `IndustryLayout.SitesOf(actor)` (CSR); `EffortAllocator` (Weight, SplitBySite, цикл E_useful) и `ToolingPlan.Compute` идут только по сайтам актора.
- F3: `ICultureRule.EUsefulAll` (default = поэлементно); `CbCultureRule.EUsefulAll` считает `lead(c,d)` один раз на сайт, `DStar`/`Pick`/`EUseful` читают таблицу; `EffortAllocator.Allocate` зовёт её.
- F2: мемо `prc_set` в `ProductsSystem.PrcSet`, ключ — альтернативы из Current; строится только в конструкторе и в `Post` (однопоточно), при чтении не пишется (требование systems-architect, `docs/architecture/notes/daily-hash-and-view.md`); несовпавший ключ — пересчёт без записи. Поле добавлено в `PrecomputedFields` архитектурного теста `ScratchTests` (не состояние, не хешируется) — прошу ревьюера проверить.
- F5: `IIndustryInputs.ToolingInvestSite` (default = по ячейкам); `IndustryInputsAdapter` строит план один раз на сайт; `IndustrySystem.Compute` вызывает его до цикла ячеек.
- F8: `Column<T>.AppendCurrentTo` на little-endian подаёт `MemoryMarshal.AsBytes` одним вызовом; прежний путь оставлен как `AppendCurrentElementwise` (big-endian и эталон теста).

Тесты: `tests/Singularity.Kernel.Tests/ColumnHashPathTests.cs` (F8), `tests/Singularity.Core.Tests/Industry/SitesOfAndRowOfTests.cs` (F1, F6), `tests/Singularity.Session.Tests/TickHotSpotEquivalenceTests.cs` (F1; F2/F3/F5 на слайсе, seeds 1 и 7, 14 месяцев, все сайты/процессы). Golden hash во всех существующих тестах не менялись.

Замеры (Release, t8, ChunkSize 8, харнесс профиля, месячный режим; до — HEAD beb3746, после — рабочее дерево):
| Точка | До, мс/мес | После, мс/мес | StateHash до = после |
|---|---|---|---|
| (6, 2000) | 2463 / 2443 | 151 / 69 | 75980fa12985 = 75980fa12985 |
| (200, 500) | 1352 / 1134 | 844 / 660 | df0d496320e1 = df0d496320e1 |
| (200, 2000) | 80–106 с (профиль §2) | 623 / 528 | e873f97e8405 (до не перемерялся) |
Аллокации: (6, 2000) 254 → 4,8 МБ/мес; (200, 500) 1063 → 311 МБ/мес; (200, 2000) 7,4 ГБ → 1,05 ГБ/мес. Числа «до» на этой машине для (6, 2000) ниже профиля (2,4 с против 6,7–8 с: профиль снимался под трассой).

Проверка: `dotnet test` — Architecture 98/98, Kernel 322 (1 пропущен), Core 1060/1060, Lab.Tests 20/20, Session 353 (2 пропущено), 0 ошибок; Lab `selftest` 48/48.
