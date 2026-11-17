# Спайк S1 (ADR-0018): Linguini.Bundle 0.11.0 на .NET 10

Код: `tools/spikes/fluent-s1/` (одноразовый, вне `Singularity.sln`). Запуск: `dotnet run --project tools/spikes/fluent-s1`. Среда: .NET 10.0.14, Linux Mint 21.3, 2026-11-17. Итог: **20 PASS, 0 FAIL** по проверкам ниже, но два допущения ADR §3.3 опровергнуты (раздел «Расхождения с ADR»).

## Результаты
| Проверка | Итог | Образец вывода |
|---|---|---|
| 1. Восстановление и запуск на net10.0 | PASS | пакет и зависимости Linguini.Shared 0.10.1, Linguini.Syntax 0.10.2 восстановились; `net10.0` берёт сборку `net8.0` |
| 2. Падеж и род через `DataRef` (ADR S1 в, §3) | PASS | `NAME($c, case: "ins")` → «война с Германией»; `GENDER($a)` → «Франция объявила», «СССР объявил»; форма атрибутов (`-germany` с `.ins`, `.gender`) даёт то же; статическая ссылка `{ -actor-DEU(case: "ins") }` тоже |
| 3. RU-категории (S1 а) | PASS | `{ $n -> … }` и `{ NUMBER($n) -> … }`: 1→one, 2→few, 5→many, 11→many, 21→one, 111→many, 1,5→other. `NUMBER(1, minimumFractionDigits: 1)` → «1,0» → `other` (сырое 1.0 дало бы `one`). EN: «1 item», «1,234.5 items» |
| 4. Ссылка на сообщение видит аргументы вызывающего (S1 б) | PASS | `fmt-date-dmy = { $day } { date-month-gen } { $year }` → «9 февраля 1945»; `DATE($d, format: "dmy")` → «9 марта 1945»; EN «March 9, 1945» |
| (в) функция получает `bundle` и AST терма | PASS | замыкание над `FrozenBundle` (`NAME` увидела тип `FrozenBundle`); `TryGetAstTerm`, `term.Attributes`, `FormatPattern(term.Value, args)` работают |
| (г) `UseIsolating = false` | PASS | `false` → «Привет, Мир!»; для контраста `true` → «Привет, ⁨Мир⁩!» (U+2068/U+2069) |
| 5. `NUMBER`/`DATE` детерминированы | PASS | одна и та же строка при 9 культурах потока (invariant, ru-RU, en-US, de-DE, tr-TR, ar-SA, fa-IR, ja-JP, th-TH): `1 234 567,891 предмета \| −1 234,5 \| 25,5 % \| 0,5 предмета \| 9 марта 1945 \| 1 000 000 предметов` (пробелы — U+00A0, минус — U+2212). Арифметика `decimal` и `InvariantCulture`, разделители из сообщений `fmt-number-*` |
| (д) чтение `FrozenBundle` из потоков | PASS | 16 потоков × 400 × 25 итераций, по 5 форматирований, у потоков разные культуры: расхождений 0, исключений 0 |
| 6. Лицензия, уведомление Unicode V3 | см. ниже | в пакете уведомления нет |

## Лицензия
- [Факт] В `.nupkg` Linguini.Bundle, Shared и Syntax нет файла лицензии и нет слов «Unicode license» или «CLDR» ни в DLL, ни в README, ни в nuspec. Есть только `Copyright (c) Daniel Fath 2021` и `MIT OR Apache-2.0` в nuspec. Единственное упоминание — ссылка на таблицу CLDR в XML-документации `Linguini.Shared.xml` (`language_plural_rules` для ru).
- Значит, текст Unicode V3 нужно брать у Unicode и класть в `LICENSES.md` самим (ADR §«Лицензия CLDR»). Текст с https://www.unicode.org/license.txt (доступ 2026-11-17):

```
UNICODE LICENSE V3

COPYRIGHT AND PERMISSION NOTICE

Copyright © 1991-2026 Unicode, Inc.

NOTICE TO USER: Carefully read the following legal agreement. BY
DOWNLOADING, INSTALLING, COPYING OR OTHERWISE USING DATA FILES, AND/OR
SOFTWARE, YOU UNEQUIVOCALLY ACCEPT, AND AGREE TO BE BOUND BY, ALL OF THE
TERMS AND CONDITIONS OF THIS AGREEMENT. IF YOU DO NOT AGREE, DO NOT
DOWNLOAD, INSTALL, COPY, DISTRIBUTE OR USE THE DATA FILES OR SOFTWARE.

Permission is hereby granted, free of charge, to any person obtaining a
copy of data files and any associated documentation (the "Data Files") or
software and any associated documentation (the "Software") to deal in the
Data Files or Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, and/or sell
copies of the Data Files or Software, and to permit persons to whom the
Data Files or Software are furnished to do so, provided that either (a)
this copyright and permission notice appear with all copies of the Data
Files or Software, or (b) this copyright and permission notice appear in
associated Documentation.

THE DATA FILES AND SOFTWARE ARE PROVIDED "AS IS", WITHOUT WARRANTY OF ANY
KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT OF
THIRD PARTY RIGHTS.

IN NO EVENT SHALL THE COPYRIGHT HOLDER OR HOLDERS INCLUDED IN THIS NOTICE
BE LIABLE FOR ANY CLAIM, OR ANY SPECIAL INDIRECT OR CONSEQUENTIAL DAMAGES,
OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS,
WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION,
ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THE DATA
FILES OR SOFTWARE.

Except as contained in this notice, the name of a copyright holder shall
not be used in advertising or otherwise to promote the sale, use or other
dealings in these Data Files or Software without prior written
authorization of the copyright holder.
```

## Расхождения с ADR (проверка прошла другим механизмом; решение за Orchestrator и ADR-0018 §3.3)
1. **`FormatterFunc` ломает выбор формы по результату функции.** [Факт, спайк] Если задан `FormatterFunc`, результат функции в селекторе превращается в строку и уходит в `*[other]`: для n=21 селектор `NUM($n)` даёт `OTHER`, селектор `$n` даёт `ONE` (строка `INFO` в выводе). ADR §3.3 («голая числовая переменная форматируется через `FormatterFunc`», `NUMBER` сохраняет операнды) в таком виде не работает. Что работает: `FormatterFunc` не задаётся; `NUMBER` возвращает `FormattedNumber` — наследник `FluentNumber`, созданный из `FluentNumber.FromString("1.0")` (публичный путь, который сохраняет видимые дробные знаки в `options`, так что CLDR считает операнды видимого числа) с переопределённым `IFluentType.AsString()` (текст локали) и `Copy()` (иначе аргумент теряет подкласс). Голые числовые аргументы фасад `Loc` обязан сам оборачивать (в спайке `Loc.Num`); без этого `{ $n }` печатает инвариантное «-1234.5».
2. **`NUMBER(style: "percent")` и `useGrouping: false` возвращают строку** и не могут быть селектором: публичного конструктора `FluentNumber(double, options)` нет (он `internal`). Для процента и компактной записи это приемлемо; правило стоит записать в ADR.
3. **`TryGetAstTerm` принимает id без ведущего «-»**: `actor-DEU`, не `-actor-DEU`. `DataRef` хранит id без дефиса.

## Прочие наблюдения
- Встроенной `NUMBER` нет (`Unknown function: NUMBER()`): подтверждает ADR.
- Linguini вызывает `new CultureInfo(locale)` в `FluentBundle.MakeUnchecked`. С ICU это работает для любого тега (`xx`, `pt-BR`, `zz-ZZ`). **В режиме `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` падает** `CultureNotFoundException: ru is an invalid culture identifier`. [Предп.] Для клиента Godot (экспорт .NET) нужно проверить, что ICU доступен, или не включать invariant-режим.
- Селектор `{ NUMBER($n) -> … }` (вызов функции) и именованные литеральные аргументы (`case: "ins"`, `minimumFractionDigits: 1`) разбираются и работают.
- `GetAttrMessage("node-s8-eng-turbojet_g1.name")` возвращает атрибут сообщения без значения.
