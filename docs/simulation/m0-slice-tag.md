---
tags: [task, determinism, testing, done]
---
# Тег среза M0: коммит, где эталоны EXP-0002 ещё совпадают (TASK-0164, T-M1-02)

> simulation-engineer, 2026-11-25. Основание: ADR-0027 п. 5 (R4), ADR-0019 п. 20, MS-M1 K6, `exp0002-result.md` §1. Тег не ставился: ставит Orchestrator.

## Краткий вывод
- [Факт] **Последний коммит `main`, где эталоны EXP-0002 совпадают: `e15a8bddaefea08b251c11ecadea8ce5ddf97087`** (2026-11-18, «chore: TASK-0104 done after review fixes»; первый родитель слияния `2a7aad2`).
- [Факт] det: матрица `EXP-0002-det.json` (20 seeds × threads 1/4/8, 60 прогонов) на `e15a8bd` даёт `golden_set_hash` **`824f4ded07216828ee6354f95f6bd942dcefd6059f70fdff24fbf9f9ab91ea5b`** — равен эталону; ok 60/60, `thread_mismatch` 0, `valid=true`.
- [Факт] main: серия `main` манифеста EXP-0002 (`--point 0`, 1000 seeds, threads 1) на `e15a8bd`: golden каждого из 1000 прогонов равен golden того же `run` в `lab/results/exp0002/main/runs.csv` (1000/1000).
- [Факт] **Уточнение эталона `183e47b9…`:** это SHA-256 строк «run golden» по **всем 21 080 прогонам** `runs.csv` (93 точки), а не только серии `main` (проверено пересчётом по `lab/results/exp0002/main/runs.csv`). Хеш одной серии `main` (1000 строк) — `74043328ab48ce57646992cafdc88e2fc78642875904c39791713b85631e3ef3`; прогон на `e15a8bd` дал ровно его. Полную матрицу (≈ 4,5 ч при 8 jobs) я не прогонял, поэтому `183e47b9…` целиком на теге не воспроизведён — совпадение подтверждено для серии `main` и det.
- [Факт] Следующий коммит `main` — `2a7aad2` (TASK-0099, `SchemaVersion` 8 → 9, ADR-0005 п. 3) — эталоны сдвигает: пилот det (seeds 1–2) даёт `3a57b9f1…`, `e23105ab…` вместо `b1146878…`, `6e28b92b…`. После него `SchemaVersion` менялась ещё в `08a9d0f`, `c00fbb3` (11), `d63289e` (12), `88fa41b` (14). Значит, ни `9d7d3a3^1`, ни текущий `main` эталонов EXP-0002 не дают: тег — только на `e15a8bd` или раньше.

## Команда тега [Предл.] (имя из ADR-0019 п. 20)
```
git tag -a m0-slice-exp0002 e15a8bddaefea08b251c11ecadea8ce5ddf97087 -m "M0 slice: EXP-0002 det 824f4ded and main-series goldens 1000/1000 reproduced (TASK-0164)"
git push origin m0-slice-exp0002
```

## Как проверено
| Кандидат | Что | Итог |
|---|---|---|
| `e15a8bd` пилот | det `--seeds 2`, 6 прогонов, 5 с | 6/6 golden равны эталону |
| `e15a8bd` | det полностью, 60 прогонов, 52 с | `824f4ded…` |
| `e15a8bd` | main `--point 0`, 1000 прогонов, 12,8 мин | 1000/1000 равны; серия `74043328…` |
| `2a7aad2` пилот | det `--seeds 2` | 0/6 равны (сдвиг ожидаем: `SchemaVersion` 9) |

- Выбор кандидата: в `StateHash` входит `SchemaVersion` (`lab-cli.md`), на `8f7b3de` (код прогона EXP-0002) она 8, первый подъём — `2a7aad2`. Поэтому первым проверялся `2a7aad2^1`.
- Сборка: `git archive <commit>` в `lab/results/m0tag/`, `dotnet build src/Singularity.Lab -c Release` (.NET SDK 10.0.403, x86_64), запуск `nice -n 10 Singularity.Lab matrix --manifest <tree>/lab/manifests/EXP-0002-det.json --out … --jobs 8` и `… EXP-0002.json --point 0 --jobs 8`; `GIT_CEILING_DIRECTORIES` — чтобы provenance не взял HEAD рабочего дерева (`core_commit` в `run.json` = `unknown`).
- Повторить на теге: те же две команды из дерева тега; сверка — `golden_set_hash` det и построчно `runs.csv` с `lab/results/exp0002/main/runs.csv`.

## Не проверено
- Полная матрица EXP-0002 (21 080 прогонов, все 93 точки) и тем самым `183e47b9…` целиком; контрольные серии (слои, каналы, старт) на теге не прогонялись.
- Коммиты между `8f7b3de` и `e15a8bd` по отдельности не прогонялись (не нужно: ищется последний).
- Другая ОС/архитектура (ARM64, Windows) — только Linux x86_64.
