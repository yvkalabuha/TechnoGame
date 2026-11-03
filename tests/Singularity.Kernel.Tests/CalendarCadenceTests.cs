using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

/// <summary>Integer Gregorian calendar and system cadence in <see cref="TickRunner"/> (decision #60, ADR-0006).</summary>
public class CalendarCadenceTests
{
    [Fact]
    public void Gregorian_MatchesDateTime_1600To2400()
    {
        // DateTime is fine in a test: it is the independent oracle here, never simulation code.
        var epoch = new DateTime(1970, 1, 1);
        for (var d = new DateTime(1600, 1, 1); d < new DateTime(2400, 1, 1); d = d.AddDays(1))
        {
            long days = (d - epoch).Days;
            var civil = new CivilDate(d.Year, d.Month, d.Day);
            Assert.Equal(days, Gregorian.DaysFromCivil(civil));
            Assert.Equal(civil, Gregorian.CivilFromDays(days));
        }
    }

    [Theory]
    [InlineData(1900, false)]
    [InlineData(1948, true)]
    [InlineData(2000, true)]
    [InlineData(2100, false)]
    public void LeapYears(int year, bool leap)
    {
        Assert.Equal(leap, Gregorian.IsLeapYear(year));
        Assert.Equal(leap ? 29 : 28, Gregorian.DaysInMonth(year, 2));
    }

    [Fact]
    public void BadDate_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Gregorian.DaysFromCivil(new CivilDate(1900, 2, 29)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Gregorian.DaysFromCivil(new CivilDate(1945, 13, 1)));
    }

    [Fact]
    public void MonthlySystem_1910To2100_Runs12TimesAYearOnDay1()
    {
        var calendar = new TickCalendar(new CivilDate(1910, 1, 1));
        var table = new Table(1);
        var monthly = new CountingSystem(table, "monthly", Cadence.Monthly, calendar);
        var yearly = new CountingSystem(table, "yearly", Cadence.Yearly, calendar);
        var runner = new TickRunner(table, 1, [monthly, yearly], chunkSize: 1, maxThreads: 1, calendar);
        long end = calendar.TickOf(new CivilDate(2101, 1, 1));
        while (runner.Tick < end)
            runner.RunTick();

        Assert.Equal(191 * 12, monthly.Runs.Count);
        for (int y = 1910; y <= 2100; y++)
        {
            List<CivilDate> year = [.. monthly.Runs.Where(d => d.Year == y)];
            Assert.Equal([.. Enumerable.Range(1, 12).Select(m => new CivilDate(y, m, 1))], year);
        }
        Assert.Equal([.. Enumerable.Range(1910, 191).Select(y => new CivilDate(y, 1, 1))], yearly.Runs);
        // 1912…2096 every 4 years = 47 leap years (2000 is one, 2100 is not).
        Assert.Equal(47, Enumerable.Range(1910, 191).Count(Gregorian.IsLeapYear));
        Assert.Equal(191 * 365 + 47, end);
        // Each system's own column counts its runs; it is unchanged on days it is not due.
        Assert.Equal(191 * 12, table.GetLong("monthly").Current[0]);
        Assert.Equal(191, table.GetLong("yearly").Current[0]);
    }

    [Theory]
    [InlineData(1945, 9, 15, Cadence.Monthly)]  // mid-month start
    [InlineData(1945, 9, 1, Cadence.Yearly)]    // not 1 January
    [InlineData(1945, 9, 1, Cadence.Weekly)]    // a Saturday, not a Monday
    public void Runner_RejectsCalendarNotAlignedWithACadence(int y, int m, int d, Cadence cadence)
    {
        // ADR-0004: firing index 0 must be the first full period, so the start must be a firing day.
        var table = new Table(1);
        var calendar = new TickCalendar(new CivilDate(y, m, d));
        var daily = new CountingSystem(table, "daily", Cadence.Daily, calendar);
        var other = new CountingSystem(table, "other", cadence, calendar);
        var e = Assert.Throws<ArgumentException>(() => new TickRunner(table, 1, [daily, other], 1, 1, calendar));
        Assert.Contains("not aligned", e.Message);
    }

    [Theory]
    [InlineData(1945, 1, 1)]  // Monday, 1 January: aligned with all cadences
    [InlineData(1951, 1, 1)]
    public void Runner_AcceptsAlignedCalendar(int y, int m, int d)
    {
        var table = new Table(1);
        var calendar = new TickCalendar(new CivilDate(y, m, d));
        ITickSystem[] systems = [.. new[] { Cadence.Daily, Cadence.Weekly, Cadence.Monthly, Cadence.Yearly }
            .Select((c, i) => new CountingSystem(table, $"s{i}", c, calendar))];
        var runner = new TickRunner(table, 1, systems, 1, 1, calendar);
        for (int i = 0; i < 800; i++)
            runner.RunTick();
        Assert.Equal(800, table.GetLong("s0").Current[0]);
    }

    [Theory]
    [InlineData(Cadence.Daily)]
    [InlineData(Cadence.Weekly)]
    [InlineData(Cadence.Monthly)]
    [InlineData(Cadence.Yearly)]
    public void NextDue_IsTheFirstDueTickFromHere(Cadence cadence)
    {
        var c = new TickCalendar(new CivilDate(1945, 9, 1));
        for (long t = -400; t < 3000; t += 3)
        {
            long next = c.NextDue(cadence, t);
            Assert.True(c.IsDue(cadence, next));
            for (long u = t; u < next; u++)
                Assert.False(c.IsDue(cadence, u));
        }
    }

    [Fact]
    public void NonDailySystem_WithoutCalendar_IsRejected()
    {
        var table = new Table(1);
        var calendar = new TickCalendar(new CivilDate(1945, 9, 1));
        var monthly = new CountingSystem(table, "m", Cadence.Monthly, calendar);
        Assert.Throws<ArgumentException>(() => new TickRunner(table, 1, [monthly], 1, 1));
    }

    [Fact]
    public void DailySystemsWithoutCalendar_RunEveryTick()
    {
        var table = new Table(1);
        var daily = new CountingSystem(table, "d", Cadence.Daily, null);
        var runner = new TickRunner(table, 1, [daily], 1, 1);
        for (int i = 0; i < 10; i++)
            runner.RunTick();
        Assert.Equal(10, table.GetLong("d").Current[0]);
        Assert.Equal(10, runner.Tick);
    }

    [Fact]
    public void EmptyTick_SkipsAllPhases_ButAdvancesTheClock()
    {
        var calendar = new TickCalendar(new CivilDate(1945, 9, 1));
        var table = new Table(2);
        var monthly = new CountingSystem(table, "m", Cadence.Monthly, calendar);
        var runner = new TickRunner(table, 1, [monthly], 1, 4, calendar);
        runner.RunTick(); // day 0 = 1 September: due
        Assert.Equal(1, monthly.ComputeCalls);
        for (int i = 1; i < 30; i++)
            runner.RunTick();
        Assert.Equal((1, 1, 1), (monthly.ComputeCalls, monthly.MergeCalls, monthly.PostCalls));
        Assert.Equal(30, runner.Tick);
        runner.RunTick(); // 1 October
        Assert.Equal(2, monthly.Runs.Count);
        Assert.Equal(new CivilDate(1945, 10, 1), monthly.Runs[1]);
    }

    /// <summary>Counts its runs in its own column; records the date of each run.</summary>
    private sealed class CountingSystem : ITickSystem
    {
        private readonly LongColumn _count;
        private readonly TickCalendar? _calendar;

        public CountingSystem(Table table, string name, Cadence cadence, TickCalendar? calendar)
        {
            _count = table.AddLong(name);
            _calendar = calendar;
            Cadence = cadence;
        }

        public int SystemId => 1;
        public Cadence Cadence { get; }
        public List<CivilDate> Runs { get; } = new();
        public int ComputeCalls, MergeCalls, PostCalls;

        public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
        {
            if (rowStart == 0)
                Interlocked.Increment(ref ComputeCalls);
            for (int r = rowStart; r < rowEnd; r++)
                _count.Next[r] = _count.Current[r] + 1;
        }

        public void Merge(in TickContext context, int chunkCount)
        {
            MergeCalls++;
            if (_calendar is not null)
                Runs.Add(_calendar.DateOf(context.Tick));
        }

        public void Post(in TickContext context) => PostCalls++;
    }
}
