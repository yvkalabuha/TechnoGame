---
id: TASK-0162
title: "K0: Kernel primitives for the daily tick (DaySlice, Apportion, DayRate) per ADR-0027 §5"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M1
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#516 (ADR-0027), #520"
created: 2026-11-25
---

# TASK-0162 — K0: Kernel primitives for the daily tick (DaySlice, Apportion, DayRate) per ADR-0027 §5

## Цель
Примитивы Kernel для дневного тика (ADR-0027 §5): `DaySlice`, `Apportion`, `DayRate` на типе `Rate` ADR-0003 ред. 3. Вливаются до веток рычагов и М1.

## Описание
Только Kernel и его тесты; эталоны не меняются (новые функции без вызовов из систем).

## Критерии приёмки
- [ ] Тесты: сумма долей дней = месячной величине побитно (Apportion), ошибка DayRate в пределах ADR-0027
- [ ] Эталоны без изменений
- [ ] reviewer APPROVE, Codex

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат

**Сделано** (ветка worktree-agent-aa92bbfc395584a29, коммиты 68bec50, 93ebe02):
- `Rate` (Q53: `Raw`, `FromRaw`, `FromFx6`, `MulFloor` в Int128, одно округление).
- `DetMath.DayRate(Fx6 δ, int Y) → Rate`: точная целочисленная бисекция (BigInteger, при загрузке); ошибка всегда в сторону медленного изменения; δ < 0 — рост.
- `DetMath.DaySlice(F, k, Y)`: `floor(F·k/Y) − floor(F·(k−1)/Y)`, k = 1..Y, сумма срезов = F точно.
- `DetMath.Apportion(F, w, Span<Fx6> parts)`: без аллокаций, сумма частей = F, W = 0 — нули.
- `DetMath.ApplyDayRate(stock, rate)` (O-069): распад `x − r·x`, рост `x + (−r)·x`, ошибка запаса в сторону медленного изменения для обоих знаков.

**Файлы:** `src/Singularity.Kernel/Rate.cs`, `DetMath.Slicing.cs`, `DetMath.cs` (partial), `tests/Singularity.Kernel.Tests/DaySlicingTests.cs` (87 тестов).

**Проверка:** `dotnet test` Kernel.Tests (445 пройдено, 1 пропущен) и Architecture.Tests (147) — с `nice -n 10`; мутация floor в FloorDiv роняет 2 теста. Golden-векторы DayRate посчитаны независимо на Python; `DayRate(0,1; 12)` = `1 − 0,9^(1/12)` в пределах 1e-14.

**Не проверено:** полный набор тестов, verify.py, CI, golden Session (код не вызывается из Core); ревью reviewer; падение теста роста без O-069 не прогонялось; `DayRate` бросает при δ > 100 % и росте ≥ 100 %/день (ADR-0003 молчит).
