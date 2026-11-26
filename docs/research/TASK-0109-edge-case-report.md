# Отчёт research-scientist для TASK-0109 (шаг 1, 2026-11-18)

Это текст для раздела «Отчёт research-scientist» в `development/tasks/TASK-0109-t4-region-status-edge-cases.md` (зона записи агента не включает файл задачи; Orchestrator переносит или ссылается). Не RR-документ, frontmatter нет.

## Краткий вывод
- 8 регионов меняют `status` core -> colony: guernsey, jersey, isle_of_man, puerto_rico, united_states_virgin_islands, algeria_ouargla, algeria_tamanghasset, taiwan.
- Alaska, Hawaii, Прибалтика (estonia, latvia, lithuania), moldova остаются `core`.
- В enum нет «зависимой территории», поэтому `colony` для коронных владений — ближайший статус (как Gibraltar, Malta, Ogaden).
- У ROC в данных 7 `core` (ADR: 6); сомнительная пара — mongolia_govi_altay, mongolia_mn_govi.
- После правок: USA 5, SUN 31, GBR 2, FRA 4, ROC 7. Предупреждения: GBR и FRA «< 5», SUN — исключение #406.

Критерий: `core` = территория юридически входит в само государство на t0 (1945-10-01). Все ссылки открыты 2026-11-18.

## Таблица

| Регион(ы) | Сейчас | Вердикт | Источник | Уверенность |
|---|---|---|---|---|
| guernsey, jersey, isle_of_man (GBR) | core | **colony** | [Факт] Коронные владения не часть Соединённого Королевства; Crown in right of Jersey/Guernsey/Man отдельна от Crown in right of UK, ответственность UK — оборона и внешние сношения. https://en.wikipedia.org/wiki/Crown_Dependencies . Освобождение островов 9 мая 1945 — [Предп.] по общеизвестным данным, не перепроверено | H (статус), M (ярлык `colony` условный) |
| puerto_rico, united_states_virgin_islands (USA) | core | **colony** | [Факт] Неинкорпорированные территории (Insular Cases): https://en.wikipedia.org/wiki/Insular_Cases . Для USVI в 1945 статья прямо не говорит; [Предп.] неинкорпорация по общему статусу | H (PR), M (USVI) |
| united_states_of_america_alaska, _hawaii (USA) | core | **core** | [Факт] Hawaii — организованная инкорпорированная территория (Organic Act 1900-04-30), штат с 1959-08-21: https://en.wikipedia.org/wiki/Territory_of_Hawaii ; Alaska — инкорпорированная: https://en.wikipedia.org/wiki/Insular_Cases . Дата Organic Act Аляски 1912 — [Предп.], не подтверждена | H |
| algeria_ouargla, algeria_tamanghasset (FRA) | core | **colony** | [Факт] Territoires du Sud — закон 1902-12-24, своя администрация и бюджет, отдельно от трёх северных департаментов, военный режим до 1957: https://wikimonde.com/article/Alg%C3%A9rie_fran%C3%A7aise , https://worldstatesmen.org/Algeria.html . Северная Алжирия — департаменты с 1848: https://en.wikipedia.org/wiki/French_Algeria . Принадлежность Уарглы и Таманрассета к Territoires du Sud — [Предп.] по географии, первичным источником не подтверждена | M |
| taiwan (owner JPN, controller ROC) | core | **colony** | [Факт] Цессия 1895-04-17 (Симоносеки), колония Японии; передача ROC 1945-10-25; отказ Японии 1952-04-28: https://en.wikipedia.org/wiki/Taiwan_under_Japanese_rule . JPN не в `great_powers`, ROC не owner на t0 — квоты не затронуты; конвенция #402 сохраняется | H |
| estonia, latvia, lithuania, moldova (SUN) | core | **core** (#406) | [Факт] Включены в СССР в августе 1940 (Литва 3.8, Латвия 5.8, Эстония 6.8): https://en.wikipedia.org/wiki/Occupation_of_the_Baltic_states ; непризнание Запада квоту не меняет. Дата образования МССР 1940-08-02 — [Предп.], не перепроверена | H (по праву СССР) |

## Почему у ROC 7 `core`, а не 6
Регионы с owner ROC и `status: core` в owners.yaml: china_guangxi, china_heilongjiang, china_inner_mongol, china_sichuan, china_xinjiang, mongolia_govi_altay, mongolia_mn_govi = **7**. Не считаются: port_arthur (ROC, leased), china_tibet (owner TIB, #401). ADR п. 2.7 даёт 6 и не называет, чей регион не вошёл; [Предп.] счёт ADR сделан без одного из двух монгольских регионов (оба из импорта RR-0020 C34). По git-истории не проверено. Расхождение безвредно: 5–15, предупреждения нет.

Сомнительная пара: **mongolia_govi_altay, mongolia_mn_govi**. [Факт] Договор СССР–Китай 1945-08-14 обязал ROC признать независимость МНР после референдума 1945-10-20; ROC признала 1946-01-05/06 (https://en.wikipedia.org/wiki/1945_Mongolian_independence_referendum). На t0 референдума ещё не было, owner ROC юридически верен по #313, но фактически МНР независима с 1921 (controller MNG). Уверенность M. Вердикт: оставить `core` (другого подходящего статуса нет), признание — событие C34.

## Правки owners.yaml для developer
Менять только `status`; в `source` дописать «TASK-0109 report»:
1. guernsey: core -> colony
2. jersey: core -> colony
3. isle_of_man: core -> colony
4. puerto_rico: core -> colony
5. united_states_virgin_islands: core -> colony
6. algeria_ouargla: core -> colony
7. algeria_tamanghasset: core -> colony
8. taiwan: core -> colony (owner JPN, controller ROC не менять)

Не менять: Alaska, Hawaii, estonia, latvia, lithuania, moldova, регионы ROC.

Ожидаемые предупреждения: GBR «< 5», FRA «< 5», SUN — исключение ≤ 35 без предупреждения. Совпадает с ADR п. 2.7, кроме ROC 7 вместо 6.

Замечание: северных алжирских департаментов в данных нет (только два южных региона), поэтому FRA не получает `core` из Алжира; это вопрос разреза карты (T3), не статуса.

Не проверено: дата Organic Act Аляски; дата образования МССР; принадлежность Уарглы и Таманрассета к конкретным Territoires du Sud по первичному источнику; счёт ADR для ROC.
