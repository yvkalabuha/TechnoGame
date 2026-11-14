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
- Резерв ledger — худший случай: `input_bytes/2` токенов входа + `max_tokens` выхода. Для pro на большом документе (~570 KB) это ≈ $0,38 + $0,26 = $0,64 > `max_usd_task` $0,50: ledger откажет (`task_cap_exceeded`), лимит не урезается. Для flash ≈ $0,17: проходит.
- `reasoning_content` в разбор JSON не идёт; в запись run пишутся `thinking`: `reasoning_chars`, `reasoning_tokens`, `finish_reason`, `attempts`, `effort`, `max_tokens`.
- Пустой/пробельный `content` при `finish_reason=stop` — сбой модели: один повтор, затем `schema_invalid`. При `length` повтора нет.

## После прогона (Pilot)
- B1: `git log -p -1 llm/<task_id>` — результат на ветке `llm/...`, не в `main`.
  В `main` — только через reviewer, `zone_check` и коммит Pilot.
- Уборка: `git worktree remove ~/.cache/llm_worker/worktrees/<dir>`, `git branch -D llm/<task_id>`.
- Уроки: `lessons/*.md`, первая строка `applies_to: A1, B1` (см. `lessons/README.md`).

## Тесты
`python3 -m unittest discover tools/llm_worker/tests` — поддельный провайдер и поддельный `dotnet`, без сети.
