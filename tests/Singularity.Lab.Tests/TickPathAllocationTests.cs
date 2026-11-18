using Singularity.Core.Commands;
using Singularity.Kernel;
using Singularity.Lab.Bench;
using Singularity.Session;
using Xunit.Abstractions;

namespace Singularity.Lab.Tests;

/// <summary>TASK-0107, ADR-0023 п. 2.1: the tick path (no snapshot) allocates at most 1 MiB per month at point A; views do not change the hash.</summary>
public sealed class TickPathAllocationTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sing-tickalloc-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch (IOException) { }
    }

    // Months of the harness load (one SetBudgetShares per actor and month); bytes of the calling thread (threads = 1 runs on it).
    private static (long[] Bytes, string Hash) Run(SynthWorld w, bool skipViews, int months)
    {
        var sw = new RunSwitches(HashCheckpoints: false, HashEveryTick: false, DailyNoop: false, FullCopy: false);
        SliceSession s = SliceSession.Create(w.Data, ProcessBench.Config(w, 1, 1, 8, daily: false, sw));
        s.SkipViews = skipViews;
        SliceWorld world = s.World;
        var bytes = new long[months];
        int local = 0;
        for (int m = 0; m < months; m++)
        {
            for (int a = 0; a < world.Table.RowCount; a++)
                Assert.True(s.Submit(SourceClass.Harness, 0, local++, new SetBudgetShares(a, world.SharesOf(a))).Accepted);
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            s.StepThroughMonth(m);
            bytes[m] = GC.GetAllocatedBytesForCurrentThread() - b0;
        }
        return (bytes, StateHash.ComputeHex(world.Table, SliceWorld.SchemaVersion));
    }

    [Fact]
    public void TickPath_AtPointA_IsWithinOneMiBPerMonth_AndViewsDoNotChangeTheHash()
    {
        var spec = new SynthWorldSpec(200, 2000, SynthGraph.CrossEdges);
        SynthWorld w = SynthWorldGenerator.Build(spec, Path.Combine(_tmp, spec.Name));
        const int months = 26;
        (long[] tick, string hashNoViews) = Run(w, skipViews: true, months);
        (_, string hashViews) = Run(w, skipViews: false, months);
        HashAssert.Equal(hashViews, hashNoViews, "views off vs on");
        output.WriteLine("tick path bytes per month: " + string.Join(", ", tick));
        // Every measured month, the yearly one (12) included, stays within 1 MiB (ADR-0023 п. 2.1).
        for (int m = 2; m < months; m++)
            Assert.True(tick[m] <= 1 << 20, $"month {m}: {tick[m]} B");
        Assert.True(tick.Skip(2).Average() <= 1 << 20, "the mean over a year");
    }
}
