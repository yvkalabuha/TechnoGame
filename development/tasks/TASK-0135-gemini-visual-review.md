---
id: TASK-0135
title: "Gemini visual second opinion in llm_worker (free tier, #441)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M05
agent: developer
category: tooling
priority: medium
needs_owner: false
depends_on: []
related: [TASK-0132]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "#441"
review_ref: "reviewer: APPROVE 61ad9fe + deltas; codex: 3 returns, waived by #432 → DEBT-0081; live runs gemini-flash-latest, gemini-4-hydrogen; landed 14fafb0"
created: 2026-11-24
---

# TASK-0135 — Gemini как второе мнение по кадрам интерфейса (#441)

## Цель
Gemini смотрит те же кадры 1920x1080 при 100 %, что и ui-art-director, и независимо оценивает их по критериям §12 `docs/ui/UI_DESIGN_SYSTEM.md` (C1–C8, A1–A12). Вердикт PASS/FAIL остаётся за арт-директором; замечания Gemini попадают в обзор задачи.

## Описание
- Профиль `gemini-*` в `tools/llm_worker/config.py` через OpenAI-совместимую точку Google (`https://generativelanguage.googleapis.com/v1beta/openai/chat/completions`); имя модели — параметр, по умолчанию проверить списком моделей (`GET /v1beta/models`) при первом запуске с ключом и записать дату проверки в комментарий, как у DeepSeek.
- Ключ только из файла `~/.config/singularity/gemini-api-key` (0600, вне репозитория), `strict_key`, как у DeepSeek.
- Только бесплатный уровень: `paid=False`, в ledger стоимость 0, но счётчик запросов в сутки и в минуту с отказом при превышении; ответ 429 — пауза и повтор с потолком.
- Картинки: сообщения с `image_url` (data URI, PNG или JPEG ≤ лимита API), несколько кадров в одном запросе.
- Команда `python3 -m tools.llm_worker visual --task TASK-NNNN --frames <png...> [--criteria docs/ui/UI_DESIGN_SYSTEM.md#12]`: ответ JSON по схеме (вердикт-мнение, находки: критерий, кадр, место, серьёзность, исправление), запись в `tools/llm_worker/runs/visual-<task>-<hash>-gemini.json` и Markdown-сводка рядом.
- Перед отправкой: gitleaks на текст; кадры — только из `shots-out/` (игровые кадры, без секретов). Бесплатный уровень Google может использовать данные для улучшения продуктов — в README записать.
- Документация: `tools/llm_worker/README.md`, строка в `.claude/skills/` не меняется до проверки пользы.

## Критерии приёмки
- [ ] Модульные тесты без сети: сборка запроса с картинками, разбор ответа, отказ без ключа, лимит запросов, 429.
- [ ] Живой прогон на кадрах TASK-0126 после того, как владелец положит ключ; запись в задаче.
- [ ] Ревьюер APPROVE; второе ревью Codex (tools/**).

## Результат
[Факт] Реализовано в `tools/llm_worker` (ветка worktree): команда `visual` (режимы `criteria` и `design`), профиль `gemini-flash` (`free_public`, ключ только из `~/.config/singularity/gemini-api-key`), `visual.py`, схемы `visual*.schema.json`, 36 тестов без сети в `tests/test_visual.py`, README. Модель по умолчанию `gemini-4-hydrogen`. Бесплатность зависит от проекта Google без биллинга; инструмент пишет расход токенов, цен не знает.

Живые прогоны 2026-11-24 на кадрах TASK-0126 (записи `tools/llm_worker/runs/visual-TASK-0126-<hash>-gemini.*`, копии в `docs/ui/reviews/gemini-*-2026-11-24-*.md`):
- критерии, `d26b368b`, gemini-flash-latest: mixed, 2 medium, 2 low;
- дизайн, `08c33cd0`, flash: mixed;
- дизайн, `fc36b812`, gemini-4-hydrogen: fail, 3 high, 5 medium, 3 low.

Проверка: `verify.py --full` OK. Ревью reviewer APPROVE 61ad9fe; Codex-ревью вернуло код 1 (замечания учтены в этом коммите).
