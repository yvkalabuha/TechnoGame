---
id: TASK-0058
title: "Порт I-OC и RANK (A41 в общем виде) в Core; golden, CONTROL_TABLES"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0060]
related: [HYP-0004, TASK-0056]
approved_by: owner
approved_at: 2026-11-16
approval_ref: "#242, #243"
created: 2026-11-16
---

# TASK-0058 — Порт I-OC и RANK (A41 в общем виде) в Core; golden, CONTROL_TABLES

## Цель
Перенести I-OC из хука Lab (`IntroCultureRule`, отражение по `IndustrySystem._rule`) в Core и сделать I-OC + RANK поведением сборки калибровки C2 по контракту r12 (TASK-0060).

## Описание
1. Core: правило культуры C-B с веткой `V_O + V_C`; чтение только снимка прошлого состояния (как `Planned`/`PlannedVolume`), без живого `ProductsSystem.Stage`.
2. A41 в коде — в общем виде по контракту r12 (слот, линейка изделия); в данных M0 — слот сплава.
3. Эквивалентность: порт побитно равен Lab-хуку + RANK на seeds 30201–30300 (CSV и `golden_hash`); L1 (2 прогона) и L2 (1 и 4 потока).
4. Тесты инвариантов V5′/V6, «`V_O` по проекту один раз», фикстура, где A40 `PROCESSES` срабатывает (нет денег наладки или истёк `T_ab`).
5. Новые golden всех сборок (один раз, с `SurfaceVersion`); регенерация CONTROL_TABLES и `L_C`/T-SK; Lab-хук и флаги `CULTURE_VARIANT`/`PILOT_CHOICE` — удалить или оставить только для воспроизведения HYP-0004 (решение — в задаче, по минимуму).
6. Без явных ключей-меток в `params` манифестов (урок `KAPPA_Q10`).

## Критерии приёмки
- [ ] Эквивалентность порта и хука на 100 seeds; L1/L2.
- [ ] Тесты V5′/V6, `V_O` один раз, A40 `PROCESSES` срабатывает.
- [ ] Golden пересчитаны один раз; `dotnet test` зелёный.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Второе ревью (codex)
- [ ] Тесты (test-engineer) — инварианты, L1/L2

## Результат
- [Факт] Шаги 1–4 — коммит `84d833d` (developer, 2026-11-16), текст — `lab/results/c2/TASK-0058-result.md`. За выключателями `INTRO_CHANNEL`, `A41_FORM=SLOT|WHOLE`, `PILOT_PROFILES=P2P3|P3`; golden main не менялись. Session test API v6.
- [Факт] Эквивалентность с хуком HYP-0004 (seeds 30201–30300, threads 1 и 4): I-OC отдельно — 20/20; WHOLE — 100/100; SLOT — 14/100 (предположение контракта §4.4a «на срезе совпадает» неверно). Форма A41 — решение владельца после печати #244.
- [Факт] `dotnet test`: Arch 97, Kernel 316, Core 1046, Lab 20, Session 345 — зелёные (проверено Orchestrator).
- [Факт] Reviewer (2026-11-16): approve, 0 high. Закрыть на шаге 5:
  - (medium) колонка `run.intro` (IntroChannel, PilotForm, PilotProfiles) в `RegisterRunColumns` — ADR-0013 п.4;
  - (medium) `SliceObserver` R5: SLOT — отличие только в слотах F, WHOLE/старый RANK — `alts == Superior`; F — публичная чистая функция Core;
  - (low) убрать rank-ветку `PilotKeepsSuperior` (дубль `ApplyPilotSlots(Whole)`); валидация Form/Profiles без Intro+Rank; R3 — случай Withdrawn с `cancelled=false`; V7 — проверить наличие prc в двух слотах; тавтология в `IntroChannel_IsOffByDefault…`; документ API v6 и `FilePath(5)`.
- Второе ревью (Codex) — после шага 5.
