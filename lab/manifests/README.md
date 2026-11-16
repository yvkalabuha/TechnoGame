# Манифесты Lab

Формат — `docs/simulation/lab-cli.md` (schema 1); ключи моделей `JetSlice`/`JetSliceCalib` — `docs/simulation/slice-csv-format.md` §3.

## Исторические манифесты (не запускаются на текущей сборке)

С коммита 840dc6b (TASK-0058 шаг 5) I-OC и A41 SLOT для P2+P3 — поведение сборки. Удалены хук Lab `IntroCultureRule` и ключи `CULTURE_VARIANT`, `INTRO_CHANNEL`, `PILOT_CHOICE` (и `c1-stand --culture-variant`). На текущей сборке эти манифесты дают ошибку параметра. Для воспроизведения нужна сборка, где ключи ещё есть:

| Манифест / код | Удалённые ключи или классы | Сборка для воспроизведения | Где записан результат |
|---|---|---|---|
| `HYP-0004-sketch.json` | `CULTURE_VARIANT` (`CB`, `VEXP`, `VEXPC`, `LAB`) | ≤ 84d833d (с TASK-0057) | `docs/simulation/introduction-channel-sketch.md` §1–§4 |
| `HYP-0004-sketch-r2.json` | `CULTURE_VARIANT` (`VEXPOC`), `PILOT_CHOICE` (`ON`, `ON_ADMITTED`) | ≤ 84d833d | там же, раунд 2 |
| `HYP-0004-round3.json` | `CULTURE_VARIANT`, `PILOT_CHOICE = RANK` | ≤ 84d833d | там же, §9–§10 |
| `TASK-0059-rank-only.json` | `PILOT_CHOICE = RANK` | ≤ 84d833d | там же, §11 |
| `HYP-0002-a41-print.json`, `HYP-0002-a41-print-calib.json` | `INTRO_CHANNEL`, `PILOT_CHOICE = RANK` | ровно 84d833d (`INTRO_CHANNEL` появился в 84d833d) | там же, §12 (#244) |
| `lab/analysis/introduction/dump_host/` | класс `IntroCultureRule`, `SliceParameters.CultureVariant` | ≤ 84d833d; не входит в `Singularity.sln` | `lab/analysis/introduction/e2decomp.py` |

Почему заметка здесь, а не в `meta` манифестов: `meta` входит в `manifest_hash` (`src/Singularity.Lab/Manifest.cs`), а хеши этих манифестов уже записаны в отчётах эскиза HYP-0004. Правка `meta` сделала бы записанные хеши неверными.

Ни `selftest` Lab, ни `exp0002.py --selftest`, ни `registry.py` эти файлы не читают; пропуск не нужен.
Воспроизведение: `git worktree add ../singularity-84d833d 84d833d`, затем команды из отчёта на этой сборке.
