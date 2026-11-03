---
id: TASK-0016
title: "Спайк Godot: Core внутри Godot .NET, сборка в CI, исключение тестового кода из release"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: tooling
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0001]
approved_by: owner
approved_at: 2026-11-03
approval_ref: DECISIONS_LOG #57
created: 2026-11-03
---

# TASK-0016 — Спайк Godot: Core внутри Godot .NET, сборка в CI, исключение тестового кода из release

## Цель
Снять неопределённость до ADR-0014 (Session и Test API): как ядро живёт внутри Godot .NET. Это спайк, не клиент: без UI, в отдельной ветке `spike/godot-compat`, в `main` не вливается (#57).

## Описание
Источник: `docs/architecture/TESTING_ARCHITECTURE_PROPOSAL.md` (§2, §10, §12), ревью critic п.9, advocate п.1, `docs/decisions/DECISIONS_LOG.md` #57.
1. Версия: последний stable Godot с официальной поддержкой .NET 10; если его нет — 4.7.2 с `net10.0`. Записать точную версию, сборку .NET, источник загрузки и контрольную сумму.
2. Минимальный Godot-проект с `ProjectReference` на `Singularity.Kernel`/`Singularity.Core`: шаг симуляции в отдельном потоке, чтение состояния в главном потоке без гонок; замерить время тика.
3. Headless-запуск и сборка из командной строки (`--headless`, `--build-solutions` или аналог этой версии) — проверить синтаксис по документации версии.
4. Исключение тестового кода из release: `Compile Remove` или `#if`, регистрация автозагрузки во время выполнения; проверка отсутствия тестовых типов в экспортированной сборке.
5. Один прогон в CI на Linux (Xvfb не нужен — headless); оценка цены для Windows/macOS.

## Критерии приёмки
- Отчёт `docs/architecture/spikes/godot-compat.md` (факты с источниками, команды, замеры, проблемы).
- Ветка со спайком, воспроизводимая одной командой.
- Явно перечислено, что из результатов меняет предложения ADR-0014.
