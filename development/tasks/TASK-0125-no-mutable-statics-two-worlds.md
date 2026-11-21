---
id: TASK-0125
title: "No mutable static fields in Kernel, Core, Session; two worlds on one thread pool (A6)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: test-engineer     # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0002, TASK-0115]
approved_by: orchestrator
approved_at: 2026-11-21
approval_ref: "PLAN.md этап 3, шаг 3.3; project-review-2026-11-19 A6 (light)"
created: 2026-11-21
---

# TASK-0125 — No mutable static fields in Kernel, Core, Session; two worlds on one thread pool (A6)

## Цель
Страж архитектуры до кода #228 (PLAN этап 3, шаг 3.3; обзор 2026-11-19, находка A6). Архитектурный тест смотрит только системы тика; буфер потока вне этой проверки уже дал утечку состояния между мирами. Нужно закрыть класс ошибок целиком.

## Описание
1. IL/reflection-тест в `tests/Singularity.Architecture.Tests`: во всех типах сборок Kernel, Core, Session (вложенные и сгенерированные компилятором типы включительно) нет изменяемых static-полей и нет `[ThreadStatic]` / `ThreadLocal<T>` / `AsyncLocal<T>`. Разрешено: `const`, `static readonly` неизменяемого значения (правила «неизменяемого» — как в `ScratchTests`, переиспользовать), явный список исключений с причиной на каждую строку. `static readonly` массив или коллекция, в которую пишут, — нарушение.
2. Тест «два мира на одном пуле потоков»: два независимых мира среза с разными seed считаются поочерёдно и параллельно (на одном `ThreadPool` / одном экземпляре исполнителя тика, 2 и 4 потока); хеши каждого мира по месяцам совпадают с хешами того же мира, посчитанного в одиночку.
3. Найденные нарушения на текущем main: исправить, если правка механическая и не меняет golden; иначе внести в список исключений с ссылкой на новую строку DEBT в отчёте (строку DEBT пишет Orchestrator).

## Критерии приёмки
- [ ] Тест static-полей падает на намеренно добавленном `static int` / `[ThreadStatic]` / изменяемом `static readonly List` в Core (мутации проверены, затем убраны).
- [ ] Тест двух миров проходит на ≥3 парах seed при 2 и 4 потоках и падает, если ввести общий static-буфер в одну систему тика (мутация проверена).
- [ ] Golden не меняются.
- [ ] reviewer APPROVE.

## Результат
**[Факт]** Тесты: `tests/Singularity.Architecture.Tests/NoMutableStaticsTests.cs` (4 теста) и `tests/Singularity.Session.Tests/TwoWorldsOnOnePoolTests.cs` (12 случаев: 3 пары seed x 2 и 4 потока x поочерёдно/одновременно, 37 месяцев, хеши каждого месяца сверяются с одиночным прогоном). `src/`, golden и SchemaVersion не менялись.

Правило static-полей — ЗАКРЫТЫЙ список: допустимы только `const`, `static readonly` из примитивов, enum, string, Int128/UInt128/decimal, readonly struct из таких полей, Immutable*/Frozen* коллекций таких типов и кэши лямбд компилятора. Интерфейс, object, массив, класс и обобщённая обёртка — нарушение, если нет исключения с точным именем и точным видом (Kind проверяется по реальному полю; устаревшая запись роняет тест). В списке исключений 9 `[ThreadStatic]`, 1 `ThreadLocal` на экземпляре, 13 константных массивов, 1 неизменяемый класс (`LeverRegistry.Slice`).

**Мутации.** Правки `src/` невозможны в зоне test-engineer, поэтому они сделаны в копии дерева в scratchpad (`.../scratchpad/mut`), в реальном `src/` ничего не менялось. Мутации самого теста (классы `StaticMutant`) лежат в репозитории.
- Копия, `IndustryMath` + `internal static int MutantCounter;`: `KernelCoreSession_HaveNoMutableStaticFields` падает: "IndustryMath.MutantCounter: static field is not readonly".
- Копия, `[ThreadStatic] internal static long[]? MutantTs;`: падает: "IndustryMath.MutantTs: [ThreadStatic]".
- Копия, `static readonly List<int> MutantList`: падает: "...MutantList: static readonly of a type that is not on the allow-list (List`1)".
- Копия, `static readonly IList<int> MutantIface = new List<int>()` (замечание Codex 1): падает: "...MutantIface: ... not on the allow-list (IList`1)".
- Копия, снят `[ThreadStatic]` с `IndustryMath.t_items/t_sorted` (общий буфер): `TwoWorldsOnOnePoolTests` падает в 7 из 12 случаев, в том числе поочерёдный прогон при 4 потоках; сообщения "System.ArgumentException : Process 44 is twice in the prc_set" и "TickComputeException : System 400 failed in Compute, chunk 0, tick 91".
- В репозитории (`StaticMutant`, тест `TheScan_Sees...`): `static int`, `[ThreadStatic]`, `static readonly List`, `IList`, `object`, `ThreadLocal<int>[]`, `ThreadLocal`, `AsyncLocal` должны быть найдены; `Fine`, `Max`, `ImmutableArray<string>`, `Int128` и кэш статической лямбды — не найдены; исключение неверного вида (`WrongKind`, `WrongArray`) даёт "listed as ThreadStatic but is None" / "listed as ConstantArray but is None".
- Устаревшая запись: временно переименовал `ClosureArena.t_sets` в `t_setsX`, упали `KernelCoreSession_HaveNoMutableStaticFields` и `EveryException_ResolvesToARealField` ("stale exception entries: ...t_setsX"); возвращено.

**Найденные нарушения (в исключениях, правка не механическая):** 9 `[ThreadStatic]` (ClosureArena x4, PolicySystem.t_decisions, ToolingPlan._temp, EffortScratch._forThread, IndustryMath.t_items/t_sorted) — потокозависимые временные буферы; нужна строка DEBT (Orchestrator). Константные массивы (13) можно заменить на ImmutableArray.

**Не проверено:** полный набор тестов и `verify.py`; мутации в реальном `src/`.

## Связанные документы
- `docs/reviews/project-review-2026-11-19.md` (A6)
- `tests/Singularity.Architecture.Tests/ScratchTests.cs`, `DeterminismRules.cs`
- `.claude/rules/csharp-core.md`
