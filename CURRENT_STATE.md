# CURRENT STATE

## Сводка
- **Фаза: MS-M0 закрыт (#419). Идёт M-play — первая играбельная сборка (#418); MS-M1 ждёт вердикта партии. План работ — `development/PLAN.md`.**
  - Срез считает до декабря 2030 (TASK-0121). Поезд 2 влит 2026-11-24 (88fa41b): 99 узлов знаний, G5/G6 с промежуточными ступенями (#457), цена опережения k 0,02 / 2 / 10 (#458, #465), освоение производства (#452), старение парка (#431). Колбы науки и сплава в живой партии (TASK-0119). Правила бюджета измерены (TASK-0122, TASK-0123; `docs/simulation/budget-rule-measurement.md`): вариант А не проходит допуски 1945–1977, Б1 и Б2 проходят и не различаются сверх шума. Записки: бюджеты ред. 3, парк без замены ред. 3 (#430, #431); **владелец правила ещё не утверждал**.
  - EXP-0007 — INCONCLUSIVE, цифры приняты как справка; решающий замер — K4 на мире M1 (#419). EXP-0008 и ADR-0024 отложены, харнесс заморожен. Отложены до вердикта M-play: #328, #364, #394, провинции #195 (#420). CI на `main` зелёный с 2026-11-19 (TASK-0116).
- **M1, блок 0:** ADR-0019 (дневной тик) и ADR-0020 (провинции) приняты условно (#353, #354); их Research: R3 опись (TASK-0089), набросок нескольких таблиц (TASK-0090), тест «один писатель» (TASK-0086) — сделаны; замер EXP-0007 — INCONCLUSIVE, решающий замер на мире M1 (#419). ADR-0001–0023 приняты; ADR-0024 отложен.
- **Гипотеза M1:** HYP-0005 ред. 3 (правила #356–#359), мощность пересчитана (`docs/simulation/hyp-0005-power.md`); ждёт Q42 и утверждения (Q34).
- **Карта:** мир собран (TASK-0091): 3124 провинции, 379 регионов (TASK-0114; предел предупреждения 400, ADR-0022), владельцы 1945 для всех регионов (TASK-0095). Модель условий событий ред. 3 (Q50, Q51), RR-0020 сверен (≈150 событий).
- **Клиент:** полный экран, Full HD. Глобус: границы 1945 и 12 тонов (TASK-0138), легенда штриховок, выноски, ровный берег (TASK-0142); пауза и скорость (TASK-0139); дерево и меню понятнее (TASK-0136); звуки реже и глуше (TASK-0140, TASK-0144 — ждут прослушивания владельцем). Страница для владельца: https://claude.ai/artifact/4CnSDVTUtD4THDka6Y5QUq. Дизайн-проход 2 по эскизам Gemini (#453, #454): `docs/ui/specs/design-pass-2-tree-science.md`.
- **Стражи архитектуры (этап 3):** TASK-0124 DONE (a86303b; DEBT-0072), TASK-0125 DONE (14226ca; остаток TASK-0127 DONE 07ea3a8, DEBT-0077). ADR-0025 реализован: TASK-0130 DONE (6fe565d; вид клиента с подпиской; DEBT-0074, DEBT-0078). `ARCHITECTURE.md` сверен.
- **Исследования волны 3:** RR-0016 (война) ред. 2 после gate, RR-0017, RR-0019 (вариант B), RR-0021 (политика), RR-0022 (ветки), RR-0023 (старт 1945) — черновики, обновлены под решения #361–#375.
- **Очередь вопросов владельцу:** пусто, кроме Q70 (правило бюджетов — после партии владельца, #433). Решения 2026-11-22: #432–#437.
- **Решения:** владельца — до #465; Orchestrator (D2, #421) — `docs/decisions/ORCH_DECISIONS.md`, до O-052.
- **Проекты механик (только документы, #459):** институты и личности ред. 2 (`docs/game-design/civilization/INSTITUTIONS_PERSONALITIES.md`, #461–#463), разведка ред. 3 (`docs/game-design/INTELLIGENCE.md`, O-051).
- **Внешние исполнители:** Codex — второй ревьюер, при его лимите — Sonnet 5.5 через agy в песочнице (#456, TASK-0146); DeepSeek — третий голос каждого gate; эскизы Gemini через agy (TASK-0141, TASK-0143).
- **Стек:** .NET 10.0.403 в `~/.dotnet`; Godot 4.7.2 mono в `~/.local/opt/godot`; Python 3 + PyYAML; ffmpeg; gitleaks 8.30.1.
- **Последний handoff:** `development/handoff/2026-11-20-1.md`.

## Активные задачи
<!-- registry:active:start -->
- TASK-0132 [IN_PROGRESS] Keyboard selection of technology tree nodes and keyboard tooltip (A6-A8) (developer)
- TASK-0145 [IN_PROGRESS] Sketches through Codex CLI image generation (llm_worker sketch --engine codex) (developer)
- TASK-0151 [IN_PROGRESS] Player levers core: research priorities through institutes (slots, funding first x1.5, institute bonus, capacity, upkeep, building institutes) (developer)
- TASK-0152 [IN_PROGRESS] Player levers core: licence market (multi-licence state, seller catalogue, BuyLicence, AdoptLicensedDesign, AI price rule, selling own projects) (developer)
- TASK-0153 [IN_PROGRESS] Player levers core: AI adviser for the player country production (no AI opening or closing of player lines) (developer)
- TASK-0157 [IN_PROGRESS] Node content: effects in numbers and short history with sources for all 99 knowledge nodes (#486) (research-scientist)
- TASK-0161 [IN_PROGRESS] Design pass 2 B: knowledge tree by year rings and direction sectors, 6 zoom steps, icons, node menu, prominent close (#473) (developer)
- TASK-0111 [REVIEW] UI instruments: Flask, Caliper, Pencil controls and E1 gauge legibility at 100% (UI_INSTRUMENTS §8 steps 3-5, DEBT-0059) (developer)
- TASK-0154 [REVIEW] Data: alliances and blocs by year 1945-2030 for the licence buyer circle (research-scientist)
- TASK-0155 [REVIEW] Data: constant 1990 US dollar display series and conversion from Core money unit (economy-analyst)
- TASK-0150 [APPROVED] Player levers core: budget reserve account with 10 %/year leak, shares at most 1, FundProgramme (developer)
- TASK-0156 [APPROVED] Player levers client: research goals, budget in money with reserve, production line window, licence market window (developer)
- TASK-0160 [APPROVED] Intelligence v1 core: security budget share, targets and modes, theft channel in CEff, counterintelligence, disinformation, confidence of estimates (#515) (developer)
<!-- registry:active:end -->

## Блокеры
- #228 требует ADR к ADR-0006 до кода E1; системам провинций нужен ADR бюджета.
- CI job `llm-worker` на GitHub не проверен.

## Точка продолжения
1. Поезд схемы 2 (O-037, O-038): данные G5/G6 и старение парка — ветка `worktree-agent-aee00c98dea970eae` (TASK-0133, verify зелёный). Зонд: контент сам простой не убирает — процессы уровня 5 никто не осваивает, ИИ держит линии на неосваиваемых проектах (`docs/simulation/g5-g6-probe.md` на ветке, §7). Решения владельца 2026-11-24: #438 (потолок кадров растёт), #439 (ИИ не запускает неосваиваемую серию; механика освоения), #440 (программа освоения сразу; для игрока — «обучение персонала», за границей или свой участок). Записки: `docs/simulation/notes/hcap-growth.md`, `docs/game-design/notes/production-mastery.md` ред. 3. Реализация — TASK-0134 (шаг 1: диагноз «Б» идёт), затем зонд B0–B3, страница владельцу, утверждение контента G5/G6 (TASK-0128), TASK-0118, TASK-0119, слияние поезда.
2. UX фаза 1 (П1.1–П1.5) и TASK-0132.
3. Q70 (правило бюджетов) — после партии владельца (#433).
