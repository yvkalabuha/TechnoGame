---
id: TASK-0141
title: "Gemini design sketches through Antigravity CLI (agy) in llm_worker (#451)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M05
agent: developer
category: tooling
priority: high
needs_owner: false
depends_on: [TASK-0135]
related: [TASK-0135]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "#451, #450, #445"
review_ref: "reviewer: APPROVE 9ab93a7; codex: waived #432 after 3rd return (98fb3d62), remainder DEBT-0086; live runs ae58bd5d (6 images) and c84d4536 (1 of 3); landed 8d58b4c"
created: 2026-11-24
---

# TASK-0141 — Эскизы дизайна от Gemini через Antigravity CLI

## Цель
Владелец (#451): Gemini должен предлагать дизайн, в том числе картинками. API-ключ не рисует (квота 0); Antigravity CLI `agy` 1.5.15 по подписке рисует: проверено 2026-11-24, `agy -p … --output-format stream-json`, картинка сохраняется в `~/.gemini/antigravity-cli/brain/<conversation_id>/*.jpg` (агенту запрещён shell, копирует наш скрипт).

## Описание
- Команда `python3 -m tools.llm_worker sketch --task TASK-NNNN --prompt-file <md> [--ref <png>...] [--count N]`: вызывает `agy -p` с `--output-format stream-json --mode accept-edits` без `--dangerously-skip-permissions` и без shell для агента, читает `conversation_id` и пути картинок из событий, копирует их в `shots-out/sketches/<task>/`, пишет запись (prompt, модель, дата, conversation_id) — это же строка атрибуции по ADR-0026.
- Ссылочные кадры (`--ref`) — только из `shots-out/`; таймаут; отказ, если `agy` не найден или не залогинен.
- Режим «сверка + предложения» по #450/#451: `visual --mode design` (API) и `sketch` (agy) в одном прогоне для задачи дизайна.

## Критерии приёмки
- [ ] Тесты без сети на разбор stream-json (фикстура событий), копирование, отказ без agy.
- [ ] Живой прогон: 3 эскиза для пакета A/B дизайна (`docs/ui/specs/design-pass-2.md`).
- [ ] Ревьюер APPROVE; второе ревью по #432.

## Результат

## Разногласия

- Третье ревью (Codex, раунд 3), п. 1 [high]: запрет shell только пост-фактум. [Факт, замер 2026-11-24, agy 1.5.16] У `agy --help` нет флага списка отключаемых инструментов; `--settings` принимает ключи settings.json; неизвестный ключ (`disabledTools`) отвергается ("unknown setting"). Рабочий механизм: `{"permissions":{"allow":[],"deny":["command(*)","write_file(*)","read_url(*)","execute_url(*)","mcp(*)"]}}`. Опыт: промпт «выполни echo > marker.txt» с этим deny (даже при allow `command(echo)`) — файл не создан, агент ответил «refused»; живой прогон со всеми deny-правилами рисует картинки. Пост-фактум проверка (allowlist инструментов по потоку и transcript) оставлена как второй слой. Ограничение: синтаксис правил выведен из строк бинарника и пробы, а не из официальной схемы.
- Не исправлено (в TECH_DEBT, Orchestrator): п. 4 — параллельные прогоны одной задачи могут перезаписать картинку/запись.
