# Number, percent, compact and date formats (ADR-0018 §3.3). Spaces in numbers are U+00A0.
fmt-number-decimal = ,
fmt-number-group = { "\u00A0" }
fmt-number-minus = −
fmt-percent = { $n } %
fmt-compact-thousand = { $n } тыс.
fmt-compact-million = { $n } млн
fmt-compact-billion = { $n } млрд
fmt-date-dmy = { $day } { date-month-gen } { $year }
fmt-date-my = { date-month-nom } { $year }
fmt-date-y = { $year }

date-month-nom =
    { $month ->
        [1] январь
        [2] февраль
        [3] март
        [4] апрель
        [5] май
        [6] июнь
        [7] июль
        [8] август
        [9] сентябрь
        [10] октябрь
        [11] ноябрь
       *[12] декабрь
    }

date-month-gen =
    { $month ->
        [1] января
        [2] февраля
        [3] марта
        [4] апреля
        [5] мая
        [6] июня
        [7] июля
        [8] августа
        [9] сентября
        [10] октября
        [11] ноября
       *[12] декабря
    }
