---
id: TASK-0037
title: "llm_worker: классификатор риска и CI (ADR-0015 1c)"
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
approval_ref: "DECISIONS_LOG #176 (ADR-0015, план 1c)"
created: 2026-11-13
tags: [task, platform, external-llm, done]
---

# TASK-0037 — llm_worker: классификатор риска и CI (ADR-0015 1c)

## Цель
Классы риска R0–R3 по путям и проверка в CI. Это задача 1c ADR-0015.

## Описание
Источник: ADR-0015 §1 (классы риска), §10 (контроль слияния). Долг: DEBT-0024.
- `tools/llm_worker/risk_paths.json` — единственный источник классов; `WRITE_FORBIDDEN` строится из него и содержит R2 ∪ R3.
- `risk.py classify --range main...llm/<id>`: `git diff --name-status --no-renames`, обе стороны переименования, нормализация путей; неизвестный путь — R2; поля одобрения во frontmatter — R3; класс только поднимается.
- Контракт v2 с полем `risk`; контракт A1 — новая версия с актуальными хешами (DEBT-0024); отчёт ловит `ContractError` и выводит задачу в «Не проверено».
- Переключение модели по цепочке flash → pro → Claude (#175 d): при исчерпании предела неудач пары (шаг, модель) — следующая модель; после pro — эскалация к Pilot. Счётчик уже есть (TASK-0035), переключения нет.
- Job `llm-worker` в CI: unit-тесты (тесты bwrap — skip) и `classify` для веток `llm/*`. Правку `.github/` делает Orchestrator по готовому тексту job из задачи.

## Критерии приёмки
- [x] Тесты классификатора: rename, `..`, регистр, сборочные файлы (`*.props`, `*.targets`, `nuget.config`, `global.json`), реальный файл теста детерминизма → R2, правка `approved_by` → R3.
- [x] `test_pilot_contracts_are_valid` зелёный.
- [x] Тест: две неудачи flash → pro; две неудачи pro → ESCALATED.
- [x] Текст CI job приложен в «Результат».

## Тесты
`python3 -m unittest discover tools/llm_worker/tests`.

## Связанные документы
ADR-0015; DEBT-0024.

## REVIEW
- [x] Code review (reviewer, 2026-11-13) — reviewer: CI классифицировал версией ветки, B1 ломался — исправлено (#179, классификатор из origin/main); test-engineer: property-тесты классификатора без нарушений; CI job применён Orchestrator; 189 тестов OK
- [x] Тесты (test-engineer, 2026-11-13) — проходят, добавлен `test_review_0036_0037.py`

## Результат

**Сделано** (не закоммичено; статус не менял).
- `tools/llm_worker/risk_paths.json` — единственный источник классов R0–R3 (глобы ADR-0015 §1; уточнено по реальным путям: `tests/Singularity.{Kernel,Core,Session,Architecture}.Tests/**` целиком R2, там `StateHashTests.cs`, `DataHashTests.cs`, `Fixtures/t6c2-baseline.txt`; сборочные файлы `**/*.props|*.targets|*.csproj|nuget.config|global.json|*.runsettings`, `*.sln` — R2). Наивысший совпавший класс побеждает, неизвестный путь — R2.
- `risk.py`: `classify_path` (нормализация, `..`/абсолютный путь — R3, регистр не важен), `classify_range` (`git diff --name-status --no-renames -z`, обе стороны переименования, смена `status/approved_by/approved_at/approval_ref` в frontmatter `.md` относительно merge-base — R3), `combine` (класс только растёт), CLI `python3 -m tools.llm_worker.risk classify --range main...llm/<id> [--max R1]`: код 0 при классе ≤ max, 1 выше, 2 при ошибке git.
- `config.WRITE_FORBIDDEN = R2 + R3` из `risk_paths.json` (тест на равенство).
- `contract.py`: версия 2, обязательное поле `risk`; риск выше R1 для внешнего исполнителя отклоняется. Runner пишет `risk` = max(контракт, по изменённым файлам) в итог задачи.
- Контракты пилота: все четыре переведены на v2 с `risk`; A1 и A2-clean получили актуальные хеши и новые `task_id` (`PILOT-A1-product-class-inventory-2`, `PILOT-A2-jet-nodes-clean-2`; старые задачи в очереди остаются как есть). DEBT-0024 по A1 закрыт, но устарел был и A2-clean (nodes.csv менялся).
- `report.py`: `ContractError` на задаче → блок «Не проверено», отчёт не падает.
- Переключение модели (`loop.py`, `runner.py`, `__main__.py`): `CandidateLoop(..., fallbacks=[...])`, позиция `model_idx` хранится в `loop.json` (переживает рестарт); при остановке модели (лимит неудач пары (шаг, модель), «тот же сбой», нет прогресса, исчерпаны попытки) — следующая модель, после последней — ESCALATED к Pilot. Проверки «нет прогресса» не срабатывают на первой попытке новой модели. CLI: профиль `deepseek-flash` получает `fallbacks=[deepseek-v4-pro]` со своим Guard (отдельная цена в ledger).
- Тесты: `tests/test_risk.py` (16), `tests/test_model_switch.py` (4: flash×2 → pro; pro×2 → ESCALATED; рестарт; без fallback).

**CI job** (версия после ревью; заменяет прежнюю). Классификатор берётся из `origin/main`, не из ветки: ветка не может ослабить собственную проверку. Тесты идут по коду ветки.
```yaml
  llm-worker:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - uses: actions/setup-python@v5
        with:
          python-version: "3.12"
      # bwrap tests skip themselves when bubblewrap is unavailable
      - name: llm_worker unit tests
        run: python -m unittest discover -s tools/llm_worker/tests
      - name: Risk class of llm/* branches (classifier and paths from main; no merge above R1)
        if: startsWith(github.head_ref, 'llm/')
        run: |
          git fetch origin main
          K="$RUNNER_TEMP/rk/llm_worker"
          mkdir -p "$K"
          touch "$K/__init__.py"
          for f in risk.py paths.py risk_paths.json; do
            git show "origin/main:tools/llm_worker/$f" > "$K/$f"
          done
          PYTHONPATH="$RUNNER_TEMP/rk" python -m llm_worker.risk classify \
            --range "origin/main...${{ github.event.pull_request.head.sha }}" \
            --paths-file "$K/risk_paths.json" --max R1
```
Заметки: `risk.py` зависит только от `paths.py` и `risk_paths.json` (импорта `config` нет). Пока `risk.py` не слит в `main`, `git show` падает и job красный: первый PR с этими файлами сливает Orchestrator вручную (bootstrap). `push` в `ci.yml` ограничен `main`: job проверяет только PR из `llm/*`.

**Доработка после ревью (решение #179 и др.):**
- `risk_paths.json`: `R1_if_added` (`tests/*/Llm/**/*.cs`) и `R1_if_added_except` (`**/*golden*`). Статус диффа `A` → R1, `M/D/R` и неизвестный статус → R2. Режимы 120000 и 160000 (symlink, submodule) → не ниже R2 (`git diff --raw`).
- `paths.write_allowed(..., exists)`: новый файл под `R1_if_added` создавать можно, существующий перезаписывать нельзя (B1 передаёт `lexists`); сравнение без учёта регистра; R3 не обходится.
- `validate_contract`: каждый `allowed_paths` классифицируется (`risk.classify_glob`: сам глоб как путь с статусом A и пересечение по литеральному префиксу с глобами R2/R3); класс выше `risk` контракта — ошибка до любого платного вызова. B1 пилота (`tests/Singularity.Kernel.Tests/Llm/*.cs`, `risk: R1`) валиден.
- frontmatter: ключи в кавычках, BOM; нечитаемый или незакрытый блок в `.md` → R3.
- `__main__`: `KeyFileError` при создании провайдера → сообщение и код 4. README: `max_rounds` на модель, бюджет и `task_cap` общие.

**Дополнение (после TASK-0036), `runner.py`:** `SecretDetected` — задача FAILED, причина `secret_detected: <rule>@<line>` (без значения), прогон продолжается; `KeyFileError` — прогон останавливается, задача в QUEUED, причина `key_error, run stopped`; `SandboxUnavailable`/`GitleaksUnavailable` — то же, причина `sandbox_unavailable, run stopped`. Причины видны в отчёте. Тесты: `RunnerStopReasonsTest` в `tests/test_risk.py`. Тест `test_b1_refuses_without_bwrap` (TASK-0036) обновлён под новое поведение: исключение + QUEUED вместо FAILED.

**Проверка:** `python3 -m unittest discover tools/llm_worker/tests` — 189 тестов, OK (в т.ч. `test_pilot_contracts_are_valid`).
