# CURRENT STATE

## Сводка
- **Фаза: MS-M0 закрыт (#419). Идёт M-play — первая играбельная сборка (#418); MS-M1 ждёт вердикта партии. План работ — `development/PLAN.md`.**
  - EXP-0007 — INCONCLUSIVE, цифры приняты как справка; решающий замер — K4 на мире M1 (#419). EXP-0008 и ADR-0024 отложены, харнесс заморожен. Отложены до вердикта M-play: #328, #364, #394, провинции #195 (#420). CI на `main` красный с 2026-11-14 — чинит TASK-0116.
- **M1, блок 0:** ADR-0019 (дневной тик) и ADR-0020 (провинции) приняты условно (#353, #354); их Research: R3 опись (TASK-0089), набросок нескольких таблиц (TASK-0090), тест «один писатель» (TASK-0086) — сделаны; замер EXP-0007 — ждёт. ADR-0003/0005/0007/0008 ред. 3 и ADR-0021 ред. 3 — ждут утверждения (Q38, Q47).
- **Гипотеза M1:** HYP-0005 ред. 3 (правила #356–#359), мощность пересчитана (`docs/simulation/hyp-0005-power.md`); ждёт Q42 и утверждения (Q34).
- **Карта:** мир собран (TASK-0091): 3124 провинции, 374 региона (379 после TASK-0114; предел предупреждения 400, ADR-0022), владельцы 1945 для всех регионов (TASK-0095). Модель условий событий ред. 3 (Q50, Q51), RR-0020 сверен (≈150 событий).
- **Клиент:** v0.5b–v0.5d приняты (меню, настройки, палитра C, рейка 7 областей, текстуры, иконки, картинки областей). Остатки — DEBT-0047, DEBT-0049.
- **Исследования волны 3:** RR-0016 (война) ред. 2 после gate, RR-0017, RR-0019 (вариант B), RR-0021 (политика), RR-0022 (ветки), RR-0023 (старт 1945) — черновики, обновлены под решения #361–#375.
- **Очередь вопросов владельцу:** пуста; следующие придут со страницей «контур игры» (M-play, шаг 4.1).
- **Решения:** владельца — до #421; Orchestrator (D2, #421) — `docs/decisions/ORCH_DECISIONS.md`, до O-007.
- **Внешние исполнители:** Codex — второй ревьюер; DeepSeek — третий голос каждого gate.
- **Стек:** .NET 10.0.403 в `~/.dotnet`; Godot 4.7.2 mono в `~/.local/opt/godot`; Python 3 + PyYAML; ffmpeg; gitleaks 8.30.1.
- **Последний handoff:** `development/handoff/2026-11-18-3.md`.

## Активные задачи
<!-- registry:active:start -->
- TASK-0115 [IN_PROGRESS] End-to-end project handle through Session, AI, UI and command log v2 (DEBT-0052) (developer)
- TASK-0116 [IN_PROGRESS] CI green on all jobs: Lab tests off Linux, llm_worker pilot contracts (test-engineer)
- TASK-0111 [REVIEW] UI instruments: Flask, Caliper, Pencil controls and E1 gauge legibility at 100% (UI_INSTRUMENTS §8 steps 3-5, DEBT-0059) (developer)
- TASK-0112 [REVIEW] Credits screen with music and sound attribution (DEBT-0050) (developer)
- TASK-0113 [REVIEW] Policy line candidate by generation and DesignView SlotGeneration rename (DEBT-0053, DEBT-0055) (developer)
- TASK-0114 [REVIEW] Northern Algeria regions for the 1945 map (DEBT-0058) (research-scientist)
- TASK-0117 [APPROVED] Client UI scale policy: text never below the A1 floor at 1280x720 (developer)
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
