---
id: TASK-0025
title: "T6c: ИИ изделия — Requirement, пресеты, запуск, снятие (A40), поправка контракта r4"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0023, TASK-0024]
related: [HYP-0002]
approved_by: orchestrator
approved_at: 2026-11-03
approval_ref: "DECISIONS_LOG #123, #125, #128; спека v0.13 §11 T6c; контракт v2.4 r4"
created: 2026-11-03
---

# TASK-0025 — T6c: ИИ изделия — Requirement, пресеты, запуск, снятие (A40), поправка контракта r4

## Цель
ИИ слоя изделия среза (T6c спеки v0.13) и поправка контракта v2.4 r4 (#128). После задачи у каждого актора, знающего APP, есть проекты, серии и правило снятия; длинные прогоны — на политике ИИ, а не только на скрипте игрока.

## Описание
- По спеке v0.13 §11 строки «T6c» и «T6c — v0.13 (контракт r4)»: `Requirement` и пресеты (в т.ч. `adaptive` по `FV`, остаток DEBT-0016), запуск по доктрине A35, `r_AI`, одна линия (#125), `cancel_design` (F14, контракт §4.4e) и правило A40 с веткой С (`idle_c`, `built_any`, `close_line`), проверка живости манифеста (`t_launch_max < T_ab`, L-C1…L-C3), прокси `FORCE`, плечо `DERIV`, предикат `out_lim` (A41), правило переключения линии у `PlayerScript` (A41).
- Контракт r4: `planned` со стадией С, `v_exp = max(proc_v, V_C) + V_PO` на `home(a)` — функция Core, адаптер делегирует; колонки, `schema_version`.
- Не входит: лицензия, перебой сплава, триггеры по `why_not` (T6d); данные SUN (#127, #130–#132 — отдельная задача после этой).

## Критерии приёмки
- [ ] Всё из спеки v0.13 §11 «T6c» и «T6c — v0.13 (контракт r4)», пункты (1)–(10).
- [ ] Инварианты контракта r4: V1–V5 (§4.1b), (8)–(12) (§4.4e).
- [ ] Общие пункты §11 T6a–T6d.
- [ ] Печать на seeds 1…20 (пункт 10) приложена к «Результат».

## Тесты
По §11; golden и окна выпуска перезаписываются с причиной (r4).

## Связанные документы
Спека `docs/simulation/slice-jet-aviation.md` v0.13 (A35, A37, A40, A41, §4, §6.8, §11); контракт `docs/architecture/LAYER_CONTRACT.md` v2.4 r4 (§3.1, §4.1a–b, §4.4d–e, §5); `docs/architecture/SLICE_COMPOSITION.md` r2; `docs/game-design/COMPONENT_SELECTION.md`; TASK-0022, TASK-0023, TASK-0024 «Результат».

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
