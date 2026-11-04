---
id: TASK-0030
title: "F17b: передача линии по рабочему выходу годных (удержание r_keep)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0029]
related: [HYP-0002, EXP-0003]
approved_by: owner
approved_at: 2026-11-04
approval_ref: "DECISIONS_LOG #138, #160; спека v0.20 A41, §11 F17b"
created: 2026-11-04
---

# TASK-0030 — F17b: передача линии по рабочему выходу годных (удержание r_keep)

## Цель
ИИ закрывает рабочую линию только когда новая серия выходит на рабочий выход годных (#160); до этого рабочая линия держится на темпе `r_keep`, остальные деньги закупок — новой линии.

## Описание
По спеке `docs/simulation/slice-jet-aviation.md` v0.20: A41 (правило F17b, `LineRate(W) = min(r_AI, r_keep)`), A35, A37, A40, A42.4 п.1, §4, §11 строка «F17b» и «F17b — приёмка» (10 пунктов); переключатель `LINE_CLOSE` {`YPROD`, `ADMIT`} (по умолчанию YPROD). Не входит: эксперимент T6e2 (simulation-engineer после задачи).

## Критерии приёмки
- [x] Все 10 пунктов «F17b — приёмка».
- [x] Общие пункты T6 (тесты, ADR-0003, нет RNG в Industry/Products, L1/L2); golden с причиной «F17b».
- [x] Результат (в ответе Orchestrator) — печать seeds 1…20: выпуск по окнам, сосуществование двух линий (месяцы), закрытия по `y_prod` и по A40.

## Тесты
По §11 F17b.

## Связанные документы
Спека v0.20; `development/experiments/EXP-0003-t6e-tooling-target.md`; DECISIONS_LOG #138, #160.

## REVIEW
- [x] Code review (reviewer) — medium закрыты (eea5ecf)
- [x] Тесты — проходят; покрытие оценено reviewer
- [x] Детерминизм L1/L2 — golden L1 ИИ seeds 1…5, ADMIT побитно

### Ревью reviewer (2026-11-04, вставлено Orchestrator)
Вердикт: changes-requested (high нет). Логика F17b по A41 v0.20 верна; детерминизм в порядке. medium: нет golden L1 ИИ-прогона YPROD и доказательства побитности ADMIT; приёмка п.1 (`LineRate(W) = r_keep`, деньги N) не доказана; порядок правил 1/2 в месяц запуска не задан спекой (решение Orchestrator: правило 2 до правила 1); нет тестов L = N в A42.4 и учебной серии = N. low: BadClose слишком мягкий; `lines` и pending starts; `LINE_CLOSE` не в хеше; ограничитель по `ProductAi` на весь прогон (TECH_DEBT). Сосуществование GBR/SUN без срока — свойство модели, не код.

## Результат
**Сделано (developer, 6a3b291; перенесено Orchestrator).** `LINE_CLOSE` {YPROD по умолчанию, ADMIT}. `y_min(d,t)` = min `AdmittedYield` по `prc_set` (неизвестный PRC = 0). Запуск ниже `y_prod` закрывает только кандидата N; на/выше — всё (#125). Месячное правило 2 закрывает W при двух линиях и `y_min(N) ≥ y_prod`. W работает на `min(r_AI, r_keep)`, `r_keep` = 6,25. Цель учебной серии и L в A42.4 — N. Больше двух линий — `PolicyInvariantException`. Golden не изменились (новых колонок нет). Тесты: Arch 80, Kernel 284 (+1), Core 938, Session 187 (+1).
**Печать seeds 1…20 (YPROD, p50, окна 48 мес.).** USA 9406 4945 3249 3008 6392 1811 1309 403; GBR 2086 1076 646 593 1534 1649 1218 870; SUN 0 0 0 663 1153 1282 1383 1810; FRA, SWE 0. Сосуществование двух линий (Σ месяцев): USA 3102, GBR 1001, SUN 546. Закрытия по `y_prod`: USA 15 (+12 в пакете запуска). A40 С: 0; О: 31 (недопуск), дефекты 0.
**Отклонения.** (1) `mult_P` P2 (A37) в коде нет — переключать нечего (DEBT-0019). (2) Нет отдельного юнит-теста A42.4 с L = N. (3) `NoRetire` и `TrainingBudget` идут с ADMIT (тесты старого правила). (4) Удержание действует только при `ProductAi` (иначе менялся Golden60). (5) Печать — в тестах, не в Lab. (6) Тесты «одна линия» → «≤ 2». (7) `LINE_CLOSE` не в манифесте Lab — для T6e2.
**OPEN.** GBR и SUN сосуществуют до конца без закрытия W (срок не ограничен, §13) — смотреть в EXP T6e2.

### Исправления по ревью (developer, eea5ecf)
1. Golden L1 seeds 1…5 (150 мес., ИИ): ADMIT = хеши сборки 29cd914 (побитно до F17b); YPROD закреплены; Golden150 перезаписан («F17b», бит 12 `run.fleet`).
2. Приёмка п.1: в месяцы сосуществования `Rate(W)` ≤ `r_keep`, при связывании ровно 6,25; N выпускает.
3. Правило 2 до запуска (решение Orchestrator); счётчик «закрытие по правилу 2 в месяц запуска»: USA 12.
4. Тесты L = N в A42.4 и цель учебной серии = N.
5. BadClose ужесточён. 6. Комментарий о pending starts. 7. `LINE_CLOSE` — бит 12 `run.fleet` (без новой колонки).
Тесты: Arch 80, Kernel 284 (+1), Core 940, Session 189 (+1).
