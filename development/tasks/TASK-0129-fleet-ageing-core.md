---
id: TASK-0129
title: "Fleet ageing without replacement in Core (#431, #435-#437)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M05
agent: developer
category: core
priority: high
needs_owner: false
depends_on: [TASK-0121]
related: [TASK-0128, TASK-0118]
approved_by: owner
approved_at: 2026-11-22
approval_ref: "#431, #435, #436, #437 (mechanic approved by owner; code is light inside it)"
review_ref: "reviewer: APPROVE train 2 (4d37a33); codex: waived #432 after 3rd return (f9ae0774), remainder DEBT-0091; landed 88fa41b"
created: 2026-11-23
---

# TASK-0129 — Старение парка без замены в Core (#431, #435–#437)

## Цель
Сейчас когорта парка обнуляется в 180 месяцев, и страны десятилетиями остаются без самолётов (замер до 2030). Владелец решил: парк без замены не списывается по возрасту, а стареет.

## Описание
Правило — `docs/game-design/notes/fleet-without-replacement.md` (редакция 4 с выборами владельца). Решения:
- #431: перестарок остаётся в строю до прихода замены; ценность и место падают постепенно.
- #435: порог «в строю» (50 единиц) считается по эффективным единицам (число × ценность возраста); ценность парка в формулу места не входит (#143).
- #436: кривая — полураспад 5 лет сверх срока службы (не линейная).
- #437: игрок может списать новейшее поколение без замены — после предупреждения.
- #433: правило потолка бюджета в этот поезд не входит.

Часть «поезда схемы 2»: новая колонка состояния (счётчик замены), `SchemaVersion` +1 и одна перезапись golden — вместе с TASK-0128 (данные), TASK-0118 и TASK-0119. Ветка готовится сейчас, сливается с поездом.

## Критерии приёмки
- [ ] Правило по записке, ред. 4: учёт замены по приходу, кривая полураспада в Fx6 без float, порог по эффективным единицам, команда списания с предупреждением (вердикт в виде).
- [ ] Тесты, падающие без правки: перестарок без замены остаётся в строю; ценность через 5 лет сверх срока = половина; 300 единиц уходят из «в строю» через ≈ 12,9 года, 2000 — ≈ 26,6 года; замена по приходу снимает перестарки сверху вниз.
- [ ] L1/L2 на 1020 месяцах; без аллокаций на тике; без static-буферов.
- [ ] Смена golden объяснена построчно; ревьюер APPROVE; второе ревью по #432.

## Связанные документы
`docs/game-design/notes/fleet-without-replacement.md`, `docs/game-design/FLEET_RETIREMENT.md`, `docs/reviews/fleet-without-replacement-deepseek.md`.

## Ход работы
- Кривая: таблица из 60 констант Fx6 + сдвиг (O-036) в `ProductsParams.AgeValue`; `f(60)=500000`, `f(120)=250000` точны. Таблица лежит в коде как `ReadOnlySpan<int>` (константные данные, не static-поле: архитектурный тест запрещает изменяемые static). В `DataHash` модов она не входит (это код, не данные мода) — отклонение от текста записки §3.
- Колонка `prd.d{d}.replCredit` (Fx6 raw), `SchemaVersion` 12 -> 13. Обнуляется в `TakeSlot` и при переходе в `Withdrawn`.
- Шаг 0 (`ExpendReplacement`): уходят самые старые перестарки в пределах кредита, кредит не выше числа целых единиц, пыль сметается. Начисление (`AccrueReplacement`) — в `Produce`, на живом состоянии шага (псевдоним проекта запущенного в этом шаге берётся из `MemoNext`). Чистые функции — в `FleetRules` (`ExpendCredit`, `Accrue`, `Replaces`, `Effective`).
- Порог «в строю»: `FieldedMasteredOf` и `UnitsBuiltMasteredOf` считают по `n_eff` на месяце запроса; `UnitsBuiltMastered` получил параметр `month`.
- ИИ: `AgeOut` убран (`FleetBook.ExpendCredit` — тот же шаг 0, что в Core); `Aged` без предела по сроку; перестарки списываются первыми (`RetireOverage`, до `u >= U_HI`, без условия линии и привязки к деньгам), `keep` считается по `n_eff` (`MaxRetire`, бинарный поиск; при отсутствии перестарков равен прежнему `Whole - keep`).
- Команда: `RetireCohort.Acknowledged` (в журнале поле `"ack":true` пишется только при true — старые журналы читаются), вердикт `CommandRefusal.NeedsConfirmation` с `BestGenBefore/BestGenAfter` в `CommandVerdict`; проверка в `SliceSession.Submit` по `RetireForecast` (в нём новые поля `GenBefore/GenAfter`). Прогноз срока — `ProductsSystem.FieldedUntil`.
- Эталоны: объяснение построчно ниже. Идентичность старых колонок с `main` до месяца 176 доказана проекционными тестами (Horizon, T6d, FleetAblation, ToolingUnmet, GoldenProjectionAgainstMain) — они ограничены месяцами < 177: после 177-го траектория отличается намеренно.

## Ревью, раунд 1 (CHANGES)
- RetireOverage: 3 теста в `FleetRuleTests` (`Overage_*`): перестарок первыми и по возрасту кохорты без линии; при линии без привязки к деньгам; `keep` по `n_eff` для Def(K). Все 3 падают с отключённым RetireOverage (проверено). Заодно фикстура теперь обнуляет `replCredit` слота.
- Проекция ИИ: за 1020 месяцев seed 3 слоем отклонено 109 retire (все BadRetireCount, USA), 0 sell; тест `FleetAgeingRunTests` проверяет sell == 0 и retire <= 220. Проекцию не менял (выпуск месяца ей и раньше неизвестен); отклонение записать как D2.
- Месяц 176: 12 строк (4 конфигурации x 3 seed) сняты на 83567c4 (main до задачи) и добавлены в `horizon-baseline.txt`; `HorizonTests` проходит.
- `new List<int>()` на пути решения ИИ заменён на stackalloc. Пункт 5 (gen/perf один раз за актор-месяц) не делал: Produce вызывается раз на линию, выигрыша нет.

## Разногласия
- Записка §2.5 предлагает `retire_forecast` как запрос Session. Я НЕ добавлял публичных методов в `SliceSession`: любое изменение публичной поверхности требует `TestApiVersion` 15, а `Lab/Models/Slice/JetSliceModel.cs` (`SurfaceVersion = 14`) вне моей зоны и падает по сборке. Предупреждение уходит в вердикте `Submit`; запрос прогноза (`ProductsSystem.RetireForecast`, `FieldedUntil`) готов в Core, его выведет TASK-0130 (Session views) вместе с повышением версии.
- ИИ-проекция не учитывает начисление кредита от выпуска текущего месяца (проекция и раньше не знала выпуска); в редком случае Core убирает перестарков больше, чем предполагал ИИ, и команда списания отклоняется слоем (счётчик отказов, состояние не меняется).
- Перестарок в `RetireOverage`: ограничение числа — `wanted` (покрыть избыток содержания), а не «все перестарки»; записка не уточняет.

## Результат
**Что сделано.** Парк без замены не списывается по возрасту (#431); ценность когорты = `2^(-m/60)` через таблицу (#436, O-036); порог «в строю» по `n_eff` (#435); замена по приходу через колонку `replCredit`; ИИ списывает перестарки первыми; игрок может списать новейшее поколение после предупреждения (#437). Потолок бюджета не вводился (#433).

**Файлы.** Core: `Products/ProductsParams.cs`, `FleetRules.cs`, `FleetBook.cs`, `ProductsSystem.cs`, `ProductsSystem.Fleet.cs`; `Policy/PolicySystem.Fleet.cs`; `Commands/SliceCommand.cs`, `CommandValidator.cs`, `CommandCodec.cs`. Session: `SliceSession.cs` (Submit), `Slice/SliceWorld.cs` (SchemaVersion 13). Тесты: `Core.Tests/Products/FleetAgeingTests.cs` (новый), `Session.Tests/FleetAgeingRunTests.cs` (новый), `FleetPlayerCommandTests.cs`, правки старых тестов ниже.

**Тесты (падают без правки: проверено откатом шага 0 к старому AgeOut и отключением проверки Submit).**
- ALoneFleet_StopsCounting (50/100/300/2000 единиц -> m = 1/61/156/320): ПАДАЕТ без правки (проверено откатом).
- Cohorts_PastServiceLife... (3 теста вместо старых «списывается в 178»): падают без правки (проверено).
- RetiringTheWholeBestFleet_NeedsConfirmation: падает без правки (проверка Submit отключена — проверено).
- TheFullHorizon (L1, L2 threads 1 vs 4 на 1020 месяцах, перестарки и кредит реально встречаются): падает без правки (проверено: нет перестарков).
- AgeValue/Effective/ExpendCredit/Accrue/Replaces/MaxRetire/BookProjection/FieldedUntil/Slot-Withdrawn: используют новые функции, без правки не компилируются; откатом по поведению не проверялись (FieldedUntil и ArrivingUnits проходят и при откате шага 0: честно отмечено).
- Codec ack, ARetirementThatKeepsTheBestGeneration: новые, без правки не компилируются.
- Переписаны под #431: ProductsSystemTests.Cohorts_..., ProductsLpTests (2), ProductsInvariantTests (ожидание кохорты, `Withdrawals == 0`), FleetRuleTests.Projection_ACohort... (перестарок ещё считается), DailyTickProbeTests (окно сдвинуто на мес. 237: причина закрытия линии теперь в мес. 239).
- Проекционные тесты ограничены месяцами < 177: HorizonTests, T6dTests, FleetAblationTests, ToolingUnmetTests (потеряно покрытие после 176-го; другого эталона старого кода нет).

**Эталоны (перезаписаны один раз в этой ветке).** Причина всех строк одна: новая колонка `prd.d*.replCredit` входит в хэш с месяца 0 + SchemaVersion 13; старые колонки до месяца 176 не меняются (проекции выше). Строки: `a0b7ab02... -> 4e38785d...` (seed 1 YProd = Main = view/calibration/series-switch, месяц 149); F17b Admit seeds 1-5 (`35a18ce3->eb6de34c`, `b4f448d2->4810c301`, `01bf7318->8e140ae6`, `27ba55c5->bd9a913a`, `485aed7d->6bb0eb11`); F17b YProd seeds 2-5 (`176268f7->05984441`, `e4d91b3e->b7a7bffa`, `b8826fc7->227d65aa`, `fa8a0682->9ed730a5`); LinkedRun `0a9560b8->667265a0`; ProductAi `557aef80->e32429bd`; TwoWorlds digests seeds 1,2,3,4,5,7 (`dd295a0d->5e8262ef`, `f084e313->e98719bf`, `f6e47b9a->9eee14c0`, `418d215c->d6323f74`, `b20711e9->24fa7353`, `b898c0f5->9e754c20`); отпечаток схемы `633423dc->821599dc`. Сумма строк подтверждена тем, что YProd seed 1 (F17b) = Main seed 1.

**Не сделано / вне зоны.** `src/Singularity.Lab/Models/Slice/SliceChecks.cs` (`Golden150 = a0b7ab02...` должен стать `4e38785d9e7141948dcd980666aef35998fbfa2f6759badfde899597d6dac028`) — Lab, зона simulation-engineer; без этого `verify.py` selftest падает на этой строке. Lab-тесты (`Singularity.Lab.Tests`) могут иметь свои эталоны, см. отчёт.
