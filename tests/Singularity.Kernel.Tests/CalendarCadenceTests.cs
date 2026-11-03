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
        var weekly = new CountingSystem(table, "weekly", Cadence.Weekly, calendar);
        var runner = new TickRunner(table, 1, [monthly, yearly, weekly], chunkSize: 1, maxThreads: 1, calendar);
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
        Assert.Equal((end + 6) / 7, weekly.Runs.Count);
        // 1912…2096 every 4 years = 47 leap years (2000 is one, 2100 is not).
        Assert.Equal(47, Enumerable.Range(1910, 191).Count(Gregorian.IsLeapYear));
        Assert.Equal(191 * 365 + 47, end);
        // Each system's own column counts its runs; it is unchanged on days it is not due.
        Assert.Equal(191 * 12, table.GetLong("monthly").Current[0]);
        Assert.Equal(191, table.GetLong("yearly").Current[0]);
    }

    [Fact]
    public void MonthIndexAndTickOfMonth_RoundTrip_MidMonthStart()
    {
        var c = new TickCalendar(new CivilDate(1945, 9, 15));
        Assert.Equal(-14, c.TickOfMonth(0)); // day 1 of the start month is before tick 0
        Assert.Equal(16, c.TickOfMonth(1)); // 15 September + 16 days = 1 October
        for (long m = 1; m < 400; m++)
        {
            Assert.Equal(m, c.MonthIndex(c.TickOfMonth(m)));
            Assert.Equal(1, c.DateOf(c.TickOfMonth(m)).Day);
        }
        Assert.Equal(-1, c.MonthIndex(-15));
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

    [Fact]
    public void PendingCommands_ApplyAtTheNextDueTick_BeforeCompute()
    {
        var calendar = new TickCalendar(new CivilDate(1945, 9, 1));
        var table = new Table(1);
        var monthly = new CountingSystem(table, "m", Cadence.Monthly, calendar);
        var runner = new TickRunner(table, 1, [monthly], 1, 1, calendar);
        runner.RunTick();          // month 0
        monthly.PendingAdd = 100;  // decided after month 0, e.g. by a player or another system
        for (int i = 1; i < 30; i++)
            runner.RunTick();
        Assert.Equal(1, table.GetLong("m").Current[0]); // not applied on idle days
        runner.RunTick();          // month 1: 1 (old) + 100 (command) + 1 (this run)
        Assert.Equal(102, table.GetLong("m").Current[0]);
        Assert.Equal(101, monthly.SeenAtCompute[1]); // compute of the same tick saw the command
        Assert.Equal(0, monthly.PendingAdd);
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
        public List<long> SeenAtCompute { get; } = new();
        public long PendingAdd;
        public int ComputeCalls, MergeCalls, PostCalls;

        public bool HasPendingCommands => PendingAdd != 0;

        public void ApplyCommands(in TickContext context)
        {
            _count.Next[0] = _count.Current[0] + PendingAdd;
            PendingAdd = 0;
        }

        public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
        {
            if (rowStart == 0)
            {
                Interlocked.Increment(ref ComputeCalls);
                SeenAtCompute.Add(_count.Current[0]);
            }
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
