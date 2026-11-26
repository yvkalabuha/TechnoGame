---
id: TASK-0100
title: "Replace client event sounds with listening-pack r2 (#403)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M1
agent: developer
category: client
priority: low
needs_owner: false
depends_on: []
related: []
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "#403"
created: 2026-11-18
tags: [task, audio, done]
---

# TASK-0100 — Client event sounds from listening-pack r2 (#403)

## Цель
Заменить звуки событий клиента (`src/Singularity.Client.Spike/assets/audio/ui/`) кандидатами `docs/audio/listening-pack/events` (E01–E20), которые владелец счёл более подходящими.

## Описание
1. sound-director: таблица соответствия событие клиента ↔ E01–E20 (карта AUDIO_DIRECTION r2 §1), выбор варианта по ответу Q66 (до ответа — по метке эпохи 1945), атрибуция из `listening-pack/CREDITS.yml` в `docs/audio/` (вместе с DEBT-0050). Отметить, где использовано HQ-превью Freesound вместо оригинала.
2. developer: заменить файлы и привязки в `AudioDirector.cs`; громкость по приоритету как в r2. Выполнять после серии EXP-0007 (#355).

## Критерии приёмки
- [x] Каждое событие клиента играет звук из пакета r2; старые неиспользуемые файлы удалены.
- [x] Лицензии только CC0 / CC BY (ADR-0009), атрибуция записана.
- [x] Клиент собирается; ui-tester или `--shots`-прогон без ошибок аудио.

## Результат
- Map: `docs/audio/EVENT_SOUNDS_MAP.md` (sound-director, commit 0ba0ee5); default variant = 1945 label until Q66.
- Client: 18 .ogg cues replace 31 .wav and manifest.json; Cue Jitter/Source/Repeats; signal_attention P1; tempo clicks from E18_a; LICENSES.md attribution (4 CC BY 4.0, 9 CC0). Commits 2d920a5, d44dfab, merge c784943.
- Review: APPROVE (reviewer), minors F1–F4 fixed in d44dfab. Audio tests 144/144; Godot import clean; `--shots` exit 0 without audio errors.
- Not checked: playback by ear (no audio device). Unwired: E12, E14, E15. HQ-preview sources to replace before release (map §6).
