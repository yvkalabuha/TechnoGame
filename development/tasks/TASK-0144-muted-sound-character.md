---
id: TASK-0144
title: "Muted sound character (#455): soften attack and low-pass all routine cues"
status: DONE
level: light
milestone: MS-M05
agent: sound-director
category: client
priority: medium
needs_owner: false
depends_on: []
related: [TASK-0140]
approved_by: orchestrator
approved_at: 2026-11-24
approval_ref: "owner preference #455; light task approved by Orchestrator"
review_ref: "sound-director: PASS (task review); owner: accepted in playtest 2026-11-24 (Звуки — нормально, без замечаний)"
created: 2026-11-24
tags: [task, audio, done]
---

# TASK-0144 — Глухой характер звуков (#455)

## Цель
Владелец предпочитает глухие звуки. Перевести рутинные звуки интерфейса и событий в глухой характер: мягкая атака, срез верхов, без звонких щелчков и колокольчиков.

## Описание
Шаг 1 — sound-director: по каждому звуку каталога (`AudioLogic.cs`, `docs/audio/EVENT_SOUNDS_MAP.md`) — оставить, обработать (low-pass, атака, длина) или заменить источником PD/CC0/CC BY (ADR-0009); параметры; тревоги P0 остаются заметными. Шаг 2 — developer по таблице.

## Критерии приёмки
- [ ] Таблица звуков с решением и параметрами; P0 различим.
- [ ] Ревьюер APPROVE; sound-director PASS; владелец слушает.

## Результат (шаг 2, developer)
- Шина UI: `AudioEffectLowPassFilter` (3200 Гц, resonance 0.5) из константы `AudioSettings.UiCutoffHz`; Alert (P0/P1) не фильтруется.
- Офлайн (ffmpeg: lowpass 2 полюса, fade-in, подрезка, fade-out, gain; libvorbis q5, 48 кГц моно) обработаны 13 файлов по спеке на месте; оригиналы остаются в git-истории. P0 (signal_alert, signal_critical, war_declared, launch_nuclear) и signal_clear не тронуты.
- Длины в каталоге: ui_tick 0.07, signal_attention 0.9, node_discovered 0.9, breakthrough 1.0, line_commissioned 1.5. `credits.json`: пометка «processed» (en/ru) у 9 записей.
- Громкость: целевой уровень = оригинал + дельта спеки − 1 LU, в пределах окна r2 ±2; пик ≤ −3 dBFS. Короткие файлы измерены ffmpeg ebur128 после добивки тишиной до 1.2 с (иначе гейт даёт −70), значения относительные.
- Отступление от спеки: line_commissioned в оригинале −28 LUFS (вне окна), поднят до −23.7 вместо снижения на 3 дБ.
- Тесты: добавлен тест шин/константы; `Ogg_matches_the_catalogue` проверяет новые длины (399 тестов Audio.Tests зелёные).
- Не проверено: прослушивание на слух.

| Cue | Prio | LP Hz | LUFS before → after | peak dBFS before → after | gain dB | length s | r2 window |
|---|---|---|---|---|---|---|---|
| ui_tick | P3 | 2500 | -32.5 → -33.8 | -12.2 → -12.4 | +6.8 | 0.07 | -32..-28 |
| queue_tick | P3 | 2500 | -30.4 → -31.5 | -5.9 → -6 | +3.9 | 0.08 | -32..-28 |
| order_ok | P2 | 2800 | -24.8 → -24.7 | -4.5 → -4.5 | +0.2 | 0.25 | -26..-22 |
| order_done | P2 | 3000 | -23.8 → -24.8 | -1.7 → -5.7 | +3.9 | 0.25 | -26..-22 |
| order_reject | P2 | 3500 | -24.4 → -25.4 | -4.2 → -4.9 | +4.6 | 0.19 | -26..-22 |
| tempo | P2 | 2800 | -24.6 → -25.6 | -4.9 → -5.9 | +3.7 | 0.25 | -26..-22 |
| pause | P2 | 2800 | -24.5 → -25.4 | -5.3 → -3.3 | +6.3 | 0.25 | -26..-22 |
| unpause | P2 | 2800 | -24.8 → -25.9 | -4.7 → -5.5 | +3.8 | 0.25 | -26..-22 |
| node_discovered | P2 | 3500 | -20.1 → -21 | -5 → -5.6 | +7.6 | 0.90 | -26..-22 |
| signal_attention | P1 | 4500 | -20.9 → -21.9 | -3.9 → -11.2 | +1 | 0.90 | -22..-18 |
| breakthrough | P1 | 4000 | -20 → -21 | -5.7 → -5.4 | +6.9 | 1.00 | -22..-18 |
| line_commissioned | P1 | 3500 | -28 → -23.7 | -18.7 → -14.1 | +4.6 | 1.50 | -22..-18 |
| launch_space | P1 | 4500 | -20.8 → -21.9 | -4.6 → -8.2 | -0.6 | 1.09 | -22..-18 |

### Спектр P0 против рутины (спека §4 п. 5), ffmpeg highpass 4 кГц + astats, файлы после обработки
| Cue | Prio | доля энергии выше 4 кГц, % |
|---|---|---|
| signal_critical | P0 | 46.6 |
| signal_alert | P0 | 36.9 |
| war_declared | P0 | 12.5 |
| launch_nuclear | P0 | 4.2 |
| signal_clear | P1 | 60.5 |
| breakthrough | P1 | 43.3 |
| signal_attention | P1 | 31.8 |
| launch_space | P1 | 1.6 |
| line_commissioned | P1 | 0.1 |
| node_discovered | P2/3 | 35.8 |
| order_reject | P2/3 | 18.4 |
| order_done | P2/3 | 11.2 |
| queue_tick | P2/3 | 6.9 |
| tempo | P2/3 | 6.3 |
| pause | P2/3 | 6 |
| unpause | P2/3 | 5.9 |
| ui_tick | P2/3 | 5.5 |
| order_ok | P2/3 | 0.2 |

Вывод [Факт]: критерий «у P0 доля выше 4 кГц минимум в 3 раза больше любого рутинного cue» НЕ выполняется: signal_alert 36.9 % против node_discovered 35.8 % (P2), signal_critical 46.6 %. war_declared (12.5 %) и launch_nuclear (4.2 %) ниже order_reject. Различимость P0 держится на атаке, тембре и шине Alert (без фильтра), не на спектре; решает прослушивание владельцем. Предл.: поднять яркость P0 high-shelf или сильнее срезать node_discovered.

### Идемпотентность шин (round 2)
Создание шин и эффектов вынесено в чистый `AudioBusPlan` (`ToCreate`, `Effects`); при повторном входе в игру новые шины, фильтр, лимитер и Record не добавляются, `_rec` берётся из существующего эффекта на Master. Тест `Bus_plan_filters_only_the_ui_bus_and_a_second_start_adds_nothing`.

### Ревью звука
Вердикт sound-director: FAIL (conditional): criterion §4 p.5 not met (alert 36.9 % vs node 35.8 %); fix node_discovered LP 2200–2500, breakthrough LP 3000, signal_attention LP 3200; war_declared/launch_nuclear accepted as distinct by timbre/rhythm; owner listening decides.

Правки (round 3): офлайн-обработка заново из оригиналов (754b81b). Один 2-полюсный фильтр оказался недостаточным (его выход усиливался на +10…+12 дБ при выравнивании громкости вместе с шумом верхов), поэтому фильтры каскадом: node_discovered 2 x 2300 Гц, breakthrough 2 x 3000 Гц, signal_attention 4 x 3200 Гц (наклон выше 12 дБ/окт спеки). order_reject, рутина и P0 не менялись, high-shelf на P0 нет. Громкость в окне r2 ±2, пик ≤ −3 dBFS (измерено ниже).

| Cue | Prio | доля энергии выше 4 кГц, % | LUFS (с добивкой тишиной) | пик, dBFS |
|---|---|---|---|---|
| node_discovered | P2 | 11.6 | -24.5 | -3.1 |
| breakthrough | P1 | 17.3 | -21.1 | -5.9 |
| signal_attention | P1 | 20.2 | -21.9 | -11.3 |
| signal_alert | P0 | 36.9 | -17.4 | -6.4 |
| signal_critical | P0 | 46.6 | -21.1 | -4 |

Цель node_discovered ≤ 12 % и breakthrough/signal_attention ~20 % и меньше достигнута (signal_attention на пределе: 20.2 %). Критерий §4 п.5 (P0 минимум в 3 раза ярче) по-прежнему не выполняется формально: P0 36.9/46.6 % против P1 ≤ 20 %; сравнение с P2: order_reject 18.4 %. Решает прослушивание владельцем. Не проверено на слух.

### Итоговые параметры обработки по cue
Каждый фильтр: ffmpeg `lowpass`, 2 полюса (12 дБ/окт) за ступень; ступеней в каскаде столько, сколько указано. Остальное: fade-in (атака), длина (atrim), fade-out, gain (выравнивание громкости). P0 и signal_clear не обрабатывались.

| Cue | cutoff, Гц | ступеней | атака, мс | длина, с | fade-out, мс | gain, дБ |
|---|---|---|---|---|---|---|
| ui_tick | 2500 | 1 | 8 | 0.07 | 15 | +6.8 |
| queue_tick | 2500 | 1 | 8 | 0.08 | 15 | +3.9 |
| order_ok | 2800 | 1 | 10 | 0.25 | 30 | +0.2 |
| order_done | 3000 | 1 | 12 | 0.25 | 60 | +3.9 |
| order_reject | 3500 | 1 | 6 | 0.19 | 20 | +4.6 |
| tempo | 2800 | 1 | 10 | 0.25 | 30 | +3.7 |
| pause | 2800 | 1 | 10 | 0.25 | 30 | +6.3 |
| unpause | 2800 | 1 | 10 | 0.25 | 30 | +3.8 |
| node_discovered | 2300 | 2 | 15 | 0.9 | 120 | +14.1 |
| signal_attention | 3200 | 4 | 10 | 0.9 | 100 | +12.3 |
| breakthrough | 3000 | 2 | 15 | 1 | 150 | +13.0 |
| line_commissioned | 3500 | 1 | 20 | 1.5 | 150 | +4.6 |
| launch_space | 4500 | 1 | 15 | 1.09 | 100 | -0.6 |

Значения gain для первых 8 строк и `launch_space`, `line_commissioned` взяты из раунда 1; три перерабатывавшихся cue из раунда 3.
