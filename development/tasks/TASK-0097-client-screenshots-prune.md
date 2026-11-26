---
id: TASK-0097
title: "Client screenshots: keep 25 key frames in git"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M1
agent: developer
category: client
priority: low
needs_owner: false
depends_on: []
related: [TASK-0088]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "#391"
created: 2026-11-18
tags: [task, platform, ui, done]
---

# TASK-0097 — Client screenshots: about 25 key frames in git (#391)

## Цель
Остановить рост репозитория из-за снимков клиента (DEBT-0047).

## Описание
Список ключевых кадров на версию даёт ui-art-director в `docs/ui/KEY_SHOTS.md`. Остальные снимки клиент генерирует `--shots` в игнорируемую папку. Старые закоммиченные снимки, не входящие в список, удаляются обычным коммитом (без переписывания истории).

## Критерии приёмки
- [ ] В git только кадры из `docs/ui/KEY_SHOTS.md`; папка вывода `--shots` в `.gitignore`.
- [ ] Ссылки на удалённые снимки в docs/ui исправлены или указывают на команду генерации.

## Результат
- Удалено 241 PNG под `docs/ui/prototypes/` (обычный коммит), оставлено 34 (25 в client-v05c + 9 старых, как в KEY_SHOTS.md); все перечисленные файлы существуют. Не-PNG (run-log.txt, audio_demo.ogg) сохранены. `docs/ui`: 86 МБ -> 18 МБ.
- `Main.cs`: `_shotDir` по умолчанию `../../shots-out` (было `docs/ui/prototypes/client-v05c`); `shots-out/` добавлен в `.gitignore`; комментарий в `run-globe-spike.sh`.
- Ссылки исправлены: CLIENT_V05B_SPEC, task-0077/0081/0088-visual, client-v02-review, globe-spike-report; в закрытые TASK-0070/0071/0077/0081/0088 добавлена пометка. KEY_SHOTS.md: объёмы по `du -sh`.
- Проверка: сборка клиента (`dotnet build`), см. отчёт оркестратору.
