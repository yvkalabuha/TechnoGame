---
id: TASK-0198
title: "Platform error catalogue: errors.yaml, errors.py add/list/stats, auto capture from land, hooks, reviewer, CI; weekly DeepSeek digest (#584)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: tooling                # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: orchestrator
approved_at: 2026-11-26
approval_ref: "DECISIONS_LOG #584"
created: 2026-11-26
---

# TASK-0198 — Platform error catalogue: errors.yaml, errors.py add/list/stats, auto capture from land, hooks, reviewer, CI; weekly DeepSeek digest (#584)

## Цель

## Описание

## Критерии приёмки
- [ ]

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
Сделано (ветка worktree-agent-afd08e65fbb5f23ca):
- `tools/development/errors.py`: add / list / stats / repeat / close / import [--ci] / digest [--dry-run] / check, схема и дедупликация по ключу (kind|area|симптом); add --from-review для вердикта CHANGES.
- `land.py`: при exit 1 и 6 одна строка в `.claude/.state/land_failures.jsonl` (не errors.yaml, чтобы не пачкать main при посадке); `watch.py`: отдельная функция `red_ci_event()`; `kb_check.py check` проверяет схему errors.yaml; `verify.py`: шаг errors; тесты `test_errors.py` (9).
- digest использует схему gate, gitleaks-скан и ledger как gate; llm_worker не менялся.

НЕ сделано (вне зоны developer, нужен Оркестратор):
1. `.claude/hooks/_common.py`: в block() до print вызвать log_refusal(msg): дописать JSON {t, hook: Path(sys.argv[0]).stem, msg[:200]} в `$CLAUDE_PROJECT_DIR/.claude/.state/hook_refusals.jsonl`, молча игнорируя OSError и пропуская при env SINGULARITY_NO_REFUSAL_LOG; в test_hooks.run_hook добавить это env, иначе тесты пишут в реальный журнал. Импортёр (errors.py import) уже читает этот файл и игнорирует guard_load.
2. `.claude/hooks/session_context.py`: в checks() добавить строку из `python3 tools/development/errors.py list --brief` (печатает "N open error(s)...").
3. `development/errors.yaml` (вне зоны): засеять 5 записей командами ниже (список задачи называет пять случаев, не шесть). Даты ТЗ неточны: [Предп.] 2026-11-26 для TASK-0184/0185 и Codex.
```
python3 tools/development/errors.py --today 2026-11-25 add --executor orchestrator --kind process --area shots --symptom "Shots run on an old client version: frames were taken before the fix was built" --cause "Client launched from a stale build / wrong branch" --found-by owner --cost high --fix "Check branch and build before a shot batch"
python3 tools/development/errors.py --today 2026-11-26 repeat ERR-NNNN
python3 tools/development/errors.py --today 2026-11-26 add --executor orchestrator --kind process --area agents --symptom "Agent dispatched before the task file was committed: the worktree lacks the task (TASK-0184, TASK-0185)" --cause "Worktree is cut from the last commit, the task was only in the working tree" --found-by self --cost medium --fix "Commit the task before dispatching an agent"
python3 tools/development/errors.py --today 2026-11-26 repeat ERR-NNNN
python3 tools/development/errors.py --today 2026-11-26 add --executor tool:codex --kind tooling --area review --symptom "Codex review fails with input_too_large when the input exceeds 1,000,000 bytes" --cause "Review input is not capped for Codex" --found-by ci --cost medium --fix "Cap or trim the review input below 1 MB, fall back to another provider"
python3 tools/development/errors.py --today 2026-11-26 add --executor tool:AskUserQuestion --kind comms --area owner-questions --symptom "AskUserQuestion times out after 2 hours without an answer; the questions then had to be repeated as a list in chat" --cause "Owner away; the dialog expires" --found-by owner --cost medium --fix "Keep the question queue in OPEN_QUESTIONS.md and repeat as a chat list on timeout"
python3 tools/development/errors.py --today 2026-11-26 add --executor orchestrator --kind process --area shots --symptom "Shot set run without checking the branch the client was built from" --cause "No branch check in the shot launch step" --found-by self --cost medium --fix "Add a branch check to the shot batch procedure or guard_shots"
```
Проверка: test_errors.py зелёный; при отключённой дедупликации 2 теста падают. verify.py см. отчёт.

## Цель
Решение #584 (вариант C): каталог ошибок платформы, чтобы не повторять уже сделанные ошибки.

## Объём (tools/development, .claude/hooks, development/errors.yaml; без src/)
1. `development/errors.yaml`: записи ERR-NNNN с полями id, date, executor (orchestrator | agent:<type> | tool:<name> | model:<name>), kind (process | code | data | ui | comms | tooling | determinism), area, symptom, cause, found_by (owner | reviewer | codex | ci | hook | self), cost (low|medium|high), fix, prevention {kind: hook|test|skill|rule|memory, ref}, repeats (список дат), status (open | prevented | wontfix).
2. `tools/development/errors.py add|list|stats|repeat ERR-N|close ERR-N --prevention ...`: stats печатает топ повторов по kind/executor за 7 и 30 дней и долю повторов.
3. Автозапись (черновик со status open, без дублей по ключу): land.py exit 1/6, отказы guard_* хуков (короткая строка в .claude/.state/hook_refusals.jsonl, сводка в errors.py import), красная CI на main (через gh в watch), CHANGES reviewer (команда `errors.py add --from-review <file>`).
4. `errors.py digest`: еженедельный разбор повторов через `python3 -m tools.llm_worker` (DeepSeek, без секретов — gitleaks как у gate) в docs/reviews/errors-digest-YYYY-WW.md.
5. kb_check/registry: проверка схемы errors.yaml; SessionStart печатает число открытых.
6. Засеять 6 записей этой сессии: старая версия на кадрах (2 повтора), агент до коммита задачи (2), Codex input > 1 МБ, AskUserQuestion 2 ч таймаут → список в чате, набор кадров без проверки ветки.

## Критерии приёмки
- verify.py --full зелёный; тесты на add/stats/import/дубли; digest имеет --dry-run.
