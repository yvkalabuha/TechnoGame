# DECISIONS LOG

Журнал решений владельца (#598, #599). Только добавление записей; старые не меняются. Отменённое решение помечается новой записью со ссылкой, а в старой строке — пометкой SUPERSEDED (там, где строка лежит сейчас).

- **Новые строки добавляются сюда**, в конец таблицы ниже. `python3 tools/development/kblog.py rollover` переносит их в месячные файлы `docs/decisions/log/ГГГГ-ММ.md` (строки #1–#400 — в архив) и оставляет здесь последние 12.
- Поиск без чтения файлов: `python3 tools/development/kb_db.py q decisions text~слово`. Все инструменты (kb_check, kb_guard, kb_db, metrics, land.py) читают все файлы журнала.

Файлы журнала:
<!-- kblog:index:start -->
- [archive-0001-0400.md](log/archive-0001-0400.md) — #1–#400, строк: 400
- [2026-11.md](log/2026-11.md) — #401–#600, строк: 200
<!-- kblog:index:end -->

| # | Дата | Решение | Ссылка |
|---|---|---|---|
| 601 | 2026-11-26 | Разовое разрешение: агент (general-purpose по роли developer) правит защитный фильтр команд .claude/hooks/guard_bash.py и тесты к нему по замечаниям ревью TASK-0201 (ложные отказы после nice/timeout/xargs/env, heredoc, kbtest_*); другие файлы .claude/ не трогает; изменения показать владельцу перед посадкой | владелец, AskUserQuestion | development/tasks/TASK-0201-platform-redis.md |
| 602 | 2026-11-26 | Влить TASK-0201 (Redis, исправленный фильтр команд guard_bash, reviewer APPROVE на c709b559); три мелкие лазейки фильтра — в долг DEBT-0124 | владелец, AskUserQuestion | development/tasks/TASK-0201-platform-redis.md |
| 603 | 2026-11-26 | Разрешение на TASK-0208 и TASK-0209: агент (general-purpose по роли developer) правит хуки в .claude/hooks только по пунктам этих задач, другие файлы .claude/ не трогает; рецензент и второе ревью; изменения хуков показать владельцу перед посадкой | владелец, AskUserQuestion | development/tasks/TASK-0208-kb-upkeep.md |
| 604 | 2026-11-26 | Уточнение #597: расхождение факта с кодом при коммите в ветке агента — предупреждение (ошибка, если в коммите есть development/FACTS.yaml); в CI и при посадке — ошибка (land.py отказывает ветке с расхождением). Влить TASK-0208 (хуки kb_guard, guard_dispatch показаны владельцу по #603) | владелец, AskUserQuestion (A22) | development/tasks/TASK-0208-kb-upkeep.md |
| 605 | 2026-11-26 | Внедрить в платформу сервис лимитов llmlimits (devLLMex, 127.0.0.1:8790, ADR-yk-0007): тонкий клиент в tools/development (только loopback, таймаут 1,5 с, без сервиса — как сейчас), карта «потребитель → окна лимитов» своя в gameDev, цепочка второго ревью и прочие вызовы llm_worker пропускают исчерпанного провайдера по данным сервиса, capacity.py показывает лимиты. Политика — только предупреждать: при прогнозе «Claude не хватит до обновления» темп и модели агентов не меняются автоматически | владелец, AskUserQuestion | docs/architecture/platform-llmlimits.md |
| 606 | 2026-11-26 | Разрешение: агент добавляет в хук начала сессии (.claude/hooks/session_context.py) одну строку о лимитах моделей из llmlimits; без сервиса строки нет; другие файлы .claude/ не трогает; изменения показать владельцу перед посадкой | владелец, AskUserQuestion | docs/architecture/platform-llmlimits.md |
| 607 | 2026-11-26 | Пределы размера (#600) к 2026-12-03: CLAUDE.md, CURRENT_STATE.md, PLAN.md ужать (старое — в архив); UI_DESIGN_SYSTEM.md и M_PLAY_LOOP.md разделить на части по темам с оглавлением, смысл не менять | владелец, AskUserQuestion | development/tasks/TASK-0209-kb-structure.md |
| 608 | 2026-11-26 | Уточнение #600: предел месячного журнала решений — 150 КБ (журнал читают поиском по базе, не целиком); месяц остаётся одним файлом (#599) | владелец, AskUserQuestion | development/tasks/TASK-0209-kb-structure.md |
| 609 | 2026-11-26 | TASK-0209 вливать двумя частями: (1) код и перенос журналов со вторым ревью, (2) теги в шапках документов; рецензент смотрит обе | владелец, AskUserQuestion | development/tasks/TASK-0209-kb-structure.md |
| 610 | 2026-11-26 | Перенос в архив (kblog.py rollover) запускает land.py автоматически после каждой посадки | владелец, AskUserQuestion | development/tasks/TASK-0209-kb-structure.md |
| 611 | 2026-11-26 | Влить TASK-0209 (хуки kb_guard и session_context показаны владельцу по #603); часть 2 (только строки tags: в 610 документах) — без внешнего второго ревью: вход 10,7 МБ больше предела 4 МБ, reviewer APPROVE | владелец, AskUserQuestion | development/tasks/TASK-0209-kb-structure.md |
| 612 | 2026-11-26 | Влить TASK-0211 часть 2 (строка лимитов в хуке начала сессии, показана владельцу по #606). Сбой сервиса llmlimits (OverflowError в forecast.py при почти нулевом расходе) владелец чинит сам в devLLMex; до починки лимиты — «нет данных» | владелец, AskUserQuestion | development/tasks/TASK-0211-llm-limits.md |
| 613 | 2026-11-26 | Пауза разработки продукта (#595) остаётся; ветки worktree-agent-a1cad8ee… (0161/0163/0167/0192/0194) и worktree-agent-a4e34095… (0193/0199/0205) ждут | владелец, AskUserQuestion | development/OPEN_QUESTIONS.md (A21) |
| 614 | 2026-11-26 | Пачку кадров клиента не снимать сейчас; позже, по слову владельца | владелец, AskUserQuestion | development/OPEN_QUESTIONS.md (A8, A15, A18) |
| 615 | 2026-11-26 | Закрытые при разборе ветки и незакоммиченные правки удалённых worktree сохранены метками archive/* (24 шт.); отправить метки на GitHub как резервную копию | владелец, AskUserQuestion | — |
| 616 | 2026-11-26 | Предложения Gemini для карты знаний (чертёж-«синька», тиснёный штамп, легенда на кальке, иерархия толщины линий) взять все в следующий раунд карты | владелец, AskUserQuestion | docs/ui/reviews/task-0192-visual.md |
