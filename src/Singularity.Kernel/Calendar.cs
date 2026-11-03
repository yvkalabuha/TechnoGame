namespace Singularity.Kernel;

/// <summary>How often a system runs (ADR-0006, decision #60: kernel tick = 1 day).</summary>
public enum Cadence
{
    /// <summary>Every tick.</summary>
    Daily,

    /// <summary>Every 7th tick counted from the calendar start (tick 0, 7, 14, ...).</summary>
    Weekly,

    /// <summary>Day 1 of each calendar month.</summary>
    Monthly,

    /// <summary>1 January.</summary>
    Yearly,
}

/// <summary>A proleptic Gregorian date.</summary>
public readonly record struct CivilDate(int Year, int Month, int Day)
{
    /// <summary>ISO 8601 text, e.g. "1945-09-01".</summary>
    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Year:D4}-{Month:D2}-{Day:D2}");
}

/// <summary>
/// Integer proleptic Gregorian arithmetic (H. Hinnant, "chrono-compatible low-level date algorithms").
/// Day numbers count from 1970-01-01. No DateTime: the result must not depend on the platform.
/// </summary>
public static class Gregorian
{
    public static bool IsLeapYear(int year) => year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);

    public static int DaysInMonth(int year, int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        return month == 2 ? (IsLeapYear(year) ? 29 : 28) : (month is 4 or 6 or 9 or 11 ? 30 : 31);
    }

    public static long DaysFromCivil(CivilDate date)
    {
        if (date.Day < 1 || date.Day > DaysInMonth(date.Year, date.Month))
            throw new ArgumentOutOfRangeException(nameof(date), date, "Not a calendar date.");
        long y = date.Year - (date.Month <= 2 ? 1 : 0);
        long era = FloorDiv(y, 400);
        long yoe = y - era * 400;
        long doy = (153 * (date.Month + (date.Month > 2 ? -3 : 9)) + 2) / 5 + date.Day - 1;
        long doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
        return era * 146097 + doe - 719468;
    }

    public static CivilDate CivilFromDays(long days)
    {
        long z = days + 719468;
        long era = FloorDiv(z, 146097);
        long doe = z - era * 146097;
        long yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
        long doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
        long mp = (5 * doy + 2) / 153;
        int day = (int)(doy - (153 * mp + 2) / 5 + 1);
        int month = (int)(mp < 10 ? mp + 3 : mp - 9);
        return new CivilDate(checked((int)(yoe + era * 400 + (month <= 2 ? 1 : 0))), month, day);
    }

    private static long FloorDiv(long a, long b) => a >= 0 ? a / b : (a - b + 1) / b;
}

/// <summary>
/// Maps day ticks to dates: tick 0 is <see cref="Start"/>. Pure functions, no state, so a system can
/// derive its month index from the tick (slice spec v0.5 §2, §9.1: monthly RNG keys use the month index).
/// </summary>
public sealed class TickCalendar
{
    private readonly long _startDay;

    public TickCalendar(CivilDate start)
    {
        _startDay = Gregorian.DaysFromCivil(start);
        Start = start;
    }

    public CivilDate Start { get; }

    public CivilDate DateOf(long tick) => Gregorian.CivilFromDays(checked(_startDay + tick));

    public long TickOf(CivilDate date) => Gregorian.DaysFromCivil(date) - _startDay;

    /// <summary>Months between the start month and the month of <paramref name="tick"/> (0 = start month).</summary>
    public long MonthIndex(long tick)
    {
        CivilDate d = DateOf(tick);
        return ((long)d.Year - Start.Year) * 12 + (d.Month - Start.Month);
    }

    /// <summary>Tick of day 1 of month <paramref name="monthIndex"/>; negative for month 0 if the start is not day 1.</summary>
    public long TickOfMonth(long monthIndex)
    {
        long k = Start.Month - 1 + monthIndex;
        long year = Start.Year + (k >= 0 ? k / 12 : (k - 11) / 12);
        int month = (int)(k - (year - Start.Year) * 12) + 1;
        return TickOf(new CivilDate(checked((int)year), month, 1));
    }

    public bool IsDue(Cadence cadence, long tick) => cadence switch
    {
        Cadence.Daily => true,
        Cadence.Weekly => tick % 7 == 0,
        Cadence.Monthly => DateOf(tick).Day == 1,
        Cadence.Yearly => DateOf(tick) is { Month: 1, Day: 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(cadence), cadence, null),
    };
}
