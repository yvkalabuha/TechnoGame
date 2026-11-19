# CURRENT STATE

## Сводка
- **Фаза: MS-M0 закрыт (#419). Идёт M-play — первая играбельная сборка (#418); MS-M1 ждёт вердикта партии. План работ — `development/PLAN.md`.**
  - Срез считает до декабря 2030 (TASK-0121, `SchemaVersion` 12); сквозной дескриптор проекта слит (TASK-0115). Замер 1978–2030 показал пустую и вырожденную вторую половину партии: решения #430 (бюджеты) и #431 (парк без замены); правила спроектированы, прошли gate, ждут утверждения владельца.
  - EXP-0007 — INCONCLUSIVE, цифры приняты как справка; решающий замер — K4 на мире M1 (#419). EXP-0008 и ADR-0024 отложены, харнесс заморожен. Отложены до вердикта M-play: #328, #364, #394, провинции #195 (#420). CI на `main` зелёный с 2026-11-19 (TASK-0116).
- **M1, блок 0:** ADR-0019 (дневной тик) и ADR-0020 (провинции) приняты условно (#353, #354); их Research: R3 опись (TASK-0089), набросок нескольких таблиц (TASK-0090), тест «один писатель» (TASK-0086) — сделаны; замер EXP-0007 — INCONCLUSIVE, решающий замер на мире M1 (#419). ADR-0001–0023 приняты; ADR-0024 отложен.
- **Гипотеза M1:** HYP-0005 ред. 3 (правила #356–#359), мощность пересчитана (`docs/simulation/hyp-0005-power.md`); ждёт Q42 и утверждения (Q34).
- **Карта:** мир собран (TASK-0091): 3124 провинции, 379 регионов (TASK-0114; предел предупреждения 400, ADR-0022), владельцы 1945 для всех регионов (TASK-0095). Модель условий событий ред. 3 (Q50, Q51), RR-0020 сверен (≈150 событий).
- **Клиент:** приборы (TASK-0111) и экран авторов (TASK-0112) слиты, ждут закрытия; полный экран и Full HD (TASK-0117) — в ветке, не слит (3 замечания Codex). v0.5b–v0.5d приняты (меню, настройки, палитра C, рейка 7 областей, текстуры, иконки, картинки областей). Остатки — DEBT-0047, DEBT-0049.
- **Исследования волны 3:** RR-0016 (война) ред. 2 после gate, RR-0017, RR-0019 (вариант B), RR-0021 (политика), RR-0022 (ветки), RR-0023 (старт 1945) — черновики, обновлены под решения #361–#375.
- **Очередь вопросов владельцу:** бюджеты (вариант правила), парк без замены (кривая старения) — после замера в Lab; список в handoff.
- **Решения:** владельца — до #431; Orchestrator (D2, #421) — `docs/decisions/ORCH_DECISIONS.md`, до O-024.
- **Внешние исполнители:** Codex — второй ревьюер; DeepSeek — третий голос каждого gate.
- **Стек:** .NET 10.0.403 в `~/.dotnet`; Godot 4.7.2 mono в `~/.local/opt/godot`; Python 3 + PyYAML; ffmpeg; gitleaks 8.30.1.
- **Последний handoff:** `development/handoff/2026-11-19-1.md`.

## Активные задачи
<!-- registry:active:start -->
- TASK-0117 [IN_PROGRESS] Client UI scale policy: text never below the A1 floor at 1280x720 (developer)
- TASK-0122 [IN_PROGRESS] Budget rule variants behind a run switch for the Lab measurement (NOW, A, B1, B2) (developer)
- TASK-0123 [IN_PROGRESS] Lab measurement of budget rule variants: workforce, alloy and limiter columns, manifest, analysis (simulation-engineer)
- TASK-0111 [REVIEW] UI instruments: Flask, Caliper, Pencil controls and E1 gauge legibility at 100% (UI_INSTRUMENTS §8 steps 3-5, DEBT-0059) (developer)
- TASK-0112 [REVIEW] Credits screen with music and sound attribution (DEBT-0050) (developer)
<!-- registry:active:end -->

## Блокеры
- #228 требует ADR к ADR-0006 до кода E1; системам провинций нужен ADR бюджета.
- CI job `llm-worker` на GitHub не проверен.

## Точка продолжения
См. `development/handoff/2026-11-18-1.md`:
1. Ревью TASK-0075 (сборщик карты) и TASK-0077 (клиент v0.5a); владелец выбирает звуки из пакета.
2. ADR-0017 и F4 в коде, затем EXP-0007 и закрытие MS-M0.
3. Клиент v0.5b: меню, настройки, палитра C, сохранения, Fluent, звуки.
4. План M1 (#321).
