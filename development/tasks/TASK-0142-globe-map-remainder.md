---
id: TASK-0142
title: "Globe map remainder after TASK-0138: hatch legend, occupation control line, small-state callouts, coast staircase"
status: PROPOSED
level: light
milestone: MS-M05
agent: developer
category: client
priority: medium
needs_owner: false
depends_on: [TASK-0138]
related: [TASK-0138]
approved_by:
approved_at:
approval_ref:
created: 2026-11-24
---

# TASK-0142 — Остаток карты глобуса после TASK-0138

## Цель
Закрыть остаток арт-директора по карте 1945 года (`docs/ui/reviews/task-0138-visual.md`, раунд 2, G2–G8; O-049), чтобы карта получила PASS без остатка.

## Описание
- Легенда штриховок (колония, мандат, оккупация) — C1/C2.
- Линия контроля у зон оккупации (Китай и др.).
- Выноски для малых государств (< 64 du²) по спеке `docs/ui/specs/task-0138-country-palette.md` §3 п.7.
- Оккупированная Германия не должна читаться как Венгрия (тон/штриховка).
- Лесенка берега и точки-островки (A2): разрешение ID-текстуры или сглаживание.
- Подписи: размещение внутри видимой части региона, а не только окна (отклонение developer в G4).
- Измерить кегль и контраст подписей колоний (A1/A3); пересчитать ΔE tint.9 при свете 0,70 и 1,05.

## Критерии приёмки
- [ ] Кадры 1920x1080 при 100 % и серые: Европа, Азия, Африка, лимб; ui-art-director PASS (с мнением Gemini, #450).
- [ ] Unit-тест чистой функции размещения подписей и выбора штриховки; проверка на живой сессии.
- [ ] Ревьюер APPROVE.

## Связанные документы
`docs/ui/reviews/task-0138-visual.md`, `docs/ui/specs/task-0138-country-palette.md`, `docs/decisions/ORCH_DECISIONS.md` O-049.

## Результат
