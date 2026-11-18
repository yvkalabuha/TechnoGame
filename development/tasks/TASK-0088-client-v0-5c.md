---
id: TASK-0088
title: "Client v0.5c: navigation rail, 7 areas, speed scale, textures, icons and area pictures"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: developer
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0081]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "#352, #360; spec docs/ui/CLIENT_V05C_SPEC.md"
created: 2026-11-18
---

# TASK-0088 — Client v0.5c: navigation rail, 7 areas, speed scale, textures, icons and area pictures

## Цель
Клиент v0.5c по `docs/ui/CLIENT_V05C_SPEC.md`, `docs/game-design/UI_INFORMATION_ARCHITECTURE.md` и макету `docs/ui/prototypes/client-v05c.html`; решения #352, #360.

## Описание
1. Кнопка звука убрана из верхней полосы (звук — в Настройках).
2. Цветная шкала скорости (токены спецификации).
3. Процедурные текстуры бумаги, волокна, потёртостей кромок и нажатия (шейдер canvas_item + запечённая плитка); пресеты Выкл/Слабые/Обычные в Настройках, по умолчанию «Обычные»; плашки тревог, ореол, карта и глобус без текстуры.
4. Левая рейка: Карта + 7 областей (Наука; Производство и ресурсы; Финансы и торговля; Силы; Разведка; Политика; Проекты и люди), узкая по умолчанию (значок + буква), широкая — в настройках; T/E/F/M/I/G/J, Esc → карта. Значки `docs/ui/assets/area-icons/`, картинки `docs/ui/assets/area-pictures/`.
5. Области без механики — серые, заперты, карточка «вопрос + срок»; живые части областей переносят существующие панели (держава, дерево знаний, бюджет, лицензии, сигналы). Без фальшивых механик.
6. «Проекты и люди» — седьмой цвет со штриховкой (#360).
7. Сигналы — бейдж на рейке и выдвижной журнал.
8. Валидатор: правила T1–T5 для текстур из спецификации; прогон в `--shots`.
9. Мелочи v0.5c из `docs/ui/reviews/task-0081-visual.md` и DEBT-0047, где дёшево (затемнение под модальными окнами заметнее, легенда не перекрывает планисферу).

Не менять Core, Session, Kernel.

## Критерии приёмки
- [ ] Критерии спецификации; валидатор PASS по всем парам и T1–T5; снимки в `docs/ui/prototypes/client-v05c/`.
- [ ] `dotnet build src/Singularity.Client.Spike` без предупреждений, тесты клиента зелёные.

## REVIEW
- [ ] Code review (reviewer)
- [ ] Визуальное ревью (ui-art-director)

## Результат
