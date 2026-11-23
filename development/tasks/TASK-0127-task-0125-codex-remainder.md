---
id: TASK-0127
title: "Open Codex findings after TASK-0125 landing (#432)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M05
agent: test-engineer
category: core
priority: medium
needs_owner: false
depends_on: [TASK-0125]
related: [TASK-0125]
approved_by: orchestrator
approved_at: 2026-11-22
approval_ref: "#432 (light)"
created: 2026-11-22
---

# TASK-0127 — Open Codex findings after TASK-0125 landing (#432)

## Цель
По решению владельца #432 задача TASK-0125 слита после круга, на котором ревьюер дал APPROVE; открытые замечания второго ревью Codex перенесены сюда, чтобы не потеряться.

## Описание
Замечания из `tools/llm_worker/runs/review-TASK-0125-265ec51e-codex.json`:
- [high] `tests/Singularity.Architecture.Tests/NoMutableStaticsTests.cs:178` — The fresh-array exemption lets a writable static buffer pass both architecture scans.
- [medium] `tests/Singularity.Session.Tests/TwoWorldsOnOnePoolTests.cs:116` — The concurrent test can pass without overlapping the tick operations that use the shared buffer.
- Ревьюер (круг 7): добавить `Marshal` и `GCHandle` в запрещённые классы; поправить причину для `ViewBuilder.Inputs` («захвачен только лямбдой `Parallel.For`, не переживающей вызов»).
- Классификация по #432: находка «свежий массив» касается полноты страж-теста, а не дефекта детерминизма в коде ядра; поэтому перенесена, а не исправлена до слияния.

Каждое замечание: исправить или отклонить с причиной (в «Разногласия»). Высокие замечания по детерминизму и сохранению по #432 не переносятся — их в этом списке нет.

## Критерии приёмки
- [ ] Каждое замечание закрыто: исправлено с тестом или отклонено с причиной.
- [ ] reviewer APPROVE; второе ревью по правилу #432.

## Ход работы
- [high] «свежий массив» (NoMutableStaticsTests): `IsFreshArray` заменён. Раньше смотрелась только предыдущая инструкция по имени (`InitializeArray`, `stelem*`), без декларирующего типа. Теперь по эффектам стека идём назад от вызова `AsImmutableArray` до инструкции, создавшей верхний слот; массив свежий, только если это `newarr` или копирующий `ToArray` (`dup`, `stelem`, `InitializeArray` лишь действуют над массивом, созданным производителем). Ветвление или неизвестный эффект стека = не свежий. Добавлены мутанты `UnsafeMutant.WrapWritten`, `WrapStaticWritten`, `WrapLocalNew`, `WrapInitStatic` (метод `FakeHelpers.InitializeArray` с тем же именем возвращает чужой массив). Проверка: со старым `IsFreshArray` тест `TheUnsafeScan_SeesEachMutant_AndOnlyTheListedPairIsExempt` падает (мутант `WrapInitStatic`); с новым проходит. Существующие записи `Fresh*` не флагуются.
- [medium] перекрытие (TwoWorldsOnOnePoolTests): добавлен счётчик перекрытия шагов двух миров (`Overlap`), утверждение `Overlapped >= Months / 2`. Проверка: если обернуть шаги в общий `lock`, 9 из 23 тестов падают («overlapped in 0 of 37 months»); без блокировки проходят. Дайджесты не менялись.
- [reviewer] `Marshal` и `GCHandle` добавлены в `UnsafeClasses`; мутанты `Pin` (GCHandle.Alloc) и `Copy` (Marshal.Copy) утверждаются в тесте. Откат проверен только вместе с откатом finding 1 (одним прогоном), отдельно утверждения Marshal/GCHandle без добавления классов не гонялись.
- [reviewer] текст причины для `ViewBuilder.Inputs` исправлен в `UnsafeUses` (причина живёт в тесте, не в src).

- Круг 2 по замечаниям reviewer (CHANGES на b9c7a37):
  1. [high] `IlScanner.Instruction` получил `Offset` и `Targets` (короткий и длинный переход, switch); цели обработчиков исключений берутся из `ExceptionHandlingClauses`. `IsFreshArray` возвращает false, если между производителем и вызовом (включительно) есть цель перехода. Мутанты `WrapCond` и `WrapSwitch`. Проверено откатом: с отключённой проверкой целей тест `TheUnsafeScan_SeesEachMutant_AndOnlyTheListedPairIsExempt` падает, с проверкой проходит.
  2. [low] `ToArray` сопоставляется по `FullName` (`System.Linq.Enumerable`) и определению дженерика (`List<>`, `ReadOnlySpan<>`). Отдельного мутанта нет (проверка только чтением кода и существующими `FreshSpread`/`FreshCollection`).
  3. [medium] `Overlap`: месяц считается один раз, когда мир вошёл в шаг при `_active == 2`; утверждение `Overlapped >= 1`, сообщение «overlapped in N of 37 months». Проверено: под общим lock падают тесты («overlapped in 0 of 37 months»), без него 23 из 23 проходят.

## Разногласия
- Доказательство перекрытия именно внутри фазы compute (Parallel.For) без хука в продукте невозможно; счётчик доказывает перекрытие целых вызовов `StepThroughMonth` (барьер на месяц). Предложение: если нужен детерминированный рандеву внутри compute, добавить в `ViewBuilder.Inputs` (internal) необязательный тестовый колбэк; в этой задаче он не добавлялся (запрет менять поведение Core/Session). Решение Orchestrator: не в этой задаче, остаётся предложением на отдельную задачу.
