---
id: TASK-0026
title: "D1: пакет стартовых данных (заменитель ранга 0, правки старта SUN/FRA/USA/GBR, узел 58)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M0
agent: developer         # ответственный агент
category: data
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0025]
related: [HYP-0002]
approved_by: owner
approved_at: 2026-11-03
approval_ref: "DECISIONS_LOG #127, #130–#136; спека v0.14 §11 D1"
created: 2026-11-03
---

# TASK-0026 — D1: пакет стартовых данных (заменитель ранга 0, правки старта SUN/FRA/USA/GBR, узел 58)

## Цель
Внести пакет стартовых данных, утверждённый владельцем (#127, #130–#135), и поля альтернатив v0.14, чтобы SUN мог производить реактивные самолёты без изменения правил культуры.

## Описание
По спеке `docs/simulation/slice-jet-aviation.md` v0.14 §11 строка «D1», §1.1 (таблица правок 1–8), §1.4 «Поля альтернатив v0.14» (`max_eng_gen`, `alloy_free`, `upkeep_mult`, `substitute`), A23, `why_not` `ENGINE_GEN_LIMIT`; пресеты по #136. Источник данных — `docs/research/sun-start-data.md` r2 (+ раздел R-KN). Абляции §6.9 — не в этой задаче (отдельный эксперимент после D1).

## Критерии приёмки
- [x] Всё из §11 «D1» (схема, валидатор, writer и `data_hash`, данные mods, эталон `products_ref.py` при необходимости).
- [x] 8 эталонных составов `product_class.md` §2 не меняются; тест K4.
- [x] Общие пункты T6 (тесты, ADR-0003, нет RNG в Industry/Products, L1/L2); golden-хеши перезаписаны с причиной «D1».

## Тесты
По §11 D1.

## Связанные документы
Спека v0.14 (§1.1, §1.3, §1.4, A23, §6.9, §11); `docs/research/sun-start-data.md`; `docs/game-design/rank0-steel-g1-review.md`; `docs/game-design/COMPONENT_SELECTION.md`; DECISIONS_LOG #127, #130–#136.

## REVIEW
- [x] Code review (reviewer) — approve; low закрыты (ee9ab40, d1ea97a)
- [x] Тесты — проходят (Arch 80, Kernel 284, Core 856, Session 137); покрытие оценено reviewer
- [x] Детерминизм L1/L2 — golden перезаписаны с причиной D1

### Ревью reviewer (2026-11-03)
Вердикт: approve. dotnet test зелёный (Arch 80, Kernel 284 +1, Core 854, Session 137). alloy_free (8 мест §1.4), upkeep·m последним множителем, спуск двигателя, правило заменителя в F-PC2/запасном, EngineLimitsHold, валидатор §1.4, writer product_classes/2, данные §1.1, K4 не тавтологичен, детерминизм — без замечаний. products_ref.py --check OK. Отклонения 2, 3, 6 приняты; 4 — уточнение спеки; 1 — временно, снято после обновления CSV.
Находки (low): (1) убрать D1-поправки из тестов сверки с CSV; (2) `ProductPresets.Derive`: сравнивать ранг двигателя фактического пресета, без второго `Superior`; (3) тест (г): вариант с перебоем (deficit = 1) и нетронутый `alloy_left` соседней никелевой линии; (4) тест (а): ссылка на побитный тест v24 или проверка цены.

## Результат
**Сделано (D1, спека v0.14 §11).**
- Схема и загрузка: `ProductClassTable` (+`Build`) — поля `max_eng_gen`, `alloy_free`, `upkeep_mult`, `substitute`, ранг 0 только у `substitute`; слот сплава ищется по полям (правило 8), а не по первой альтернативе; валидатор отклоняет: `alloy_free`+`alloy_kg`, `alloy_kg: 0`, смесь `alloy_kg/alloy_free` в слоте или поле вне слота сплава, слот сплава без `alloy_kg`, `substitute` в слоте двигателя / без не-`substitute` рядом / с рангом не ниже прочих, `max_eng_gen` в слоте двигателя и вне 1..G_max, `upkeep_mult` ≤ 0. Writer: поля для каждой альтернативы с умолчаниями, версия `product_classes/2` → `data_hash` новый. JSON-схема `mods/schemas/product_class.schema.json` дополнена.
- Core: `DesignRules` — `EngineAllows/AvailableWith/EngineLimitsHold`, `Superior`/`Producible` перебирают двигатели сверху вниз (для `producible` — от выбранного правилом слота вниз), заменитель участвует в основном правиле, пропускается в запасном варианте и F-PC2 при наличии не-`substitute`; `alloy_free` — в `L_P`, шаге 7 закупок, `FleetValue` (`ProductsMath.AlloyFreeCount`), `alloyOk` пресета, `why_not` (по флагу); `upkeep·m(d)` последним множителем (`ProductsMath.Upkeep(..., mult)`; 5-арг. форма не тронута); `compose_design`/`derive` — `SLOT_UNAVAILABLE` по итоговому составу; `DERIV` считает двигатель по `Superior`; `why_not` `ENGINE_GEN_LIMIT` (`Link.Slot`, `SubjectId` = узел рычага, `Have` = max_eng_gen, `Need` = eng_gen; `ResearchPriority` разрешён для него в `LeverValidator`).
- Данные мода: правки 1–8 §1.1 (SUN AERO qc 3,50/3,90, GBR MECH precision 4,00/4,20, USA/GBR CHEM purity 4,00/4,20, SUN знает 58 и `K_d` aero 0,30, FRA знает 54 и `K_d` prop 0,20, узел 58 без 30), `hot_steel_g0` и строка процесса 47 в `product_classes.yaml`.
- Эталон `products_ref.py` (`v24`): `hot_steel_g0`, `BASE_COST[47]` = 37, `upkeep_m`, `n_alloy` для `alloy_free`, секция `steel_g1` (complexity 20, floor 1324, upkeep 23 → 57 raw, perf 0,90); старые значения не менялись (diff `out/products.json` — только добавление; `--check` зелёный).
- Тесты: `SubstituteTests` (30 кейсов: эталон стали, `m` = 1 побитно, 8 эталонов, спуск двигателя, пресеты, compose/derive, `alloy_free` в `L_P`/серии/`FV`, валидатор), `SliceStartDataTests` (K1, K4, пресет SUN, `ENGINE_GEN_LIMIT` с рычагом 30 → 33 → процессы); обновлены тесты данных (значения клеток, `known`, 18 альтернатив, 20 строк процессов, 58 без 30, замороженные хеши `data_hash`/`scenario_hash`, `CurrentRequirement` SUN). Golden-хеши `LinkedRun` и `ProductAiSession` перезаписаны, причина «D1». Полный `dotnet test` зелёный.

**K4 (печать):** `FV(сталь)/FV(ранг 1)` G1 при `FvBudget`, t0: USA 83,55 / 151,90 = 0,550 (n 190 против 310); GBR 46,10 / 78,42 = 0,588 (n 190 против 290). Условие K4 выполнено. **K1:** все известные PRC всех акторов на t0 имеют `yield > 0` (минимум FRA PRC 54 = 0,000338; SUN PRC 54 = 0,037).

**Отклонения от спеки / допущения (ближайшее к спеке):**
1. `nodes.csv`/`scenario_1945.md` (docs/research, зона research-scientist) не менялись: тесты сверки с CSV (`Nodes_MatchTask0011Csv`, `KnownAtStart_MatchesNodesCsv`) содержат явные D1-поправки (58 без 30; SUN +58; FRA +54). Когда CSV обновят, поправки в тестах нужно убрать.
2. Рычаг `ENGINE_GEN_LIMIT`: `Link.SubjectId` = номер узла для изучения (первый неизвестный `requires`/PRC не-`substitute` альтернативы с наименьшим рангом: 30, затем 33, затем 39, 45, 49), слот в новом поле `Link.Slot`.
3. Отказ `compose_design`/`derive` по `max_eng_gen` — код `SLOT_UNAVAILABLE`; текста «max_eng_gen» в журнале нет (журнал хранит только коды).
4. Для `producible` двигатель выбирается прежним правилом слота, ниже него — спуск при пустом слоте (спека требует спуск «сверху вниз»; при полном знании сплава результат совпадает с v0.13).
5. `IndustryMath.LineCapacity/SplitAlloy` не менял: Products их не вызывает (расчёт в `ProductsSystem`); линии `alloy_free` туда не попадают.
6. Формат сохранения не затронут (поля — данные мода).

**Информационный прогон (не приёмка), ИИ A41 включён, 384 мес., выпуск = Σ темпа линий в серии, окна по 48 мес. (0–47 … 336–383):**
```
seed 3  USA 0 0 0 0 4539 4232 947 26   | GBR 0 0 0 0 197 411 459 521 | SUN 0 0 0 537 868 1169 260 26   | FRA, SWE 0
seed 1  USA 0 0 806 3260 3407 2564 0 0 | GBR 0 0 0 0 203 396 434 535 | SUN 0 0 0 195 836 782 234 287   | FRA, SWE 0
seed 5  USA 0 0 0 0 3988 5023 312 0    | GBR 0 0 0 0 197 411 459 521 | SUN 0 0 0 819 993 1212 1423 728 | FRA, SWE 0
seed 7  USA 0 0 0 487 3002 265 6 0     | GBR 0 0 0 0 197 411 459 521 | SUN 0 0 0 195 836 587 241 221   | FRA, SWE 0
seed 2  USA 0 0 0 782 1957 0 0 0       | GBR 0 0 0 0  76 320 356 393 | SUN 0 0 0 384 1066 218 249 149  | FRA, SWE 0
seed 11 USA 0 0 0 10 266 112 0 0       | GBR 0 0 0 0 203 396 795 841 | SUN 0 0 0 384 1017 1001 219 173 | FRA, SWE 0
```
Первый серийный G1 SUN (стальной, `eng_g1 + hot_steel_g0`): месяц 156 (seed 3), 180 (1), 144 (5), 180 (7), 168 (2), 168 (11) — K-SUN (< 36 мес.) **не выполняется**. Диагностика seeds 3/5: SUN знает 58 с тика 0, узел 48 (PRC центробежного компрессора) открывается на месяце 123 / 113, проект — сразу после (мес. 133 / 121), серия через 23 / 23 мес.; узел 30 открывается позже или раньше 48 (75 / 132). Узкое место — срок открытия 48 в слое знаний, а не данные D1. FRA/SWE за 384 мес. серийного G1 не имеют ни в одном seed (по FRA 54 известен, но PRC 54 имеет yield ≈ 0,0003). Для USA/GBR «первый серийный G1» в этом прогоне — месяц 212 (G1-парк USA/GBR — стартовый, новая серия идёт уже по G2+).

### Исправления по ревью
1. Из `CoreNodesTests` убрана поправка `requires` 58 (CSV обновлён, 00dbaa1): сверка идёт с CSV напрямую. В `KnownAtStart_MatchesNodesCsv` поправка осталась: колонка `known_at_start` в `nodes.csv` всё ещё без SUN/58 и FRA/54 (нужна правка research-scientist). Поправки клеток культуры в `ActorValues_...` остаются (они не из CSV, а из таблицы сценария).
2. `ProductPresets.Derive`: ранг двигателя сравнивается по `full[BaseSlot]` после `Compose`, второй вызов `Superior` убран.
3. `SubstituteTests.AlloyFree_SteelLineLeavesTheNeighbouringNickelLinesAlloyUntouched` (supply 1450 кг и 0 = перебой deficit 1): стальная линия (меньший `design_id`) выпускает, никелевая получает ровно `supply/290`, т.е. стальная линия сплав не берёт.
4. Тест (а): комментарий ссылается на побитную сверку составов и цен в `ProductsV24ReferenceTests` (сравнение с `products.json`).
