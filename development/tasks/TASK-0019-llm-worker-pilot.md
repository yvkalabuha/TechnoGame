---
id: TASK-0019
title: "Пилот внешней LLM (Qwen): tools/llm_worker"
status: REVIEW
level: light
milestone: MS-M0
agent: developer
category: tooling
priority: medium         # high | medium | low
needs_owner: false
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-03
approval_ref: "DECISIONS_LOG #110"
created: 2026-11-03
---

# TASK-0019 — Пилот внешней LLM (Qwen): tools/llm_worker

## Цель
Пилот дешёвого исполнителя: внешняя Qwen делает узкие задачи, детерминированные проверки решают приёмку, Claude получает утренний отчёт. Измерить, экономит ли это токены Claude без потери качества.

## Описание
Python-инструмент `tools/llm_worker/` по `docs/ai-agents/EXTERNAL_LLM_STRATEGY.md` §4–§8:
- провайдер: OpenAI-совместимый `/v1/chat/completions`, `enable_thinking=false` по умолчанию, `response_format: json_schema`; ключ из переменной окружения `LLM_WORKER_API_KEY`, в логи не пишется; повторы с паузой; обрыв соединения не теряет задачу;
- контракт задачи: JSON-схема с версией, валидация до постановки в очередь; неизменяем;
- очередь в файлах (`tools/llm_worker/queue/`, состояния CREATED…ACCEPTED/REJECTED/ESCALATED/FAILED, переходы с причиной); возобновление после перезапуска;
- пакет контекста: только `context_files` (с хешами) + выбранные уроки; исключения (`.env`, ключи, `~/.ssh`, `lab/results/`);
- проверки: модули для A1 (сверка имён с исходником), A2 (сравнение с эталонным скриптом проверки данных), B1 (`dotnet build`, `dotnet test`, мутации) в отдельном `git worktree` на ветке `llm/<task_id>`;
- цикл: попытка 0 + до 2 повторов; перед исправлением шаг «объясни, почему не прошло»; после каждой попытки — все проверки; регрессия → откат; стоп по §6;
- 2 независимых кандидата — только для B1;
- история раундов на диске; библиотека уроков `tools/llm_worker/lessons/` (пополняет Pilot);
- утренний отчёт `tools/llm_worker/reports/<date>.md` (принято / отклонено / провалено / эскалировано / не проверено отдельно; токены, время).
Выходные данные прогонов (`queue/`, `runs/`, `reports/`) — в `.gitignore`.

## Критерии приёмки
- [ ] Инструмент подключается к серверу, обрабатывает обрыв и тайм-аут без потери задачи (тест с поддельным провайдером).
- [ ] Контракт валидируется по схеме; неверный контракт не попадает в очередь.
- [ ] Worker не пишет вне `allowed_paths`; изменения кода только в worktree; `main` не меняется.
- [ ] Цикл доработки: не больше 2 повторов, повтор всех проверок, откат при регрессии (тест с поддельным провайдером).
- [ ] Отчёт отделяет принятое от непроверенного.
- [ ] Модульные тесты инструмента на поддельном провайдере (без сети) проходят.
- [ ] Задачи пилота A1, A2, B1 подготовлены как контракты (по одной), готовы к ночному прогону.

## Тесты
`python3 -m unittest discover tools/llm_worker/tests` — поддельный провайдер, без сети.

## Связанные документы
`docs/ai-agents/EXTERNAL_LLM_STRATEGY.md`; DECISIONS_LOG #109, #110.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно

## Результат

### Исправления по ревью (developer, 2026-11-03)
- HIGH: `sandbox.py` — все команды B1 через `bwrap` (`--ro-bind / /`, `--dev`, `--proc`, `--tmpfs /tmp`, запись только worktree и кэш NuGet, `tmpfs` над `~/.ssh` и `~/.config/local-llm`, файл ключа закрыт `/dev/null`, `--unshare-net` кроме `restore`, `--die-with-parent`, чистое окружение). Нет `bwrap` или не проходит self-test → `VerifierConfigError` → FAILED; запуска без песочницы нет. В контракт B1 добавлен `restore_cmd`, `build_cmd` с `--no-restore`.
- HIGH: `worktree.verify()` перед reset/clean/commit: `--show-toplevel` == каталог worktree, ветка == ожидаемая `llm/...`; иначе `GitError`. `WORKTREES_DIR` перенесён в `$XDG_CACHE_HOME|~/.cache/llm_worker/worktrees`. Переменные `GIT_*` не наследуются.
- MED: finalize внутри try; любая ошибка (включая `GitError`) → FAILED, `unverified`; задача никогда не остаётся RUNNING. Коммит в worktree с `--no-verify` и `core.hooksPath=/dev/null`.
- MED: HTTP 401/403 → `AuthError`: задача обратно в QUEUED, весь прогон останавливается.
- LOW: повторная проверка `write_allowed` по реальному пути после resolve (symlink); расширен `CONTEXT_EXCLUDE`; стоп «rolled back and quality did not improve»; в отчёте текст модели/компилятора в code span с пометкой «недоверенный»; пути мутантов проверяются `paths.normalize` в `validate_contract`.
- Проверка: `python3 -m unittest discover tools/llm_worker/tests` — 55 тестов OK (новые в `tests/test_safety.py`: поддельный `dotnet` в bwrap не пишет вне worktree и в «репозиторий», не читает ключ, не видит ключ в env, без сети; worktree без `.git` → `GitError`, main не тронут; symlink; finalize/непредвиденная ошибка → FAILED; 401 останавливает прогон). Реальный прогон B1 в песочнице на скретч-клоне: restore с сетью, build/test/7 мутантов без сети — 64 с, все жёсткие проверки пройдены, мутанты убиты; вариант с ошибкой компиляции → `build` FAIL с `error CS0103`.
