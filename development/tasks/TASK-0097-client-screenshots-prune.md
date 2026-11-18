---
id: TASK-0097
title: "Client screenshots: keep 25 key frames in git"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
---

# TASK-0097 — Client screenshots: about 25 key frames in git (#391)

## Цель
Остановить рост репозитория из-за снимков клиента (DEBT-0047).

## Описание
Список ключевых кадров на версию даёт ui-art-director в `docs/ui/KEY_SHOTS.md`. Остальные снимки клиент генерирует `--shots` в игнорируемую папку. Старые закоммиченные снимки, не входящие в список, удаляются обычным коммитом (без переписывания истории).

## Критерии приёмки
- [ ] В git только кадры из `docs/ui/KEY_SHOTS.md`; папка вывода `--shots` в `.gitignore`.
- [ ] Ссылки на удалённые снимки в docs/ui исправлены или указывают на команду генерации.
