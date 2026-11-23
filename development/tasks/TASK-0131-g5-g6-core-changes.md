---
id: TASK-0131
title: "Core changes for G5/G6 data: licence mask word 2, generation cost multipliers, control-layer guards (train 2, T-G)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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

## Результат
