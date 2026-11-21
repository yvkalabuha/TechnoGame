# CURRENT STATE

## Сводка
- **Фаза: MS-M0 закрыт (#419). Идёт M-play — первая играбельная сборка (#418); MS-M1 ждёт вердикта партии. План работ — `development/PLAN.md`.**
  - Срез считает до декабря 2030 (TASK-0121, `SchemaVersion` 12). Правила бюджета измерены (TASK-0122, TASK-0123; `docs/simulation/budget-rule-measurement.md`): вариант А не проходит допуски 1945–1977, Б1 и Б2 проходят и не различаются сверх шума. Записки: бюджеты ред. 3, парк без замены ред. 3 (#430, #431); **владелец правила ещё не утверждал**.
  - EXP-0007 — INCONCLUSIVE, цифры приняты как справка; решающий замер — K4 на мире M1 (#419). EXP-0008 и ADR-0024 отложены, харнесс заморожен. Отложены до вердикта M-play: #328, #364, #394, провинции #195 (#420). CI на `main` зелёный с 2026-11-19 (TASK-0116).
- **M1, блок 0:** ADR-0019 (дневной тик) и ADR-0020 (провинции) приняты условно (#353, #354); их Research: R3 опись (TASK-0089), набросок нескольких таблиц (TASK-0090), тест «один писатель» (TASK-0086) — сделаны; замер EXP-0007 — INCONCLUSIVE, решающий замер на мире M1 (#419). ADR-0001–0023 приняты; ADR-0024 отложен.
- **Гипотеза M1:** HYP-0005 ред. 3 (правила #356–#359), мощность пересчитана (`docs/simulation/hyp-0005-power.md`); ждёт Q42 и утверждения (Q34).
- **Карта:** мир собран (TASK-0091): 3124 провинции, 379 регионов (TASK-0114; предел предупреждения 400, ADR-0022), владельцы 1945 для всех регионов (TASK-0095). Модель условий событий ред. 3 (Q50, Q51), RR-0020 сверен (≈150 событий).
- **Клиент:** экран авторов закрыт (TASK-0112). Полный экран, Full HD, рейка и дерево (TASK-0117) — в ветке `worktree-agent-ad4c3f162fff85a3a` (237d986), ревьюер APPROVE и арт-директор PASS на 04ccb8a, **не слит**: второе ревью Codex возвращало пять раз. Приборы (TASK-0111) в REVIEW: колб в живой партии нет (TASK-0119). v0.5b–v0.5d приняты. Остатки — DEBT-0047, DEBT-0049, DEBT-0066, DEBT-0068.
- **Исследования волны 3:** RR-0016 (война) ред. 2 после gate, RR-0017, RR-0019 (вариант B), RR-0021 (политика), RR-0022 (ветки), RR-0023 (старт 1945) — черновики, обновлены под решения #361–#375.
- **Очередь вопросов владельцу:** правило бюджетов (А / Б1 / Б2 / отложить), три вопроса по парку без замены, порядок слияния TASK-0117 при новых замечаниях Codex — список в handoff.
- **Решения:** владельца — до #431; Orchestrator (D2, #421) — `docs/decisions/ORCH_DECISIONS.md`, до O-030.
- **Внешние исполнители:** Codex — второй ревьюер; DeepSeek — третий голос каждого gate.
- **Стек:** .NET 10.0.403 в `~/.dotnet`; Godot 4.7.2 mono в `~/.local/opt/godot`; Python 3 + PyYAML; ffmpeg; gitleaks 8.30.1.
- **Последний handoff:** `development/handoff/2026-11-20-1.md`.

## Активные задачи
<!-- registry:active:start -->
- TASK-0124 [IN_PROGRESS] Kernel table snapshot and save-load-continue continuity test (A4) (developer)
- TASK-0125 [IN_PROGRESS] No mutable static fields in Kernel, Core, Session; two worlds on one thread pool (A6) (test-engineer)
- TASK-0111 [REVIEW] UI instruments: Flask, Caliper, Pencil controls and E1 gauge legibility at 100% (UI_INSTRUMENTS §8 steps 3-5, DEBT-0059) (developer)
- TASK-0117 [REVIEW] Client UI scale policy: text never below the A1 floor at 1280x720 (developer)
<!-- registry:active:end -->

## Блокеры
- #228 требует ADR к ADR-0006 до кода E1; системам провинций нужен ADR бюджета.
- CI job `llm-worker` на GitHub не проверен.

## Точка продолжения
См. `development/handoff/2026-11-20-1.md`:
1. TASK-0117: `land.py` ветки `worktree-agent-ad4c3f162fff85a3a` с полным вторым ревью; затем `/show` с кадрами Full HD.
2. Вопросы владельцу: правило бюджетов и правило парка (AskUserQuestion, простым языком).
3. После утверждения — второй «поезд схемы»: данные поколений 5–6, оба правила, одна перезапись эталонов.
