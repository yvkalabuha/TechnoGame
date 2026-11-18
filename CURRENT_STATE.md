# CURRENT STATE

## Сводка
- **Фаза: MS-M0, закрытие.**
  - HYP-0002 REJECTED по EXP-0002 (#266), критерий среза засчитан (#267).
  - Осталось: бенчмарк тика. ADR-0016 и ADR-0017 приняты; горячие места исправлены, месяц на (200, 2000) занимает 0,53–0,62 с; F4, реализация ADR-0017 и EXP-0007 впереди.
- **Клиент.** Godot 4.7.2 .NET, `src/Singularity.Client.Spike`, запуск `./run-globe-spike.sh`.
  - Сделано: v0.4 на реальной симуляции, OFL-шрифты, EN/RU (CSV), звуковая система с заглушками.
  - Дизайн v2: палитра C «Акварель», меню и настройки (#302, #303) — не реализован.
- **Локализация.** ADR-0018 (Fluent) принят и реализован (TASK-0074/0076); DataHash 7deed8f0 (#316). Клиент ещё на CSV.
- **Карта.** ADR-0011 rev 3 принят (#315): провинции, владелец и контролёр, старт 1945-10-01 (#311). Сборщик Европы — TASK-0075.
- **Решения:** до #322.
- **PHASE F, волна 3:**
  - RR-0016 (война), RR-0017 (разведка), RR-0019 (логистика) — черновики;
  - альтернативная история (#299) запланирована.
- **Внешние исполнители:** Codex — второй ревьюер; DeepSeek — третий голос каждого gate (входит в `/review-gate`).
- **Стек:** .NET 10.0.403 в `~/.dotnet`; Godot 4.7.2 mono в `~/.local/opt/godot`; Python 3 + PyYAML; ffmpeg; gitleaks 8.30.1.
- **Последний handoff:** `development/handoff/2026-11-18-1.md`.

## Активные задачи
<!-- registry:active:start -->
- TASK-0075 [REVIEW] Map builder T2: tools/map on NTS, Europe first, 1945 cuts, coverage simplification check (developer)
- TASK-0077 [REVIEW] Client v0.5a: planisphere zoom and node icons, node card with schematic and effects, map click fix, starry sky (developer)
- TASK-0064 [APPROVED] lab bench harness (ADR-0016 T1) (developer)
- TASK-0066 [APPROVED] Tick benchmark run and outcome (ADR-0016 T3) (simulation-engineer)
<!-- registry:active:end -->

## Блокеры
- ADR-0003 (численная политика) PROPOSED.
- #228 требует ADR к ADR-0006 до кода E1; системам провинций нужен ADR бюджета.
- CI job `llm-worker` на GitHub не проверен.

## Точка продолжения
См. `development/handoff/2026-11-18-1.md`:
1. Ревью TASK-0075 (сборщик карты) и TASK-0077 (клиент v0.5a); владелец выбирает звуки из пакета.
2. ADR-0017 и F4 в коде, затем EXP-0007 и закрытие MS-M0.
3. Клиент v0.5b: меню, настройки, палитра C, сохранения, Fluent, звуки.
4. План M1 (#321).
