# CURRENT STATE

## Сводка
- **Фаза: MS-M0 закрыт (#419). Идёт M-play — первая играбельная сборка (#418); MS-M1 ждёт вердикта партии. План работ — `development/PLAN.md`.**
  - Срез считает до декабря 2030 (TASK-0121, `SchemaVersion` 12). Правила бюджета измерены (TASK-0122, TASK-0123; `docs/simulation/budget-rule-measurement.md`): вариант А не проходит допуски 1945–1977, Б1 и Б2 проходят и не различаются сверх шума. Записки: бюджеты ред. 3, парк без замены ред. 3 (#430, #431); **владелец правила ещё не утверждал**.
  - EXP-0007 — INCONCLUSIVE, цифры приняты как справка; решающий замер — K4 на мире M1 (#419). EXP-0008 и ADR-0024 отложены, харнесс заморожен. Отложены до вердикта M-play: #328, #364, #394, провинции #195 (#420). CI на `main` зелёный с 2026-11-19 (TASK-0116).
- **M1, блок 0:** ADR-0019 (дневной тик) и ADR-0020 (провинции) приняты условно (#353, #354); их Research: R3 опись (TASK-0089), набросок нескольких таблиц (TASK-0090), тест «один писатель» (TASK-0086) — сделаны; замер EXP-0007 — INCONCLUSIVE, решающий замер на мире M1 (#419). ADR-0001–0023 приняты; ADR-0024 отложен.
- **Гипотеза M1:** HYP-0005 ред. 3 (правила #356–#359), мощность пересчитана (`docs/simulation/hyp-0005-power.md`); ждёт Q42 и утверждения (Q34).
- **Карта:** мир собран (TASK-0091): 3124 провинции, 379 регионов (TASK-0114; предел предупреждения 400, ADR-0022), владельцы 1945 для всех регионов (TASK-0095). Модель условий событий ред. 3 (Q50, Q51), RR-0020 сверен (≈150 событий).
- **Клиент:** полный экран, Full HD, рейка и дерево (TASK-0117) — DONE; остаток TASK-0126 DONE (37fe1f8: засечки 2 px, подписи с выносками, подсказка узла с названием; DEBT-0080); выбор узлов с клавиатуры — TASK-0132 (PROPOSED). Режимы окна — O-034. Страница для владельца: https://claude.ai/artifact/9ebaGwWdUvhMEqRap1ZVg2. Приборы (TASK-0111) в REVIEW: колб в живой партии нет (TASK-0119). Остатки — DEBT-0047, DEBT-0049, DEBT-0066, DEBT-0068, DEBT-0073.
- **Стражи архитектуры (этап 3):** TASK-0124 DONE (a86303b; DEBT-0072), TASK-0125 DONE (14226ca; остаток TASK-0127 DONE 07ea3a8, DEBT-0077). ADR-0025 реализован: TASK-0130 DONE (6fe565d; вид клиента с подпиской; DEBT-0074, DEBT-0078). `ARCHITECTURE.md` сверен.
- **Исследования волны 3:** RR-0016 (война) ред. 2 после gate, RR-0017, RR-0019 (вариант B), RR-0021 (политика), RR-0022 (ветки), RR-0023 (старт 1945) — черновики, обновлены под решения #361–#375.
- **Очередь вопросов владельцу:** пусто, кроме Q70 (правило бюджетов — после партии владельца, #433). Решения 2026-11-22: #432–#437.
- **Решения:** владельца — до #437; Orchestrator (D2, #421) — `docs/decisions/ORCH_DECISIONS.md`, до O-034.
- **Внешние исполнители:** Codex — второй ревьюер; DeepSeek — третий голос каждого gate.
- **Стек:** .NET 10.0.403 в `~/.dotnet`; Godot 4.7.2 mono в `~/.local/opt/godot`; Python 3 + PyYAML; ffmpeg; gitleaks 8.30.1.
- **Последний handoff:** `development/handoff/2026-11-20-1.md`.

## Активные задачи
<!-- registry:active:start -->
- TASK-0128 [IN_PROGRESS] G5/G6 content for the jet slice: nodes, alternatives, anchors (train 2, T-C) (research-scientist)
- TASK-0129 [IN_PROGRESS] Fleet ageing without replacement in Core (#431, #435-#437) (developer)
- TASK-0133 [IN_PROGRESS] G5/G6 data in mods on the train-2 branch and calibration probe (O-038) (developer)
- TASK-0134 [IN_PROGRESS] Hcap growth and production mastery in Core: series gate, candidate close, foresight, mastery programme (#438-#440) (developer)
- TASK-0143 [IN_PROGRESS] Gemini sketch runs: detect subagent quota errors, one image per subagent, no extra refinements (developer)
- TASK-0144 [IN_PROGRESS] Muted sound character (#455): soften attack and low-pass all routine cues (sound-director)
- TASK-0111 [REVIEW] UI instruments: Flask, Caliper, Pencil controls and E1 gauge legibility at 100% (UI_INSTRUMENTS §8 steps 3-5, DEBT-0059) (developer)
- TASK-0140 [REVIEW] Sound audit: sounds that play at the wrong moment or irritate (owner feedback) (sound-director)
<!-- registry:active:end -->

## Блокеры
- #228 требует ADR к ADR-0006 до кода E1; системам провинций нужен ADR бюджета.
- CI job `llm-worker` на GitHub не проверен.

## Точка продолжения
1. Поезд схемы 2 (O-037, O-038): данные G5/G6 и старение парка — ветка `worktree-agent-aee00c98dea970eae` (TASK-0133, verify зелёный). Зонд: контент сам простой не убирает — процессы уровня 5 никто не осваивает, ИИ держит линии на неосваиваемых проектах (`docs/simulation/g5-g6-probe.md` на ветке, §7). Решения владельца 2026-11-24: #438 (потолок кадров растёт), #439 (ИИ не запускает неосваиваемую серию; механика освоения), #440 (программа освоения сразу; для игрока — «обучение персонала», за границей или свой участок). Записки: `docs/simulation/notes/hcap-growth.md`, `docs/game-design/notes/production-mastery.md` ред. 3. Реализация — TASK-0134 (шаг 1: диагноз «Б» идёт), затем зонд B0–B3, страница владельцу, утверждение контента G5/G6 (TASK-0128), TASK-0118, TASK-0119, слияние поезда.
2. UX фаза 1 (П1.1–П1.5) и TASK-0132.
3. Q70 (правило бюджетов) — после партии владельца (#433).
