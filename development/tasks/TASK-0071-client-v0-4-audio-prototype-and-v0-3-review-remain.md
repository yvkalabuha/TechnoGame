---
id: TASK-0071
title: "Client v0.4: audio prototype and v0.3 review remainder"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0070]
approved_by: owner
approved_at: 2026-11-17
approval_ref: "DECISIONS_LOG #296, #298, #301"
created: 2026-11-17
---


## Goal
Client v0.4 in src/Singularity.Client.Spike. Two parts: the audio prototype (#298) and the remaining review items from v0.3 (#301). Core, Session and Kernel stay unchanged.

## Part A: audio prototype
- Spec: docs/audio/AUDIO_DIRECTION.md (sound map, priorities P0–P3, fatigue rules, synthesis recipes for ~15 sounds, LUFS targets, Godot notes).
- Write an offline synthesis generator in `tools/audio/`. Python with numpy, or C#. It must be deterministic and write OGG/WAV into the client assets. The recipes are those of the direction document; normalise to the target loudness.
- In the client:
  - audio buses (Master, Music, Ambient, UI, Alerts);
  - an `AudioDirector` that maps simulation and UI events to sounds by priority;
  - fatigue rules: aggregation, voice limit, ducking;
  - alerts can be turned down but not off (#298);
  - volume sliders in a small settings popover.
- Music: 1–2 temporary CC0 tracks, played in sessions with pauses (#298), with sources and licences in LICENSES.md. Ambient: one CC0 or CC BY bed. Licences PD/CC0/CC BY only; record each file.
- If a download is refused, stop and report.

## Part B: v0.3 review remainder
- Code review (TASK-0070):
  - SignalField.cs:150 maps by `.Actor` (medium);
  - the language segment shows the active language;
  - Applied/pending status edge cases and non-command job errors;
  - F3 marks the panel dirty;
  - InvariantCulture formatting;
  - Fatal log printed once;
  - the `GetTranslationObject` deprecation;
  - the power segment localized;
  - the launcher shows import errors.
- Visual review (docs/ui/prototypes/client-v02-review.md, «Второе ревью (v0.3)»):
  - no API names in the text;
  - rewrite the «Цели нет» phrase;
  - the leader line and the goal label must not overlap;
  - acknowledged signal = calm frame with a red ■;
  - Jura heading contrast;
  - no faux-bold;
  - numbers in prose set in mono;
  - comma decimal separator in RU;
  - the orphan "13" and the missing №20;
  - the insert height and screen B fill.

## Acceptance
- `./run-globe-spike.sh` and `--shots` work, and the shots include the "снят" and "СБОЙ" signal states.
- An audio demo: the `--shots` run also records a short WAV of an event sequence into docs/ui/prototypes/client-v04/ for review.
- The main solution's full `dotnet test` is green.

## Результат

Клиент v0.4 в `src/Singularity.Client.Spike/`; Core, Session, Kernel, Lab не менялись. Снимки и демо: `docs/ui/prototypes/client-v04/` (RU и EN, `run-log.txt`, `audio_demo.wav`, `audio_popover.png`, состояния сигнала `signal_active_new/acked`, `signal_cleared` («снят»), `signal_fail_new/acked` («СБОЙ»)).

**Замечание владельца:** звуки по стилю не подходят; рецепты не дорабатывались. Генератор и обвязка сделаны общими: звук = строка каталога `AudioCatalog` (id, приоритет, класс агрегации, длина, число вариантов) + файл `assets/audio/ui/<id>[_n].wav`. Замена рецептов или сэмплов не трогает код: правится `tools/audio/synth.py` (словарь `SOUNDS`) или кладётся файл с тем же именем; длина в каталоге сверяется тестом.

**Часть A (аудио).**
- Генератор `tools/audio/synth.py`: done. Чистый stdlib (numpy нет), детерминирован (свой xorshift, `--seed`, проверено двойным прогоном, md5 совпали), 48 кГц 16 бит моно, нормировка по K-взвешенной громкости до цели рецепта, потолок пика −3 dBFS, `--era` (era_tone 0..1; в клиенте набор era=0). Сверка с ffmpeg ebur128 только на звуке >1 с (launch_space: −19,2 против −20); короткие ebur128 не меряет.
- Шины Master (HardLimiter −1 dB), Music, Ambient, UI, Alert: done (создаются в коде).
- `AudioDirector` + чистая логика `AudioLogic.cs` (без Godot): done. Приоритеты P0–P3, окно агрегации 400 мс (темп 150 мс), ≥3 событий за 2 с = один собирательный звук на +2 полутона и +2 дБ, бюджет 3 голоса с вытеснением (P0 вытесняет низшие), варианты без повтора подряд, MinGap (critical 60 с, nuclear 5 с), дакинг Music/Ambient −6 дБ на P0/P1 + 400 мс, пауза −6 дБ на музыку и фон, скрытая адаптация через 90 мин (P3 −3, P2 −1,5, сброс кнопкой), напоминание о держащемся сигнале один раз через 60 с.
- Тревоги не выключаются: слайдер Alert ограничен снизу 20% (−14 дБ), Alert не дакается; Master-слайдера нет.
- Слайдеры (Музыка, Фон, Эффекты, Тревоги) и «музыки мало/обычно/много» в поповере по кнопке ♪; настройки в `user://audio.cfg`.
- Музыка: 2 временных CC0-трека (Tsorthan Grove, OpenGameArt), сессии 3–5 мин, пауза 30–90 с (`MusicScheduler`), цель −23 LUFS (после лимитера фактически −22). Фон: один CC0 (тот же автор), −32 LUFS (фактически −31), включается после 2 с неподвижной камеры, зацикленный. Источники, URL, лицензии: `LICENSES.md`. Отказов загрузки не было.
- Not done: стемы L0–L3 и стинги (адаптивная музыка по состояниям), era_tone в рантайме, плейлист фона из 4–6 кусков, кроссфейд по зуму, шумы с `ui_tick` на наведение мыши (только выбор державы и открытие дерева).
- Демо: `--shots` играет скрипт из 24 событий + музыку и фон, пишет Master в `audio_demo.wav` (19,5 с, 44,1 кГц стерео, пик −3,6 dBFS). Рекордер Godot даёт 44,1 кГц.

**Что сейчас представляет каждый звук** (рецепт; событие). Громкость в LUFS на файл.
| id | рецепт | событие |
|---|---|---|
| order_ok ×4 | треугольник 880→660 Гц 60 мс + щелчок-шум HP 3 кГц, LP 4 кГц; −26; P2 | команда принята (Pending) |
| order_reject | квадрат 140 Гц ×2 по 50 мс, LP 800, насыщение 20%; −20; P1 | команда отклонена или ошибка |
| queue_tick ×4 | розовый шум 15 мс + синус 1,2 кГц 10 мс; −30; P3 | команда принята на паузе |
| order_done ×4 | синус 523→784 Гц 120 мс + хвост 200 мс; −24; P2 | Pending → Applied на тике |
| signal_attention | две ноты FM 660, 784 Гц (инд. 1) + хвост; −20; P0 | сигнал СПЛАВ или ЛИЦЕНЗИЯ поднят; напоминание через 60 с на −6 дБ |
| signal_clear | то же вниз (784→660), −4 дБ; −24; P1 | эти сигналы сняты |
| signal_alert | два импульса FM 880→1046 + 3-я гармоника; −17; P0 | в клиенте ни к чему не привязан (в Session нет такого события), только демо |
| signal_critical | квадрат 440 Гц PWM 30%, 3 импульса по 80 мс, LP 3 кГц; −16; P0 | СБОЙ потока симуляции |
| node_discovered | FM-колокол 1047 Гц ratio 3,5 индекс 3→0, щелчок реле, хвост 1 с; −20; P1 | у выбранной державы стало больше известных узлов |
| breakthrough | node_discovered + нота +7 пт через 120 мс + шум-свист HP 6 кГц; −18; P1 | у выбранной державы выросло FieldedGen |
| line_commissioned | клик BP 1 кГц + синус 110 (+220) Гц нарастание 350 мс, хвост 300 мс; −22; P1 | у выбранной державы стало больше изделий в серии |
| war_declared | синус 55 (+110) Гц удар 300 мс + квадрат 220 Гц 600 мс LP 1,2 кГц; −16; P0 | не привязан (войны в Session нет), только демо |
| launch_space | розовый шум BP 200→2000 Гц 1,2 с + синус 1175 Гц; −20; P1 | не привязан (запусков в Session нет), только демо |
| launch_nuclear | синусы 82 и 83,5 Гц (биения) + 164 Гц, атака 600 мс; −16; P0 | не привязан, только демо |
| tempo_1..3 | 1–3 клика квадрат 2 кГц 8 мс, шаг +8% питча; −26; P2 | смена темпа между день/неделя/месяц |
| pause / unpause | синус 110 Гц с LP-свипом вниз / вверх 250 мс; −26; P2 | пауза / снятие паузы |
| ui_tick ×4 | синус 1,8 кГц 25 мс BP; −30; P3 | выбор державы, открытие дерева |

**Часть B, код-ревью (TASK-0070).** Все done:
- SignalField: сопоставление по `.Actor` (FirstOrDefault).
- Сегмент языка показывает активный язык (`--lang en` тоже).
- Applied/pending: команда обрабатывается ровно один раз (`_cmdSeq` до try), сбой Submit даёт `CmdStatus.Error` только этой команде; ошибки не-командных задач вынесены в `HostSnapshot.JobError` (видно по F3), результат команды не затирают.
- F3 помечает панель грязной (`ActorPanel.Debug` пересобирает).
- Форматирование только через `Loc.N` (InvariantCulture, запятая в RU) и явный Invariant в отладочных строках.
- Fatal в лог один раз; `GetTranslationObject` убран (один `Translation` на файл и язык); сегмент держав локализован (`actor.short.*`); лаунчер показывает ошибки импорта.
- Не проверено: сбой потока вживую (в снимках СБОЙ вызван хуком `SimHost.SimulateFatal`, потоки не падают).

**Часть B, визуальное ревью.** done: названия API (`research_priority`, `Links`, `Money`/`Alloy`, enum рычагов, причин отказа) заменены ключами (`bind.*`, `lever.*`, `cause.*`, `refusal.*`), «Session» убран из текста; фраза «Цели нет» переписана; выноска и подпись цели не пересекаются (линия и маршрут — препятствия для подписей, больше запасных мест); квитированный сигнал = спокойная рамка + залитый ■, снятый = полый □ на 8 с; заголовки Jura через настоящий вес 600 (FontVariation) и ink-2; псевдожирный у «США»: причина была остаточная обводка, убрана, метка в Literata 700; числа в прозе PT Mono (`Prose`, RichTextLabel); запятая в RU; неразрывный пробел («месяц 5», «мес.»); №20: номера всех фронтирных узлов ставятся первыми, имена потом; подписи клиньев вне кольца; высота вклейки по содержимому; ведомость B: пояснение шкалы и столбиков, строки растут с окном. Частично: нижняя часть ведомости B на 900 px всё ещё свободна на ~150 px; наличие №20 проверено в RU, в EN снимок не пересмотрен.

**Проверка.** `dotnet build` клиента без предупреждений. Весь `dotnet test` решения зелёный: Architecture 98, Kernel 322, Core 1060, Lab 20, Session 353 и новый `tests/Singularity.Client.Audio.Tests` (54: планировщик, напоминания, настройки, музыка, фон, WAV-файлы против каталога; добавлен в sln, компилирует `AudioLogic.cs` ссылкой, клиент вне sln). `./run-globe-spike.sh --shots` отрабатывает; при выходе Godot пишет утечки рендерера (ERROR/WARNING «leaked at exit»), сравнение с v0.3 не делалось.

**OPEN.** (1) Пересмотр audio direction владельцем: рецепты и привязки выше временные. (2) Нужен ли в sln тест-проект, ссылающийся на файл клиента (решение systems-architect, ADR-0014). (3) Реальные источники для войны, запусков и тревоги (`signal_alert`) в Session отсутствуют.
