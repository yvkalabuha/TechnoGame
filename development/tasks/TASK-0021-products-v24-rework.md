---
id: TASK-0021
title: "Доработка Core.Products (TASK-0018) под контракт v2.4"
status: DONE
level: light
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false
depends_on: [TASK-0018, TASK-0020]
related: [HYP-0002]
approved_by: orchestrator
approved_at: 2026-11-03
approval_ref: "DECISIONS_LOG #119"
created: 2026-11-03
---

# TASK-0021 — Доработка Core.Products (TASK-0018) под контракт v2.4

## Цель
Довести слой изделий TASK-0018 до контракта v2.4 r1 и слить в main (#117).

## Описание
Пункты T-1…T-15 контракта §12: rebase ветки `worktree-agent-adfc3fdb48ab41772` на main (не удалить `Lab/Smoke/Culture*.cs`); ёмкость `max_designs`; `compose_design` с пресетами; `derive` с переводом линии (#118); `g_p` и `gen`; R-PS2m по `af_class`/`r_PU`/горячей части; `readiness` по `D_ref`; допуск к серии и `force` с доработкой; порядок compute §4.4d; `FV` с `perf^γ` и сплавом; функции `planned`/`v_exp`; новые инварианты; версия сохранения.

## Критерии приёмки
- [ ] Все пункты T-1…T-15 выполнены или явно перенесены в TECH_DEBT с причиной.
- [ ] Эталон изделий совпадает побитно (после обновления `jet_ref.py`).
- [ ] Инварианты I1–I7 + новые T-14 — тесты.
- [ ] Слито в main без потери кода main.

## Тесты
`$HOME/.dotnet/dotnet test`; тесты Products; эталон.

## Связанные документы
`docs/architecture/LAYER_CONTRACT.md` v2.4 r1 (§4.0, §4.1–§4.1b, §4.4a–d, §6, §9.1, §12); DECISIONS_LOG #111–#119; `docs/simulation/culture-round2.md` §1, §10.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — затронут src/

## Результат

- 2026-11-03: слито в main (4bad87d). Ревью reviewer approve, test-engineer — property и L1/L2 (golden hash связанного прогона). Отложено: DEBT-0013, DEBT-0015, DEBT-0016.
