---
id: TASK-0024
title: "T7: why_not по контракту §6 и 4 сценария «первый слой»"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0021]
related: [HYP-0002]
approved_by: orchestrator
approved_at: 2026-11-03
approval_ref: "спека v0.12 §11 T7; контракт v2.4 r3 §6"
created: 2026-11-03
---

# TASK-0024 — T7: why_not по контракту §6 и 4 сценария «первый слой»

## Цель
Реализовать `why_not(actor, target)` по контракту слоёв §6 и 4 сценария, где первым звеном по очереди оказывается каждый слой (T7 спеки). Это вход для правил ИИ T6d и прокси читаемости §5.6.

## Описание
- Чистая функция чтения состояния слоёв знания, способности и изделия (без записи, без RNG); формат звеньев, `eta`, `limiter`, рычаги — по контракту §6 (в т.ч. v2.4: снятый проект не даёт звеньев; F10 — `limiter` процесса без разрыва).
- Место в коде — по `docs/architecture/SLICE_COMPOSITION.md` (чтение снимка; Session отдаёт результат наружу). Не встраивать в compute слоёв.
- Задача идёт параллельно с T6b в отдельном worktree: не менять `PolicySystem`, `ToolingInvest`, `IProductsInputs`.

## Критерии приёмки
- [ ] Контракт §6 исполнен полностью; каждое расхождение с ним записано в «Результат».
- [ ] 4 сценария «первый слой» (спека §6.4) — тесты.
- [ ] Валидатор «рычаги соответствуют сборке» (§5.6 d).
- [ ] Детерминизм: результат не зависит от порядка обхода и числа потоков; нет float.

## Тесты
Юнит-тесты звеньев; 4 сценария; тест на `ENGINEERS` при `granted/n < 0,9` (§5.6 b); архитектурные тесты.

## Связанные документы
`docs/architecture/LAYER_CONTRACT.md` v2.4 r3 §6; спека v0.12 §5.6, §6.4, §11 T7; `docs/game-design/PROCESS_DEVELOPMENT.md` §10 (слова `limiter`), `docs/game-design/STATE_WORDS.md` (если есть).

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
**Сделано.** `why_not(actor, target)` — чистая функция снимка в `Singularity.Core.WhyNot` (`WhyNotAnalyzer`, `Link`, `LeverRegistry`, `LeverValidator`); наружу отдаёт `SliceSession.WhyNot(actor, target)` и `CurrentRequirement(actor)` (цель §5.6: новейшее известное APP без единицы в строю). Цели: `App(node)`, `Design(slot)`, `Requirement(g)`. Список полный, порядок — контракт §6 (слой, `yield_if_fixed` ↓, `eta` ↑ с `null` в конце, `subject_id`; хвостовые поля делают порядок полным). Слои: KNOWLEDGE (ближайший недостающий узел замыкания `requires` по остатку цены `(need−prog)·c_eff`; недоступная альтернатива слота), CAPABILITY (узкие процессы с `yield < 0,7`: H/M-источник или `ramp`; для H — `limiter` и `subject_id = subject_prc`), PRODUCT (нет проекта, `granted/n < 0,9`, `SERIES_GATE`, дефекты, дефицит сплава), ECONOMY (допущено, `unit_cost·n_plan + upkeep` выше бюджета закупок). Рычаги — только из `LeverRegistry` сборки (в срезе все; `process_development` выключен под абляцией `EffortZero`); `force_series` помечен «только игрок».

**Файлы.** Новые: `src/Singularity.Core/WhyNot/{Link,WhyNotAnalyzer,WhyNotAnalyzer.Knowledge,WhyNotAnalyzer.Capability,WhyNotAnalyzer.Product,LeverValidator}.cs`; `src/Singularity.Session/Slice/WhyNotInputsAdapter.cs`; `tests/Singularity.Session.Tests/WhyNotTests.cs`; `tests/Singularity.Architecture.Tests/WhyNotArchitectureTests.cs`. Минимальные хуки: `KnowledgeSystem` (+`InFrontier`, `CEff`, `ProgressRate`; `ComputeFull` берёт `c_eff` из общего `CEffOf`, поведение то же), `ProductsSystem` (+`UnitCostAt`), `SliceWorld` (+`ProductsParameters`, `WhyNot`), `SliceSession` (+`WhyNot`, `CurrentRequirement`).

**Проверка.** `dotnet test` целиком зелёный (Architecture 80, Kernel 284, Session 112, Core 779). 21 тест `WhyNotTests`: 4 сценария «первый слой» (KNOWLEDGE: USA, `Requirement(2)`; CAPABILITY: USA, `Requirement(1)` со слабой культурой; PRODUCT: сильная способность, проекта нет; ECONOMY: допущенный прототип, содержание выше бюджета), `ENGINEERS` при `granted/n` = 0,89 (звено) и 0,90 (нет звена; §5.6 b), `SERIES_GATE` (have/need/eta/рычаг «только игрок»), снятый проект и пустой слот без звеньев, `ramp`-звено, `AltUnavailable`, `eta` знания = потолок остатка/скорости, `limiter ≠ NONE` при `yield < 0,7` с источником H (месяцы 0/12/24, все акторы и поколения), порядок списка, валидатор рычагов (§5.6 d: выключенный, неподходящий, пропущенный рычаг; реальные списки чисты), чистота (хеш состояния не меняется, повтор даёт то же), одинаковый результат при 1/4/3/8 потоках и разных чанках, с зондами и без. Архитектурные: нет RNG, нет записи в колонки (`Next`/`Load`) в `Core.WhyNot` и адаптере.

**Расхождения с контрактом (решено по ближайшему к нему варианту).**
1. `lever` — список `Levers` (в контракте пример с двумя рычагами); плюс поля `Process`, `Dim`, `Limiter`, `VariantSlot` сверх восьми полей.
2. `eta` звена KNOWLEDGE для hazard-узла: `need = θ_start`; при `prog ≥ θ_start` `eta = null` (открытие — розыгрыш).
3. Звено без процесса и узла (`NoDesign`, `AlloyDeficit`, `NotAffordable`) несёт `eta = null`; `yield_if_fixed` у KNOWLEDGE = 0, у `NoDesign` — `yield` гипотетического состава `superior` (если он собирается).
4. «Открыты `defects_hidden`» (PRODUCT) прочитано как: проект в С/В, скрытые + открытые дефекты выше `k_gate·D_ref`; у прототипа с закрытым допуском вместо него — `SERIES_GATE`.
5. «Дефицит CMP»: `Alloy.Deficit` или поставка меньше `alloy_kg·темп` (темп — последний шаг серии, иначе `r_ref`); `n_plan` экономики — тот же темп.
6. Звено «нет проекта» — без рычага (в реестре рычагов для него нет).
7. `eta` для M-источника: скорость — `IWhyNotInputs.ToolingRate`; в сборке пока константа 0 (плана оснастки нет), поэтому `eta = null`. Так же `LicenceOffered = false` до событий T6d.
8. `eta` для `ramp`: скорость — темп линии × `n_p`; `need` — `X`, при котором `ramp = 0,7`.
9. Снятый проект: поля `cancelled` в коде ещё нет (F14 — T6c); сейчас любой `Withdrawn` не даёт звеньев и не считается проектом, этого же требует контракт для снятого из П/О.
10. Цель `CurrentRequirement` на старте сценария пуста у всех акторов (новейшее известное APP у каждого уже «в строю» по стартовому парку или APP не известно) — следующее поколение объясняется явным `Requirement(g)`.

**Заметки для слияния с T6b.** (а) `WhyNotInputsAdapter.ToolingRate` подключить к правилу оснастки T6b (`IndustryMath.Tooling` с вложением плана) — сейчас 0; (б) `ProcurementBudget` в адаптере считается так же, как в `ProductsInputsAdapter` — если T6b/T6c поменяют формулу бюджета закупок, поправить обе; (в) конфликты возможны только в `SliceWorld` (+2 свойства и строка конструктора `WhyNotAnalyzer` после `RegisterRunColumns`) и в `ProductsSystem` (+1 метод рядом с `Upkeep`); (г) T6d: `LicenceOffered` читать из предложения лицензии.
