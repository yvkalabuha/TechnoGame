---
tags: [experiment, gameplay, ui, done]
---
# Owner playtest of the M-play build, 2026-11-24

Source: owner message in the Orchestrator session, 2026-11-24 (verbatim below). Build: main at 51a1659.

## Verbatim (Russian)

1. Нет чувства решений, я не могу выбирать исследования, они сами. Цели, выбор направления, одно исследование на опережение, за счет другого - не понимаю как. Само время бежит очень быстро, игра на пол часа получается, что происходит - не понятно.
2. Что делать - не понятно. Бюджет - суммы какието должны быть. Ползунки взаимозависимы, трудно настроить точные значения, может сделать не более 100% Что до 100 - в резерв, на проекты или другие влияния, лицензии.
3. Механика лицензий не понятна, выскакивает сообщение и типа не одобрили, зачем это. Должна быть страна, у нее список доступных лицензий, я выбрал, там цена. У меня доступные лицензии, я их продаю, устанавливаю цену, доступность своим/всем/друзьям. Средние цены по миру. Выглядит пустой механикой.
4. примерно сразу стало уныло.
5. Звуки - нормально, без замечаний.
6. Глобус - визуал да, но не понятно зачем он сейчас, с ним нет взаимодействия или пользы, так, просто визуал. Колбы видно состояние, но зачем, не понятно, я их не выбирал, стратегию не формировал, №XX - номер чего вообще не понятно, зачем эти номера, что они.
7. Производство - совсем не ясно, я ничего не выбираю, ничего не назначаю или формирую, №1 G1 - что это, не понятно, а как создать линию, я хочу произвести, варианты, количество, модели их список, самолет это или танк или ? Само без меня живет. нет расшифровки пунктов бюджета, что на что влияет, зачем.
8. Сплав - что это, какой сплав, чего, ресурс это, он один что ли ? Кнопка на панели есть, нажать нельзя или это светофор ? Ну и не понятно, что дает, что тормозит.
9. Дерево знаний - кнопка закрыть - ну не удобно, ее искать нужно, само дерево, все поколения самолетов на одной окружности, год по часовой стрелке получается, я ожидал - год, удаление от центра, и не чувствуется масштабности, выбора, поиска, полазить поизучать. Радиальные сектора будут добавляться. Больше шагов зума есть смысл и не только текст а иконки на максимальном. Узел имеет связь, но как влияет - скорость +30, ночные операции +10, ресурс +50, прочность +5, эпическое открытие и подобное. Пример, планер с треугольным крылом, что повышает - тут точно преимущества и недостатки есть, что-то должен давать, но там "ничего не повышает". "почему не" - не понятно, не что ? Черновик пуст - что за черновик ? Отдать приказ - фокус - не активно, почему ? Как назначить цель ? Чем отличается цель от фокуса ? Контрол меню по правой кнопке может, а может радиальное меню на узле сделать по зажатию левой кнопки мыши ?

## Verdict (Orchestrator reading)

The build fails the M-play question "does the player make meaningful decisions" (#418). The simulation runs, but the player has no visible levers and cannot read what the numbers mean. Sound passes.

| # | Area | Finding | Kind |
|---|---|---|---|
| F1 | Agency | Research is not chosen by the player; goal vs focus unclear; "give order / focus" disabled with no reason | missing lever |
| F2 | Pace | 1945-2030 passes in about 30 minutes; events unreadable | tuning |
| F3 | Budget | Shares without money amounts; coupled sliders; wants independent sliders capped at 100 %, remainder to reserve, projects, licences | lever redesign |
| F4 | Licences | Popup "not approved" with no context; wants a market: seller country, list, price, buy; own licences for sale with price and audience (own bloc / all / friends); world average prices | mechanic redesign |
| F5 | Production | No player action; "№1 G1" unreadable; wants to create a line: type (aircraft, tank ...), model list, quantity | missing lever |
| F6 | Legibility | №XX labels, "G1", alloy (what alloy, one resource?), alloy button looks pressable, flasks meaning, budget items without explanation | legibility |
| F7 | Globe | Pretty but no interaction or use yet | missing lever |
| F8 | Tree layout | Wants radius = year, sectors = directions; more zoom steps with icons at max zoom; close button hard to find; radial menu on long left press or context menu on right click | layout |
| F9 | Node effects | Wants explicit effects (+30 speed, +10 night ops, +50 resource, +5 strength, epic discovery); delta wing shows "improves nothing" | content + UI |
| F10 | Tree texts | "Почему не" unclear; "Черновик пуст" unclear | legibility |
| F11 | Sound | OK, no remarks | pass |
