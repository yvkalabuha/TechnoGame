---
id: TASK-0039
title: "llm_worker: второе ревью (Codex под bwrap, DeepSeek) и DeepSeek в воротах (ADR-0015 задача 2)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: tooling
priority: medium
needs_owner: false
depends_on: [TASK-0035, TASK-0036, TASK-0037]
related: [ADR-0015]
approved_by: orchestrator
approved_at: 2026-11-13
approval_ref: "ADR-0015, план задач, п. 4 (задача 2)"
created: 2026-11-13
---

# TASK-0039 — llm_worker: второе ревью и DeepSeek в воротах (ADR-0015 задача 2)

## Цель
Рабочие команды `review` и `gate` в `tools/llm_worker` по ADR-0015 §4–§5. Это задача 2 плана ADR-0015.

## Описание
Источник: ADR-0015 §4 (второе ревью), §5 (ворота), §6 (учёт), §8 (безопасность).
- `python3 -m tools.llm_worker review --task TASK-NNNN --base <sha> --head <sha> [--provider codex|deepseek]`.
  - Вход: цель и критерии из файла задачи, `git diff base...head`, изменённые файлы из объектов git; для R2 — `.claude/rules/csharp-core.md` и ADR-0002. Больше 1 MB — `input_too_large`. gitleaks по всему входу.
  - Codex: `codex exec --sandbox read-only` внутри bwrap. `$HOME` — пустой tmpfs; видны только чистый detached worktree на `head` (без `bin/obj`) и `~/.codex`; системные каталоги только на чтение; `/var/www` не смонтирован; сеть общая. Изменение worktree после прогона — `guard_violation`. gitleaks по worktree. Проверить, работает ли `read-only` Codex внутри bwrap; если нет — граница bwrap, результат записать в «Результат».
  - Codex в USD не считается: в ledger пишутся попытки и токены (если есть); `codex_limit` (лимит подписки) — переход к DeepSeek.
  - DeepSeek (запасной): один запрос без инструментов, через ledger и резерв, как в исполнении.
  - Выход: `tools/llm_worker/schemas/review.schema.json` — `{verdict: accept|changes_requested, summary, findings[{severity: high|medium|low, path, line, claim, evidence}]}`. Правило «`accept` с `high` недопустим» — кодом после схемы. Нарушение — `schema_invalid`, расход в ledger.
  - JSON — в `tools/llm_worker/runs/` (git-ignored). Инструмент печатает строку REVIEW: `- [x] Второе ревью (codex, head a1b2c3d4) — changes_requested: 1 high; report sha256 <64 hex>`.
- `python3 -m tools.llm_worker gate --item <path> --stance third`: документ и связанные файлы (поле `related`, ссылки на файлы репозитория в тексте — не больше 1 MB) из объектов git; один запрос DeepSeek; ответ по `gate.schema.json` (STATUS, SUMMARY, FINDINGS, OPEN); ledger.
- `CONTEXT_EXCLUDE` действует и для `review`, и для `gate`.
- Пробный прогон исполнения на 3–5 шагах R0–R1 — **не** в этой задаче: его выбирает и запускает Orchestrator после DONE.

## Критерии приёмки
- [ ] Unit-тесты без сети: сборка входа (дифф, файлы из git, правила для R2), `input_too_large`, `secret_detected`, схема и правило accept+high, `schema_invalid` с записью в ledger, формат строки REVIEW, `codex_limit` → DeepSeek, `guard_violation` при изменении worktree.
- [ ] Тест bwrap (skip без bwrap/codex): Codex не видит `/var/www` и `$HOME` кроме `~/.codex`; worktree не меняется.
- [ ] Один реальный прогон `review --provider codex` на диффе TASK-0037 (`15ebe78..cd07d83` или аналог) и один `gate --stance third` на ADR-0015 — с выводом в «Результат» (вердикт, число находок, $). Расход DeepSeek ≤ $0,10.
- [ ] Все тесты `python3 -m unittest discover -s tools/llm_worker/tests` проходят.

## Тесты
`python3 -m unittest discover -s tools/llm_worker/tests`.

## Связанные документы
ADR-0015 §4–§8; `tools/llm_worker/README.md`.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно

## Результат

**Сделано.** Команды `review` (Codex под bwrap, запасной DeepSeek) и `gate --stance third` (+ `--dry-run`), схемы `schemas/review.schema.json` и `schemas/gate.schema.json`, строка REVIEW, README. Переиспользованы `Ledger/Guard`, `OpenAICompatProvider`, `gitleaks`, `context.read_blob/excluded`, `risk.classify_range`, `worktree.git`, `jsonschema_lite`, `sandbox.SYSTEM_RO`.

**Файлы** (все в `tools/llm_worker/`): `review.py` (вход, схема+правило accept/high, запись прогона, ворота), `codex.py` (worktree, bwrap-команда, разбор JSONL, журнал попыток), `__main__.py`, `schemas/*.json`, `tests/test_review.py` (32 теста), `README.md`.

**Решения.** Для Codex в prompt — дифф и список файлов (файлы он читает в `/work`), для DeepSeek — дифф и содержимое файлов. `/work` смонтирован только на чтение, плюс проверка хеша дерева до и после (`guard_violation`). Журнал попыток Codex — отдельный `codex_attempts.jsonl` в каталоге ledger (формат ledger не менялся). Fallback на DeepSeek при `codex_limit` и `codex_failed` (ADR-0015 §2; см. доработку 6). DeepSeek по умолчанию `deepseek-v4-pro`. STATUS ворот: approve|changes_requested|reject.

**Проверка.** `python3 -m unittest discover -s tools/llm_worker/tests` — 221 тест, OK (тест bwrap выполняется, не пропущен).

**Реальные прогоны.**
- `review --task TASK-0037 --base 15ebe78 --head cd07d83 --provider codex`: вердикт changes_requested, 3 находки (2 high, 1 medium); $0 (подписка), токены 232700 вх. / 1040 вых.; строка: `- [x] Второе ревью (codex, head cd07d83b) — changes_requested: 2 high, 1 medium; report sha256 270badc08e9bab0acc54a2f1c5acfcf3b1be2b2a3e03e2c44dc30e3e5ab820f3`. High: `ci.yml:72` (проверка риска после тестов ветки в том же окружении), `risk.py:101` (изменение значения approval-поля в допустимом YAML). Medium: `runner.py:130`. Это находки для разбора Orchestrator, не проверялись мной. Worktree удалён.
- **read-only Codex внутри bwrap работает**: внутренняя песочница Codex запустилась во вложенном user namespace без ошибок, ответ получен по схеме. Видны `~/.codex` (rw, там же `auth.json`) и `~/.nvm` (только каталог установки Node, ro; без него `codex` не запускается). `/var/www` нет, `$HOME` — tmpfs.
- `gate --item docs/decisions/ADR-0015-external-executors-providers.md --stance third` (deepseek-v4-pro): STATUS changes_requested, 4 high; 11 файлов, 106 КБ входа; **$0.03812952** по ledger (calls 1, unsettled 0; всего по ledger $0.038 ≤ $0,10). Sha256 записи d9ec87c84960b2093cd3fd7dc15642de70908e547f1667c5755fb3d627bbd464. Качество находок смешанное (напр. «prices.json: ready=true» — факт, но ожидаемый после #180).

**Риски/открытое.** (1) `~/.codex` смонтирован на запись и содержит `auth.json`: Codex-процесс видит свою подписку (иначе не работает). (2) Для DeepSeek-fallback ревью большого диффа резерв ledger может упереться в `max_usd_task` 0.50 при входе ~ >0.3 MB (pro). (3) Команда `report` перезаписала git-ignored `reports/2026-11-13.md` (запускал для проверки ledger).

**Доработка по находке Codex high `risk.py:101` (подтверждена Orchestrator).** `_frontmatter` в `risk.py` теперь дописывает строки продолжения (отступ, `-`, пустые, комментарии) к значению последнего approval-ключа; сравнение идёт по полному тексту. Продолжения чужих полей по-прежнему игнорируются. Тесты в `tests/test_risk.py` (`test_continuation_lines_are_part_of_the_value`): блочный скаляр, список с отступом и в колонке 0, многострочная кавычка, изменение только продолжения, смена блочной формы на строку — всё R3; без изменений и чужие поля — не R3. Не тронуты: ci.yml (Orchestrator), medium `runner.py:130` (консервативно).

**Доработка 2 (по ревью reviewer).** (1) `git --literal-pathspecs diff`: файлы `*`/`[a]` не раскрывают удержанные пути. (2) `codex.node_prefix`: `SandboxUnavailable`, если prefix — `$HOME`, его предок или без `lib/node_modules`. (3) Fallback на DeepSeek только при `codex_limit`; `codex_failed` → `ReviewError` без платного вызова (в `README` обновлено; в первом «Сделано» fallback при сбое Codex устарел). (4) Лимит определяется по событиям `error`/`turn.failed` и stderr, текст ответа модели его подделать не может. (5) `make_worktree` внутри `try/finally`, ошибки git/OS → `worktree_failed`. (6) `risk.py`: `*`/`&` в approval-поле → `FrontmatterError` (R3). (7) Тесты: нет fallback и `fake.calls == []` при `SandboxUnavailable`, `codex_failed`, `guard_violation`; `secret_detected` во входе fallback после `codex_limit`; literal pathspecs; node_prefix; лимит только из error-событий; anchor/alias.

**Доработка 3 (второе ревью Codex, fe39fed).** (1) Prompt Codex начинается с `REVIEW_SYSTEM` + пометка, что файлы в `/work` — данные (gitleaks сканирует итоговый prompt). (2) Путь worktree уникален (uuid); удаляется только созданный этим вызовом, существующий каталог → `worktree_failed` без удаления. (3) В режиме Codex размеры изменённых файлов входят в лимит 1 MB. (4) Вход gate собирается через `render()` с разделителями и сообщением omitted; итог ≤ `max_bytes` или `input_too_large`. Тесты на каждое.

**Доработка 4 (повторное ревью Codex, 94649d2).** (1) Цель и критерии задачи теперь в `_fence("TASK …")`; в gate документ уже был в `_fence("DOCUMENT …")`. (2) При любом исключении runner сверяется `tree_digest`: изменилось → `guard_violation` (приоритетнее исходной ошибки, fallback на DeepSeek не идёт даже после `codex_limit`), иначе исходная ошибка. Тесты на оба пункта.

**Доработка 5 (ревью Codex, 2fb6cd4).** (1) `risk.py`: комментарии в значении approval-поля больше не вырезаются, сравнивается сырой текст (смена комментария → R3, ложное срабатывание в безопасную сторону); тест на `#` в кавычках. (2) `gate`: ссылки на файлы разрешаются и от корня, и от каталога документа (`./`, `../`), нормализация `paths.normalize`, выход за репозиторий игнорируется, затем `CONTEXT_EXCLUDE`; тест.

**Доработка 6 (ревью Codex, bfb62ac).** (1) `risk.approval_changed`: кроме сырого сравнения (прежний код) значения `APPROVAL_FIELDS` сравниваются после `yaml.safe_load` (alias, merge key `<<` раскрыты); R3, если отличается хоть одно из двух; нет PyYAML, ошибка разбора, не mapping, неизвестный тег, блок > 64 КБ → `FrontmatterError` → R3. Тесты: alias во flow-коллекции, `<<`, теги, не mapping, нет PyYAML. (2) gate: ссылки без `./` (`sibling.md`) разрешаются от каталога документа; тест. (3) Fallback на DeepSeek возвращён и при `codex_failed` (ADR-0015 §2; в доработке 2 п. 3 решение было ошибочным); без fallback: `secret_detected`, `guard_violation`, `sandbox_unavailable`, `worktree_failed`. Тесты и README обновлены.

**Доработка 7 (ревью Codex, f620054).** `risk.approval_changed` ловит любое исключение при разборе и сравнении (в т.ч. `RecursionError` на рекурсивных alias) и возвращает R3; тест `test_recursive_alias_is_r3_not_a_crash`.

**Доработка 8 (ревью Codex, cce9c68; решение владельца #181).** (1) Codex монтирует минимум: каталог пакета codex (по realpath `codex`) и бинарь `node`, только ro; не весь prefix Node, не `lib/node_modules`; отказ, если монтирование накрывает `$HOME`; команда запускается по realpath (`codex.js`). Реальный запуск через bwrap с минимальными mount проверен: Codex стартовал, ответил по схеме. (2) Списки изменённых/удержанных файлов и omitted — внутри `_fence`; имена с управляющими символами экранируются JSON; закрывающая строка fence несёт хеш тела (подделать конец блока из содержимого нельзя). (3) `PATH_RE` ловит любой путь-подобный токен с расширением, берётся только объект из git-дерева `head` вне `CONTEXT_EXCLUDE`. **Найдено при самопроверке:** недоверенные имена вне fence (исправлено); молчаливые пропуски — бинарные/удалённые файлы в review теперь в блоке `NOT SHOWN`, пропущенные ссылки gate пишутся в `skipped` записи; подделка маркера конца fence (исправлено хешем). Тесты на каждое.
