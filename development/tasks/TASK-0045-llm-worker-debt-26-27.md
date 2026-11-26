---
id: TASK-0045
title: "llm_worker: DEBT-0026 и DEBT-0027 (fallback при сбое песочницы, related в gate, метрики, verifier unittest)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: tooling
priority: medium
needs_owner: false
depends_on: [TASK-0039, TASK-0043]
related: [ADR-0015]
approved_by: orchestrator
approved_at: 2026-11-13
approval_ref: "DEBT-0026, DEBT-0027; ADR-0015 §4, §7"
created: 2026-11-13
tags: [task, platform, external-llm, done]
---

# TASK-0045 — llm_worker: долги DEBT-0026 и DEBT-0027

## Цель
Закрыть DEBT-0026 и DEBT-0027 до регулярного использования `review`/`gate` и расширения прогонов DeepSeek.

## Описание
1. DEBT-0026 (1): если bwrap не работает (нет бинаря, нет user namespace, не стартует), это `sandbox_unavailable` без платного fallback на DeepSeek. Сейчас такой случай считается `codex_failed` (`codex.py:~177`).
2. DEBT-0026 (2): gate разбирает поле `related` frontmatter через PyYAML, включая блочный YAML-список. Ошибка разбора → поле пропускается и пишется в `skipped`.
3. DEBT-0027 (1): раздел «Метрики» в `report.py` по ADR-0015 §7 — раунды до accept, вердикт первого раунда, высшая тяжесть находок в принятом раунде, $ на задачу, подтверждённые находки второго ревьюера (из записей `runs/review-*.json`, если есть). Сбежавший high — по ссылкам `fixes: TASK-…` в файлах задач.
4. DEBT-0027 (2): верификатор тестов понимает вывод `unittest` (`Ran N tests`, `OK`/`FAILED (failures=…, errors=…)`) и `dotnet test`, включая русскую локаль (`Пройден!`, `не пройдено N, пройдено M`). Контракт L1 переходит с обхода на прямой `python3 -m unittest`.

## Критерии приёмки
- [x] Тесты на каждый пункт; все тесты `llm_worker` зелёные.
- [x] `python3 -m tools.llm_worker report` показывает раздел «Метрики» на данных TASK-0043.
- [x] Второе ревью Codex — до чистого accept (#181).

## REVIEW
- [x] Code review (reviewer, 2026-11-13) — accept, только low → DEBT-0028
- [x] Второе ревью (codex, head 854ad457) — accept: 2 medium; report sha256 15f370cac8a9c89bb1b2816306202f99ca420526868f9d4ba702de85546e234f; medium → DEBT-0028

## Результат
developer, 2026-11-13 (записал Orchestrator; файл задачи вне зоны developer).
- (1) `codex.py`: пустой ответ + stderr `bwrap:` → `SandboxUnavailable`, без платного fallback; лимит подписки распознаётся раньше.
- (2) `review.py` `_related_ids`: PyYAML, инлайн/блочный список/скаляр; ошибка → `skipped` (`related: <причина>`).
- (3) `report.py`: раздел «Метрики (ADR-0015 §7)» из `runs/review-*.json` и ledger; сбежавшие high — по `fixes: TASK-…`; `config.TASKS_DIR`.
- (4) `verifiers/b1_tests.py` `parse_test_output`: unittest, dotnet, русская локаль; L1 на прямом `unittest discover`.
- Тесты: `tests/test_debt_0026_0027.py`; `test_verifiers.py` — `max_candidates=2` задаётся в тесте (контракт B1 после TASK-0043 = 1). Всего 261 OK.
- Ограничения [Факт]: записи ревью без времени и подтверждения находок (порядок по mtime, находки «не подтверждены»).
