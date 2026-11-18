# Number, percent, compact and date formats (ADR-0018 §3.3).
fmt-number-decimal = .
fmt-number-group = ,
fmt-number-minus = −
fmt-percent = { $n }%
fmt-compact-thousand = { $n }K
fmt-compact-million = { $n }M
fmt-compact-billion = { $n }B
fmt-date-dmy = { date-month-nom } { $day }, { $year }
fmt-date-my = { date-month-nom } { $year }
fmt-date-y = { $year }

date-month-nom =
    { $month ->
        [1] January
        [2] February
        [3] March
        [4] April
        [5] May
        [6] June
        [7] July
        [8] August
        [9] September
        [10] October
        [11] November
       *[12] December
    }

date-month-gen =
    { $month ->
        [1] January
        [2] February
        [3] March
        [4] April
        [5] May
        [6] June
        [7] July
        [8] August
        [9] September
        [10] October
        [11] November
       *[12] December
    }
