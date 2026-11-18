---
id: TASK-0100
title: "Replace client event sounds with listening-pack r2 (#403)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
---

# TASK-0100 — Client event sounds from listening-pack r2 (#403)

## Цель
Заменить звуки событий клиента (`src/Singularity.Client.Spike/assets/audio/ui/`) кандидатами `docs/audio/listening-pack/events` (E01–E20), которые владелец счёл более подходящими.

## Описание
1. sound-director: таблица соответствия событие клиента ↔ E01–E20 (карта AUDIO_DIRECTION r2 §1), выбор варианта по ответу Q66 (до ответа — по метке эпохи 1945), атрибуция из `listening-pack/CREDITS.yml` в `docs/audio/` (вместе с DEBT-0050). Отметить, где использовано HQ-превью Freesound вместо оригинала.
2. developer: заменить файлы и привязки в `AudioDirector.cs`; громкость по приоритету как в r2. Выполнять после серии EXP-0007 (#355).

## Критерии приёмки
- [ ] Каждое событие клиента играет звук из пакета r2; старые неиспользуемые файлы удалены.
- [ ] Лицензии только CC0 / CC BY (ADR-0009), атрибуция записана.
- [ ] Клиент собирается; ui-tester или `--shots`-прогон без ошибок аудио.
