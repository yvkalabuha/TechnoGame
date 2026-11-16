---
id: TASK-0060
title: "Контракт §4.1/§4.1a: I-OC (V5′/V6) и A41; полосы A40"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M0
agent: systems-architect
category: docs
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [HYP-0004, TASK-0057, TASK-0058]
approved_by: owner
approved_at: 2026-11-16
approval_ref: "#242, #243"
created: 2026-11-16
---

# TASK-0060 — Контракт §4.1/§4.1a: I-OC (V5′/V6) и A41; полосы A40

## Цель
Внести в контракт слоёв принятые владельцем I-OC и правило ИИ A41 (RANK) по #242/#243, с проверяемыми инвариантами, до порта в Core (TASK-0058).

## Описание
1. `docs/architecture/LAYER_CONTRACT.md` §4.1/§4.1a (новая ревизия r12): рост культуры C-B от `V_O + V_C` (номинал плана опытной линии + серия); `V_O` по проекту учитывается один раз (два прототипа одного проекта не удваивают); V5 заменить на V5′ (рост `x_V` > 0 только при своём проекте в стадии О/С с процессом в `prc_set` на `home`, иначе прежний предел `ℓ`) и V6 (`ṽ_grow` не входит в `m_p`, `maint_p`, `proc_X`); отметить изменение #99 ссылкой на #243.
2. Правило A41 в общем виде: P2/P3 сохраняют `superior`, если в слоте, где альтернатива `superior` вводит процесс выше ранга `producible`, ранг `producible` не выше лучшего ранга актора в этом слоте среди живых проектов той же линейки. Оговорка: в M0 реализован только слот сплава среза. Уточнение #99 в части ИИ.
3. A40: ветка `PROCESSES` — резервная (нет денег наладки или истёк `T_ab`); правило переобъявления полос (механически, по v0.26) — сослаться на спеку.
4. Спека `slice-jet-aviation.md`: ссылки на контракт r12 (только статус и §, без изменения калибровки).
Источник: `docs/simulation/introduction-channel-sketch.md` §6, §7.3, §9–§10, «Разногласия»; код варианта: `src/Singularity.Lab/.../IntroCultureRule.cs`, `src/Singularity.Core/Policy/ProductPresets.cs`.

## Критерии приёмки
- [ ] Контракт r12: формулы I-OC, V5′/V6, A41 общий вид, ограничение M0, A40 резервная ветка.
- [ ] Каждый инвариант проверяем тестом (указать, каким).
- [ ] Gate (critic + DeepSeek) по ревизии контракта.

## REVIEW
- [ ] Gate (critic, DeepSeek)

## Результат
