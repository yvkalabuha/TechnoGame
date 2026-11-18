# EVENT_SOUNDS_MAP — звуки событий клиента из listening-pack r2 (#403, TASK-0100 шаг 1)

Статус: [Предл.] карта для developer. Q66 открыт: до ответа владельца — вариант с меткой эры 1945 (первый по порядку; если 1945 нет, первый вариант). Выбор вариантов — не решение, владелец выбирает на слух.
Источники [Факт]: `docs/audio/AUDIO_DIRECTION_r2.md` §1, `docs/audio/listening-pack/index.html` и `CREDITS.yml` (проверено 2026-11-18), `AudioDirector.cs`, `AudioLogic.cs` (каталог `AudioCatalog`), `assets/audio/ui/`.
Лицензии: все 20 выбранных файлов CC0 или CC BY (3.0/4.0). Лицензий вне ADR-0009 в пакете нет. CC BY 3.0 (stratcat322, system_fm) допустима, в умолчаниях не используется.
«HQ» = Freesound HQ preview (mp3), оригинала нет; «ориг.» = оригинальный WAV (BigSoundBank). Все файлы пакета — .ogg, нарезка/обработка по r2 §4 уже сделана.

## 1. Таблица (умолчание = вариант 1945)

| Событие клиента (Cue id) | Текущий файл | Новый файл | Prio / громкость r2 (LUFS-S) | Лицензия | Автор | Источник |
|---|---|---|---|---|---|---|
| order_ok | order_ok_0..3 | E01_a | P2, −26…−22 | CC0 | I.fekry | HQ |
| order_reject | order_reject | E02_a | P1, −22…−18 | CC0 | bassmosphere | HQ |
| queue_tick | queue_tick_0..3 | E03_a | P3, −32…−28 | CC0 | moose13088 | HQ |
| order_done | order_done_0..3 | E04_a (Triumph, метка 1945) | P2, −26…−22 | CC BY 4.0 | YleArkisto | HQ |
| signal_attention | signal_attention | E05_a | P1, −22…−18 | CC BY 4.0 | jakebagger | HQ |
| signal_alert | signal_alert | E06_a (только эра 1960, 1945-варианта нет) | P0, −20…−16 | CC0 | Hockinfinger | HQ |
| signal_critical | signal_critical | E07_a | P0, −20…−16 | CC BY 4.0 + CC0 | jakebagger + bassmosphere | HQ |
| signal_clear | signal_clear | E08_a | P1, −22…−18 | CC BY 4.0 | jakebagger | HQ |
| node_discovered | node_discovered | E09_a (Triumph) | P1, −22…−18 | CC BY 4.0 | YleArkisto | HQ |
| breakthrough | breakthrough | E10_a | P1, −22…−18 | CC0 | Joseph Sardin | ориг. |
| line_commissioned | line_commissioned | E11_a | P1, −22…−18 | CC0 | kentspublicdomain | HQ |
| war_declared | war_declared | E13_a (рубильник синтез + морзе) | P0, −20…−16 | CC0 + CC BY 4.0 | EchoCinematics + ERH | HQ |
| launch_space | launch_space | E16_a (только эра 1960) | P1, −22…−18 | CC BY 4.0 + CC0 | YleArkisto + kentspublicdomain | HQ |
| launch_nuclear | launch_nuclear | E17_a | P0, −20…−16 | CC0 | EchoCinematics + kyles | HQ |
| tempo_1 / _2 / _3 | tempo_1..3 | E18_a (один файл: 1/2/3 удара программно) | P2, −26…−22 | CC BY 4.0 | joedeshon | HQ |
| pause | pause | E19_a (вниз) | P2, −26…−22 | CC BY 4.0 | joedeshon | HQ |
| unpause | unpause | E19_b (вверх) | P2, −26…−22 | CC BY 4.0 | joedeshon | HQ |
| ui_tick | ui_tick_0..3 | E20_a | P3, −32…−28 | CC0 | bevibeldesign | HQ |

Громкость: в пакете файлы уже нормированы под приоритет (например E05_b −20.0, E01_a −24.8 LUFS), `GainDb` в каталоге оставить 0, шины UI (P2/P3) и Alert (P0/P1) без изменений.
Если у владельца другие варианты (Q66) — таблица пересчитывается по `index.html`; лицензии вариантов см. §4.

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
Только файлы, реально ушедшие в клиент (умолчание Q66). CC0 по лицензии не требует кредита; строки даны для полноты, CC BY — обязательны. Изменения: нарезка, фильтрация, нормировка громкости (питч ±2 полутона у E08, E19). Для E04_a, E09_a, E16_a использован фрагмент записи, нарезанный автоматически.

CC BY 4.0 (кредит обязателен):
- "Bell Ding" — jakebagger — https://freesound.org/people/jakebagger/sounds/499787/ — CC BY 4.0 (E05_a, E07_a, E08_a)
- "electrical_switch_02.wav" — joedeshon — https://freesound.org/people/joedeshon/sounds/139062/ — CC BY 4.0 (E18_a, E19_a, E19_b)
- "morse static.wav" — ERH — https://freesound.org/people/ERH/sounds/34418/ — CC BY 4.0 (E13_a)
- Triumph Matura 300 («Kirjoituskone, vanha, mekaaninen / An old manual typewriter, 1970s…») — YleArkisto — https://freesound.org/people/YleArkisto/sounds/271525/ — CC BY 4.0 (E04_a, E09_a, E16_a)
Лицензия: https://creativecommons.org/licenses/by/4.0/

CC0 (кредит не требуется, указать по желанию):
- "traditional stamp.wav" — I.fekry — https://freesound.org/people/I.fekry/sounds/470710/ (E01_a)
- "A relay switch being activated." — bassmosphere — https://freesound.org/people/bassmosphere/sounds/384698/ (E02_a, E07_a)
- "Paper Shuffle and Stack.aif" — moose13088 — https://freesound.org/people/moose13088/sounds/182889/ (E03_a)
- "Stromberg Carlson 1543.wav" — Hockinfinger — https://freesound.org/people/Hockinfinger/sounds/274436/ (E06_a)
- "Typewriter, bell #2" — Joseph Sardin (BigSoundBank) — https://bigsoundbank.com/typewriter-bell-s2845.html (E10_a)
- "Factory whistle, train whistle" — kentspublicdomain — https://freesound.org/people/kentspublicdomain/sounds/477827/ (E11_a, E16_a)
- "Kill Switch (Large Breaker Switch) .WAV" — EchoCinematics — https://freesound.org/people/EchoCinematics/sounds/131599/ (E13_a, E17_a; запись синтезирована)
- "hum electric transformer in large room1.flac" — kyles — https://freesound.org/people/kyles/sounds/453444/ (E17_a)
- "shuffling papers.wav" — bevibeldesign — https://freesound.org/people/bevibeldesign/sounds/635156/ (E20_a)
Доступ к страницам: 2026-11-18. Хэши источников — `docs/audio/listening-pack/CREDITS.yml`; при переносе записей в `assets/audio/CREDITS.yml` добавить хэш итоговых .ogg.

Если Q66 даст другие варианты, дополнительно могут понадобиться: CC BY 4.0 — eduardfrigola (E01_b, E15_b), smokeyvw (E02_b, E13_b, E17_b, E18_c), lonemonk (E01_c), MrAuralization (E03_c, E04_c, E12_b), FreqMan (E04_b, E09_b), unreadpages (E11_c), joedeshon modem (E10_c); CC BY 3.0 — system_fm (E02_c), stratcat322 (E13_b, E14_b). Записи есть в `CREDITS.yml` пакета.

## 6. Оговорки [Предп.]
- HQ-превью — mp3 ~128 кбит, пересжатое в ogg: потеря на ВЧ; для релиза [Предл.] заменить на оригиналы (Freesound требует вход; BigSoundBank — оригиналы уже).
- E04_a/E09_a/E16_a (Triumph, 1970-е) помечены 1945 в пакете, но запись 1970-х; E13_a/E17_a используют синтезированный рубильник (EchoCinematics): подлинность решается слухом (r2 открытый вопрос 2).
- E06_a и E16_a в 1945-вариантах отсутствуют; умолчание = 1960, до ответа Q66.
