---
id: TASK-0002
title: "Анализ EXP-0001: расчёт критериев HYP-0001 на Python stdlib"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: simulation-engineer
category: lab
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [HYP-0001, EXP-0001]
approved_by: owner
approved_at: 2026-11-02
approval_ref: DECISIONS_LOG #12–#15; EXP-0001 «Ревью»
created: 2026-11-02
---

# TASK-0002 — Анализ EXP-0001: расчёт критериев HYP-0001 на Python stdlib

## Цель
Скрипт `lab/analysis/exp0001.py`, который по CSV-результатам прогонов считает критерии HYP-0001 и печатает markdown-таблицу для EXP-0001. Не зависит от того, кто сгенерировал CSV (прототип или C# Lab).

## Описание
- Только stdlib: свои KS, Wilson CI, bootstrap с фиксированным seed.
- Правила из EXP-0001 «Ревью»: точечная оценка + флаг «на грани»; гистерезис смены лидера; левая цензура вех; ярлыки узких мест с NOISE; агрегирование по сетке β×γ (подтверждение в большинстве точек); A6 — отдельный критерий догоняемости.
- Формат входного CSV задокументировать в `docs/simulation/` (одна таблица колонок).

## Критерии приёмки
- [ ] На синтетическом CSV с заранее известным ответом все критерии считаются верно (тест `lab/analysis/test_exp0001.py`).
- [ ] Повторный запуск даёт побайтно тот же вывод.
- [ ] Формат CSV задокументирован.

## REVIEW
- [x] Code review (reviewer) — changes-requested → исправлено (a)–(f), каждое с тестом
- [x] Тесты — 41 OK (проверено Orchestrator); вывод побайтно стабилен
- [x] Детерминизм — не применимо (анализ), вывод детерминирован

## Результат
`lab/analysis/exp0001.py` + `test_exp0001.py` (34 теста OK, проверено Orchestrator), формат CSV — `docs/simulation/exp0001-csv-format.md`. Пороги — `DEFAULTS`, перекрываются `--config`; `--dump-config` для заморозки манифеста.
Трактовки для утверждения при заморозке: п.2 итоговый лидер = argmax 1950; п.5 NOISE в топ-2 → необъяснено (возможен порог); N/A = не прошло; «на грани» в main → inconclusive.
После ревью: отсутствие данных (N/A, нет контроля) → `incomplete`, не `refutes`; цензура п.3 по группам; CI п.5 — кластерный bootstrap по seeds; п.5 без отстающих страна-лет → N/A → `incomplete`.
