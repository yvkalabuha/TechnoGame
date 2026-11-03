using Singularity.Kernel;

namespace Singularity.Kernel.Tests.Audit;

/// <summary>Property tests of the integer Gregorian calendar and <see cref="TickCalendar"/> (TASK-0014 audit).</summary>
public class CalendarPropertyTests
{
    [Fact]
    public void RandomDay_RoundTrips_AndIsAValidDate()
    {
        var rnd = new Random(601);
        for (int i = 0; i < 200_000; i++)
        {
            long day = rnd.NextInt64(-100_000_000, 100_000_000);
            CivilDate c = Gregorian.CivilFromDays(day);
            Assert.InRange(c.Month, 1, 12);
            Assert.InRange(c.Day, 1, Gregorian.DaysInMonth(c.Year, c.Month));
            Assert.Equal(day, Gregorian.DaysFromCivil(c));
        }
    }

    [Fact]
    public void RandomDate_RoundTrips()
    {
        var rnd = new Random(602);
        for (int i = 0; i < 200_000; i++)
        {
            int year = rnd.Next(-200_000, 200_000);
            int month = rnd.Next(1, 13);
            int dayOfMonth = rnd.Next(1, Gregorian.DaysInMonth(year, month) + 1);
            var c = new CivilDate(year, month, dayOfMonth);
            Assert.Equal(c, Gregorian.CivilFromDays(Gregorian.DaysFromCivil(c)));
        }
    }

    [Fact]
    public void ConsecutiveDays_AreConsecutiveDates()
    {
        // Successor rule written independently of the algorithm: next day, else day 1 of next month/year.
        var rnd = new Random(603);
        for (int i = 0; i < 100_000; i++)
        {
            long day = rnd.NextInt64(-50_000_000, 50_000_000);
            CivilDate a = Gregorian.CivilFromDays(day), b = Gregorian.CivilFromDays(day + 1);
            CivilDate expected = a.Day < Gregorian.DaysInMonth(a.Year, a.Month) ? a with { Day = a.Day + 1 }
                : a.Month < 12 ? new CivilDate(a.Year, a.Month + 1, 1)
                : new CivilDate(a.Year + 1, 1, 1);
            Assert.Equal(expected, b);
        }
    }

    [Fact]
    public void RandomDay_MatchesDateTime()
    {
        var rnd = new Random(604);
        var epoch = new DateTime(1970, 1, 1);
        long min = (new DateTime(1, 1, 1) - epoch).Days, max = (new DateTime(9999, 12, 31) - epoch).Days;
        for (int i = 0; i < 100_000; i++)
        {
            long day = rnd.NextInt64(min, max + 1);
            DateTime d = epoch.AddDays(day);
            Assert.Equal(new CivilDate(d.Year, d.Month, d.Day), Gregorian.CivilFromDays(day));
        }
    }

    [Fact]
    public void TickCalendar_RandomStarts_TickDateMonthConsistency()
    {
        var rnd = new Random(605);
        for (int s = 0; s < 300; s++)
        {
            int year = rnd.Next(1500, 2500), month = rnd.Next(1, 13);
            var start = new CivilDate(year, month, rnd.Next(1, Gregorian.DaysInMonth(year, month) + 1));
            var cal = new TickCalendar(start);
            Assert.Equal(start, cal.DateOf(0));
            Assert.Equal(0, cal.MonthIndex(0));
            for (int i = 0; i < 300; i++)
            {
                long t = rnd.NextInt64(-50_000, 200_000);
                CivilDate d = cal.DateOf(t);
                Assert.Equal(t, cal.TickOf(d));
                long m = cal.MonthIndex(t);
                Assert.Equal(((long)d.Year - start.Year) * 12 + d.Month - start.Month, m);
                long first = cal.TickOfMonth(m);
                Assert.InRange(t, first, cal.TickOfMonth(m + 1) - 1);
                Assert.Equal(cal.TickOfMonth(m + 1) - first, Gregorian.DaysInMonth(d.Year, d.Month));
                Assert.Equal(d.Day == 1, cal.IsDue(Cadence.Monthly, t));
                Assert.Equal(t == first, cal.IsDue(Cadence.Monthly, t));
                Assert.Equal(d is { Month: 1, Day: 1 }, cal.IsDue(Cadence.Yearly, t));
                Assert.True(cal.IsDue(Cadence.Daily, t));
            }
        }
    }

    [Fact]
    public void TickOfMonth_NegativeAndPositiveIndices_RoundTrip()
    {
        var rnd = new Random(606);
        for (int s = 0; s < 200; s++)
        {
            int year = rnd.Next(1800, 2200), month = rnd.Next(1, 13);
            var cal = new TickCalendar(new CivilDate(year, month, rnd.Next(1, Gregorian.DaysInMonth(year, month) + 1)));
            for (long m = -400; m <= 2400; m += rnd.Next(1, 13))
            {
                long t = cal.TickOfMonth(m);
                Assert.Equal(1, cal.DateOf(t).Day);
                Assert.Equal(m, cal.MonthIndex(t));
                Assert.Equal(m - 1, cal.MonthIndex(t - 1));
            }
        }
    }
}
