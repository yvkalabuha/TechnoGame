---
id: TASK-0145
title: "Sketches through Codex CLI image generation (llm_worker sketch --engine codex)"
status: APPROVED
level: light
milestone: MS-M05
agent: developer
category: tools
priority: medium
needs_owner: false
depends_on: [TASK-0143]
related: [TASK-0141, TASK-0143]
approved_by: orchestrator
approved_at: 2026-11-24
approval_ref: "owner in chat 2026-11-24: use Codex with its separate limit for sketches; light task approved by Orchestrator"
created: 2026-11-24
---

# TASK-0145 — Эскизы через Codex CLI (второй генератор картинок)

## Цель
Владелец 2026-11-24: квоты Gemini мало; у Codex отдельный небольшой лимит — использовать его для эскизов.

## Факты [Факт: проба 2026-11-24 13:44]
- В agy нет модели Codex (`agy models`: Gemini, Claude, GPT-OSS); генератор картинок agy один при любой модели, поэтому вызов «Codex через agy» квоту не добавляет.
- Codex CLI 0.170.0 имеет встроенный инструмент `image_generation` (`codex features list`: stable, true) и `codex exec -i FILE` для референса.
- Проба `codex exec --sandbox read-only --json PROMPT -i ref.png`: `turn.failed` «You've hit your usage limit … try again at 3:44 PM». Лимит Codex общий со вторым ревью (ADR-0015): эскизы его съедают.

## Описание
1. `python3 -m tools.llm_worker sketch --engine codex|agy` (по умолчанию agy; `--engine auto`: agy, при квоте — codex).
2. Codex: `codex exec --sandbox read-only --skip-git-repo-check --json`, без `--dangerously-bypass-approvals-and-sandbox`, в bwrap как `codex.py`, env по allowlist; промпт: один вызов генерации, без shell. Картинки найти по событиям JSONL (тип item с картинкой / путь) или в папке сессии Codex — выяснить живой пробой после сброса лимита; копировать в `shots-out/sketches/<task>/`, та же проверка целостности и запись (engine, модель, thread_id) — строка атрибуции ADR-0026.
3. Лимит Codex (`usage limit … try again at`) — статус `codex_quota` (код 4) с временем сброса.
4. Защита второго ревью: эскизы через Codex не запускаются, если идёт `land.py` (lock), и по умолчанию не больше N картинок в день (`--codex-daily-cap`, по умолчанию 4).

## Критерии приёмки
- [ ] Unit-тесты разбора JSONL Codex (картинка, лимит, отказ) на фикстурах; живой прогон одной картинки.
- [ ] Ревьюер APPROVE; `verify.py --full`.
