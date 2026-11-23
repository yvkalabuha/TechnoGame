---
id: TASK-0126
title: "Open Codex findings after TASK-0117 landing (#432)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M05
agent: developer
category: client
priority: medium
needs_owner: false
depends_on: [TASK-0117]
related: [TASK-0117]
approved_by: orchestrator
approved_at: 2026-11-22
approval_ref: "#432 (light)"
created: 2026-11-22
---

# TASK-0126 — Open Codex findings after TASK-0117 landing (#432)

## Цель
По решению владельца #432 задача TASK-0117 слита после круга, на котором ревьюер дал APPROVE; открытые замечания второго ревью Codex перенесены сюда, чтобы не потеряться.

## Описание
Замечания из `tools/llm_worker/runs/review-TASK-0117-6883be22-codex.json`:
- [high] `src/Singularity.Client.Spike/TechTree.cs:488` — The technology-tree clock draws its decade ticks at half the A2 minimum width.
- [medium] `src/Singularity.Client.Spike/Main.cs:890` — The screenshot matrix can pass without verifying that the requested UI scale was applied.
- [medium] `src/Singularity.Client.Spike/Main.cs:892` — The scaled main-menu screenshots capture the title menu over the live game HUD.
- [medium] `src/Singularity.Client.Spike/Main.cs:1417` — The screenshot run can report success after failing to save the flask probe frames.
- Ревьюер (круги 9–13): `Para` не ловит слово шире строки на всех путях; арт-директор (круг 13): подписи «Теплообмен в турбине», «Сборка и испытание ТРД» стоят далеко от узлов; подпись «№2» без названия. Аллокации в `_Draw` — DEBT-0073.

Каждое замечание: исправить или отклонить с причиной (в «Разногласия»). Высокие замечания по детерминизму и сохранению по #432 не переносятся — их в этом списке нет.

## Критерии приёмки
- [ ] Каждое замечание закрыто: исправлено с тестом или отклонено с причиной.
- [ ] reviewer APPROVE; второе ревью по правилу #432.
