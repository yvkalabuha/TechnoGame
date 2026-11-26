---
id: TASK-0079
title: "F4-A: parallel ViewBuilder.Inputs per actor, TrainingBudget plan once per actor"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0078]
related: [ADR-0016, ADR-0017, EXP-0007]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "#317 (next: ADR-0017 and F4); light, ADR-0017 «Вне ADR»"
created: 2026-11-18
---

# TASK-0079 — F4-A: parallel ViewBuilder.Inputs per actor, TrainingBudget plan once per actor

## Цель
F4-A из `docs/simulation/tick-profile-m0.md` §4: убрать повторный `Allocate` в Session без общего кэша.

## Описание
1. `ViewBuilder.Inputs` строится параллельно по акторам; результаты пишутся в слоты `rows[a]` по индексу актора, порядок вывода не зависит от потоков.
2. `ProductsInputsAdapter.TrainingBudget`: `Plan`/`RestActor` считаются один раз на актора внутри `Purchases` (локальная переменная цикла по линиям).
3. Условие ADR-0017 «Вне ADR»: кэши F2 не заполняются лениво при чтении из параллельных потоков; если заполняются — заполнить заранее или доказать потокобезопасность и детерминизм.

Вариант B (общий мемо `Allocate`) вне задачи: он задевает правило записи в compute и требует записи в ADR.

## Критерии приёмки
- [ ] `StateHash` и снимок view побайтово равны прежним (golden, `lab selftest`).
- [ ] L2: threads 1/4/8/16 × ChunkSize 1/8/32 — одинаковый `StateHash` и одинаковый view.
- [ ] Замер до/после `ViewBuilder.Inputs` на (200, 2000) в «Результате» (справочно; официальный — EXP-0007).
- [ ] `dotnet test Singularity.sln` зелёный.

## Тесты
Свойство «view параллельный == view последовательный» на (24, 180) с командами в нескольких месяцах; L2.

## Связанные документы
`docs/simulation/tick-profile-m0.md` §4 (F2, F4), ADR-0017 «Вне ADR», ADR-0016.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят
- [ ] Детерминизм L1/L2

## Результат
**Сделано.**
- `ViewBuilder.Inputs` строит акторов параллельно (`Parallel.For`, `MaxDegreeOfParallelism = Config.Threads`; при 1 — обычный цикл), результат пишется в слот `rows[a]`, порядок от потоков не зависит. Тело актора вынесено в `ActorInputs`. Перегрузка с `maxThreads` — internal.
- F2: отдельных действий не нужно. `PrcSet` читает `_prcMemo`, который строится только в конструкторе и `Post` (однопоточно), при чтении не заполняется; устаревшая запись не возвращается. Других ленивых кэшей в Core/Session на пути `Inputs` нет (поиск `??=`/Lazy/Memo/Interlocked).
- `TrainingBudget`: изменений нет. `Plan` считается только при `TrainingTarget(a) == d`, то есть не более одного раза на актора за `Purchases` (цель одна). Вариант «локальная переменная цикла» ничего бы не дал; общий мемо между вызовами — вариант B, вне задачи.

**Файлы.** `src/Singularity.Session/Views/ViewBuilder.cs`; тест `tests/Singularity.Lab.Tests/ParallelViewInputsTests.cs` (в Lab.Tests, так как нужен синтетический мир (24, 180)).

**Проверка.** `lab selftest` (Release): 48/48, golden hash тот же. Тест: (24, 180, CrossEdges), 6 месяцев, команды в месяцах 1, 2, 4; threads 1/4/8/16 × ChunkSize 1/8/32 — одинаковые `StateHash` (HashAssert) и текст всего view по месяцам. `dotnet test Singularity.sln`: 0 падений (Audio 77, Arch 103, Kernel 330, Loc 131, Core 1060, Lab 21, Session 362).

**Замер `ViewBuilder.Inputs`, (200, 2000, CrossEdges), месяц 1, Release, 16 ядер (справочно).** До (последовательно, путь threads=1 = прежний код): 240 и 238 мс. После (8 потоков): 51 и 42 мс (≈5×). Замер временным тестом, удалён.

**Ревью (medium+low).** Ошибки актора пишутся в `Exception?[] errors[a]`, после цикла перебрасывается ошибка минимального актора через `ExceptionDispatchInfo` (как в TickRunner), тип не зависит от числа потоков; тест `tests/Singularity.Session.Tests/ViewInputsFailureTests.cs` (фантомные акторы, threads 1 и 8) использует internal-перегрузку `Inputs(w, month, maxThreads)`.
