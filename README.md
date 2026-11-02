# PROJECT SINGULARITY

Глобальный цивилизационный симулятор (1910–2100) с акцентом на науку и технологическую эволюцию. Ранняя стадия: создан процесс разработки, ядро симуляции ещё не написано.

- Видение: [docs/vision/PROJECT_VISION.md](docs/vision/PROJECT_VISION.md)
- Текущее состояние: [CURRENT_STATE.md](CURRENT_STATE.md)
- План: [development/ROADMAP.md](development/ROADMAP.md)
- Архитектура: [docs/architecture/ARCHITECTURE.md](docs/architecture/ARCHITECTURE.md)
- Процесс разработки: [docs/architecture/DEVELOPMENT_FRAMEWORK.md](docs/architecture/DEVELOPMENT_FRAMEWORK.md)

## Как работать с проектом

Разработка ведётся в Claude Code. Основная сессия — Orchestrator; она запускает специализированных агентов (`.claude/agents/`).

### Первый запуск на новой машине
```bash
git clone git@github.com:yvkalabuha/TechnoGame.git && cd TechnoGame
git config core.hooksPath .githooks          # проверка реестра перед коммитом
python3 -m pip install --user pyyaml          # если PyYAML не установлен
# .NET 10 SDK: https://dotnet.microsoft.com/download (или dotnet-install.sh в ~/.dotnet)
claude
```

### Команды владельца в Claude Code
| Команда | Что делает |
|---|---|
| `/idea <идея>` | Зарегистрировать идею и провести её по протоколу нужного уровня |
| `/new <TYPE> <название>` | Создать задачу, гипотезу, эксперимент, ADR, RR или milestone из шаблона |
| `/review-gate <файл>` | Независимое ревью Critic + Advocate (+ Reviewer) |
| `/handoff` | Завершить сессию: статусы, состояние, handoff, коммит |
| `/release <версия> <milestone>` | Релиз milestone |
| `/usage` | Расход токенов (встроенная команда; результат можно записать в handoff) |

### Реестр
```bash
python3 tools/development/registry.py list               # все элементы
python3 tools/development/registry.py list --type TASK --status IN_PROGRESS
python3 tools/development/registry.py check              # валидация
python3 tools/development/registry.py index              # пересобрать BACKLOG.md
python3 tools/development/test_hooks.py                  # самотест hooks и реестра
```

## Лицензия
Лицензия пока не выбрана: все права принадлежат владельцу репозитория. Внешние данные — только PD, CC0, CC BY (ADR-0009).
