---
tags: [review, ui, visual, done]
---
# TASK-0137 visual review: globe variant B (physical atmosphere) as default

Reviewer: ui-art-director. Frames: `.claude/worktrees/agent-ad14f255be74df44a/shots-out/task-0137/` (the path given in the task, `shots-out/task-0137/`, does not exist in the main tree; frames were read from the agent worktree). Choice of B is the owner's (#444) and is not re-argued.

## Verdict: PASS with adjustments (3 required tweaks, none blocks landing if tracked as follow-up in the same task)

1. C5 labels near the limb: PASS. Пакистан (x~1610, ~60 px from the limb), Россия, Гренландия, Эфиопия stay readable at 100 %: dark serif on light fill, the limb brightening is under them but does not drop the contrast below the body-text level. The outer halo is outside the disc and carries no labels.
2. Atmosphere vs data: PASS in colour, borderline in size. The outer glow is ~70 px wide and bright blue at the limb, the brightest saturated thing in the frame next to the player country. Adjust: reduce outer glow width by ~25 % (about 50 px) and peak alpha by ~15 %, so the halo reads as an edge and not as a ring. Should not draw the eye away from Франция/Россия.
3. A4 tints near the limb: PASS-with-note. On the day frame the peach, green, pink and grey country fills in the last ~80 px before the limb (Казахстан, Иран, Пакистан, east edge) are pulled toward pale blue-white; green vs grey neighbours become hard to tell apart at the very edge. Adjust: cap the inner limb whitening (rim brightening inside the disc) at about 60 % of the current strength, keeping hue shift but not lightness lift; the borders (dark lines) remain visible and neighbours stay separable, so this is not a fail.
4. A5 grey: PASS-with-note. In `b_europe_day_grey` the halo becomes a wide neutral white-grey band; the inner limb goes near-white, and the tonal steps between land fills compress there (Казахстан, Пакистан fills are nearly the same value). Adjust: the same inner-whitening cap as item 3 fixes it; also tint the halo with a luminance no higher than the sea fill so it does not become the lightest object in the grey frame.
5. Terminator (`b_terminator`): PASS. The lit limb carries a blue rim, the night side gets only a faint rim; all labels readable; the day/night boundary does not clash with the labels or borders. Night (`b_night`): PASS, halo is faint, tints stay distinct (orange/green/blue/pink still separate), labels readable.
6. HUD frame: PASS. The halo goes under the left rail and top bar; no overlap with controls, the limb glow does not reduce the legibility of panel edges. Main menu: PASS, the globe with halo sits well behind the menu card; card contrast unaffected.
7. Settings row «Вид глобуса»: PASS. Four options (Прежний / Матовый / Матовый + дымка / Атмосфера), the current one is outlined and bold, so it is not shown by colour alone; the row fits the panel width but touches the right content edge (x=1436); another long label would wrap. Note: «Атмосфера» is selected as default, matching the owner choice; consider a short hint line under the row on what each does only if the owner asks. The «Рейка областей» row is the same style, consistent.
8. A8 (no motion): NOT VERIFIED from stills. Static frames show no animated artefacts; whether the halo or the rim pulses or drifts over time cannot be seen in PNG. Required from the developer: confirm in code that the atmosphere has no time-dependent term (no shimmer, no breathing); under «Анимации: Уменьшить» it must be static anyway.

## Required adjustments summary
- Outer glow: width -25 %, peak alpha -15 %.
- Inner limb whitening: strength x0.6, hue shift kept.
- Grey mode: halo luminance at most the sea fill; no pure-white band.
- Re-shoot day, grey, terminator at 100 % after the change; check Казахстан/Иран/Пакистан still separate in grey.

## Frame times
GPU: NVIDIA GeForce RTX 3050 Ti Laptop GPU
1920x1080, vsync off, 300 frames of Europe by day, no HUD, 100 % UI scale
current: 300 frames, mean 16.67 ms, p99 16.67 ms, GPU render 2.02 ms
a: 300 frames, mean 16.67 ms, p99 16.67 ms, GPU render 1.78 ms
aplus: 300 frames, mean 16.67 ms, p99 16.67 ms, GPU render 1.95 ms
b: 300 frames, mean 16.67 ms, p99 16.67 ms, GPU render 1.80 ms
limb sample Russia: ratio 0.960
  pixels Russia: centre A eebe9d B eebe9d, 0.96 R A ecbe9e B ecbe9e, 0.99 R A edbe9e B dec3bb, ratio last 0.990
tint check Russia: point inside country True; atmosphere share dE at centre 0


## Раунд 2 (после настройки #449, кадры 1920x1080, 100 %)

Проверено по кадрам b_europe_day, b_europe_day_grey, b_terminator. Вариант В (#444) и его настройка (#449) приняты владельцем; замечания «убрать атмосферу» вне объёма.

### Что исправлено из раунда 1
- Внешнее свечение: в цвете сузилось и стало мягче, не спорит с Франция/Россия. OK.
- Лимб в цвете: страны у края (Казахстан, Иран, Пакистан) различимы, границы видны. OK.
- Терминатор: светлый лимб с голубой каймой, ночной край тёмный, подписи читаются. OK.

### Не исправлено
- Серый кадр (A5): ореол по-прежнему широкая почти белая полоса (около 60 px), светлее моря и светлее любой суши; внутри лимба заливки Казахстана, Пакистана и востока сливаются в одно значение. Требование раунда 1 «яркость ореола не выше заливки моря, без чисто белой полосы» не выполнено.

### Таблица Gemini и мой ответ
| Sev (Gemini) | Критерий | Согласен? | Причина |
|---|---|---|---|
| high | A5 синий vs оранжевый в сером | Частично | В сером Франция и оранжевые страны близки по яркости, это верно. Но подсвеченное отличается от фона яркостью и чёрным контуром (второй канал есть); различие синий/оранжевый несёт смысл только если пользователь его должен читать, а легенды нет. Это дизайн подсветки (проход 2), не атмосфера. Предл.: добавить штриховку/значок для второй группы в проходе 2. |
| medium | C1 легенда; серая чёрточка в Центральной Европе | Согласен по чёрточке, по легенде вне объёма | Серая горизонтальная чёрточка (около x=975, y=455) и квадратик (x=935, y=483) без подписи читаются как артефакт; передать разработчику: подписать или убрать. Легенду решает проход 2. |
| medium | A4 границы и точки в сером | Частично | Границы между нейтральными странами в сером тонкие и слабые (меньше 3:1), но это старое свойство карты, не результат атмосферы. В бэклог, не блокирует это задание. |
| low | A3 подписи на береговой линии | Не согласен | Великобритания, Норвегия, Швеция читаются, у подписей есть ореол; пересечение с контуром не мешает чтению. |
| low | C8 нет выхода/назад | Не согласен | Кадр глобуса без HUD для съёмки атмосферы; выход проверяется на кадрах HUD/меню. |

### Вердикт раунда 2: FAIL (узкий)
Единственная причина: серый кадр, ореол остаётся самым светлым объектом (A5). Исправление: в режиме серого ограничить яркость ореола яркостью заливки моря и снизить внутреннее осветление лимба до ~0,6 от прежнего; переснять b_europe_day_grey и проверить, что Казахстан, Иран и Пакистан различимы. Цветные кадры принимаются, перепроверка только серого. A8 (движение) по-прежнему не проверено по статике.
