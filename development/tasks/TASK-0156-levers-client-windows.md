---
id: TASK-0156
title: "Player levers client: research goals, budget in money with reserve, production line window, licence market window"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M05
agent: developer
category: client
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0149, TASK-0150, TASK-0151, TASK-0152, TASK-0153, TASK-0155]
related: [TASK-0148, TASK-0156]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "#482 (designs approved), #470-#481"
created: 2026-11-24
---

# TASK-0156 — Player levers client: research goals, budget in money with reserve, production line window, licence market window

## Цель
Окна рычагов в клиенте: цели, бюджет в деньгах с резервом, окно линии, рынок лицензий.

## Описание
docs/game-design/PLAYER_LEVERS_WAVE1.md и docs/game-design/economy/production-lines-and-licence-market.md: интерфейс каждого рычага с ценой решения до клика и причиной недоступности словами. Может быть разделена на подзадачи по окнам. Дизайн каждого окна — ui-art-director до кода.

## Остаток TASK-0149 (ui-art-director, раунд 2; DEBT-0095 — первым шагом)
- Красный квадрат «Главная помеха» без подписи; повтор «Главная помеха. Мешает:».
- Производство: «себест.», «готовность», «в строю» без единиц; «Закрыть линию» без подтверждения.
- Контраст текста неактивной кнопки «Отдать приказ: фокус» ≥ 4,5:1.
- «Что можно сделать» — кнопками, когда действия подключены.
- Автопауза по событиям (#196, #481).
- Gemini (запись visual-TASK-0149-81d8eda3): сплав — свой значок (слиток/полоса запаса), не колба; «себест.» полностью и с единицами, «готовность» в %, «скорость выпуска» в ед./мес.; убрать «в M1» из карточки производства.

## Критерии приёмки
- [ ] Окно целей: цена приоритета для остальной науки; без обещания «раньше» (O-082); подсказка советника о цели для института без уровня
- [ ] Коэффициент долларов 1990 K = 0,64828 с источником (BEA NIPA, PD; `docs/game-design/economy/dollars-1990.md`) лежит в данных и читается `MoneyFormat` (O-079)
- [ ] Кадры 1920x1080 100 %, ui-art-director PASS
- [ ] reviewer APPROVE
- [ ] Партия владельца на сборке (#474)

## Связанные документы
`docs/game-design/PLAYER_LEVERS_WAVE1.md`, `docs/game-design/economy/production-lines-and-licence-market.md`, `docs/game-design/playtests/2026-11-24-owner-m-play.md`

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
