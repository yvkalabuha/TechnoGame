# CURRENT STATE

## Сводка
- **2026-11-26 (2):** разработка продукта на паузе (#595); платформа — полоса A. Влиты: автопилот (0197), каталог ошибок (0198), PostgreSQL-индекс (0200), метрики (0206), релизы (0207), хуки от повторов (0202), память отклонений (0204), skills /shots /recheck /error (0203). Redis (0201) — на ревью. Дальше: 0208 (поддержка базы знаний), 0209 (структура, теги ≤50, пределы размера). Решения #579–#600, O-109…O-112. Факты обновлены. Игра ждёт: 0192/0194 (кадры), 0193+0199+0205 (пакет эталонов), 0196 (спек этапа 2). Советник Fable — проба с 26.11 15:35 до 03.12; code mode у developer — проба с 26.11.
- **2026-11-26:** новая модель науки влита (поезд f12684f): один институт — одна тема, очередь 5, ускорение за деньги, 13 направлений с уровнями, опыт и новые направления, ИИ по пути к самолёту, воздушная мощь каждый месяц (#561–#578); все рычаги волны 1 по умолчанию (TASK-0171); 57 новых тем (TASK-0188). Замер: первые G2/G3/G4 ≈ 1954/1959/1969, простоя до 1990 нет. Влиты: Монголия, рельеф, 3D G1–G6 (Gemini), pace_check, Gemini через подписку (agy, #578 — Gemini ведущий арт-директор), проверка фактов через DeepSeek (#577), CI Windows/macOS раз в сутки. Идут: экран карты технологий этап 1 (TASK-0192, кадры r1 сняты, правки), дерево 1990–2030 (TASK-0193, 55 тем + поздние предпосылки). Долг: DEBT-0108…0115. Вопросы в очереди: A17 (Тибет/Тайвань/Гданьск), A19 (излишек денег науки после 1990), A20 (стиль карты по Gemini).
- **2026-11-25 (5):** ядро волны рычагов 1 влито целиком: разведка (0160), советник (0153), институты (0151, 0174, 0175; P-7 PASS — 0173), K0 и таблицы Kernel (0162, 0165); Session API v26. Окна рычагов в клиенте (0156 части 1–2) ждут кадров. Включение механик по умолчанию (0171) остановлено: институты сдвигают калибровку 1945–1977 (A16). 3D делает Gemini (#544), набор частей G4 готов.
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
- **Последний handoff: `development/handoff/2026-11-26-2.md`

## Активные задачи
<!-- registry:active:start -->
- TASK-0163 [IN_PROGRESS] Globe country names: no plate, size by country extent with a minimum, hide when not fitting until zoomed (#523) (developer)
- TASK-0166 [IN_PROGRESS] M1: verify 1945 owners of 379 regions against sources (T-M1-10) (research-scientist)
- TASK-0167 [IN_PROGRESS] Globe colours by political-map traditions (#527): fixed tones for great powers, colony = metropolis tint (developer)
- TASK-0192 [IN_PROGRESS] Tech map stage 1: node choice card, institute strip, queue, path to goal — Gemini design, then client (#561, #578) (developer)
- TASK-0194 [IN_PROGRESS] Air power goal (#565): Session goal state parity/superiority/dominance and a header indicator in the client (developer)
- TASK-0195 [IN_PROGRESS] Era price of research nodes: node cost grows with era so leaders spend science money after 1990 (#579) (simulation-engineer)
- TASK-0196 [IN_PROGRESS] Tech map stage 2 design: immersive volumetric map, free mouse travel, compact institute cards with emblems and icons (#582) (ui-art-director)
- TASK-0199 [IN_PROGRESS] Core: AI builds institutes on science surplus + era node price curve D by min(node year, calendar year) (#589) (developer)
- TASK-0205 [IN_PROGRESS] Data: build candidate institutes 1960-2030 for USA, SUN, GBR, FRA, SWE with directions and levels (#589, O-110) (research-scientist)
- TASK-0111 [REVIEW] UI instruments: Flask, Caliper, Pencil controls and E1 gauge legibility at 100% (UI_INSTRUMENTS §8 steps 3-5, DEBT-0059) (developer)
- TASK-0161 [REVIEW] Design pass 2 B: knowledge tree by year rings and direction sectors, 6 zoom steps, icons, node menu, prominent close (#473) (developer)
- TASK-0193 [REVIEW] Research + data: knowledge tree 1990-2030 (avionics, stealth, composites, computing, space, missiles) so leaders' institutes do not idle after 1990 (research-scientist)
- TASK-0201 [REVIEW] Redis for agent heartbeat, locks and expiring permits (#590), after redis-server is installed (developer)
- TASK-0208 [APPROVED] Knowledge base upkeep: fact drift is an error, decision-to-facts check at landing, freshness, /fact, facts in kb_db and mandatory facts block for code/data agents, agent DB access read-only (#597) (developer)
- TASK-0209 [APPROVED] Knowledge base structure: tag vocabulary (<=50, 3 facets), docs catalogue and chains in kb_db, monthly decisions log, archives, shorter hooks (#598, #599) (developer)
<!-- registry:active:end -->

## Блокеры
- A16: включать ли институты у ИИ по умолчанию (калибровка) — блокирует TASK-0171 и сборку для партии.
- Кадры игры (A8, A15) отложены владельцем (#545) — блокируют приёмку TASK-0156, 0161, 0163, 0167.

## Точка продолжения
Последний handoff: `development/handoff/2026-11-25-5.md`. Спросить A16 (и A9, A13, A14); по ответу — доделать TASK-0171 (worktree agent-a5360ae99120f7be5, не закоммичено) → кадры → партия владельца.
