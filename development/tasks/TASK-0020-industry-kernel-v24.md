---
id: TASK-0020
title: "Industry и Kernel под контракт v2.4: правило культуры C-B, наладка производства"
status: APPROVED
level: light
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false
depends_on: []
related: [HYP-0002]
approved_by: orchestrator
approved_at: 2026-11-03
approval_ref: "DECISIONS_LOG #119"
created: 2026-11-03
---

# TASK-0020 — Industry и Kernel под контракт v2.4: правило культуры C-B, наладка производства

## Цель
Реализовать в Core.Industry и Kernel временное правило культуры C-B и рычаг «Наладка производства» по контракту v2.4 r1.

## Описание
Пункты I-1…I-6 контракта §12: `SmoothStep01`, `MulCeil` в Kernel (одно округление, `Int128`); `StepCulture` по C-B §4.1 (max, ℓ = 0,1, канал лицензии в росте и поддержании, пол G-5); колонка `proc_E`; входы `EffortBudget`, `EffortFocus`, `PlannedVolume`; распределение §4.1b внутри compute Industry, остаток в `ToolingInvest`, неизрасходованное возвращается (#118); функции снимка `E_useful`, `limiter`, `dH_forecast`, внутренние `eligible`, `lead`, `limiter₀`, `d*`; why_not по `limiter` (§6). Правило v2.3 — только как абляция Lab (за интерфейсом §4.1a). Без `double`.

## Критерии приёмки
- [ ] Все инварианты §4.1/§4.1a/§4.1b из контракта — тесты (включая G4-1: усилие во всех активных d; регрессии FRA×40/SUN×40 и «процесс без выпуска против малого выпуска»).
- [ ] Совпадение с эталоном `jet_ref.py` (после его обновления simulation-engineer) побитно.
- [ ] Детерминизм L1/L2 тесты зелёные; CI на 3 ОС.
- [ ] `save_format_version` +1 в пакете с TASK-0021.

## Тесты
`$HOME/.dotnet/dotnet test`; новые unit-тесты Kernel/Industry; эталонные трассы.

## Связанные документы
`docs/architecture/LAYER_CONTRACT.md` v2.4 r1 (§4.0, §4.1–§4.1b, §4.4a–d, §6, §9.1, §12); DECISIONS_LOG #111–#119; `docs/simulation/culture-round2.md` §1, §10.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — затронут src/

## Результат
