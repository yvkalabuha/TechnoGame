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

## Связанные документы
- `docs/reviews/project-review-2026-11-19.md` (A6)
- `tests/Singularity.Architecture.Tests/ScratchTests.cs`, `DeterminismRules.cs`
- `.claude/rules/csharp-core.md`
