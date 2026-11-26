---
id: TASK-0036
title: "llm_worker: песочница, gitleaks, контекст из git (ADR-0015 1b)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
- [x] Тест: из bwrap нельзя прочитать `/var/www/gameDev/.mcp.json`, `lab/results/`, `~/.git-credentials`; `dotnet --info` работает.
- [x] Тест: при build/test запись в `~/.nuget/packages` не проходит.
- [x] Тест: секрет в контексте или в `error_lines` останавливает отправку.
- [x] Тест: неотслеживаемый файл в рабочем дереве не попадает в пакет.
- [x] Все тесты `tools/llm_worker/tests` зелёные (кроме DEBT-0024, если TASK-0037 ещё не закрыта).

## Тесты
`python3 -m unittest discover tools/llm_worker/tests`.

## Связанные документы
ADR-0015; DEBT-0023.

## REVIEW
- [x] Code review (reviewer, 2026-11-13) — reviewer: 2 замечания (gitleaks:allow, .gitleaksignore) исправлены; test-engineer: реальный restore падал (corefx) — исправлено, интеграционный тест dotnet в песочнице проходит; повторная сверка Orchestrator 2026-11-13, 189 тестов OK
- [x] Тесты (test-engineer, 2026-11-13) — проходят, добавлен `test_review_0036_0037.py`

## Результат

**Сделано.**
- `sandbox.py`: без `--ro-bind / /`. Порядок: системные `/usr`, `/etc/*` (список), `/bin`.. — только чтение; tmpfs `$HOME`; `~/.dotnet` ro; `~/.nuget/packages` ro (rw при `network=True`, то есть на шаге `restore`); worktree rw; `/var/www` не монтируется. Убраны `default_hidden` и `default_writable`.
- `gitleaks.py` (новый): `scan_text`, `scan_dir`; явный конфиг `useDefault` (конфиг дерева и `GITLEAKS_*` не ослабляют правила); находка даёт `SecretDetected` (только rule и строка), сбой или нет бинаря — `GitleaksUnavailable` (подкласс `SandboxUnavailable`).
- `provider.py`: для платного профиля весь `messages` (контекст и `error_lines` повтора) сканируется до резерва и запроса. Ключ DeepSeek — `load_strict_key`: только файл, 0600, владелец, не симлинк, не пуст, иначе `KeyFileError`; env отключён (`Profile.strict_key`).
- `context.py`: файлы читаются из объектов git (`git ls-tree` + `cat-file`, параметр `head="HEAD"`), симлинки и неотслеживаемые файлы отказ. `config.py`: `CONTEXT_EXCLUDE` + `.mcp.json*`, `**/.mcp.json*`, `**/.git/**`; `DEEPSEEK_KEY_FILE`.
- gitleaks: релиз v8.30.1 `gitleaks_8.30.1_linux_x64.tar.gz`, sha256 архива `551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb` совпал с `gitleaks_8.30.1_checksums.txt`; бинарь в архиве и уже стоявший `~/.local/bin/gitleaks` совпадают, sha256 `88f91962aa2f93ac6ab281d553b9e125f5197bbbce38f9f2437f7299c32e5509`. Повторная установка не потребовалась.

**Файлы.** `tools/llm_worker/{sandbox,gitleaks,provider,context,config}.py`, `README.md`; тесты `tests/test_secrets_context.py` (новый), `test_safety.py`, `test_provider_context.py`, `test_ledger.py`, `test_ledger_invariants.py` (в двух последних gitleaks замокан: иначе property-тест тормозит).

**Проверка.** `python3 -m unittest discover tools/llm_worker/tests`: 162 теста OK (включая `dotnet --info` в bwrap и `test_pilot_contracts_are_valid`). Реальный `dotnet restore/build/test` в новой песочнице не гонялся.

**Доработка после ревью.**
- HIGH (реальный dotnet): `~/.dotnet` теперь tmpfs с ro-bind каждого элемента, кроме `corefx`; `corefx` — отдельный tmpfs. Запись уходит в память, на хост ничего не пишется, mountpoint не нужен на хосте. Тест `SandboxDotnetIntegrationTest`: реальные `dotnet restore` (сеть), `build`, `test` крошечного проекта xunit в песочнице (пропуск без bwrap, dotnet или кэша пакетов). Сначала воспроизведён MSB4018, затем проходит.
- HIGH (gitleaks): в провайдере сканируются сырые `content` сообщений через `\n`, без JSON-экранирования; добавлен `--ignore-gitleaks-allow`. Тесты: `gitleaks:allow` + секрет блокирует (текст, каталог, промпт провайдера).
- MEDIUM: `cwd` = пустой tmp, `--gitleaks-ignore-path` на пустой каталог; `scan_dir` сканирует копию дерева без `.gitleaksignore` (gitleaks читает его из сканируемого каталога в любом случае). Тесты: ignore-файл в cwd и в каталоге не гасит находку.
- LOW: `extra_ro` монтируется после tmpfs `$HOME`; проверка порядка в `SandboxArgvTest`.
