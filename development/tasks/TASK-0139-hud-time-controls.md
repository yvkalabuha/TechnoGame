---
id: TASK-0139
title: "HUD time controls: centred pause and speed buttons, no speed text, speed gauge active frame alignment"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M05
agent: developer
category: client
priority: medium
needs_owner: false
depends_on: []
related: [TASK-0136, TASK-0117]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "owner in chat 2026-11-24"
created: 2026-11-24
---

# TASK-0139 — Кнопки времени в верхней полосе

## Цель
Владелец: кнопки паузы и ускорения не отцентрованы и некрасивы; скорость словами («1 день/с», «1 месяц/с») не нужна; у шкалы скорости рамка активного деления стоит не по центру. Gemini (Hydrogen) тоже отметил тонкие деления шкалы (A2).

## Описание
Шаг 1 — ui-art-director: спецификация (размеры в du, центровка значков по оптическому центру, состояние нажатия и фокуса, шкала скорости без подписи с числом в подсказке и для читалки экрана, рамка активного деления). Шаг 2 — developer: `TimeIcons.cs`, `TimeGlyphMetrics.cs`, разметка верхней полосы.

## Критерии приёмки
- [ ] Кадры 1920x1080 при 100 %, 125 %, 150 %: значки центрированы (отклонение ≤ 0,5 du, проверка в прогоне кадров), рамка активного деления симметрична.
- [ ] Скорость без подписи, но доступна (подсказка, A6–A7); пауза — пробел.
- [ ] ui-art-director PASS; ревьюер APPROVE.

## Результат
Сделано (developer, ветка worktree-agent-a17c6a377f7d4cb9c): кнопки 32 du с зазором 4 du, глифы по целым координатам с оптическим сдвигом ±1 du у стрелок (центр масс по пикселям 16.00/16.19/15.81 ±0.5), пауза/стрелки рисуются целыми пикселями (прямые рёбра заливкой, косые с AA); шкала 60x32 du, группа центрирована, рамка активной ступени = столбик +3 du, штрих 2 du, симметрична; слова скорости убраны (подсказка и имя шкалы «Скорость: …», подсказка паузы по состоянию); фокус клавиатурой (кольцо 2 du снаружи, 15.7:1), Space = пауза, Enter на кнопке; фокус уходит на паузу, если кнопка стала недоступной. Одна ось у даты, кнопок и шкалы.
Файлы: src/Singularity.Client.Spike/{TimeGlyphMetrics,TimeIcons,GaugeControl,SignalField,Main,MainTimeShots}.cs, locale/ui.csv, tests/Singularity.Client.Audio.Tests/TimeGlyphMetricsTests.cs.
Проверка: `./run-globe-spike.sh --shots --shot-size 1920x1080 --shot-dir ../../shots-out/task-0139 --time-shots` (70 PASS, 0 FAIL: центры глифов, рамка по пикселям, ось, кольцо фокуса, Enter меняет скорость живой сессии, Space ставит паузу); кадры 100 %/150 % и серые в shots-out/task-0139/. Юнит-тесты TimeGlyphMetricsTests; тест центра масс падает без сдвига (проверено локально).
Не проверено: PASS ui-art-director и ревью; 125 %; мнение Gemini (#450).
