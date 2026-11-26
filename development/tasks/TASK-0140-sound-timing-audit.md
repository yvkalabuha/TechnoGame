---
id: TASK-0140
title: "Sound audit: sounds that play at the wrong moment or irritate (owner feedback)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M05
agent: sound-director
category: client
priority: medium
needs_owner: false
depends_on: []
related: [TASK-0135]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "owner in chat 2026-11-24 («звуки невпопад, раздражают немного»)"
review_ref: "sound-director: PASS (task review); owner: accepted in playtest 2026-11-24 (Звуки — нормально, без замечаний)"
created: 2026-11-24
---

# TASK-0140 — Звуки невпопад

## Цель
Владелец: звуки звучат невпопад и немного раздражают. Найти, какие звуки и почему (событие-триггер, частота, громкость, повтор), и дать правки.

## Описание
Шаг 1 — sound-director: аудит по коду (`AudioDirector.cs` и вызовы), карте звуков и настройкам; таблица «звук → когда звучит → когда должен → правка». Шаг 2 — developer по таблице. Позже — второе мнение Gemini по аудио (расширение TASK-0135).

## Критерии приёмки
- [ ] Таблица аудита и правки; повторы ограничены (не чаще N раз за M секунд), звук только на значимые события.
- [ ] Ревьюер APPROVE; sound-director PASS; владелец слушает партию.

## Результат
