# EVENT_SOUNDS_MAP — звуки событий клиента из listening-pack r2 (#403, TASK-0100 шаг 1)

Статус: [Факт] решение владельца #407: один вариант звука на событие на всю игру, выбор за sound-director; остальные варианты пакета отбрасываются. Таблица §1 — финальный выбор (2026-11-18), на слух мной не проверялся [Предп.]; владелец может заменить любой пункт на слух.
Критерии выбора: r2 (тишина по умолчанию, предметы бюро, без дрейфа по эпохам в v1), примечания пакета, лицензия (при равенстве CC0 > CC BY), оригинал > HQ, семейства звуков (колокольчик = внимание/критично/снято; телефон = только тревога; пишмашинка = исполнено/узел/прорыв; реле = отказ/критично).
Источники [Факт]: `docs/audio/AUDIO_DIRECTION_r2.md` §1, `docs/audio/listening-pack/index.html` и `CREDITS.yml` (проверено 2026-11-18), `AudioDirector.cs`, `AudioLogic.cs` (каталог `AudioCatalog`), `assets/audio/ui/`.
Лицензии: все 20 выбранных файлов CC0 или CC BY (3.0/4.0). Лицензий вне ADR-0009 в пакете нет. CC BY 3.0 (stratcat322, system_fm) допустима, в умолчаниях не используется.
«HQ» = Freesound HQ preview (mp3), оригинала нет; «ориг.» = оригинальный WAV (BigSoundBank). Все файлы пакета — .ogg, нарезка/обработка по r2 §4 уже сделана.

## 1. Финальная таблица (один вариант на всю игру)

Колонка «Изм.»: «=» совпадает с текущим клиентом (вариант `_a`), иначе «было → стало».

| Событие клиента (Cue id) | Файл | Изм. | Prio / громкость r2 (LUFS-S) | Лицензия | Автор | Источник | Причина выбора |
|---|---|---|---|---|---|---|---|
| order_ok | E01_a | = | P2, −26…−22 | CC0 | I.fekry | HQ | Резиновый штамп, CC0; b — CC BY, c — электромашинка 1980 |
| order_reject | E02_a | = | P1, −22…−18 | CC0 | bassmosphere | HQ | Реле: сухо и громче b на 5 дБ, CC0; c — принтер 1980 CC BY 3.0 |
| queue_tick | E03_a | = | P3, −32…−28 | CC0 | moose13088 | HQ | Шорох листа CC0; b отдан под E20, чтобы тики различались |
| order_done | E04_a | = | P2, −26…−22 | CC BY 4.0 | YleArkisto | HQ | Каретка машинки вне эпох; b — автонарезка Olympia 1960, c — дисковод 1980. Уровень −34 LUFS ниже окна P2: поднять ~+8 дБ |
| signal_attention | E05_a | = | P1, −22…−18 | CC BY 4.0 | jakebagger | HQ | Настольный звонок: общее семейство с E07/E08; b (CC0) — дверной колокольчик магазина, не бюро; c — телефон, занят под тревогу |
| signal_alert | E06_a | = | P0, −20…−16 | CC0 | Hockinfinger | HQ | Один звонок телефона, CC0; c (оригинал) — это набор номера, не звонок (проверено на странице); b тише по пику |
| signal_critical | E07_a | = | P0, −20…−16 | CC BY 4.0 + CC0 | jakebagger + bassmosphere | HQ | Звонок+реле отличается от тревоги-телефона; b (телефон+реле) слился бы с E06 |
| signal_clear | E08_a | = | P1, −22…−18 | CC BY 4.0 | jakebagger | HQ | Тот же звонок тише и ниже: пара к E05; b (CC0) слишком тихий, пик −15 дБ |
| node_discovered | E09_c | E09_a → E09_c | P1, −22…−18 | CC0 | Joseph Sardin | ориг. | Оригинал WAV, CC0, уровень в окне P1; a — автонарезка 1970-х CC BY. Одиночный звонок — эскалация к E10 (двойной) |
| breakthrough | E10_a | = | P1, −22…−18 | CC0 | Joseph Sardin | ориг. | Двойной звонок, оригинал CC0; c модем 2000 — [Предп.] |
| line_commissioned | E11_a | = | P1, −22…−18 | CC0 | kentspublicdomain | HQ | Далёкий свисток мягче для частого события (уместно «звук = отклонение»); b ближе и резче. Уровень −28 LUFS ниже окна P1: поднять ~+6 дБ |
| war_declared | E13_a | = | P0, −20…−16 | CC0 + CC BY 4.0 | EchoCinematics + ERH | HQ | Рубильник + морзе; b — CC BY 4.0 и CC BY 3.0 (две атрибуции) |
| launch_space | E16_b | E16_a → E16_b | P1, −22…−18 | CC0 + CC0 | bassmosphere + kentspublicdomain | HQ | Свисток+реле целиком CC0; a — автонарезка Triumph CC BY |
| launch_nuclear | E17_a | = | P0, −20…−16 | CC0 | EchoCinematics + kyles | HQ | Рубильник + нарастающий гул, оба CC0; b — реле smokeyvw CC BY |
| tempo_1 / _2 / _3 | E18_a | = | P2, −26…−22 | CC BY 4.0 | joedeshon | HQ | Тумблер, одно семейство с E19; b/c — реле, совпали бы с отказом E02 |
| pause | E19_a | = | P2, −26…−22 | CC BY 4.0 | joedeshon | HQ | Тумблер вниз; c (рубильник, синтез) занят под P0 |
| unpause | E19_b | = | P2, −26…−22 | CC BY 4.0 | joedeshon | HQ | Тумблер вверх (пара) |
| ui_tick | E20_a | = | P3, −32…−28 | CC0 | bevibeldesign | HQ | Сухой тик бумаги, самый тихий пик (−12 дБ) для самого частого звука; b — реле, совпало бы с E02 |

Итог: из 20 событий меняются 2 (node_discovered, launch_space). Метки эры («1945/1960») в v1 больше не выбирают файл: вариант один на всю игру (#407), дрейф по году — вне v1.
Громкость: файлы пакета нормированы под приоритет; `GainDb` оставить 0, кроме E04_a и E11_a (поднять, см. причины); E17_a −21.1 при окне P0 −20…−16 допустимо. Шины UI (P2/P3) и Alert (P0/P1) без изменений.

## 2. Без пары
- Событие клиента без E##: нет. У `ui_tick` пара E20 (наведение/выбор) есть.
- E## без события клиента: **E12** Идёт стройка (P3), **E14** Потеря территории (P0), **E15** Мир/перемирие (P1). Не подключать, пока нет событий в клиенте; файлы остаются в пакете. Их лицензии (CC0, CC BY 4.0) в титры не идут, пока не поставлены.

## 3. Замечания для developer (кодовые расхождения)
1. [Факт] В пакете по одному файлу на событие, а `AudioCatalog` ждёт `order_ok_0..3`, `queue_tick_0..3`, `ui_tick_0..3` (Variants=4). Решение: `Variants=1` и вариативность питчем ±3% / громкостью ±1.5 дБ (r2 §4 п.5); 4–6 разных ударов — только если владелец/ я нарежем дополнительно из Triumph/moose13088 [Предл.].
2. [Факт] `Stream()` грузит `ui/<id>.wav`; пакет — `.ogg`. Нужно расширение .ogg (или конвертация в WAV 48 кГц 16 бит моно по r2 §4 п.6). Решает developer; Godot читает оба.
3. [Факт] `Cue.Seconds` проверяется тестом по длине WAV; новые длины иные (звонки/свистки до 2 с, телефон/рубильник до 1.5 с). Обновить значения и `MinGap` под новую длину.
4. [Факт] В каталоге `signal_attention` = P0, по r2 §1 «внимание» = P1 (шина Alert та же, но порядок вытеснения/усталости отличается). Привести к r2.
5. [Факт] tempo: r2 — 1/2/3 щелчка тумблера одним файлом; при `Variants=1` проигрывать E18_a 1/2/3 раза с интервалом ~60 мс (или файл с питчем −/0/+). Решение за developer, отметить для прослушивания.
6. [Факт] `LICENSES.md` строка про `ui/*.wav (own work, synth.py)` станет неверной: заменить ссылкой на атрибуцию ниже.

## 4. Старые файлы к удалению (`src/Singularity.Client.Spike/assets/audio/ui/`)
Удалить после того как все 20 cue играют новые файлы и ui-tester/`--shots` без ошибок аудио. Каждый `.wav` с парным `.wav.import`:
- `order_ok_0..3`, `order_reject`, `queue_tick_0..3`, `order_done_0..3`
- `signal_attention`, `signal_alert`, `signal_critical`, `signal_clear`
- `node_discovered`, `breakthrough`, `line_commissioned`, `war_declared`, `launch_space`, `launch_nuclear`
- `tempo_1`, `tempo_2`, `tempo_3`, `pause`, `unpause`, `ui_tick_0..3`
Итого 31 `.wav` + 31 `.wav.import` = 62 файла. Плюс `manifest.json` (описывает старые файлы; удалить или пересобрать `tools/audio` под новые — решает developer). Музыку и `ambient/pale_window.ogg` не трогать: вне этого задания. `tools/audio/synth.py` оставить как служебный генератор, но убрать из описания источников UI-звуков.

## 5. Блок атрибуции для титров (DEBT-0050)
Только файлы финального выбора §1 (#407). CC0 по лицензии не требует кредита; строки даны для полноты, CC BY — обязательны. Изменения: нарезка, фильтрация, нормировка громкости (питч ±2 полутона у E08, E19). Для E04_a использован фрагмент записи, нарезанный автоматически.

CC BY 4.0 (кредит обязателен):
- "Bell Ding" — jakebagger — https://freesound.org/people/jakebagger/sounds/499787/ — CC BY 4.0 (E05_a, E07_a, E08_a)
- "electrical_switch_02.wav" — joedeshon — https://freesound.org/people/joedeshon/sounds/139062/ — CC BY 4.0 (E18_a, E19_a, E19_b)
- "morse static.wav" — ERH — https://freesound.org/people/ERH/sounds/34418/ — CC BY 4.0 (E13_a)
- Triumph Matura 300 («Kirjoituskone, vanha, mekaaninen / An old manual typewriter, 1970s…») — YleArkisto — https://freesound.org/people/YleArkisto/sounds/271525/ — CC BY 4.0 (E04_a)
Лицензия: https://creativecommons.org/licenses/by/4.0/

CC0 (кредит не требуется, указать по желанию):
- "traditional stamp.wav" — I.fekry — https://freesound.org/people/I.fekry/sounds/470710/ (E01_a)
- "A relay switch being activated." — bassmosphere — https://freesound.org/people/bassmosphere/sounds/384698/ (E02_a, E07_a, E16_b)
- "Paper Shuffle and Stack.aif" — moose13088 — https://freesound.org/people/moose13088/sounds/182889/ (E03_a)
- "Stromberg Carlson 1543.wav" — Hockinfinger — https://freesound.org/people/Hockinfinger/sounds/274436/ (E06_a)
- "Typewriter, bell #2" — Joseph Sardin (BigSoundBank) — https://bigsoundbank.com/typewriter-bell-s2845.html (E09_c, E10_a)
- "Factory whistle, train whistle" — kentspublicdomain — https://freesound.org/people/kentspublicdomain/sounds/477827/ (E11_a, E16_b)
- "Kill Switch (Large Breaker Switch) .WAV" — EchoCinematics — https://freesound.org/people/EchoCinematics/sounds/131599/ (E13_a, E17_a; запись синтезирована)
- "hum electric transformer in large room1.flac" — kyles — https://freesound.org/people/kyles/sounds/453444/ (E17_a)
- "shuffling papers.wav" — bevibeldesign — https://freesound.org/people/bevibeldesign/sounds/635156/ (E20_a)
Доступ к страницам: 2026-11-18. Хэши источников — `docs/audio/listening-pack/CREDITS.yml`; при переносе записей в `assets/audio/CREDITS.yml` добавить хэш итоговых .ogg.

Отброшенные варианты (#407) в титры не идут; записи для них удаляются из `CREDITS.yml` пакета (список в отчёте Orchestrator).

## 6. Оговорки [Предп.]
- HQ-превью — mp3 ~128 кбит, пересжатое в ogg: потеря на ВЧ; для релиза [Предл.] заменить на оригиналы (Freesound требует вход; BigSoundBank — оригиналы уже).
- E04_a (Triumph) помечен 1945 в пакете, но запись 1970-х, автонарезка; E13_a/E17_a используют синтезированный рубильник (EchoCinematics): подлинность решается слухом (r2 открытый вопрос 2).
- E06_a и E16_b — звуки эпохи 1960, остаются на всю игру (#407).
- E12, E14, E15 клиентом не используются: их файлы и записи CREDITS.yml (dbache, stratcat322, eduardfrigola, MrAuralization) в пакете остаются.


> Note (TASK-0104 review, 2026-11-18): E04_a was re-normalised for the client from -34.0 LUFS / -5.3 dBTP to -23.8 LUFS-I / -1.7 dBTP (+14 dB, alimiter 0.78); the pack file was replaced with the same file. order_done GainDb is 0.
