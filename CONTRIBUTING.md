# CONTRIBUTING

Правила для людей и AI-агентов. Подробности процесса — `docs/architecture/DEVELOPMENT_FRAMEWORK.md`.

## Настройка
- `git config core.hooksPath .githooks` — включает проверку реестра перед коммитом.
- Python 3.10+ и PyYAML для `tools/development/`.
- .NET 10 SDK для кода ядра (с PHASE E).

## Элементы и статусы
- Задачи, гипотезы, эксперименты, ADR, research reports, milestones — по одному файлу с YAML frontmatter. Создавать через `/new` или `registry.py new`.
- Статус меняет только Orchestrator. Одобренные элементы содержат `approved_by`, `approved_at`, `approval_ref`.
- Статусы задачи: `PROPOSED → APPROVED → IN_PROGRESS → REVIEW → DONE` (+ `BLOCKED`, `REJECTED`, `DEFERRED`).
- Риски, техдолг, решения владельца — строки в `development/RISKS.md`, `development/TECH_DEBT.md`, `docs/decisions/DECISIONS_LOG.md`.

## Решения
- ACCEPTED ADR не редактируется (кроме строк `status:` и `superseded_by:`). Изменение — новый ADR с `supersedes`. Это проверяют hook и `registry.py check`.
- Старые решения не удаляются: помечаются superseded с причиной.

## Код (с PHASE E)
- Правила детерминизма и структуры: `.claude/rules/csharp-core.md`.
- Каждое изменение в `src/`, `tests/`, `mods/` проходит reviewer и тесты.

## Данные
- Только PD, CC0, CC BY с атрибуцией (ADR-0009).

## Git
- Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:`, `test:`, `refactor:`).
- Без секретов, `lab/results/`, `bin/`, `obj/`.
- Без force push и переписывания истории.
