---
id: TASK-0130
title: "Scoped client view: node blocks only for subscribed actors (ADR-0025)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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

## Ход работы
- 2026-11-23: Session: `ViewScope` (All по умолчанию, Subscribed), `SetViewSubscription` (owner thread), `RebuildView` (полная сборка без тика, того же Tick/хеша/месяца), флаг смены подписки отключает путь F7a, `NodeBlockCache.Drop`, `NodeList.IsPresent`, исключение "actor not subscribed", `TestApiVersion` 15 + `session-surface.v15.txt`.
- Клиент: `SimHost.PlayerActor`, подписка {играемый, WhyActor} синхронизируется в `Publish` на потоке симуляции (при смене сразу `RebuildView`, и на паузе, и в беге); `TechTree`, `AudioDirector` проверяют `IsPresent`.

## Разногласия
Замечания Codex 1–3 (проба P8 на 200 акторах, тест SimHost без Godot) отклонены Orchestrator: O-041, DEBT-0074; SimHost тонкий, логика подписки вынесена в `ViewSubscriptionSync` и покрыта тестами.

## Результат
Реализовано ADR-0025 разделы 1–5 (Session: `ViewScope`, `SetViewSubscription`, `RebuildView`, `NodeList.IsPresent`, кэш блоков только подписанных; клиент: `SimHost` + `ViewSubscriptionSync`; `TestApiVersion` 15). Тесты: `tests/Singularity.Session.Tests/ViewSubscriptionTests.cs`, `TwoWorldsSubscribedViewsTests.cs`, `tests/Singularity.Client.Audio.Tests/ViewSubscriptionSyncTests.cs`. `verify.py` зелёный.

Кадры 1920x1080, 100 %, прогон `./run-globe-spike.sh --shots` (exit 0, после правок SimHost), каталог `shots-out/` рабочей копии (в git не входит):
- `shots-out/tree_nolegend_1920x1080.png`: дерево знаний играемого актора (США, 1977-12-24), узлы из подписанного блока.
- `shots-out/tech_tree_a.png`: дерево другого выбранного актора (СССР, 1946-07-14, середина месяца): подписка сменилась и вид пересобран без тика; узлы и цель показаны. Актор выбран тем же путём, что и при клике на карте (`ShowPower` затем `OpenTree`), а не переключателем дерева.
Копии: `/tmp/claude-1000/-var-www-gameDev/c69b946c-6bbb-4630-b796-1ef4f3d98814/scratchpad/final_tree_played_USA_1920x1080.png`, `.../final_tree_other_actor_SUN_1920x1080.png`.
