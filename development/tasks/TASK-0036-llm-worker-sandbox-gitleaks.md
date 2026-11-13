---
id: TASK-0036
title: "llm_worker: песочница, gitleaks, контекст из git (ADR-0015 1b)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0035]
related: [ADR-0015]
approved_by: owner
approved_at: 2026-11-13
approval_ref: "DECISIONS_LOG #176 (ADR-0015, план 1b)"
created: 2026-11-13
---

# TASK-0036 — llm_worker: песочница, gitleaks, контекст из git (ADR-0015 1b)

## Цель
Закрыть каналы утечки данных `llm_worker` до первого внешнего вызова. Это задача 1b ADR-0015.

## Описание
Источник: ADR-0015 §8 (безопасность данных). Долг: DEBT-0023.
- Порядок монтирования bwrap: tmpfs `$HOME` → ro `~/.dotnet` → ro `~/.nuget/packages` (rw только на шаге restore) → rw worktree; `/var/www` не монтируется.
- gitleaks: релиз с GitHub в `~/.local/bin`, проверка контрольной суммы, без root (#176). Проверка по входу провайдера и по `error_lines` перед повторным промптом; находка — отказ.
- `CONTEXT_EXCLUDE`: добавить `.mcp.json`, `**/.git/**`.
- Пакет контекста читается из объектов git (`git show <head>:path`), а не из рабочего дерева.
- Отдельный файл ключа DeepSeek (0600, вне репозитория), значения по умолчанию конфига — не под Qwen.

## Критерии приёмки
- [ ] Тест: из bwrap нельзя прочитать `/var/www/gameDev/.mcp.json`, `lab/results/`, `~/.git-credentials`; `dotnet --info` работает.
- [ ] Тест: при build/test запись в `~/.nuget/packages` не проходит.
- [ ] Тест: секрет в контексте или в `error_lines` останавливает отправку.
- [ ] Тест: неотслеживаемый файл в рабочем дереве не попадает в пакет.
- [ ] Все тесты `tools/llm_worker/tests` зелёные (кроме DEBT-0024, если TASK-0037 ещё не закрыта).

## Тесты
`python3 -m unittest discover tools/llm_worker/tests`.

## Связанные документы
ADR-0015; DEBT-0023.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно

## Результат
