---
id: TASK-0131
title: "Core changes for G5/G6 data: licence mask word 2, generation cost multipliers, control-layer guards (train 2, T-G)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
