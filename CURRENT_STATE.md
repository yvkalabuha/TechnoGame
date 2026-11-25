# CURRENT STATE

## Сводка
- **Фаза: M-play: вердикт партии 2026-11-24 — решений у игрока нет (#470). Идёт волна рычагов 1 (одна сборка для партии, #474, #497) и М1 параллельно (#504, ADR-0027…0029). Режим работы /autopilot (#529).**
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
- **Последний handoff:** `development/handoff/2026-11-25-3.md` (подробности — `2026-11-25-2.md`).

## Активные задачи
<!-- registry:active:start -->
- TASK-0132 [IN_PROGRESS] Keyboard selection of technology tree nodes and keyboard tooltip (A6-A8) (developer)
- TASK-0156 [IN_PROGRESS] Player levers client: research goals, budget in money with reserve, production line window, licence market window (developer)
- TASK-0161 [IN_PROGRESS] Design pass 2 B: knowledge tree by year rings and direction sectors, 6 zoom steps, icons, node menu, prominent close (#473) (developer)
- TASK-0163 [IN_PROGRESS] Globe country names: no plate, size by country extent with a minimum, hide when not fitting until zoomed (#523) (developer)
- TASK-0164 [IN_PROGRESS] M1: tag the M0 slice where EXP-0002 goldens still match (K6, T-M1-02) (simulation-engineer)
- TASK-0166 [IN_PROGRESS] M1: verify 1945 owners of 379 regions against sources (T-M1-10) (research-scientist)
- TASK-0168 [IN_PROGRESS] Terrain M1: download sources, bake per-province terrain and rivers, 1945 corrections (#528, #533-#536) (developer)
- TASK-0173 [IN_PROGRESS] Lab: P-7 build-everything-early does not dominate (years of key nodes) and project limit check, before flags on (simulation-engineer)
- TASK-0175 [IN_PROGRESS] Institutes: stretched project pays the price of its actual opening year; overload safety rule speed x min(1, 1/sum shares) (developer)
- TASK-0111 [REVIEW] UI instruments: Flask, Caliper, Pencil controls and E1 gauge legibility at 100% (UI_INSTRUMENTS §8 steps 3-5, DEBT-0059) (developer)
- TASK-0167 [APPROVED] Globe colours by political-map traditions (#527): fixed tones for great powers, colony = metropolis tint (developer)
- TASK-0171 [APPROVED] Golden batch: turn on Reserve, Institutes, LicenceMarket, Adviser, Intelligence by default; re-record goldens once with explanation (developer)
<!-- registry:active:end -->

## Блокеры
- #228 требует ADR к ADR-0006 до кода E1; системам провинций нужен ADR бюджета.
- CI job `llm-worker` на GitHub не проверен.

## Точка продолжения
1. Слить ветки ядра по порядку (0160, 0162, 0165, 0153, 0151; версии API Session при слиянии) — `development/handoff/2026-11-25-2.md`.
2. Пачка кадров с разрешения владельца: дерево (0161), глобус (0163/0167); затем ui-art-director + Gemini.
3. Рельеф 0168 (ветка агента), 3D-пилот и сравнение Claude vs Gemini (#539).
4. Личности в институтах (#540–#543).
5. После всех веток ядра — пакет изменений, меняющих эталоны (флаги по умолчанию), затем окна рычагов TASK-0156.
