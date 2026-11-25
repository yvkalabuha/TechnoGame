# llm_worker — пилот внешней LLM (TASK-0019)

Узкие задачи для Qwen на сервере владельца; приёмку решают детерминированные проверки, Claude читает
утренний отчёт. Основание: `docs/ai-agents/EXTERNAL_LLM_STRATEGY.md` §4–§8, DECISIONS_LOG #109, #110.
Только stdlib Python 3.

## Ночной прогон
```bash
cd /var/www/gameDev
# ключ: LLM_WORKER_API_KEY или файл LLM_WORKER_API_KEY_FILE (по умолчанию ~/.config/local-llm/key)
python3 -m tools.llm_worker validate tools/llm_worker/contracts/*.json
python3 -m tools.llm_worker submit tools/llm_worker/contracts/*.json
mkdir -p tools/llm_worker/runs && nohup python3 -m tools.llm_worker run > tools/llm_worker/runs/run.log 2>&1 &
# утром
python3 -m tools.llm_worker status
cat tools/llm_worker/reports/$(date +%F).md
```
`run` обрабатывает всё в состоянии QUEUED и в конце пишет отчёт. Если сервер недоступен, задача
возвращается в QUEUED, через `--outage-wait` (300 с) попытка повторяется; после `--max-outages` (12)
подряд прогон останавливается (код 3). Повторный `run` продолжает с последнего сохранённого шага.
Флаги: `--url`, `--model` (или `LLM_WORKER_URL`, `LLM_WORKER_MODEL`), `--queue`, `--runs`, `--reports`.
Ключ не пишется в логи, записи раундов и отчёты.

## Как устроено
| Модуль | Что делает |
|---|---|
| `contract.py` | схема контракта (версия 1) и смысловые правила; неверный контракт в очередь не попадает |
| `taskqueue.py` | `queue/<task_id>/{contract.json,state.json}`: CREATED → QUEUED → RUNNING → ACCEPTED / REJECTED / ESCALATED / FAILED; каждый переход с причиной; хеш контракта проверяется при чтении |
| `context.py` | пакет контекста: только `context_files` (sha256 должен совпасть) + уроки; исключения в `config.CONTEXT_EXCLUDE` |
| `provider.py` | `/v1/chat/completions`, `enable_thinking=false`, `response_format: json_schema`, тайм-аут 3600 с, повторы с паузой |
| `loop.py` | попытка 0 + до 2 повторов; перед исправлением шаг «объясни» (`explain`); после каждой попытки все проверки заново; новый жёсткий провал → откат к лучшему раунду; стоп: всё пройдено / повторы исчерпаны / тот же провал дважды / качество не выросло / нарушена область / бюджет |
| `runner.py` | задача целиком; для B1 — 2 независимых кандидата (`seed` 1 и 2), выбирается лучший |
| `verifiers/` | A1 — сверка имён с исходником (regex, не Roslyn); A2 — сравнение с `reference/nodes_check.py`; B1 — `dotnet build`, `dotnet test`, мутанты |
| `worktree.py` | B1 пишет только в `~/.cache/llm_worker/worktrees/<task_id>-c<k>` (или `$XDG_CACHE_HOME/...`, вне репозитория) на ветке `llm/<task_id>` (кандидат 2: `llm/<task_id>-c2`); перед reset/clean/commit проверяется, что каталог — корень этого worktree и ветка `llm/...`, иначе FAILED; коммит с `--no-verify`; `main` не меняется |
| `sandbox.py` | каждая команда B1 идёт через `bwrap`: корень не монтируется целиком; системные каталоги и `~/.dotnet` только чтение, `$HOME` — пустой tmpfs, `/var/www` отсутствует, `~/.nuget/packages` только чтение (запись лишь на шаге `restore`), запись — только worktree; окружение очищено от ключей; сеть только у `restore`. Нет `bwrap` → задача FAILED |
| `gitleaks.py` | gitleaks по всему входу платного провайдера (включая `error_lines` повторного prompt); находка → `SecretDetected`, нет бинаря → `GitleaksUnavailable`. Ключ DeepSeek — только файл `~/.config/singularity/deepseek-api-key` (0600), иначе `KeyFileError` |
| цепочка моделей | `deepseek-flash` → `deepseek-v4-pro` → эскалация к Pilot. `max_rounds` считается **на модель**, не на задачу (контракт ≤ 2 повторов у каждой). Бюджет контракта (`budget`) и `max_usd_task`/`max_usd_total` из ledger общие для всей цепочки |
| `report.py` | `reports/<date>.md`: принято / отклонено / провалено / эскалировано / не проверено — раздельно |

Состояния: ACCEPTED — все жёсткие проверки пройдены (ревью Claude отмечено, если `requires_review`);
REJECTED — запись вне `allowed_paths`; ESCALATED — проверки не пройдены, решает Pilot; FAILED —
техническая ошибка (изменился контекст, сломана проверка, нет песочницы, ошибка git, 4xx, любая
непредвиденная ошибка), результат не проверен. HTTP 401/403 останавливает весь прогон, задача остаётся в QUEUED.
В отчёте причины, подробности проверок и имена файлов даны в `code` как недоверенный текст.

Записи раундов: `runs/<task_id>/c<k>/round_<n>/` (запрос, ответ, отчёт проверки) и `loop.json` (решение и причина).

## Контракты пилота (`contracts/`)
- `A1-product-class-inventory.json` — инвентарь `src/Singularity.Core/Data/ProductClassTable*.cs`
  (каталога `Products/` в ядре нет; это классы изделий).
- `A2-jet-nodes-clean.json` — дубли, висячие `requires`, циклы, порядок поколений, замкнутость стартовых знаний
  в `docs/research/data/jet-slice/nodes.csv`. Эталон сейчас находит 0 нарушений: это тест ложных находок.
- `B1-fx6-tests.json` — тесты xUnit для `Singularity.Kernel.Fx6` в `tests/Singularity.Kernel.Tests/Llm/`,
  7 мутантов, порог 0.8. `restore_cmd` (с сетью) выполняется один раз до проверок; `build_cmd`/`test_cmd` — `--no-restore`, без сети.
- `A2-jet-nodes-seeded.json` — та же проверка на `fixtures/nodes_seeded.csv` с 10 эталонными находками.

Если файл контекста изменился, контракт перестаёт проходить проверку: обновить хеши
`python3 -m tools.llm_worker fill-hashes <contract>` (только до `submit`; после — новый `task_id`).

## Второе ревью и ворота (ADR-0015 §4–§5, TASK-0039)
```bash
python3 -m tools.llm_worker review --task TASK-NNNN --base <sha> --head <sha> [--provider codex|deepseek]
python3 -m tools.llm_worker gate --item <путь> --stance third [--profile deepseek-flash] [--dry-run]
```
- `review`: цель/критерии из файла задачи в `head`, `git diff base...head`, файлы из объектов git, для R2 — `csharp-core.md` и ADR-0002; > 1 MB — `input_too_large`; gitleaks по всему входу. Печатает строку REVIEW; JSON — `runs/review-<task>-<head8>-<provider>.json`, невалидный ответ — `*-invalid.json` (`schema_invalid`, расход в ledger).
- Codex (`codex.py`): `codex exec --sandbox read-only` в bwrap; `/work` — чистый detached worktree на `head` (только чтение, без `bin/obj` и файлов из `CONTEXT_EXCLUDE`), `~/.codex` (запись) и каталог установки Node (только чтение); `$HOME` — tmpfs, `/var/www` нет, сеть общая. Изменение worktree — `guard_violation`. Попытки и токены — `codex_attempts.jsonl` рядом с ledger. `codex_limit` (лимит подписки, по событиям `error`/`turn.failed` и stderr) и сбой Codex (`codex_failed`) — переход к DeepSeek (`deepseek-v4-pro`, через ledger, ADR-0015 §2); `secret_detected`, `guard_violation`, `sandbox_unavailable`, `worktree_failed` — ошибка без платного вызова. Каталог установки Node должен иметь `lib/node_modules` и не быть `$HOME` или его предком.
- `gate`: документ + файлы из `related` и ссылок на пути в тексте (из объектов git, не больше 1 MB; не поместившиеся перечислены как omitted), один запрос; схемы `schemas/{review,gate}.schema.json`; правило «accept/approve с high» — `schema_invalid`. Ключ ledger: `gate:<ID>`.

### Режим размышления DeepSeek (TASK-0052)
- `gate`, `review` (ветка DeepSeek) по умолчанию шлют `thinking: enabled` + `reasoning_effort: high`, без `temperature`; флаги `--thinking on|off`, `--effort low|high|max`. `run` — по умолчанию `--thinking off`, те же флаги.
- `max_tokens` покрывает и рассуждение (замер: flash/high на RR-0013 при 32768 съел весь лимит, `content` пуст, `finish_reason=length`). Поэтому с thinking `max_tokens = config.THINKING_MAX_TOKENS` (65536), без thinking — 8000 (review) и 6000 (gate).
- Резерв ledger — худший случай: `input_bytes/2` токенов входа + `max_tokens` выхода. Для pro на большом документе (~570 KB) это ≈ $0,38 + $0,26 = $0,64. Для gate-задач (ключ `gate:<ID>`) действует `max_usd_task_gate` = $1,00 (#226), поэтому pro проходит; для остальных задач — `max_usd_task` $0,50. Для flash ≈ $0,17.
- `reasoning_content` в разбор JSON не идёт; в запись run пишутся `thinking`: `reasoning_chars`, `reasoning_tokens`, `finish_reason`, `attempts`, `effort`, `max_tokens`.
- Пустой/пробельный `content` при `finish_reason=stop` — сбой модели: один повтор, затем `schema_invalid`. При `length` повтора нет.
- При повторе `usage` и статистика рассуждения в записи — сумма всех попыток (как в ledger); сырое тело пустой первой попытки — `thinking.first_attempt_raw`. `run --thinking on` пишет `thinking` в `round_N/*_response.json`.
- `review`: лимит задачи по ledger — `max_usd_task` $0,50 (ключ задачи `TASK-NNNN`); $1,00 действует только для `gate:<ID>`. Номер `round` выбирается под `flock` (`runs/.review-<task>.lock`); старые записи без `round`/`created_at` упорядочиваются по mtime.

## Gemini как второе мнение по кадрам (TASK-0135, #441)
```bash
python3 -m tools.llm_worker visual --task TASK-NNNN --frames shots-out/a.png shots-out/b.png \
    [--criteria docs/ui/UI_DESIGN_SYSTEM.md#12] [--model <id>] [--dry-run]
```
- Профиль `gemini-flash` (OpenAI-совместимая точка Google), только бесплатный уровень: `paid=False`, денег в ledger нет. Ключ — только файл `~/.config/singularity/gemini-api-key` (0600, не симлинк, вне репозитория); без него команда отказывает (код 4) с подсказкой.
- Лимиты `GEMINI_RPM=5`, `GEMINI_RPD=100` — осторожные оценки, сверить со страницей квот.
- `--mode design` (по умолчанию `criteria`): в запрос добавляется `docs/ui/VISUAL_DIRECTION_V06.md`, ответ содержит ещё `proposals` (название, что менять, зачем, кадры, объём S/M/L, риск для критериев понятности); схема `schemas/visual-design.schema.json`.
- Профили `free_public` недоступны в `run`; провайдер требует их собственный URL и счётчик (иначе ключ и контекст репозитория могли бы уйти на другой хост).
- Счётчик суток сбрасывается в 00:00 UTC, у Google квота — в полночь по Тихоокеанскому времени: запас на границе суток. Нечитаемый файл счётчика — отказ, а не «ноль».
- **Бесплатность не проверяется инструментом.** Бесплатный уровень действует, только если в проекте Google (откуда ключ) не включён биллинг; инструмент биллинг не видит и цен не знает. Поэтому каждый запуск пишет расход токенов (prompt / completion / thinking, как сообщил провайдер) в запись и в суточные итоги `gemini-usage.json`, и печатает их.
- Кадры меньше 1920x1080 отклоняются (размер читается из заголовка), `--allow-small` снимает проверку. Находки с критерием, которого нет в разделе критериев, или с кадром, которого не отправляли, помечаются `unverified` в JSON и в `.md`. JSON и `.md` пишутся парой из одного ответа (временные файлы, затем переименование), в обоих `response_sha256`.
- Модель по умолчанию `gemini-4-hydrogen` (в списке `GET /v1beta/models` 2026-11-24 вместе с gemini-4-argon, -carbon, -neon; живой прогон прошёл).
- Счётчик запросов (в минуту и за сутки UTC) — `$XDG_DATA_HOME/singularity-llm/gemini-usage.json`; считается каждая HTTP-попытка, при превышении отказ (код 4). 429 — пауза и повтор, не больше `PROVIDER_RETRIES`.
- Кадры только из `shots-out/` (PNG/JPEG, ≤ 7 MB каждый, ≤ 8 шт., ≤ 18 MB всего), уходят как data URI. gitleaks сканирует только текст запроса, не пиксели.
- **Данные:** бесплатный уровень Google может использовать присланное для улучшения продуктов. Отправлять только игровые кадры, без личных данных и секретов.
- **Провайдер (TASK-0190, владелец 2026-11-25):** `--provider agy` (по умолчанию) — подписка Antigravity: `agy --mode plan` в bwrap-Box (как второе ревью), кадры копируются в личную рабочую папку `frameNN.png` (единственная читаемая; в prompt — соответствие имени файла и имени кадра), модель `gemini-4-neon-high` (#544; список — `agy models`), ответ по `--json-schema`, `--dangerously-skip-permissions` не передаётся, shell/запись/URL/MCP запрещены, поток и transcript проверяются после прогона (иной инструмент или путь — `agent_tool_violation`; иная модель в init — `agy_failed`). Ключ AI Studio остаётся запасным: `--provider key`. Счётчик `agy-usage.json` (общий со sketch): `AGY_RPM=2`, `AGY_RPD=20`; 503/«No capacity» (`agy_capacity`) попытку не считает, остальные отказы считает. Запись: `runs/visual-<task>-<hash8>-agy.json`. Ответ публикуется только если transcript показывает чтение каждого `frameNN.png` (иначе `agy_failed`); в prompt уходят только имена `frameNN`, обратно в имена кадров они переводятся после разбора. Флаг `--sandbox` не передаётся: изоляцию даёт Box (не проверено, что `--sandbox` внутри Box работает). Живой прогон 2026-11-25: TASK-0161-r2, 8 кадров, ≈4,5 мин, 24K/32K/28K токенов (prompt/completion/thinking).
- Результат ключа: `runs/visual-<task>-<hash8>-gemini.json` и `.md` рядом; невалидный ответ — `*-invalid.json`. Вердикт PASS/FAIL остаётся за ui-art-director; замечания Gemini идут в обзор задачи.

## Эскизы Gemini через Antigravity CLI (TASK-0141, #451, ADR-0026)
```bash
python3 -m tools.llm_worker sketch --task TASK-NNNN --prompt-file <brief.md> [--ref shots-out/a.png ...] [--count 1..4] [--with-visual] [--dry-run]
```
- Вызывает `agy -p <prompt> --print-timeout <N>s --mode accept-edits --output-format stream-json` в пустой временной папке; `--dangerously-skip-permissions` не передаётся никогда, shell агента запрещён заранее (`--settings` с `permissions.deny`: `command(*)`, `write_file(*)`, `read_url(*)`, `execute_url(*)`, `mcp(*)`; флага отключения инструментов у agy нет, ключ `disabledTools` отвергается: «unknown setting»; замер 2026-11-24, agy 1.5.16), и дополнительно любой `run_command` в потоке — отказ; после прогона поток проверяется по allowlist инструментов (generate_image, invoke_subagent, finish/wait, view_file/read_file/list_dir внутри рабочей папки; аргументы вызовов берутся из transcript.jsonl brain-папок, т.к. поток их не содержит; send_message допустим только в transcript и только получателю из разговоров этого прогона или `parent`: иной получатель — нарушение; 503 «No capacity available» подагента — `agy_capacity` (код 4, без времени сброса, «server capacity, try later»)): иной вызов — `agent_tool_violation`; fail closed: нет читаемого transcript.jsonl хотя бы у одного id прогона, или поток называет `view_file`/`read_file`/`list_dir`, а в transcript нет их аргументов — отказ; эскиз засчитывается только как целая картинка (PNG: chunk-CRC и IEND, JPEG: SOF/SOS и EOI, WebP: размер RIFF; не меньше 100 байт) с неизменным размером между двумя опросами; запись прогона `sketch-<task>-<hash8>-<UTC время>[-N].json` не перезаписывает прежнюю; квота (RESOURCE_EXHAUSTED/quota/429) — `agy_quota` (код 4); 429 подагентов ищется в их transcript.jsonl (главный поток его не показывает; замер 2026-11-24, TASK-0143: окно квоты картинок ~5 ч, около 13 картинок): картинок меньше заказанного из-за квоты — запись получает `quota` (число ошибок, `reset_seconds`, `reset_at`) и `stop_reason: agy_quota`, вывод «got K of N, reason: quota, reset in 4h40m37s» (время считается от сейчас до сохранённого `reset_at`), код 4 и при частичном результате; квота считается по подагентам (лишние картинки одного не скрывают 429 другого), готовые картинки сохраняются и при падении родителя; ни одной картинки — отказ `agy_quota` с той же строкой. Промпт требует ровно один вызов `generate_image` на подагента без уточнений (`_v2`/`_refined`) и повторов; подагенты идут по очереди, `--parallel` запускает их одновременно; окружение agy — allowlist без ключей; `--prompt-file`: только .md/.txt, без dot-путей и файлов, игнорируемых git. Нет `agy` (PATH или `~/.local/bin`) — отказ `agy_missing`; не залогинен — `agy_not_logged_in` (код 4).
- Движок эскизов (`--engine auto|gemini|agy|codex`, agy = gemini; по умолчанию auto, т.к. владелец просил fallback Gemini→Codex; TASK-0145, владелец 2026-11-25): auto = Gemini (agy), при `agy_quota`/`rate_limit` и `--count 1` — Codex (`codexsketch.py`: `codex exec --sandbox read-only --json` в bwrap как у второго ревью, env по allowlist, без `--dangerously-bypass-approvals-and-sandbox`, одна картинка за вызов). Лимит Codex общий со вторым ревью: запомненный лимит (`codex-limit.json`) блокирует запуск, лимит из ответа записывается туда же; отказ `codex_quota` (код 4); суточный потолок `--codex-daily-cap` (4 попытки, `codex_daily_cap`). Запись прогона несёт `engine`, `model`, `fallback` (откуда и почему), строка атрибуции называет Codex CLI. Копируются только файлы из `~/.codex/generated_images/` или base64 из события; shell-элемент в потоке — `agent_tool_violation`. ВНИМАНИЕ: форма события с картинкой и каталог сохранения — предположение, живой прогон не делался (лимит Codex).
- Разбор NDJSON (`sketch.parse_stream`): `init`/`step_update`/`result`. **Замер 2026-11-24 (agy 1.5.15):** картинку рисует под-агент `image-generator` (событие `invoke_subagent` со своим `conversation_id`), но файл `<name>_<ts>.jpg` лёг в brain-папку **главного** разговора. Поэтому ищем во всех id из событий; копируются только файлы верхнего уровня brain-папки с mtime не раньше старта (минус 2 с). Фикстура реального прогона: `fixtures/agy_sketch_stream.ndjson`.
- Копии — `shots-out/sketches/<task>/`; запись `runs/sketch-<task>-<hash8>.json` и `.md`: prompt, refs, conversation_id(s), файлы с sha256, токены, время, строки атрибуции для `mods/core/ATTRIBUTION.md` (одобряющий вписывается после решения ui-art-director; без одобрения в mods ничего не добавлять).
- `--ref`: только из `shots-out/` (PNG/JPEG); копии кладутся в рабочую папку agy и называются в prompt абсолютными путями (агент должен открыть их файловым просмотром `view_file`). **Не проверено:** читает ли agy эти кадры (живой прогон с `--ref` не делался; смотреть в `tools` записи — `view_file`/`read_file`).
- Счётчик `agy-usage.json` рядом с ledger (RateGuard): `AGY_RPM=2`, `AGY_RPD=20` — осторожные оценки; каждый запуск тратит подписку владельца.
- `--with-visual`: перед эскизами выполняет `visual --mode design` на тех же `--ref`; провайдер `--visual-provider agy|key` (по умолчанию agy, подписка: тратит из общего суточного счётчика `agy-usage.json` одну попытку, которая нужна и эскизам; `key` — бесплатный ключ AI Studio).
- Живое подтверждение (2026-11-24, Orchestrator): эскизы макетов Финансов и Производства в `shots-out/sketches/design-pass-2/`; картинку писал под-агент `image-generator` в brain-папку главного разговора.
- Результат неполный (картинок меньше `--count`): запись помечена `partial`, код возврата 6.
- Данные уходят в Google: только текст брифа и игровые кадры.

## После прогона (Pilot)
- B1: `git log -p -1 llm/<task_id>` — результат на ветке `llm/...`, не в `main`.
  В `main` — только через reviewer, `zone_check` и коммит Pilot.
- Уборка: `git worktree remove ~/.cache/llm_worker/worktrees/<dir>`, `git branch -D llm/<task_id>`.
- Уроки: `lessons/*.md`, первая строка `applies_to: A1, B1` (см. `lessons/README.md`).

## Тесты
`python3 -m unittest discover tools/llm_worker/tests` — поддельный провайдер и поддельный `dotnet`, без сети.
