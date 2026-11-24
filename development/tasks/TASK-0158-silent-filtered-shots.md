---
id: TASK-0158
title: "Client shot runs: silent by default and a frame filter (only the frames a task needs)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M05
agent: developer         # ответственный агент
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: orchestrator
approved_at: 2026-11-24
approval_ref: "owner complaint 2026-11-24: sounds and long shot runs; light task"
created: 2026-11-24
---

# TASK-0158 — Client shot runs: silent by default and a frame filter (only the frames a task needs)

## Цель
Кадровые прогоны клиента не шумят и снимают только нужные кадры (жалоба владельца 2026-11-24: «куча звуков при тестовых запусках», «почему такие долгие запуски»).

## Описание
Сейчас `./run-globe-spike.sh --shots` снимает 351 кадр (ru/en, 100/125/150 %, все экраны) и пишет звуковое демо, звук идёт в колонки. Нужно: (1) кадровые прогоны по умолчанию с `--audio-driver Dummy`; звуковое демо только по явному флагу (`--audio-demo`); (2) фильтр `--only <имена через запятую или маска>` — снимать только перечисленные кадры; (3) `--time-shots` так же; (4) README и подсказка скрипта.

## Критерии приёмки
- [ ] Прогон `--shots` без звука в колонках
- [ ] `--shots --only area_science_1920x1080` снимает один кадр и завершается быстро (время указать)
- [ ] Полный набор по-прежнему доступен
- [ ] reviewer APPROVE

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
Сделано (ветка worktree-agent-a434e71c79ff245be):
- `ShotFilter.cs` (чистая логика, тесты `ShotFilterTests.cs`, 409/409 в Client.Audio.Tests): `--only a,b,c*`; в `Main.cs` фильтр стоит в `SaveShot`; шаблон с размером (`area_science_1920x1080`) ещё пропускает первый этап и лишние комбинации масштаб/язык; проверки покрытия масштабов и иконок в отфильтрованном прогоне пропускаются.
- Демо звука (запись шины Master) идёт только с `--audio-demo`; без него прогон завершается сразу после очереди размеров.
- README клиента обновлён.
- Замер: `--shots --only area_science_1920x1080` = 47.6 с от запуска Godot до выхода (1 кадр, exit 0); `./run-globe-spike.sh` дополнительно каждый раз копирует ассеты, импортирует и собирает C# (сборка ~66 с в этой среде).
- Не сделано, вне зоны developer: `run-globe-spike.sh` (корень репозитория). Нужно: Orchestrator применяет правку ниже.

Правка скрипта (заменить блок `if [[ "${1:-}" == "--shots" ]] ... elif --bench ... ` и обновить шапку-справку):
```bash
has_arg() { local w="$1" a; shift; for a in "$@"; do [[ "$a" == "$w" ]] && return 0; done; return 1; }
AUDIO=(--audio-driver Dummy)
if has_arg --audio-demo "$@"; then AUDIO=(); fi

if has_arg --shots "$@" || has_arg --time-shots "$@" || has_arg --map-frames "$@"; then
  "$GODOT" "${AUDIO[@]}" --path . -- "$@"
elif [[ "${1:-}" == "--bench" ]]; then
  "$GODOT" --audio-driver Dummy --path . -- --bench --out bench-out
  echo "Benchmark output: $PROJECT/bench-out"
else  # ...как было
```
Справка в шапке: описать `--shots` как тихий, `--only`, `--time-shots`, `--audio-demo`.

Проверка: запуск 1 (фильтр) — OK; запуск 2 (полный набор, остановлен через 40 с) — кадры пошли (28 файлов). Режимы --time-shots, --bench, --audio-demo и сам скрипт не запускались.
