---
id: TASK-0159
title: "llm_worker: Codex review sees earlier rejected findings; visual accepts frames from the client shots-out"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M05
agent: developer         # ответственный агент
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-24
approval_ref: "owner answer 2026-11-24 (hooks and tools fixes)"
created: 2026-11-24
---

# TASK-0159 — llm_worker: Codex review sees earlier rejected findings; visual accepts frames from the client shots-out

## Цель
Меньше лишних кругов второго ревью и ручного копирования кадров (ответ владельца 2026-11-24).

## Описание
1. `llm_worker review` (и `land.py`): при повторном ревью той же задачи передать Codex/Sonnet/DeepSeek список ранее отклонённых находок (из прежних записей `runs/review-TASK-NNNN-*.json`, где `review-confirm --rejected`) с причинами; в промпте — «не повторять без новых доводов». Причину отклонения хранить в записи: добавить `--reason` в `review-confirm`. Случай TASK-0149: Codex трижды повторил отклонённые #1/#2.
2. `llm_worker visual`: принимать кадры и из `src/Singularity.Client.Spike/shots-out/` (куда пишет клиент), не только из корневого `shots-out/`; проверка «только каталоги кадров» сохраняется.

## Критерии приёмки
- [ ] Тест: повторное ревью получает отклонённые находки с причинами в промпте
- [ ] `review-confirm --rejected --reason` сохраняет причину
- [ ] Тест: visual принимает кадр из папки клиента, отклоняет чужой путь
- [ ] reviewer APPROVE

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
