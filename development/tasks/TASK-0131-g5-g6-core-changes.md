---
id: TASK-0131
title: "Core changes for G5/G6 data: licence mask word 2, generation cost multipliers, control-layer guards (train 2, T-G)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M05
agent: developer
category: core
priority: high
needs_owner: false
depends_on: [TASK-0121]
related: [TASK-0128, TASK-0129]
approved_by: owner
approved_at: 2026-11-19
approval_ref: "#423 (Core change for later generations allowed); O-038"
review_ref: "reviewer: APPROVE 19f67df + deltas 208d2d6, e22608d (3 rounds); simulation-engineer Lab part; codex: 3 returns, waived by #432, open → DEBT-0079; landed 951ce9d"
created: 2026-11-23
---

# TASK-0131 — Правки Core под данные G5/G6 (поезд 2, шаг T-G)

## Цель
Данные G5/G6 (TASK-0128) нельзя загрузить и прогнать без правок Core: узлы с номерами 64 и выше не помещаются в лицензионную маску, у поколений 5–6 нет множителей стоимости, контрольные слои Lab знают только G1–G4. Правки делаются до данных и на текущих данных ничего не меняют (`docs/architecture/notes/slice-horizon-2030.md` §3, §5 «Поезд 2»).

## Описание
1. Лицензионная маска на 2 слова целиком по H13 заметки: `LicenceRules`, `Has`, `LicenceView.Mask`, цикл `KnowledgeInputsAdapter` (сейчас 1…63), колонки `PolicySystem.Events`, сохранение и загрузка. `Has` вне диапазона — громкая ошибка.
2. Множители стоимости узлов для поколений 5–6: параметры (значения задаст TASK-0128 после калибровки; до тех пор — значения из данных, без констант в коде).
3. `af_class` 5–6, ранги планера 6–7, `pu_ranks` 5–6: подтвердить тестом, что `ProductClassTable.Build` их принимает, иначе поправить.
4. Lab: контрольные слои L_K, L_K0, L_P на данных с поколением > 4 отклоняются громкой ошибкой (страж), CSV по поколениям до G6. Ключи L_K (коллизия H10) не меняются, пока слои не расширены; это записывается в DEBT.
5. Доказательство «ничего не меняется на текущих данных»: хеши проекций равны до и после правки на seeds 1–3, месяцы 387 и 1019.

## Критерии приёмки
- [ ] Тесты, падающие без правки: бит лицензии 64 и 77 туда-обратно (включая сохранение и загрузку); `Has(…, 64)` при одной маске — громкая ошибка; стоимость узла G5 (на старом коде — `ArgumentOutOfRangeException`); `actor_month` с 6 поколениями; страж контрольных слоев.
- [ ] Golden на текущих данных не меняются; `SchemaVersion` поднимается один раз вместе с поездом 2 (O-037).
- [ ] L1/L2 на 1020 месяцах; без аллокаций на тике; без static-буферов.
- [ ] Ревьюер APPROVE; второе ревью по #432.

## Связанные документы
`docs/architecture/notes/slice-horizon-2030.md`, `docs/research/slice-g5-g6-content.md`, `docs/reviews/slice-g5-g6-content-*.md`.

## Ход работы
- 2026-11-23 developer: базовые хеши (все колонки, seeds 1–3, месяцы 387 и 1019, Main) сняты на 2590da6 до правок: `tests/Singularity.Session.Tests/Fixtures/train2-baseline.txt`, тест `Train2IdentityTests`.
- Core/Session/tests сделаны (см. «Результат»). Lab (страж слоёв, CSV по поколениям, одна строка в `SliceChecks.cs:381`) не сделан: `src/Singularity.Lab/**` вне зоны записи developer. Нужен simulation-engineer.
- 2026-11-23 simulation-engineer: часть Lab (п. 4 описания) — см. ниже. **Для Orchestrator (строка TECH_DEBT, H10):** ключи RNG L_K в `KnowledgeRng.cs:46–52` — `n = 1 + g` для часов и `n = 5 + g` для «никогда». При g ≤ 4 они не пересекаются (2…5 и 6…9), но если L_K расширят на g ≥ 5, часы поколения g = 5 (n = 6) и «никогда» поколения g = 1 (n = 6) возьмут один ключ — коллизия тихая, выборки станут зависимыми. Сейчас не срабатывает: L_K/L_K0/L_P на данных с поколением > 4 отклоняются стражем `JetSliceModelFactory.CheckControlGenerations`. Предл.: при расширении L_K перейти на `n = 1 + g` / `n = 1 + G_max + g` (или отдельный поток) и перезаписать эталоны L_K одним поездом.
- 2026-11-23 simulation-engineer, Lab: `SliceChecks.cs:381` (`LicenceView` с `LicenceMask`); `SurfaceVersion` 14 → 15; страж `CheckControlGenerations` (L_K, L_K0, L_P при узле или поколении проекта > 4 → `ParamException` до чтения `CONTROL_TABLES` и первого месяца); `actor_month` по `SliceTables.Generations(data)` (`ActorMonthFor`, `CalibrationFor`; при 4 поколениях — та же таблица), `SliceObserver.ActorMonthCells`: поколение выше раскладки — `SliceFormatException` вместо молчаливого отброса. Проверка `slice: G5/G6` (`SliceModelTests.Six_generation_…`): фикстура — копия `mods/core` с G5/G6 (те же правки, что `SixGenerationDataTests`). Падение без правки проверено: страж выключен → «Lk accepted six-generation data»; старый отброс gen > 4 → строка `…,0,0,…` вместо `…,2,1,…`. Формат: `slice-csv-format.md` v0.5 r15.
- **Находка (developer, блокирует verify):** `LicenceMask.Of` бросает на номере > 127, а синтетический мир стенда (`SynthWorld`, напр. `a6-n180-cross`) имеет процессы с номерами до 180: `IndustryInputsAdapter.LicenceShare` → `LicenceRules.Has(mask, 138)` → `ArgumentOutOfRangeException` уже при `SliceSession.Create` (тест `BenchHarnessTests.Cli_Series_Calibration_NoRepeats`, лог дочернего процесса). Раньше `Has` молча давал `false` для номеров > 63. Кроме того, для синтетики с номерами 64…127 `wideLicenceMask` теперь включает лицензии на процессы, которые раньше не лицензировались — хеши стенда могут измениться. Правка вне зоны simulation-engineer (Core/Session).

## Результат
**Статус: частично. Часть Core/Session/tests готова, часть Lab (п. 4 описания) заблокирована зоной записи.**

Сделано:
- `LicenceMask` (два слова, узлы 1…127), `LicenceRules.Bit/Has/Validate` на нём; `Has` вне 1…127 бросает исключение (раньше молча `false`).
- `PolicySystem`: второе слово масок (`pol.lic.mask.1`, `pol.lic.offer.1`, `pol.lic.q.mask.1`) регистрируется только при `wideLicenceMask` (SliceWorld: максимальный номер узла > 63). На текущих данных (максимум 61) колонок нет, схема и хеши не меняются. Узел выше 63 в узкой политике — исключение.
- `KnowledgeInputsAdapter`: цикл 1…127; `LicenceView.Mask/OfferMask` — `LicenceMask`; `TestApiVersion` 14 → 15 (+ `session-surface.v15.txt`).
- `KnowledgeParams.CostGenMultiplier`: G5 = 6,4, G6 = 10,2 (первое предложение TASK-0128 до калибровки; G0…G4 не тронуты, G7 по-прежнему бросает).
- Тест `ProductClassTable`/`ProductsLayout` на 6 поколениях (af_class 5, ранги планера 6–7, pu_ranks 5–6): загрузчик принимает без правок.
- `SchemaVersion` не менялся, golden не перезаписывались.

Не сделано (зона): страж L_K/L_K0/L_P при поколении > 4; CSV `out_g/active_g` до G6 (`SliceObserver.cs:708`, `SliceTables.cs:51`); тест `actor_month` с 6 поколениями; запись в DEBT о коллизии ключей H10; `SliceChecks.cs:381` (`new LicenceView(0, -1, -1, 0, 0, …)` → `default, default`): без неё Lab не собирается, поэтому `verify.py` красный.
