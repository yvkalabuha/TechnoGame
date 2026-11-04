---
id: TASK-0033
title: "T6d: лицензии, перебой сплава, правила ИИ по why_not, множитель наладки mult_P"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0032]
related: [HYP-0002]
approved_by: orchestrator
approved_at: 2026-11-04
approval_ref: "спека v0.18–v0.22 §3.5 A43, §11 T6d; контракт v2.4 r7–r8; DECISIONS_LOG #152, #154; DEBT-0019"
created: 2026-11-04
---

# TASK-0033 — T6d: лицензии, перебой сплава, правила ИИ по why_not, множитель наладки mult_P

## Цель
Завершить T6 среза: событие лицензии, перебой сплава, правила ИИ по `why_not` (программа качества P1/P3, лицензия и учебная серия P3), множитель наладки по профилю `mult_P` (A37, DEBT-0019).

## Описание
По спеке `docs/simulation/slice-jet-aviation.md` (актуальная v0.22): §3.5 A43 (A43.1–A43.8: предложение и цена лицензии, оплата через `LicenceFlow`/`B_in`, срок A43.4 по #152, маска PRC, перебой сплава A43.6, правила ИИ A43.7), A37 (`mult_P`; с F17b — у линии N), §4, §6.11 (ожидания объявлены до прогона), §9.1 потоки `EVT_LIC`/`EVT_DIS`, §11 строка «T6d» и приёмка (9 пунктов); контракт `docs/architecture/LAYER_CONTRACT.md` r8 (колонки политики `lic_*`, `offer_mask`, `licence_flow`, `dis_start`/`dis_end`; команда `take_licence` с валидатором `BAD_LICENCE{SELF, EMPTY, GEN, MONEY}` (#154); функции `LicenceActive`, `LicenceShare`, `LicenceOffered`, `AlloyDeficit`; инварианты L-M1/L-M2/L-S1/L-D1, E-LIC-0; переключатели `LIC_OFF`, `T6D_OFF`). F27 (`LicenceFlow` в `LineRate` продавца) — только если спека его включила; иначе отклонение. Не входит: лицензионное производство (#157). Ответ с Результатом — Orchestrator.

## Критерии приёмки
- [ ] Все 9 пунктов §11 «T6d» и инварианты контракта для лицензии и перебоя.
- [ ] `mult_P` по A37 (DEBT-0019) с тестом различия профилей.
- [ ] Общие пункты T6; golden с причиной «T6d».
- [ ] Печать §6.11 на seeds 1…20 (ожидания объявлены до прогона) — как информация.

## Тесты
По §11 T6d.

## Связанные документы
Спека v0.22; контракт r8; DECISIONS_LOG #47, #104, #107, #149–#154; DEBT-0019; `docs/simulation/fra-swe-zero-diagnosis.md`.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
