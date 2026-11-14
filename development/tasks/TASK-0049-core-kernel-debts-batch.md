---
id: TASK-0049
title: "Долги Core/Kernel: DEBT-0004, 0006, 0011, 0012, 0013, 0017, 0021"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: core
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: orchestrator
approved_at: 2026-11-14
approval_ref: "Владелец 2026-11-14: «долги закончи»"
created: 2026-11-14
---

# TASK-0049 — Долги Core/Kernel: DEBT-0004, 0006, 0011, 0012, 0013, 0017, 0021

## Цель
Закрыть долги ядра DEBT-0004, 0006, 0011, 0012, 0013, 0017, 0021 (`development/TECH_DEBT.md`; полный текст каждого — там).

## Описание
- 0004: `DetMath.CheckFinite` — сообщение через `InvariantCulture`.
- 0006: тест инвариантов ToyFunnel — по `RowCount`, без жёстких 6 стран. Расхождение «Merge vs post» описать одной строкой в спеке ToyFunnel или в контракте (что место проверки оправдано).
- 0011: атрибут `[Scratch]` и IL-проверка ADR-0013 §4 (scratch не читается вне своей системы, не хешируется), с исключениями для дескрипторов колонок и неизменяемых предвычисленных данных. Переиспользовать механизм `[Observation]` (TASK-0047) без дублирования.
- 0012: транзитивная IL-проверка «нет RNG в способности» (через `IIndustryInputs` и другие namespace) и явная проверка источника skilled-запросов.
- 0013: члены `IIndustryInputs` — abstract; Lab (`SmokeT4s`, `LineStand`) реализует их явно.
- 0017: загрузчик мода нормализует CRLF до парсинга; тест на CRLF-мод.
- 0021: план оснастки A39 считается один раз на (актор, месяц) — кэш в scratch или индексная сортировка без LINQ/аллокаций.

## Правила
- Golden-хеши и поведение не меняются ни в одном пункте. Если меняются — остановиться и описать.
- `src/Singularity.Lab/Models/Slice/*` в это время правит simulation-engineer (TASK-0044) — не трогать.

## Критерии приёмки
- [ ] Тест на каждый пункт; полный `dotnet test` зелёный; golden без изменений.
- [ ] Второе ревью Codex — до чистого accept (#181).

## REVIEW
- [ ] Code review (reviewer)
- [ ] Второе ревью (codex)

## Результат

Сделано (golden и поведение не менялись):
- 0004: `DetMath.CheckFinite` форматирует через `InvariantCulture`.
- 0006: `ToyFunnelInvariantTests` — по `RowCount`; спека ToyFunnel §5 поясняет, почему проверка стоит в Merge.
- 0011: `[Scratch]` (поля), `ScratchTests` + общий `OwnerOnlyScan` (его же использует `ObservationTests`). Правила: поле private, не Table/Column, не читается вне своего класса (в т.ч. через не-private член, отдающий буфер); любой изменяемый буфер системы — `[Scratch]` либо в явном списке предвычисленных. Помечены буферы Knowledge/Industry/Products/Policy.
- 0012: `CapabilityNoRngTests` транзитивен (вызовы + реализации интерфейсов в Core/Session); `WorkforceRequestSourceTests` — источник skilled-запросов.
- 0013: `EffortMultiplier` и `ToolingInvest(..., out unmet)` — abstract; Lab (`LineStand`) и тестовые реализации — явно.
- 0017: `StrictYaml.Parse` нормализует CRLF; тест `CrlfMod_LoadsToTheSameHashes`.
- 0021: выбран вариант «индексная сортировка»: LINQ в `ToolingPlan` заменён сортировкой span с детерминированным tie-break (`Position`), без аллокаций. Кэш плана не делался: Plan зовут и в compute (параллельно), и после merge — кэш потребовал бы инвалидации.

### 2026-11-14 — ответ на Codex review, раунд 1 (base 71d32a9, head 30d7fcf)
Все четыре замечания приняты; golden и поведение не менялись.
1. high, `ScratchTests`: denylist типов заменён allowlist безопасных форм (`IsSafeType`): примитив/enum/string, дескриптор колонки или Table, интерфейс (инжектируемый сервис), неизменяемое значение (`ImmutableArray`, `Nullable`, кортеж, struct/class, все поля которого readonly/init-only и сами безопасны). Остальное (массив, `List`, класс с изменяемым полем) должно быть `[Scratch]` или в списке исключений с комментарием (`KnowledgeSystem._p/_start` — конфиги с константными массивами; `ProductsSystem/PolicySystem._mailbox` — канал в IndustrySystem; `FunnelSystem.<model>P` — адаптер). Поле не-readonly: допустимо только если пишется лишь в конструкторе или `Add*Columns` (IL: stfld/stsfld, включая вложенные типы/замыкания). Негативные тесты: `private int _counter` (пишется в Compute), изменяемый объект `Blob`, auto-property `Total {get;set;}` в `MutantSystem`. Прежних нарушений в production не найдено (кроме перечисленных исключений).
2. high, `WorkforceRequestSourceTests`: выбран вариант «одна фабрика» вместо трассировки данных. В `ProductsLayout` добавлены `SkilledRequest(site, line, cluster, n)` и `static EngineerRequest(site, requesterId, n)`; `ProductsSystem` (3 места) и `PolicySystem` (1) переведены на них. Архитектурный тест: конструктор `WorkforceRequest` в Core вызывают ровно эти две фабрики (rogue с произвольной категорией/ID ловится). `WorkforceRequestFactoryTests` (Core.Tests) фиксирует категорию и `requester_id == SkilledRequester(line, cluster)`. Значения запросов идентичны прежним.
3. medium, `CapabilityNoRngTests`: `Targets` теперь идёт и в overrides virtual/abstract методов собственных классов (по `GetBaseDefinition`). Overrides корней фреймворка (`object.ToString/Equals`) намеренно не разворачиваются: иначе достижим `PrintMembers` любого record с CounterRng (`SliceStreams`) — ложное срабатывание; ограничение описано в doc-комментарии. Тест `TheTransitiveCheck_SeesARngInAnOverrideOfAVirtualAndOfAnAbstractMethod`.
4. medium, `DetMathPowFracTests.OverflowMessage_IsTheSameInEveryCulture`: сообщение `PowFrac(1.5, ...)` одинаково при invariant/ru-RU/de-DE/fr-FR (проверено: без `InvariantCulture` в `CheckFinite` тест падает).
Файлы: tests/Singularity.Architecture.Tests/{ScratchTests,WorkforceRequestSourceTests,CapabilityNoRngTests}.cs, tests/Singularity.Core.Tests/Products/WorkforceRequestFactoryTests.cs, tests/Singularity.Kernel.Tests/DetMathPowFracTests.cs, src/Singularity.Core/Products/{ProductsLayout,ProductsSystem}.cs, src/Singularity.Core/Policy/PolicySystem.cs.

### 2026-11-14 — Codex review, раунд 2 (WorkforceRequestSourceTests:22)
Принято, проверено: скан видел только конструктор; публичный `SkilledRequest(site, line, cluster, n)` можно вызвать с любыми line/cluster, а `with` (для record struct = копия + вызов init-сеттера `set_*`) и `default`/`new()` (`initobj`) конструктор не вызывают. Исправлено только в тесте (production не менялся): `WorkforceRequestSourceTests` теперь (а) считает созданием вызов конструктора, любого `set_*`, `<Clone>$` и `initobj WorkforceRequest` и разрешает это только двум фабрикам `ProductsLayout`; (б) фабрики можно вызывать только из списка методов: `ProductsSystem.Produce` (Skilled), `BureausRequestOrIdle`, `DetectDefects`, `PolicySystem.Compute` (Engineer); замыкание `<M>b__` считается за M. Негативные фикстуры: вызов фабрики из неразрешённого метода (обе фабрики), `with { Category = 1 }`, `default`. Golden/поведение не менялись.

### 2026-11-14 — Codex review, раунд 3
1. `ScratchTests.Writers`: `ldflda`/`ldsflda` поля со скалярным типом (примитив/enum) считается записью (`Interlocked.Increment(ref _n)`); поля-структуры не считаются: безопасная структура целиком readonly, небезопасную ловит `IsSafeType`. Негатив: `MutantSystem._viaRef`.
2. Структурно: публичной фабрики `SkilledRequest(site, line, cluster, n)` больше нет (private). Единственный вход — `ProductsLayout.ReserveSkilled(box, chunk, site, line, clusterHours)`: кластер берётся из индекса `clusterHours`, requester id и n считает сама (`SkilledRequester`, `ProductsMath.People`), длина массива и `line` проверяются в рантайме. `ProductsSystem.Produce` вызывает её вместо цикла; порядок и значения запросов те же. Arch-тест: билдеры (конструктор) — только `SkilledRequest`/`EngineerRequest`, `SkilledRequest` вызывается только из `ReserveSkilled`, `ReserveSkilled` — только из `ProductsSystem.Produce`. Тесты `WorkforceRequestFactoryTests`: по запросу на занятый кластер с верным id/n/category и отказы при неверной длине/line.
