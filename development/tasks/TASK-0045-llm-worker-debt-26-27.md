---
id: TASK-0045
title: "llm_worker: DEBT-0026 и DEBT-0027 (fallback при сбое песочницы, related в gate, метрики, verifier unittest)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
- [ ] Тесты на каждый пункт; все тесты `llm_worker` зелёные.
- [ ] `python3 -m tools.llm_worker report` показывает раздел «Метрики» на данных TASK-0043.
- [ ] Второе ревью Codex — до чистого accept (#181).

## REVIEW
- [ ] Code review (reviewer)
- [ ] Второе ревью (codex)

## Результат
