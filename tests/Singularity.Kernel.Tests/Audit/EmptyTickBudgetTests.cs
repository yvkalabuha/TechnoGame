using Xunit.Abstractions;
using System.Diagnostics;
using Singularity.Kernel;

namespace Singularity.Kernel.Tests.Audit;

/// <summary>Runs only when SINGULARITY_BENCH=1 (CI benchmark job on Linux, ADR-0006 "Проверка").</summary>
public sealed class BenchmarkFactAttribute : FactAttribute
{
    public BenchmarkFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SINGULARITY_BENCH") != "1")
            Skip = "Benchmark: set SINGULARITY_BENCH=1 to run.";
    }
}

/// <summary>
/// Cost of empty day ticks over a slice run: 11 688 day ticks from 1945-09-01, 384 non-empty (ADR-0006,
/// slice spec §10: total &lt; 1 ms). Only the idle stretches between monthly ticks are timed.
/// </summary>
public class EmptyTickBudgetTests(ITestOutputHelper output)
{
    private const int SliceTicks = 11_688;

    [Fact]
    public void EmptyDays_OfASliceRun_AreCheap_GenerousThreshold()
    {
        // Generous bound so shared CI runners do not flake; the strict 1 ms budget is the benchmark below.
        (TimeSpan idle, int idleDays) = MeasureBest(runs: 3);
        Assert.Equal(SliceTicks - 384, idleDays);
        Assert.True(idle < TimeSpan.FromMilliseconds(25), $"empty days took {idle.TotalMilliseconds:F3} ms");
    }

    [BenchmarkFact]
    [Trait("Category", "Benchmark")]
    public void EmptyDays_OfASliceRun_Under1Ms()
    {
        foreach (int systems in new[] { 1, 6 })
        {
            (TimeSpan idle, _) = MeasureBest(runs: 10, systems);
            output.WriteLine($"{systems} monthly system(s): empty days {idle.TotalMilliseconds:F3} ms");
            Assert.True(idle < TimeSpan.FromMilliseconds(1), $"{systems} systems: empty days took {idle.TotalMilliseconds:F3} ms (budget 1 ms)");
        }
    }

    private static (TimeSpan, int) MeasureBest(int runs, int systemCount = 6)
    {
        Measure(systemCount); // JIT warm-up
        TimeSpan best = TimeSpan.MaxValue;
        int idleDays = 0;
        for (int i = 0; i < runs; i++)
        {
            (TimeSpan t, idleDays) = Measure(systemCount);
            best = t < best ? t : best;
        }
        return (best, idleDays);
    }

    private static (TimeSpan, int) Measure(int systemCount)
    {
        var cal = new TickCalendar(new CivilDate(1945, 9, 1));
        var table = new Table(5);
        // The slice has several monthly systems (knowledge, capability, AI, budget …): each one adds an IsDue check per day.
        var systems = Enumerable.Range(0, systemCount).Select(i => (ITickSystem)new MonthlyNoop(table, $"s{i}")).ToArray();
        var runner = new TickRunner(table, 1, systems, chunkSize: 1, maxThreads: 4, cal);
        long ticks = 0;
        int idleDays = 0;
        for (long m = 0; m < 384; m++)
        {
            runner.RunTick(); // day 1 of month m: not timed
            long end = cal.TickOfMonth(m + 1);
            idleDays += (int)(end - runner.Tick);
            long t0 = Stopwatch.GetTimestamp();
            while (runner.Tick < end)
                runner.RunTick();
            ticks += Stopwatch.GetTimestamp() - t0;
        }
        Assert.Equal(SliceTicks, runner.Tick);
        return (Stopwatch.GetElapsedTime(0, ticks), idleDays);
    }

    private sealed class MonthlyNoop(Table table, string name) : ITickSystem
    {
        private readonly LongColumn _c = table.AddLong(name);

        public int SystemId => 4;
        public Cadence Cadence => Cadence.Monthly;

        public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
        {
            for (int r = rowStart; r < rowEnd; r++)
                _c.Next[r] = _c.Current[r] + 1;
        }

        public void Merge(in TickContext context, int chunkCount) { }
        public void Post(in TickContext context) { }
    }
}
