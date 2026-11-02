# RR-004 — Механизмы Claude Code для фреймворка разработки

**ID:** RR-004
**Дата:** 2026-11-02
**Статус:** DRAFT
**Автор:** Research agent (Phase B, claude-code-guide), оформление — Orchestrator
**Источники проверены:** 2026-11-02, официальная документация code.claude.com (список в конце)

> Все пункты ниже — факты из документации, если не помечено иначе. Выводы для проекта помечены **[Предл.]**.

---

## 1. Краткий вывод

- Все нужные механизмы существуют: subagents, skills, path-scoped rules (`.claude/rules/`), hooks, project settings, worktrees.
- **Исправление к PHASE A (C2):** subagent **может** запускать другие subagents (по умолчанию до 3 уровней). Ограничение, из-за которого Orchestrator нельзя было сделать subagent, не существует. Предложение «Orchestrator = основная сессия» остаётся в силе, но по другой причине: основная сессия общается с владельцем и видит весь контекст.
- `CLAUDE.md` носит рекомендательный характер. Правила, которые должны выполняться всегда, надо реализовывать через hooks и deny-правила.
- Главная статья расхода сейчас — стоимость токенов, а не размер окна контекста (окна большие, compaction автоматический).

---

## 2. Subagents — `.claude/agents/*.md`

- Markdown + YAML frontmatter; тело файла = system prompt. Обязательны только `name` и `description`. Неизвестные поля молча игнорируются (проверять через `claude --debug`).
- Поля: `name`, `description`, `tools`, `disallowedTools`, `model`, `permissionMode`, `maxTurns`, `skills`, `mcpServers`, `hooks`, `memory`, `background`, `omitClaudeMd`, `effort`, `isolation`, `color`, `initialPrompt`.
- `model`: `sonnet` / `opus` / `haiku` / `fable` / полный ID / `inherit`.
- Делегирование выбирается по `description`.
- Что получает subagent: новое окно контекста; system prompt из тела файла; prompt делегирования; всю иерархию `CLAUDE.md` (кроме Explore/Plan или `omitClaudeMd: true`); git status; skills из поля `skills` (полностью). **Не получает** разговор основной сессии и прочитанные ею файлы.
- `memory: project` — память агента в `.claude/agent-memory/<name>/`, можно коммитить.
- `isolation: worktree` — работа во временном git worktree.
- Вложенность: до 3 уровней (`CLAUDE_CODE_MAX_SUBAGENT_SPAWN_DEPTH`). Запрет вложенности для конкретного агента: убрать `Agent` из `tools`.
- Параллельно: до 20 subagents (`CLAUDE_CODE_MAX_CONCURRENT_SUBAGENTS`).
- Hooks во frontmatter агента: только `PreToolUse`, `PostToolUse`, `Stop`.
- Agent teams (`SendMessage`, общий список задач) — **экспериментальная** функция, выключена по умолчанию, дороже по токенам. **[Предл.]** Не использовать.

## 3. Skills — `.claude/skills/<name>/SKILL.md`

- Поля: `name`, `description`, `when_to_use`, `argument-hint`, `arguments`, `disable-model-invocation`, `user-invocable`, `allowed-tools`, `disallowed-tools`, `model`, `effort`, `context: fork`, `agent`, `hooks`, `paths`.
- Progressive disclosure: до вызова в контексте только `name` + `description` (+ `when_to_use`, всего до 1536 символов). Полный `SKILL.md` грузится при вызове `/name` или автоматическом совпадении. Вспомогательные файлы читаются только по ссылке из `SKILL.md`.
- `SKILL.md` — до 500 строк.
- Аргументы: `$ARGUMENTS`, `$0`, `$1`, именованные.
- `.claude/commands/` — устаревший формат, используем skills.
- `disable-model-invocation: true` — skill вызывается только владельцем вручную. **[Предл.]** Для release и других действий с побочными эффектами.

## 4. Rules и CLAUDE.md

- `.claude/rules/*.md` существует; рекурсивный поиск по подпапкам.
- Без `paths` — грузится при старте. С `paths` (glob) — грузится только при Read/Write/Edit подходящего файла. **[Предл.]** Правила кода `game/**`, правила данных `data/**`, правила документов `docs/**`.
- `CLAUDE.md`: целевой размер — до 200 строк. Подпапочные `CLAUDE.md` грузятся по требованию.
- `@imports` не экономят контекст: импортированный файл грузится вместе с родителем.
- `CLAUDE.local.md` — личный, в `.gitignore`.

## 5. Hooks

- Конфигурация в `.claude/settings.json` (коммитится). Скрипты принято класть в `.claude/hooks/` и вызывать как `${CLAUDE_PROJECT_DIR}/.claude/hooks/<name>.sh`.
- Ключевые события: `SessionStart`, `UserPromptSubmit`, `PreToolUse` (может блокировать), `PostToolUse`, `Stop`, `SubagentStop`, `PreCompact`, `SessionEnd`.
- Exit code 2 = блокировка, stderr уходит в Claude. Другие ненулевые коды не блокируют. При сбое или timeout hook **пропускает** действие, если не задано `onFailure: "block"`.
- `SessionStart` может добавить текст в контекст (`additionalContext`). Текст должен быть фактами, а не командами.
- Hooks выполняют произвольные команды с правами пользователя. Это код, его нужно ревьюить.

**[Предл.] Кандидаты hooks для проекта:**
1. `SessionStart`: вывести краткую сводку из `CURRENT_STATE.md` (текущий milestone, активные задачи).
2. `PostToolUse` на `Edit|Write` в `data/**`: валидация схем данных (когда появится валидатор).
3. `PreToolUse`: блокировать правку утверждённых ADR (статус ACCEPTED) — только через новый ADR, который помечает старый superseded.
4. `Stop`: напоминание обновить handoff, если менялись файлы, а `CURRENT_STATE.md` нет.

## 6. Settings и permissions

- `.claude/settings.json` — общий, коммитится. `.claude/settings.local.json` — личный, не коммитится.
- Коммитятся: `CLAUDE.md`, `.claude/rules/`, `.claude/skills/`, `.claude/agents/`, `.claude/settings.json`, `.claude/agent-memory/`.
- Не коммитятся: `settings.local.json`, `CLAUDE.local.md`, `agent-memory-local/`.
- Порядок оценки правил: deny → ask → allow.
- Для путей использовать `Edit(...)` и `Read(...)`. Правила `Write(...)` не срабатывают.
- Deny-правила Read/Edit не мешают подпроцессу (например, Python-скрипту) читать файл.

## 7. Экономия токенов

- `CLAUDE.md` — только то, что нельзя вывести из кода; без описания структуры папок.
- Повторяемые процедуры — в skills: они почти ничего не стоят до вызова.
- Subagents изолируют шумный вывод, но сами стоят токенов. Просить короткие результаты. Для механической работы — `model: haiku`.
- Read-only роли (Critic, Reviewer): `tools: Read, Grep, Glob`.
- `/usage` — расход по skills, subagents и MCP.
- `/clear` между несвязанными задачами, `/compact <focus>` для сжатия.

## 8. Выводы для дизайна фреймворка (PHASE C) [Предл.]

1. Orchestrator = основная сессия + `CLAUDE.md`. Subagents не видят её разговор, поэтому вся передача — через prompt и файлы (`CURRENT_STATE.md`, handoff, git).
2. Critic и Reviewer — свежие read-only subagents, которые не видели рассуждений автора (рекомендованный документацией паттерн ревью).
3. Developer — `isolation: worktree` для крупных изменений. Нужен `worktree.baseRef: head`, если работа строится на незапушенных коммитах.
4. Обязательные проверки — hooks и deny-правила, а не текст в `CLAUDE.md`.
5. Handoff, release, session-start — skills; release с `disable-model-invocation: true`.
6. Agent teams не включать.
7. Язык файлов агентов и skills — английский (решение владельца Q4).

## 9. Открытые вопросы

- Лимит параллельных tool calls за один ход — в документации не найден (UNVERIFIED). На дизайн не влияет.

## 10. Источники (доступ 2026-11-02)

- https://code.claude.com/docs/en/sub-agents.md
- https://code.claude.com/docs/en/skills.md
- https://code.claude.com/docs/en/memory.md
- https://code.claude.com/docs/en/hooks.md
- https://code.claude.com/docs/en/hooks-guide.md
- https://code.claude.com/docs/en/permissions.md
- https://code.claude.com/docs/en/claude-directory.md
- https://code.claude.com/docs/en/best-practices.md
- https://code.claude.com/docs/en/agent-teams.md
- https://code.claude.com/docs/en/worktrees.md
