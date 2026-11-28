# CURRENT STATE

## Сводка
- **2026-11-28 (1):** владелец снял паузу продукта (#617): первым вливается пакет эталонов 0193+0199+0205, затем ветка карты технологий; кадры — после новой модели науки (#618); предел руководителя +12 % (#619); 3D G1–G6 уже готов (#620, ERR-0007).
- **2026-11-27 (1):** платформа: ветки разобраны — 15 закрыто, сохранены метками `archive/*` (24 метки, отправлены на GitHub, #615); worktree 57 → 3; #607 по размерам выполнен (все файлы в пределах). Красный CI (тест лимитов зависел от часов) исправлен — TASK-0214, CI зелёный. Решения #613–#616: пауза остаётся, кадры позже, идеи Gemini для карты — все в следующий раунд.
- **2026-11-26 (3):** платформа: влиты Redis (0201, фильтр команд по #601), поддержка базы знаний (0208, #604), структура базы знаний (0209: теги, журнал по месяцам, архивы, пределы размера, перенос в архив при посадке; #607–#611), лимиты моделей из llmlimits (0211, #605, #606, #612), исправления CI (0210, 0212). Осталось по платформе: ужать большие файлы до 2026-12-03 (#607), долги DEBT-0122…0125, 0127, 0128. Продукт на паузе (#595).
- **Постоянное:** пауза продукта снята (#617); режим /autopilot (#583); лимиты моделей — `python3 tools/development/llm_limits.py` (#605). Решения: журнал `docs/decisions/DECISIONS_LOG.md` (+ `docs/decisions/log/`), Orchestrator — `ORCH_DECISIONS.md`. Стек: .NET 10 в `~/.dotnet`, Godot 4.7.2 mono в `~/.local/opt/godot`, Python 3 + PyYAML, gitleaks. Внешние: второе ревью Codex → agy-sonnet → DeepSeek (#456, #605); DeepSeek — третий голос gate и сбор фактов; Gemini (agy) — ведущий арт-директор (#578).
- **История сводки:** `development/archive/CURRENT_STATE-history.md`.
- **Последний handoff:** `development/handoff/2026-11-28-1.md`

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
<!-- registry:active:end -->

## Блокеры
- Посадка ветки worktree-agent-a4e34095… (0193/0199/0205) ждёт Codex (до 2026-11-28 08:04Z); ветка worktree-agent-a1cad8ee… — после неё.
- Лимиты: Claude — прогноз исчерпания до 2026-11-29; Codex исчерпан до 2026-11-28 (второе ревью при посадке — agy-sonnet/DeepSeek или отказ с причиной).

## Точка продолжения
Последний handoff: `development/handoff/2026-11-28-1.md`. `/land` ветки worktree-agent-a4e34095… (пакет эталонов 0193+0199+0205), затем ветка карты технологий. Платформа (#607, долги) — полоса B.
