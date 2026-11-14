---
id: TASK-0051
title: "Ключ RNG знания actor·2^16 + node (#197), вместе с TASK-0050"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false
depends_on: [TASK-0049]
related: [TASK-0050, RR-0008]
approved_by: owner
approved_at: 2026-11-14
approval_ref: "DECISIONS_LOG #197; docs/architecture/notes/contract-proposals-wave1.md A2; rr-0008-contract-disputes.md"
created: 2026-11-14
---

# TASK-0051 — Ключ RNG знания actor·2^16 + node (#197)

## Цель
Снять потолок номера узла 63: ключ сущности RNG знания `entity = actor·2^16 + node` вместо `actor·64 + node`. Делается в одном пересчёте golden с TASK-0050 (#197: сейчас, до T12).

## Описание
- `KnowledgeRng`: ключ `actor·65536 + node`, `MaxNodeNumber` = 65535; node 0 остаётся за L_K (выборка часов поколений — `entity = actor·65536`).
- `KnowledgeSchedule.Delta`: размер `actorCount·(maxNumber + 1)` по фактическому графу, индекс без `·64`.
- `KnowledgeGraph`: сообщение об ошибке и проверка диапазона по новой константе.
- Спека среза §9.1 и формат ключа в документах — правка simulation-engineer (не developer): developer описывает в «Результат», что изменить.
- Golden: пересчитать один раз вместе с TASK-0050; в таблице «старый → новый хеш» — причина «ключ RNG (#197) + хеш без tick (TASK-0050)». L1 и L2 (потоки 1/4/8) дают одинаковые новые хеши.
- Тест: граф с узлом номер 64 и 1000 загружается; выборки двух акторов с одинаковым номером узла различаются; ключи не пересекаются для actor < 32768.

## Критерии приёмки
- [ ] Новый ключ, тесты выше; полный `dotnet test` зелёный; golden пересчитаны один раз вместе с TASK-0050.
- [ ] Второе ревью Codex — accept (#181).

## REVIEW
- [ ] Code review (reviewer)
- [ ] Второе ревью (codex)
- [ ] Детерминизм L1/L2 (test-engineer)

## Результат

Общий пересчёт с TASK-0050: полная таблица «старый → новый хеш» — в «Результат» TASK-0050 (причина: ключ RNG (#197) + хеш без tick). Изменения TASK-0051: KnowledgeRng (actor·65536+node, MaxNodeNumber=65535), LkTables(actorCount, maxNodeNumber)/DeltaIndex, BuildLk и KnowledgeGraph по новой константе; тесты в StreamKeyTests и KnowledgeSystemTests.

Codex round 1: номер актора > 32767 отвергается при построении (`KnowledgeStart`, `KnowledgeRng.MaxActorNumber`), а не исключением посреди прогона; тест `StreamKeyTests.KnowledgeStart_RejectsActorNumberAbove32767_AtConstruction`. `jet_ref.py` (a*64+n) правит simulation-engineer.
