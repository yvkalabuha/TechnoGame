---
id: TASK-0130
title: "Scoped client view: node blocks only for subscribed actors (ADR-0025)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M05
agent: developer
category: core
priority: high
needs_owner: false
depends_on: []
related: [ADR-0025, TASK-0124, TASK-0125]
approved_by: owner
approved_at: 2026-11-22
approval_ref: "#434 (ADR-0025 rev 2 accepted)"
created: 2026-11-23
---

# TASK-0130 — Вид клиента с подпиской (ADR-0025)

## Цель
Шаг этапа 3 «стражи архитектуры» до кода #228: сессия строит блоки узлов только для подписанных акторов, чтобы снимок вида при 200 акторах не рос до 6,9 МиБ.

## Описание
Реализовать ADR-0025 разделы 1–5 целиком: `SliceRunConfig.ViewScope` (`All` по умолчанию, `Subscribed` в клиенте), `session.SetViewSubscription` только с потока-владельца, полная пересборка вида при смене подписки на паузе, `NodeList.IsPresent` и исключение «actor not subscribed», `NodeBlockCache` хранит только подписанных, клиент (`SimHost`) подписывает играемого и выбранного актора. `TestApiVersion` +1. Хеш состояния и `SchemaVersion` не меняются.

## Критерии приёмки
- [ ] Проверки из раздела «Проверки» ADR-0025, каждая тестом, который падает без правки.
- [ ] Хеш L1/L2 не зависит от `ViewScope` и подписки (тест); golden не меняются.
- [ ] Клиент: дерево и звук играемого актора работают, выбор другого актора показывает его узлы; кадр 1920x1080.
- [ ] Ревьюер APPROVE; второе ревью по #432.

## Связанные документы
`docs/decisions/ADR-0025-view-snapshot-buffer-ring.md`, `docs/reviews/adr-0025-*.md`.

## Результат
