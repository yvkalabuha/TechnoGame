---
tags: [note, visual, ui, active]
---
# Node schematics (решение #318)

Чертежи-«синьки» для карточки узла дерева технологий. Свои, лицензия проекта; ничего не трассировано. [Факт] 27 файлов.

Формат: SVG 320x200, фон `#1E4673`, линии `#EEF2F0` (контур 1.5, вторичные 1.0), `#B9CBDA` (штриховка, осевые, размеры 0.75), сетка `#3A6491`. Подписи: только мелкие размерные (L, b, Λ, t...). Зеркальные половины (двигатель, самолёт) сделаны через `<defs>/<use>`.

## Правило выбора (для разработчика)
1. Если есть файл по id узла (таблица 2): взять его.
2. Иначе `{type}-{discipline}.svg` (таблица 1), type и discipline в нижнем регистре.
3. Иначе без картинки (все комбинации из nodes.yaml покрыты).

## Таблица 1: тип x дисциплина (16)
| Файл | Тип | Дисц. | Сюжет |
|---|---|---|---|
| fnd-prop.svg | FND | prop | цикл Брайтона p-v |
| fnd-aero.svg | FND | aero | пограничный слой на пластине |
| fnd-metal.svg | FND | metal | кривая ползучести |
| prn-prop.svg | PRN | prop | газотурбинный принцип C-B-T |
| prn-aero.svg | PRN | aero | стреловидность, нормальная составляющая скорости |
| prn-metal.svg | PRN | metal | зёрна и кубоиды гамма-штрих |
| eng-prop.svg | ENG | prop | реактивный двигатель в разрезе |
| eng-aero.svg | ENG | aero | планер: шпангоуты, нервюры |
| cmp-metal.svg | CMP | metal | разрез катаного листа, вытянутые зёрна |
| cmp-prop.svg | CMP | prop | сечение лопатки с каналами охлаждения |
| prc-metal.svg | PRC | metal | прокатный стан |
| prc-chem.svg | PRC | chem | ректификационная колонна |
| prc-mech.svg | PRC | mech | токарный станок |
| prc-aero.svg | PRC | aero | обшивка, стрингеры, шпангоут, заклёпки |
| prc-prop.svg | PRC | prop | стенд для тяговых испытаний |
| app-aero.svg | APP | aero | самолёт, кольца радиуса действия |

## Таблица 2: отдельные узлы (11)
| Файл | Узел (id) |
|---|---|
| turbojet_g1.svg | s8.eng.turbojet_g1 |
| turbojet_g2.svg | s8.eng.turbojet_g2 |
| turbojet_g3.svg | s8.eng.turbojet_g3 |
| afterburner.svg | s8.eng.afterburner |
| swept_wing.svg | s8.eng.swept_wing_airframe |
| jet_fighter_g1.svg ... g4.svg | s8.app.jet_fighter_g1 ... g4 |
| superalloy_melt_g2.svg | s5.prc.superalloy_melt_g2 |
| axial_compressor.svg | s8.cmp.axial_compressor_assembly |

Примечание [Факт]: ПРН s8.prn.swept_wing использует prn-aero.svg (принцип), ENG s8.eng.swept_wing_airframe использует swept_wing.svg (планформа).

## Ограничения
- Файлы написаны вручную без рендера в окружении автора; визуальная проверка в Godot (ThorVG) и в браузере обязательна до приёмки. [Предп.] `<use>` с `href` и `xlink:href` поддержан ThorVG; если нет, развернуть вручную.
- Размерные подписи не локализуются (латиница, символы).
