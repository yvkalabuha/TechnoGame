---
id: TASK-0171
title: "Golden batch: turn on Reserve, Institutes, LicenceMarket, Adviser, Intelligence by default; re-record goldens once with explanation"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core           # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: high           # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0150, TASK-0151, TASK-0152, TASK-0153, TASK-0160, TASK-0173, TASK-0174, TASK-0175]
related: []
approved_by: orchestrator # owner | orchestrator (orchestrator — только level: light)
approved_at: 2026-11-25
approval_ref: "#474 one build for the owner; handoff 2026-11-25-2 «Не забыть»"
review_ref: "train f12684f: reviewer APPROVE (a74ab0a, rounds to 8a4b96b7); codex 4 returns, waived by #432, DEBT-0115"
created: 2026-11-25
tags: [task, gameplay, determinism, done]
---

# TASK-0171 — Golden batch: turn on Reserve, Institutes, LicenceMarket, Adviser, Intelligence by default; re-record goldens once with explanation

## Цель
Одна сборка для партии владельца (#474): механики волны рычагов 1 работают по умолчанию.

## Описание
Включить по умолчанию флаги Reserve, Institutes, LicenceMarket, Adviser (для страны игрока), Intelligence в `SliceRunConfig` по умолчанию для клиента и среза. Один пакет изменений, меняющих эталоны (orchestration §5): перезаписать эталоны один раз и объяснить каждую перезапись. Проверить Lab-замер TASK-0148 на включённых флагах (калибровка годов 1945–1977, допуски) — если допуски не проходят, остановиться и доложить.

## Критерии приёмки
- [ ] Флаги включены по умолчанию; режим «всё выключено» остаётся доступным для тестов
- [ ] Эталоны перезаписаны один раз, у каждой перезаписи объяснение
- [ ] Калибровка годов в допусках (или остановка с отчётом)
- [ ] reviewer APPROVE, второе ревью

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
Сделано (ветка worktree-agent-a0d58d313e41da42c, коммит 58997ac): флаги Reserve, Institutes, LicenceMarket, Intelligence и советник игрока включены по умолчанию (`SliceRunConfig.Legacy` — всё выключено); правило целей институтов ИИ PATH (#559: `IInstituteInputs.GoalPath`, `InstituteInputsAdapter`, `PickForFreeSlots`); рынок M2 (#560: `AiMaxGenJump` 1, `AiBuyFromFriendsOnly`, `AiPriceK` 1,2, предел 64 каталогов заменён бегущей суммой, друзья SWE–GBR 1945–1990 и SWE–USA 1960–1990); клиент `SimHost` получает актора игрока. Эталоны пересняты один раз (LinkedRun Golden60, Views, TwoWorlds Pinned). Замер Lab на сборке (манифест levers-558-pair, seeds 1–10): G2/G3/G4 = 138 / 269,5 / 388, структура 9 пунктов, §8-d 35, сделок до 1977 — 20; совпадает с песочницей PATH+M2. pace_check --base main: PASS (калибровочный мир Lab по умолчанию LEGACY, дельты 0).
