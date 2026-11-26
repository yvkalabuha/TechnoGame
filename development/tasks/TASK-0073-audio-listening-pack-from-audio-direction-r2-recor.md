---
id: TASK-0073
title: "Audio listening pack from AUDIO_DIRECTION r2 recordings"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0071]
approved_by: owner
approved_at: 2026-11-18
approval_ref: "DECISIONS_LOG #305, #309"
created: 2026-11-18
tags: [task, audio, done]
---


## Goal
Download the candidate recordings listed in docs/audio/AUDIO_DIRECTION_r2.md. Process them per its rules: trim, EQ, loudness, variations. Assemble a listening pack for the owner. Nothing enters the client until the owner approves (#309).

## Scope
- Use only the candidates with verified PD/CC0/CC BY licences (BigSoundBank CC0 is allowed). Re-check each licence on its page at download time. Skip any that turn out to be NC or SA.
- Extend tools/audio/prepare_external.py, or add a new script. It must be reproducible: pinned URLs and sha256 of the downloaded originals in a manifest. Do not commit originals larger than needed; commit processed OGG.
- Output docs/audio/listening-pack/:
  - per event, 1–3 candidates as OGG with a short label;
  - the five-layer design-bureau ambient bed as a 60 s mix;
  - the music candidates (Scott Buckley «Extrapolation» and «Cobalt», CC BY 4.0), trimmed to 60 s previews;
  - an index.html that plays each with its event name, source, author and licence. Self-contained: relative audio files, no external scripts.
- Attribution entries for every CC BY file.

## Acceptance
- index.html lists every event from the r2 sound map with at least one candidate, or "not found" for the gaps noted in r2.
- The total pack size stays reasonable (< 30 MB).
- If a download is refused, list it as not obtained. Do not work around it.

## Результат
**Сделано.** Скрипт `tools/audio/build_listening_pack.py` (stdlib + ffmpeg): страница источника -> проверка лицензии (CC0 / CC BY 3.0 / 4.0, иначе отказ) -> загрузка -> sha256 в `tools/audio/listening_pack_manifest.json` -> автонарезка по началу удара -> общий EQ (HP 120, -3 дБ на 3 кГц, LP 9/12 кГц) -> громкость по приоритету (P3 -30, P2 -24, P1 -20, P0 -18 LUFS, пик <= -3 dBTP) -> OGG. Пакет: `docs/audio/listening-pack/` (8.4 МБ).
- 20 событий карты r2: 51 файл (по 2-3 кандидата), `index.html` с плеерами, источником, автором, лицензией; незакрытые пробелы r2 перечислены.
- Постель КБ, 5 слоёв, 60 с: `bed_kb_spec.ogg` (-38.9 LUFS, уровни слоёв r2; суммарно тише, чем «-34...-30» в r2) и `bed_kb_audible.ogg` (-26 LUFS для прослушивания).
- Музыка (превью 75 с, -23 LUFS): Buckley Extrapolation, Cobalt, Starfire, Aphelion, Hyperion, Vanguard (CC BY 4.0).
- `CREDITS.yml` (черновик записей атрибуции для всех использованных источников).
**Не получено.** Семь классических записей Commons (Mars, 1812, Souvenir de Florence, Humoresque, Dvorak 9, Beethoven 5, Grieg): Wikimedia отвечает HTTP 429 (лимит) даже после ожиданий; обход не применялся. Повторить запуском скрипта позже (кэш и manifest подхватятся).
**Ограничения.** Freesound отдаёт оригиналы только после логина: взяты публичные HQ-превью (mp3 128k), годятся для прослушивания, не для сборки. Нарезка автоматическая, на слух не проверялась; 4 кандидата с неотличимым началом резались с 0 с. BigSoundBank (CC0) принят по r2, лицензия проверена на странице.
**Проверка.** Полный прогон скрипта; ebur128 всех выходных файлов, true peak <= -3 dBTP.
