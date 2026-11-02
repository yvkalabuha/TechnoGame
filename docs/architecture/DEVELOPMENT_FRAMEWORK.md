# DEVELOPMENT FRAMEWORK — проект процесса разработки

**ID:** DF-001
**Версия:** 2 (после ревью Critic и Advocate, 2026-11-02)
**Статус:** APPROVED (владелец, 2026-11-02)
**Автор:** Orchestrator
**Основания:** `development/phase-a/REQUIREMENTS_ANALYSIS.md`, `docs/research/PHASE_B_SUMMARY.md`, RR-0004 (механизмы Claude Code)

> Это проект. Реализация — PHASE D. Каждый элемент обязан иметь практическую функцию сейчас. Элементы без функции — в «Отложено» с условием появления.

---

## 1. Принципы

1. **Файлы и git — источник истины.** Память диалога не хранит состояние проекта.
2. **Минимальный каркас.** Документ, агент, skill, hook появляется, когда для него есть работа. Пустые заглушки запрещены. Пустые разделы шаблона удаляются.
3. **Обязательное — через автоматику.** Правило «всегда» реализуется hook, валидатором, pre-commit или CI. `CLAUDE.md` — только роль, принципы, навигация.
4. **Автоматика защищает от ошибок, не от злого умысла.** Агенты с Bash технически могут обойти hooks на Edit/Write. Последний барьер — `registry.py check` в pre-commit и CI плюс ревью.
5. **Progressive disclosure.** В каждую сессию грузятся `CLAUDE.md` и факты из `CURRENT_STATE.md` (hook). Остальное — по ссылке.
6. **Один элемент — один файл** для задач, гипотез, экспериментов, ADR, milestones. Агент читает список (`registry.py list`), затем только нужный файл.
7. **Вес процесса = вес решения.** Два уровня протокола (раздел 6).
8. **Статусы меняет только Orchestrator.** Агенты пишут содержание.

---

## 2. Роли

### 2.1. Orchestrator — основная сессия Claude Code

- Не файл в `.claude/agents/`. Поведение задают `CLAUDE.md` и skills.
- Единственный, кто: общается с владельцем; запускает агентов; меняет статусы; пишет задачи, риски, техдолг, `DECISIONS_LOG.md`, `CURRENT_STATE.md`; коммитит и пушит.
- Вопросы владельцу — только через `AskUserQuestion`.

### 2.2. Subagents (9)

Общее для всех:
- Агенты не запускают агентов: `Agent` не входит в `tools`.
- `model: inherit`. Понижение модели — только по данным расхода (раздел 10).
- Ответ — в формате 2.4, не длиннее 40 строк. Подробности — в файлы.
- Зона записи задаётся `PreToolUse` hook во frontmatter самого агента (`guard_zone.py --allow <globs>`, `onFailure: block`).

| Агент | Ответственность | Инструменты | Зона записи | Когда вызывать |
|---|---|---|---|---|
| `game-designer` | Механики, игровые циклы, взаимодействие систем, игровая ценность | Read, Grep, Glob, Write, Edit, WebSearch, WebFetch | `docs/game-design/**` кроме `research-system/`; тело файлов `development/hypotheses/**`; `docs/research/RR-*.md` (с PHASE F) | Новая механика, гипотеза о геймплее |
| `research-scientist` | Модель науки и технологий, исторические и научные факты, источники | Read, Grep, Glob, Write, Edit, WebSearch, WebFetch | `docs/research/**`, `docs/game-design/research-system/**` | Исследование темы, проверка фактов, граф технологий |
| `systems-architect` | Архитектура ПО, ядро, многопоточность, производительность, ADR | Read, Grep, Glob, Write, Edit, WebSearch, WebFetch | `docs/architecture/**`, `docs/decisions/ADR-*.md` | Архитектурное решение, ADR |
| `simulation-engineer` | Математические модели, детерминизм, Simulation Lab, эксперименты | Read, Grep, Glob, Write, Edit, Bash | `src/Singularity.Lab/**`, `lab/**`, `docs/simulation/**`, тело `development/experiments/**` | Модель системы; код Lab; запуск и анализ экспериментов |
| `developer` | Реализация утверждённых задач, unit-тесты к своему коду | Read, Grep, Glob, Write, Edit, Bash | `src/**` кроме Lab, `tests/**`, `mods/**`, `tools/**` | Задача в статусе `APPROVED` |
| `test-engineer` | Инварианты, property-based, golden-run, проверки детерминизма L1/L2, полнота тестов | Read, Grep, Glob, Write, Edit, Bash | `tests/**`, `lab/golden/**` | После реализации каждой задачи в ядре; изменения ADR-0002..0007 |
| `critic` | Слабые места, противоречия, риски, избыточная сложность | Read, Grep, Glob, WebSearch | нет | Уровень Gate |
| `advocate` | Сильнейшая версия отвергнутых альтернатив | Read, Grep, Glob, WebSearch | нет | Уровень Gate |
| `reviewer` | Независимое ревью кода и архитектурной совместимости | Read, Grep, Glob, Bash (whitelist hook: `git diff/log/show`, `dotnet build/test`) | нет | Каждое изменение в `src/`, `tests/`, `mods/` |

Critic, Advocate и Reviewer получают только артефакт и критерии, без рассуждений автора.

Developer для задач, затрагивающих больше 5 файлов, работает с `isolation: worktree` (настройка `worktree.baseRef: head`).

### 2.3. Отложенные роли

| Роль | Условие появления |
|---|---|
| Economy Analyst | Начало проектирования экономической модели (PHASE F) |
| Geopolitics Analyst | Начало проектирования ИИ государств и дипломатии |
| Civilization Systems Designer | Начало модели государства и общества |
| Modding Architect | Нужен второй мод помимо `core` |
| Balance Analyst | Lab выдаёт статистику по 3 и более системам |
| Release Manager | Не агент — skill `/release` |
| QA Engineer | Покрыт `test-engineer`; отдельная роль — если появятся UI/интеграционные тесты клиента |

### 2.4. Формат ответа агента

```
STATUS: done | blocked | needs-decision
SUMMARY: 1–3 строки
FILES: изменённые/созданные файлы
FINDINGS: пункты (critic/advocate/reviewer: severity, пункт, обоснование, предложение)
OPEN: вопросы к Orchestrator или владельцу
```

---

## 3. Skills

Все в `.claude/skills/<name>/SKILL.md`, инструкции на английском.

| Skill | Назначение | Вызов | Побочные эффекты |
|---|---|---|---|
| `/handoff` | Конец сессии: статусы, `registry.py index`, `CURRENT_STATE.md`, handoff-файл, коммит | **только владелец** | коммит |
| `/new <type> <title>` | Элемент из шаблона со следующим ID | авто | новый файл |
| `/idea <title>` | Регистрация идеи, выбор уровня протокола, план агентов. Агентов запускает только после подтверждения уровня владельцем, если уровень Gate | **только владелец** | файл элемента |
| `/review-gate <path>` | critic + advocate (+ reviewer для кода) параллельно; сводка находок и разногласий в элемент | авто | правка элемента |
| `/release <version>` | Milestone-релиз: проверка критериев, `CHANGELOG.md`, release-файл, git tag, push | **только владелец** | tag, push |

Отказались от `/start`: SessionStart hook уже выдаёт факты из `CURRENT_STATE.md`. Последний handoff Orchestrator читает по ссылке из `CURRENT_STATE.md`.

Отложено: `/experiment` — вместе с Simulation Lab (PHASE E).

---

## 4. Rules, hooks, проверки

### 4.1. Rules (`.claude/rules/`)

| Файл | `paths` | Содержание |
|---|---|---|
| `docs.md` | `docs/**`, `development/**` | Русский язык для владельца; пометки Факт/Предп./Предл.; статусы; superseded вместо удаления; источники с датой |
| `csharp-core.md` | `src/**`, `tests/**` | Правила детерминизма из ADR-0002..0007; зависимости сборок; стиль; тесты обязательны |
| `data.md` | `mods/**` | Лицензии (ADR-0009), стабильные строковые ID, схема, атрибуция |

### 4.2. Claude Code hooks (Python stdlib, `.claude/hooks/`)

| Hook | Где | Событие | Действие | Блокирует |
|---|---|---|---|---|
| `session_context.py` | `settings.json` | `SessionStart` | Вывести факты из `CURRENT_STATE.md` (≤15 строк) | нет |
| `guard_global.py` | `settings.json` | `PreToolUse` `Edit\|Write` | Запрет записи в `.env*`, `*.key`, `*secret*`; запрет `Write` поверх ADR со статусом `ACCEPTED`; `Edit` такого ADR — только строк статуса | да, `onFailure: block` |
| `guard_zone.py` | frontmatter агента | `PreToolUse` `Edit\|Write` | Путь вне зоны агента — отказ | да, `onFailure: block` |
| `guard_bash.py` | frontmatter `reviewer` | `PreToolUse` `Bash` | Только команды из whitelist | да |
| `stop_reminder.py` | `settings.json` | `Stop` | Один раз за сессию (флаг по `session_id` в `.claude/.state/`, в `.gitignore`): если менялись `src/` или `docs/`, а `CURRENT_STATE.md` нет — напомнить про `/handoff` | нет |

### 4.3. `tools/development/registry.py`

- `check` — уникальные ID; допустимые статусы; обязательные поля frontmatter; ссылки на существующие файлы; `APPROVED` без полей одобрения — ошибка; эксперимент `DONE` без полей воспроизводимости — ошибка; ACCEPTED ADR изменён не только в статусе (по `git diff`) — ошибка; генерируемые файлы совпадают с результатом генерации.
- `list [--type] [--status]` — вывод в консоль.
- `index` — генерирует `development/BACKLOG.md` и блок «Активные задачи» в `CURRENT_STATE.md` между маркерами. Шапка: `GENERATED — do not edit`.
- Запуск: `/handoff`; git pre-commit (`.githooks/`, `git config core.hooksPath .githooks`, описано в `CONTRIBUTING.md`); CI.

### 4.4. CI (GitHub Actions)

- PHASE D: `registry.py check` на ubuntu, на каждый push.
- PHASE E: `dotnet build` + `dotnet test` на ubuntu, windows, macos. Проверки L1/L2 (два прогона; 1 поток против N потоков; сравнение хешей) — на ubuntu. Сравнение хешей между ОС — только после решения по L3.

---

## 5. Элементы и жизненные циклы

### 5.1. Типы и ID

Формат ID — 4 цифры. Отчёты `RR-001…004` переименованы в `RR-0001…0004` (PHASE D).

| Тип | ID | Хранение | Кто пишет тело |
|---|---|---|---|
| Milestone | `MS-M0` | `development/milestones/` | Orchestrator |
| Задача | `TASK-0001` | `development/tasks/` | Orchestrator; раздел «Результат» — исполнитель |
| Гипотеза | `HYP-0001` | `development/hypotheses/` | game-designer или профильный агент |
| Эксперимент | `EXP-0001` | `development/experiments/` | simulation-engineer |
| ADR | `ADR-0001` | `docs/decisions/` | systems-architect |
| Research Report | `RR-0001` | `docs/research/` | research-scientist / профильный |
| Решение владельца | строка в `docs/decisions/DECISIONS_LOG.md` (дата, решение, ссылка) | один файл, append-only | Orchestrator |
| Риск | строка в `development/RISKS.md` | одна таблица | Orchestrator |
| Техдолг | строка в `development/TECH_DEBT.md` | одна таблица | Orchestrator |
| Code Review, Test Report | раздел в файле задачи | — | reviewer / test-engineer через Orchestrator |
| Релиз | `vX.Y.Z` | `development/releases/` | Orchestrator через `/release` |
| Handoff | `YYYY-MM-DD-N` | `development/handoff/` | Orchestrator через `/handoff` |

Пример мастер-промта «EXP-0001 — Зависимость науки от промышленности» — гипотеза; регистрируется как `HYP-0001`.

Шаблоны — `development/templates/`. Файл элемента: YAML frontmatter (машиночитаемые поля) + тело на русском.

### 5.2. Статусы

**Задача** (решение владельца: упрощено):

`PROPOSED → APPROVED → IN_PROGRESS → REVIEW → DONE`; боковые: `BLOCKED`, `REJECTED`, `DEFERRED`. Ревью и тесты — чек-боксы в разделе REVIEW.

Соответствие статусам мастер-промта: IDEA, RESEARCH → живут в гипотезах и RR; AWAITING_APPROVAL → `PROPOSED` с полем `needs_owner: true`; TESTING → чек-бокс в `REVIEW`; COMPLETED → `DONE`.

**Гипотеза** (как в мастер-промте): `IDEA → HYPOTHESIS → UNDER_RESEARCH → EXPERIMENTAL → VALIDATED | REJECTED | DEFERRED`.

**Эксперимент:** `PLANNED → RUNNING → DONE | FAILED`, поле `outcome: supports | refutes | inconclusive`.

**ADR:** `PROPOSED → ACCEPTED → SUPERSEDED | DEPRECATED`; `REJECTED`.

**Milestone:** `PLANNED → ACTIVE → RELEASED`.

### 5.3. Обязательные поля

- **Одобрение** (задача, ADR, milestone, гипотеза при переходе в `EXPERIMENTAL`): `approved_by` (`owner` | `orchestrator`), `approved_at`, `approval_ref` (handoff или цитата ответа владельца).
- **Воспроизводимость эксперимента:** `core_commit`, `data_hash`, `manifest_hash`, `seeds`, `threads`, `golden_hash`, `outcome`. Lab записывает их сама; `registry.py check` проверяет.

### 5.4. Одобрение (решение владельца: пакетом)

- Владелец утверждает: план milestone (цели, критерии, список задач), каждый ADR, каждую новую или изменённую механику, всё фундаментальное и необратимое.
- Orchestrator утверждает сам (`approved_by: orchestrator`): задачи уровня Light внутри утверждённого milestone.
- Новая задача вне утверждённого плана — к владельцу.

### 5.5. Обязательные документы мастер-промта

| Документ | Место | Когда |
|---|---|---|
| PROJECT_VISION.md | `docs/vision/` (+ оригинал `MASTER_PROMPT.md`) | PHASE D |
| GAME_DESIGN_DOCUMENT.md | `docs/game-design/` | PHASE F, первая утверждённая механика |
| TECHNICAL_DESIGN_DOCUMENT + ARCHITECTURE | объединены: `docs/architecture/ARCHITECTURE.md` | PHASE D |
| ROADMAP.md | `development/` (список milestones со ссылками) | PHASE D |
| BACKLOG.md | `development/` (генерируется) | PHASE D |
| CURRENT_STATE.md | корень | PHASE D |
| OPEN_QUESTIONS.md | `development/` | PHASE D |
| DECISIONS_LOG.md | `docs/decisions/` (вручную, append-only) | PHASE D |
| RISKS.md | `development/` | PHASE D |
| CHANGELOG.md | корень | PHASE D |
| CONTRIBUTING.md | корень | PHASE D |
| MODDING_GUIDE.md | `docs/modding/` | Когда ядро загружает мод `core` |

---

## 6. Протокол идей: два уровня (решение владельца: упрощено)

| Уровень | Когда | Шаги (номера мастер-промта) | Агенты | Одобрение |
|---|---|---|---|---|
| **Light** | Задача внутри утверждённой архитектуры и механики; багфикс; рефакторинг | DESIGN(7) → IMPLEMENTATION(10) → REVIEW(8) → TESTING(11) | developer, test-engineer, reviewer | Orchestrator |
| **Gate** | Новая/изменённая механика; новая подсистема; гипотеза; ADR | IDEA(1) → CONTEXT(2) → ANALYSIS(4) → ADVOCACY+CRITIQUE(5,6) → DESIGN(7) → REVIEW(8) → APPROVAL(9) → далее 10–14 | профильный + critic + advocate | владелец |
| **Gate + research** | Фундаментальная концепция, принципы симуляции, ADR-0002..0007, формат сохранений, необратимое | Gate + RESEARCH(3) с источниками | + research-scientist | владелец |

Жёсткие правила: ADR, изменение правил детерминизма, формата сохранений или данных — всегда Gate или выше. Владелец может повысить уровень любой задачи.

**Разногласия** записываются в раздел «Разногласия» элемента — только при реальном несогласии: позиция, кто, аргумент, решение или «вынесено владельцу».

---

## 7. Структура папок

```
/
├── CLAUDE.md                 # ≤120 строк
├── CURRENT_STATE.md          # ≤60 строк; блок активных задач генерируется
├── README.md  CONTRIBUTING.md  CHANGELOG.md
├── .gitignore  .editorconfig
├── .githooks/pre-commit
├── .claude/
│   ├── agents/               # 9 агентов
│   ├── skills/               # 5 skills
│   ├── rules/                # docs.md (+ csharp-core.md, data.md в PHASE E)
│   ├── hooks/                # python-скрипты
│   └── settings.json
├── .github/workflows/
├── docs/
│   ├── vision/               # MASTER_PROMPT.md, PROJECT_VISION.md
│   ├── architecture/         # ARCHITECTURE.md, DEVELOPMENT_FRAMEWORK.md
│   ├── decisions/            # ADR-*.md, DECISIONS_LOG.md
│   └── research/             # RR-*.md, PHASE_B_SUMMARY.md
├── development/
│   ├── ROADMAP.md  BACKLOG.md  OPEN_QUESTIONS.md  RISKS.md  TECH_DEBT.md
│   ├── templates/  milestones/  tasks/  hypotheses/  experiments/
│   ├── handoff/  releases/
│   └── phase-a/
├── tools/development/registry.py
# PHASE E:
├── Singularity.sln
├── src/
│   ├── Singularity.Kernel/   # тик, RNG, fixed-point, таблицы — без доменной логики
│   ├── Singularity.Core/     # доменные системы, namespaces по доменам
│   └── Singularity.Lab/      # консольная Simulation Lab
├── tests/
│   ├── Singularity.Kernel.Tests/
│   ├── Singularity.Core.Tests/
│   └── Singularity.Architecture.Tests/   # проверка зависимостей сборок
├── mods/core/                # базовая игра как мод `core`
└── lab/                      # манифесты, golden/, analysis/ (python); results/ в .gitignore
```

Отличия от предварительной структуры мастер-промта:
- `game/<15 доменов>` → `src/Singularity.Kernel` + `src/Singularity.Core` (namespaces); отдельные сборки доменов — когда границы устоятся. Зависимости проверяет архитектурный тест.
- `data/` → `mods/core/`: базовая игра с первого дня загружается через загрузчик модов (ADR-0010).
- `simulation-lab/` → `src/Singularity.Lab/` (код) + `lab/` (эксперименты, анализ).
- `active-tasks/`, `completed/`, `backlog/` → `tasks/` + статус во frontmatter + генерируемый `BACKLOG.md`.
- `releases/` → `development/releases/`; бинарники — GitHub Releases, не git.

---

## 8. Сессии и handoff

**Начало:** `CLAUDE.md` (авто) → факты `CURRENT_STATE.md` (hook) → последний handoff (по ссылке) → нужные элементы → нужные документы.

**Конец (`/handoff`):** статусы → `registry.py index` → `CURRENT_STATE.md` (точка продолжения, блокеры) → handoff-файл ≤40 строк (сделано, решения, открытое, точка продолжения, расход сессии) → коммит. Push — в конце фазы или milestone (политика владельца).

---

## 9. Ревью, тестирование, релизы

- **Код:** каждый diff в `src/`, `tests/`, `mods/` → `reviewer`; находки `high` закрываются до `DONE`.
- **Тесты:** developer — unit-тесты к своему коду; test-engineer — инварианты (сохранение денег и ресурсов), property-based, golden-run, L1/L2.
- **Релизы:** версия `0.M.P` до первой играбельной версии. Релиз = milestone с выполненными критериями. Файл релиза: цель, задачи, критерии, результаты тестов, ограничения, оставшиеся проблемы.

---

## 10. Экономия токенов

1. Лимиты: `CLAUDE.md` ≤120 строк, `CURRENT_STATE.md` ≤60, handoff ≤40, ответ агента ≤40.
2. Перед новым исследованием — проверить `docs/research/` и гипотезы. Повтор — только с обоснованием в задаче.
3. Critic/Advocate — только Gate. Reviewer — код и ADR.
4. Параллельно — только независимые агенты. Агенты не вызывают агентов.
5. Документы читаются с раздела «Краткий вывод».
6. **Измерение:** Claude не может сам вызвать `/usage`. Владелец по желанию запускает `/usage` перед `/handoff`; значение записывается в handoff. В конце M0 — сравнение по сессиям и решение о `model: haiku` для механических задач.

---

## 11. Что нужно до PHASE E (разрешения владельца — спрашиваются в PHASE D)

- Установка .NET 10 SDK в `~/.dotnet` скриптом `dotnet-install.sh` (рядом с имеющимся .NET 8, без root, обратимо).
- Включение GitHub Actions в репозитории.

---

## 12. Разногласия (ревью 2026-11-02)

| # | Тема | Позиции | Итог |
|---|---|---|---|
| 1 | Число агентов | Critic: 4–5 агентов, остальные по условию. Advocate: невызванный агент почти ничего не стоит; нужен test-engineer | Владелец: 8 + test-engineer |
| 2 | Статусы и уровни | Critic и Advocate: 12 статусов и 3 уровня — бюрократия для одного человека | Владелец: упростить (5.2, 6) |
| 3 | Одобрение | Critic: без пакетного одобрения каждая мелкая задача ждёт владельца | Владелец: пакетом (5.4) |
| 4 | Язык служебных скриптов | Advocate: PHP понятнее владельцу | Владелец: Python |
| 5 | Проверка зон записи | Critic и Advocate: hook во frontmatter агента надёжнее и понятнее общего hook по `agent_type` (RR-0004 упоминает `agent_type` во входе hooks, но зона рядом с агентом читается проще) | Принято: `guard_zone.py` во frontmatter |
| 6 | Обход hooks через Bash | Critic: hooks не защищают от Bash | Принято: принцип 4; барьер — `registry.py check` в pre-commit и CI |
| 7 | GitHub Issues вместо файлов | Advocate: видно с телефона. Против: нет `gh`, токены на API, нет офлайн-истины | Отклонено; возможен односторонний экспорт позже |
| 8 | Цепочка developer → reviewer | Advocate сам признал, что альтернатива проигрывает | Агенты не вызывают агентов |
| 9 | Haiku для механики сразу | Advocate: haiku для `/handoff` | Отложено до замеров в конце M0 (10.6) |
| 10 | Push после каждой задачи | Advocate: быстрее обратная связь от CI | Не менялось: политика владельца — push в конце фазы. Владелец может изменить |
| 11 | `/start` | Critic: дублирует SessionStart hook | Принято: `/start` убран |
| 12 | Генерируемые индексы | Critic: 5 индексов не окупаются | Принято: один `BACKLOG.md` + блок в `CURRENT_STATE.md`; остальное — `registry.py list`; DECISIONS_LOG, RISKS, TECH_DEBT вручную |
