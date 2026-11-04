---
id: TASK-0028
title: "T-fleet: списание и продажа парка (A42, контракт r6)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0027]
related: [HYP-0002]
approved_by: owner
approved_at: 2026-11-04
approval_ref: "DECISIONS_LOG #139, #141–#148; спека v0.17 A42, §11 T-fleet; контракт v2.4 r6 §4.4f"
created: 2026-11-04
---

# TASK-0028 — T-fleet: списание и продажа парка (A42, контракт r6)

## Цель
Реализовать списание и продажу парка на внешний рынок (#139, #141–#148): команды игрока и правило ИИ при перевооружении по поколению, цену, квоты, внешний приток денег в бюджет закупок, печати и абляции.

## Описание
По спеке `docs/simulation/slice-jet-aviation.md` v0.17: A42 (все пункты), §6.10, §11 строка «T-fleet» с приёмкой; контракт `docs/architecture/LAYER_CONTRACT.md` v2.4 r6 (§3.1, §4.0, §4.4d шаги 0, 2a–2g, 7, 8; §4.4e; §4.4f; §5; §6; §8; §9.1). Переключатель `FLEET_LINE_COND` (main = GEN, KAPPA и NONE — абляции); абляции NO_RETIRE, NO_SALE, PURCHASE_CAP. Не входит: T6d, F25 (why_not, следующая ревизия контракта).

## Критерии приёмки
- [x] Всё из §11 «T-fleet» (в т.ч. 5 и 5a: в NO_RETIRE сверяются колонки T6c2).
- [x] Инварианты FL1–FL7 и журнала ИИ (контракт §4.4f); `spent ≤ B_in`.
- [x] Общие пункты T6 (тесты, ADR-0003, нет RNG в Industry/Products, L1/L2); golden с причиной «T-fleet».
- [x] В «Результат» — печать на seeds 1…20 по §6.10 (E-gen, R2″, продажи gen ≥ 2, «парк на keep», Δ) как информация.

## Тесты
По §11 T-fleet.

## Связанные документы
Спека v0.17; контракт v2.4 r6; `docs/game-design/FLEET_RETIREMENT.md` v0.2; DECISIONS_LOG #139, #141–#148.

## REVIEW
- [x] Code review (reviewer) — approve с условиями; low закрыты (c1f817f); medium — контракт r7
- [x] Тесты — проходят; E-gen, продажи, keep, Δ в main не проверяемы до исправления `fielded` (диагностика), механика — фикстурами
- [x] Детерминизм L1/L2 — golden с причиной T-fleet

### Ревью reviewer (2026-11-04, вставлено Orchestrator)
Вердикт: approve с условиями; блокирующих ошибок в коде нет; dotnet test зелёный (Kernel 284 +1, Arch 80, Core 903, Session 172). Валидатор, слоты, когорты и пыль, квота Int128, год политики, `B_in` и `spent ≤ B_in`, порядок шагов 2c–2f, `line_bind`, `MergeMastered`, единая реализация `GFront`/`Fielded`/`DefK`/`KeyOf`, NO_RETIRE побитно — без замечаний.
- medium: ИИ решает продажу по `G_front(t)`, слой проверяет по `G_front(t−1)` при исполнении — ошибка контракта §4.4f (между снимком и шагом 2d проходит весь месяц t); в контракт r7.
- medium: отклонения 1–2 (проекция ИИ) — код верен, причина та же; в r7.
- low: `AiLineRate` без ⌊…⌋ и защиты от нуля; магическое число в тестах; отклонение 11 — комментарий.
- Отклонения 3, 5–8, 10 приемлемы; 9 — ошибка спеки §11 п.4 (simulation-engineer).
- Находка `fielded ≡ ∅` — данные/спека, не код (диагностика у simulation-engineer).

## Результат
**Сделано (developer, 2026-11-04; спека v0.17 A42, §6.10, §11 «T-fleet»; контракт v2.4 r6).**
- **Industry.** Колонка `ind.mr.{p}` (`mastered_run`, 0…6): пишется в merge сразу после `report_output`, старт `min(6, −since_month)` для `mastered` сценария; `IndustryParams.MasteredYield` 0,7, `VMin` 937,5 ч (5% от `V_ref` = 18 750 ч, `ProductsLayout.ReferenceVRef`).
- **Products.** `retire_cohort`/`sell_cohort` (валидатор с порядком кодов, слоты, старшие первыми, пыль, `loss_acc`), год политики (2a), `sale_income`, `B_in = ProcurementBudget + sale_income` (шаг 4), `ext_total` (2g), `line_bind` (шаг 7), колонки `retired/sold/quotaBase/soldY/lineBind`, `saleIncome/saleCap/saleY/extTotal`, 9 новых кодов отказа. Порядок шага 2: `close_line` → `sell` → `retire` → `start_series`. Чистые функции: `FleetRules` (цена, `Quote`, `SaleMax`, `Take`, `SweepDust`), `FleetBook` (одна реализация для слоя и проекции ИИ), `Fielded`, `FieldedGen`, `KeyOf` (`K = (G,T,N,P)`), `GFront`, `DefK`, `Keep`, `Quote`, `RetireForecast`/`SellForecast`, `WordOf(u)`. `LayoutVersion` 4.
- **Policy.** `PolicySystem.Fleet.cs`: правило A42.4 на проекции состояния шага 2 месяца t+1, `FLEET_LINE_COND` (`Gen` main / `Kappa` / `None`), абляции `FleetArm` (`NoRetire`, `NoSale`, `PurchaseCap`), `AiLineRate` с `inc_exp` и ограничением `PURCHASE_CAP`, колонки `pol.flt.*`. `PolicyParams`: `FleetUHi` 0,25/0,80/0,50, зазор 0,15, `FleetAProt` 60.
- **Session.** Команды игрока `RetireCohort`/`SellCohort` (кодек, 8 слотов `cmd.flt.*`), `SliceRunConfig.FleetArm/FleetLineCond/SaleArm` (колонка `run.fleet`), плечи продажи (`SigmaLo`, `PhiFlat`, `QuotaLo`, `QuotaHi`, `ChiOff`), `ProcurementBase`/`LicenceFlow` (0 до T6d), `SchemaVersion` 5. `why_not`: рычаги `RetireCohort`, `SellCohort` в звене `ECONOMY`, бюджет звена — `B_in`.
- **Golden (причина «T-fleet»):** `Golden60` 30a75d4f… → d25c32c2…; `Golden150` 513238aa… → e7547e32…; схема v4 → v5 (`39a36a45…`); `LayoutVersion` 3 → 4.
- **Тесты (новые).** Core `FleetCommandTests` (22), Session `FleetRuleTests` (14), `FleetAblationTests` (`NO_RETIRE` побитно воспроизводит колонки T6c2; эталон снят со сборки T6c2 до правок, `Fixtures/t6c2-*.txt`), `FleetPlayerCommandTests` (6), `FleetRunTests` (seeds 1…20 × 384 мес. по месяцам: FL6, FL7, R1, R4, R-gen, ноль отказов ИИ, согласованность `mastered_run`; плечи `NO_SALE`/`PURCHASE_CAP`/`NO_RETIRE`; печать). Итог `dotnet test`: Kernel 284 (+1 пропуск), Architecture 80, Core 903, Session 172 — зелёный.

**Отклонения / допущения (ближайшее к контракту).**
1. Проекция ИИ шире текста спеки: решение месяца t читает снимок «после t−1», а команды ИИ месяца t−1 выполняются в t уже после снимка. Проекция: шаг 0 t → сброс года t → собственные команды t−1 → шаг 0 t+1 → сброс года t+1. Иначе `COUNT`/FL7 нарушались бы. Проверено: ноль отказов, FL7 на всех месяцах seeds 1…20.
2. Кандидат не берётся при скрытых дефектах выше допуска (`X > 0`, форс-серия): аварии прошлого месяца неизвестны проекции. В main не встречается.
3. Проект, закрываемый связкой этого же compute, в кандидаты не входит.
4. `G_front` для продажи ИИ — по снимку t−1, исполнение — по снимку t. Если `fielded` пропадёт между ними, команда ИИ будет отклонена `NOT_SURPLUS` (P0). В естественных прогонах `G_front ≡ 0`, не проверено; в фикстурах мастерство удерживается вручную.
5. Слот `SLOT` занимает только принятая команда.
6. `line_bind` у линии без допуска — `NONE`.
7. `LicenceFlow` ≡ 0 до T6d.
8. `T`, `N` ключа — по процессам класса изделия; метрика §5.2 в Lab не реализована, Δ — по ключу `K` трёх великих держав с удержанием 24 мес.
9. Спека §11 п. 4 «`g_p = 2` на двигателе G1 → `gen` 1» не совпадает с данными (`eng_gen(G1) = 1` → `min(2, 2) = 2`); тест использует `g_p = 3` на G1 → 2.
10. Печати §6.10 — тест `FleetRunTests.Print_…` (команды Lab нет).
11. Проекция ИИ берёт доли закупок из политики (без `set_budget_shares` игрока).

**Печать (информация, не приёмка): seeds 1…20, 384 мес., main | NO_RETIRE.**
```
Выпуск по окнам 48 мес. (ед., p50):
USA  7940 5037 3693 3677 5541 3099 2021 1379 | 7940 5037 3677 2780 4159 4394 2021 1422
GBR  1286 1159  868  695 1130 1446 1386 1449 | 1286 1159  868  695 1130 1446 1258  877
SUN     0    0    0  539 1083 1349 1487 1773 |    0    0    0  539 1083 1349 1487 1773
FRA, SWE: 0 во всех окнах (оба режима)
u (‰ от ProcurementBase, месяцы с парком), p50 / p90 / max:
Quality  627/799/854 | 660/790/839   Mass 626/772/845 | 626/772/845   Adaptive 467/753/839 | 571/763/839
```
- E-gen: команд 137: (gen L, gen d) = (2,1) 123, (3,2) 14, остальные 0; доля `gen(L) ≥ 3` = 0,10 (ожидалось ≥ 0,2), доля (2,1) = 0,90 (ожидалось < 0,8). Взведено (`u ≥ U_HI` при линии) 8793 мес., молчит из-за `line_bind ≠ MONEY` 144; USA окна 6–7: 869 / 0. R-gen: нарушений 0.
- R2″ (USA P1/P3, 14 seeds): окна 1–2: медиана (main − NO_RETIRE) `u` = 0‰; окна 6–7: +23‰ (ожидалось ≤ −20‰ — не выполнено: освобождённые деньги идут в линию, `u` растёт). Выпуск USA в окне 7: медиана разности 0.
- R3″: USA/GBR с P1 или P3 и ≥ 1 командой: 26/27; P2: 0.
- Продажи: `sell_cohort` 0 (USA, GBR: 0/20 seeds); продано gen ≥ 2: 0/20; «отстающими своего фронта»: 0; `ext_total` = 0. **`G_front ≥ 1` в 0 из 7680 seed-месяцев.**
- Парк на keep (актор-годы окон 5–7, великие державы): P1 0/216, P2 0/228, P3 0/276.
- Δ (#143, по ключу `K`, 3 великие державы, 360 мес.): `chg` main 0,00; NO_RETIRE 0,00; NO_SALE 0,00; PURCHASE_CAP 0,20; Δ = 0,00 (ДИ ±0,00, n = 20).
- Списано ИИ (сумма по seeds): USA 244 491, GBR 49 631, остальные 0.

**Находка (высокая → simulation-engineer / владелец).** `fielded` пусто у всех акторов во все месяцы: у USA/GBR процессы 45, 49, 54 стартового проекта (`high_purity_nickel_refining`, `precision_blade_forging`, `jet_engine_assembly_test`) входят в `mastered` сценария, но их стартовый `yield` 0,10–0,40 (< 0,7), поэтому `mastered_run` обнуляется в первом месяце и не восстанавливается за 384 мес. (USA окна 6–7: yield 0,29 / 0,31 / 0,60). Следствие: `G_front ≡ 0` → продажа (`NOT_SURPLUS`) и защита `keep` в main инертны; E-gen, продажи, «парк на keep», Δ по ключу не проверяемы на этих данных. Механика проверена фикстурами. Числа и правило не подстраивались (#143).

(Перенесено Orchestrator из черновика developer: хук запрещает developer писать в development/tasks.)

### Исправления по ревью (developer, c1f817f)
1. `AiLineRate` при PURCHASE_CAP: ⌊(U_LO·B_base − need)/upkeep_L⌋; при upkeep_L ≤ 0 потолка нет.
2. Тесты: `>= (int)CommandRejection.BadRetireSlot` вместо магического числа.
3. Тест `FrontRunnerLostBetweenDecisionAndExecution_TheLayerRefusesTheSale_NotSurplus` фиксирует текущее поведение (отказ NOT_SURPLUS); решение — контракт r7.
4. Комментарий к отклонению 11 в `DecideFleet`.
