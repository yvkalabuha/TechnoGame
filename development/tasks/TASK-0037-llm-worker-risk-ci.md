---
id: TASK-0037
title: "llm_worker: классификатор риска и CI (ADR-0015 1c)"
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
approval_ref: "DECISIONS_LOG #176 (ADR-0015, план 1c)"
created: 2026-11-13
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
- [ ] Тесты классификатора: rename, `..`, регистр, сборочные файлы (`*.props`, `*.targets`, `nuget.config`, `global.json`), реальный файл теста детерминизма → R2, правка `approved_by` → R3.
- [ ] `test_pilot_contracts_are_valid` зелёный.
- [ ] Тест: две неудачи flash → pro; две неудачи pro → ESCALATED.
- [ ] Текст CI job приложен в «Результат».

## Тесты
`python3 -m unittest discover tools/llm_worker/tests`.

## Связанные документы
ADR-0015; DEBT-0024.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно

## Результат
