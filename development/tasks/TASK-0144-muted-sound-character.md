---
id: TASK-0144
title: "Muted sound character (#455): soften attack and low-pass all routine cues"
status: IN_PROGRESS
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
created: 2026-11-24
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
