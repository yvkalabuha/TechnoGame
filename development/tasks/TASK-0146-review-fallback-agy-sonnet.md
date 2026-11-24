---
id: TASK-0146
title: "Second review fallback to Sonnet 5.5 through agy when the Codex limit is exhausted (#456)"
status: IN_PROGRESS
level: light
milestone: MS-M05
agent: developer
category: tools
priority: medium
needs_owner: false
depends_on: []
related: [TASK-0141, TASK-0145]
approved_by: orchestrator
approved_at: 2026-11-24
approval_ref: "owner decision #456 (chat 2026-11-24); light task approved by Orchestrator"
created: 2026-11-24
---

# TASK-0146 — Запасной второй ревьюер: Sonnet 5.5 через agy при лимите Codex (#456)

## Цель
Решение владельца #456: если у Codex кончился лимит, второе ревью (ADR-0015) делает Claude Sonnet 5.5 через Antigravity CLI; после сброса лимита — снова Codex.

## Факты
- Лимит Codex общий для ревью и эскизов; 2026-11-24 13:44 `codex exec` вернул «You've hit your usage limit … try again at 3:44 PM» (`turn.failed`).
- agy: `--model claude-sonnet-5-5-{low,medium,high}`, `--json-schema` (структурный ответ), `--settings` с `permissions.deny` (`command(*)`, `write_file(*)`, `read_url(*)`, `execute_url(*)`, `mcp(*)`, как в `sketch.py`); у группы Claude/GPT в agy своя квота (экран владельца: 100 %).

## Описание
1. `tools/llm_worker` review: распознать лимит Codex (`usage limit`, `try again at`) → провайдер `agy-sonnet` с той же схемой находок и тем же промптом/диффом; запись: `provider: agy-sonnet`, модель, причина замены, время сброса Codex.
2. Запоминать время сброса (файл состояния в `~/.cache/singularity/`); до него сразу идти в agy, после — снова Codex. Ручной флаг `--provider codex|agy-sonnet|auto` (по умолчанию auto).
3. agy только на чтение: deny-правила, без `--dangerously-skip-permissions`, env по allowlist, чтение файлов только в рабочей копии ревью; ключи не передаются; проверка инструментов по потоку и transcript, как в `sketch.py` (переиспользовать функции).
4. `land.py` печатает, кто сделал второе ревью.

## Критерии приёмки
- [ ] Unit-тесты: распознавание лимита Codex, выбор провайдера по времени сброса, разбор ответа agy по схеме, отказ при запрещённом инструменте.
- [ ] Живой прогон `review` через agy-sonnet на малом диффе.
- [ ] Ревьюер APPROVE; `verify.py --full`.

## Результат
- `tools/llm_worker/limitstate.py` (новый): разбор «try again at H:MM AM/PM» / «in N hours M minutes», файл состояния `~/.cache/singularity/codex-limit.json` (0600, без времени в тексте — 30 мин), `limited_until`/`clear`.
- `tools/llm_worker/agyreview.py` (новый): `agy -p --model claude-sonnet-5-5-medium --mode plan --json-schema`, `sketch.SETTINGS` (deny command/write_file/read_url/execute_url/mcp), без `--dangerously-skip-permissions`, env по allowlist `sketch.safe_env`, cwd = чистая рабочая копия ревью; проверка потока и transcript через функции `sketch.py` (параметризованы набором инструментов: без generate_image и суб-агентов; пути только в рабочей копии и brain-папках запуска); нет transcript — отказ.
- `review.py`: `--provider auto|codex|agy-sonnet|deepseek` (по умолчанию auto: Codex → agy-sonnet → DeepSeek; пока лимит Codex не сброшен, сразу agy). `codex` оставляет прежнюю цепочку Codex → DeepSeek и состояние не трогает. Запись: `provider`, `model`, `fallbacks[]`, `codex_reset_at`. Запрещённый инструмент → `ReviewError agent_tool_violation`, без запасного провайдера.
- `land.py` печатает «second review by <provider> (<model>), replacing codex: <reason>» и кладёт модель в строку Second-Review.
- Тесты: `tools/llm_worker/tests/test_agy_review.py` (11): лимит и время сброса, выбор по состоянию, разбор ответа agy, отказ при запрещённом инструменте/пути/без transcript, цепочки. Проверено: без проверки нарушений падают 6, без чтения состояния — 1.
- Живой прогон (diff в одну строку, Codex под лимитом до 15:44): первый — `codex unavailable (codex_limit): using agy-sonnet`, сброс 15:44 записан; agy вызвал run_command (отклонён правилами, но попытка = нарушение, ревью отказано) → усилен промпт. Второй — запись `review-TASK-0146-656cc7a2-agy-sonnet.json`, провайдер agy-sonnet (claude-sonnet-5-5-medium), codex_limit_cached, exit 0.
- Правки после ревью: (1) явный `--provider agy-sonnet` — только agy, при сбое/квоте ошибка `agy_failed`/`agy_limit`, DeepSeek не вызывается (тест); (2) чтение ограничено правилами agy: проба на реальном agy 1.5.16 показала, что deny бьёт allow, а путь без правила НЕ запрещается, поэтому allow-список невозможен; вместо него `sketch.scoped_deny_rules` запрещает `read_file`/`view_file` для всего, кроме рабочей копии и brain-папки (обход от /, соседи каждого предка). Проверено на канарейках: чтение внутри разрешено; снаружи, через симлинк, через `..` и `~` — отказ. В режиме plan у agy только read_file (list_dir/grep_search/find_by_name нет). Правила пишутся в временный файл (около 900 правил); то же применено к `sketch.py` (рабочая папка теперь в `~/.cache/llm_worker/agy-ws`); (3) время сброса Codex ограничено 24 ч.
- Живой повторный прогон после правки (2) невозможен: квота Claude в agy исчерпана до ~18:45 (fail closed подтверждён: `error: agy_limit`, DeepSeek не вызван).
- Правки после Codex-ревью: промпт идёт в agy через stdin (`--input-format stream-json`, сообщение `{"event":"user",...}`), а не аргументом (E2BIG на больших ревью); успешный поток без conversation id — отказ (fail closed); кэш лимита пишется через уникальный временный файл + `os.replace`, ошибка записи не останавливает ревью; промпт для agy строится заново (имя папки подставляется до подсчёта хэшей данных, без замены текста после); при слиянии `main` конфликтов не было.
- Остаточный риск (проба 2026-11-24, agy 1.5.16): шаблонов внутри сегмента пути нет (`run-[!a]*` не работает), чтение без правила разрешено во всех режимах (default, accept-edits, auto), deny сильнее allow. Поэтому папку, созданную рядом с рабочей копией во время запуска, правилами закрыть нельзя; послепроверка отклонит результат, но модель файл уже увидела. Полное решение — запуск agy внутри bwrap (как Codex), отдельная задача.
- Живой прогон после всех правок: Sonnet недоступен (квота Claude в agy до ~18:45), поэтому полный конвейер (stdin, правила чтения, проверка потока и transcript, запись) прогнан на `gemini-3.8-flash-medium`: accept, 0 замечаний. Прогон на Sonnet 5.5 остаётся не выполненным.
