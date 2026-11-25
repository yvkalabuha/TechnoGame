---
id: TASK-0156
title: "Player levers client: research goals, budget in money with reserve, production line window, licence market window"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
Часть 1 из 2 (ветка worktree-agent-a6b87971094bb2d05). [Факт] Сделано:
- Остаток TASK-0149 / DEBT-0095: кавычки в `ui.csv` (две обрезанные строки) + тест всех строк таблиц; подсказки сплава больше не говорят, что без сплава не выйдет ни один двигатель; блокер «предел поколения двигателя» называет узел для изучения (`WhyLines.SubjectNode`); красный квадрат «Главная помеха:» с подписью, без повтора «Мешает»; в строке изделия «себестоимость машины» в $1990, «готовность» в %, «в строю … шт.», «выпуск … шт./мес.»; «в M1» убрано; «Закрыть линию» с подтверждением (что теряется — по economy-analyst); цвет текста неактивной кнопки = ink-2 (≥ 4,5:1, тест); «Что можно сделать» — кнопка «Выбрать цель (институты)»; автопауза по сигналам alloy/refusal/licence (`AutoPauseLogic`).
- K = 0,64828 с источником BEA в `src/Singularity.Client.Spike/data/dollars_1990.json`, читает `MoneyFormat` (O-079); 1 индекс/мес = 389,03 млн $1990.
- Окна «Бюджет и резерв» (B) и «Цели науки / институты» (N): `LeverWindow.cs`, модели `LeverModels.cs`, конфиг клиентской партии `ClientRun.cs` (PlayerActor, Reserve, Institutes, LicenceMarket, Adviser только для клиента; умолчания ядра не тронуты).
- Тесты: `tests/Singularity.Client.Audio.Tests/LeverModelTests.cs` (+ вся сборка 438 ок), `Singularity.Localization.Tests` 133 ок. Клиент собран (`dotnet build src/Singularity.Client.Spike`), игра не запускалась.
- Не сделано в части 1: окна «Линия производства» и «Рынок лицензий», эмблемы институтов, кадры и PASS ui-art-director, ревью reviewer.

## Контраст по пикселям кадров (ui-art-director, раунд 2, п. 1)
[Факт] `python3 -I tools/ui/frame_contrast.py` читает кадры `/var/www/gameDev/shots-out/TASK-0156-r2/*_1920x1080.png`, берёт пиксель чернил (дальше всего от фона по яркости) и фон (медиана области) внутри контрола, считает WCAG.

| кадр | контрол | чернила | фон | отношение | нужно |
|---|---|---|---|---|---|
| lever_budget | Сбросить (неактивна) | #55575A | #EEE9DA | 5,97 | 4,5 |
| lever_budget | Применить (неактивна) | #55575A | #F3EEDF | 6,25 | 4,5 |
| lever_budget | «+» (неактивна) | #55575A | #F3EEDF | 6,25 | 3,0 |
| lever_market_buy | Купить (неактивна) | #55575A | #EFEADB | 6,03 | 4,5 |
| lever_market_buy | Выставить (неактивна) | #55575A | #F0EBDC | 6,08 | 4,5 |
| lever_budget | ▲ (оранжевый D9860F) | #D9860F | #F5F2EA | **2,55 — ниже 3,0** | 3,0 |
| lever_budget | ▲ резерв | #D9860F | #F7F4EB | **2,59 — ниже 3,0** | 3,0 |

Вывод: текст неактивных кнопок 5,97–6,25 (≥ 4,5), впечатление «серый» — от тонкого начертания, не от цвета; замер art director по тексту не подтверждается. Оранжевый ▲ действительно ниже 3:1: теперь маркер предупреждения `Palette.WarnMark` = B35F00 (≈ 4,1 на бумаге; тест `WarnMark_HasThreeToOneOnPaperAndSheet...`, текст — `InactiveAndSubtitleText...`). Таблица выше — по кадрам до правки маркера; после следующей партии скрипт надо прогнать снова (кадры lever_budget, lever_market_buy).
