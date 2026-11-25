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

**Codex раунд 1:** (1) рост 5 %: ошибка запаса до 1,025·Y raw (запас 7480, Y=365: −374) — потери floor (<1 raw/день) усиливаются последующим ростом, т.е. граница роста Y·(1+|δ|), а не Y; найдено сканом, тесты на запасах 7480/18426; это поправка к ADR-0027 «Проверка» п. 2 (Orchestrator). (2) `S − d` теперь в BigInteger; тест на δ = −9·10¹⁸. (3) GoldenProjectionAgainstMain на ветке: 3/3 пройдено. (4) golden DayRate для δ 3,54/4,70/5,84/26,2 % и роста 2/5 % при Y=365/366 (посчитаны на Python). Нижние: `Y ≤ 366`, `Debug.Assert(x ≥ 0)`, тест распада `error ≥ 0`.

**Решение владельца (по сообщению Orchestrator; запись в DECISIONS_LOG на main) вместо расширенного допуска:** `ApplyDayRate(stock, rate, ref long remainder)` переносит дробный остаток Q53 в колонке вызывающего (та же строка, старт 0); изменение — floor накопленной суммы точных произведений, потери floor не копятся. Годовая ошибка ≤ Y raw + допуск DayRate для роста и распада (строгая граница восстановлена, тесты на запасах 1000/7480/18426/…); крошечный запас (1000 raw при 5 %) теперь растёт до 1049–1050. Старый вариант без остатка удалён. Вызывающему нужна колонка остатка на каждый запас (роль и класс — на ветке `levers`/`m1-228`).

**Codex раунд 3:** (1) `ApplyDayRate` хранит скрытое значение V = x·2⁵³ + remainder (remainder со знаком); V меняется на floor(V·|r|), затем x округляется в медленную сторону (распад — вверх, рост — вниз), поэтому на точной цели результат не быстрее (1011 raw, 26,2 %, Y=365 → 747, цель 746,118). (2) `DayRate` отказывает при годовом росте > 100 % (δ < −1) и Y вне 1..366 (при загрузке), граница ошибки ≤ Y raw + ≈4·Y·x₀/2⁵³ задокументирована. (3) remainder пишется только после успешного шага. Тесты: строгое направление и граница на рациональной цели, случайные 150 пар, отказ роста > 100 %, неизменность остатка при переполнении.
