using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// Determinism L2 for the licence deal queue (<c>pol.lic.q.*</c>, TASK-0033 deviation 1): a deal decided in compute of month t is applied by
/// <c>Policy.ApplyDeals</c> at the start of month t+1, so the queue is state that must cross a tick boundary. Session has no save/load, so the
/// closest path is used: the Current values of every column are copied at the boundary, a second session of the same seed is brought to the same
/// tick, its queue is wiped, and the copy is written back with <c>Column.Load</c> (the call the loader would use); then both sessions run on.
/// </summary>
public class LicenceQueueDeterminismTests
{
    private const int Months = 14;
    private const int After = 6;

    private static (long[][] longs, int[][] ints) Snapshot(Table t)
    {
        var longs = new long[t.Columns.Count][];
        var ints = new int[t.Columns.Count][];
        for (int i = 0; i < t.Columns.Count; i++)
        {
            if (t.Columns[i] is LongColumn l) longs[i] = l.Current.ToArray();
            else if (t.Columns[i] is IntColumn n) ints[i] = n.Current.ToArray();
        }
        return (longs, ints);
    }

    private static void Restore(Table t, (long[][] longs, int[][] ints) s, bool skipQueue)
    {
        for (int i = 0; i < t.Columns.Count; i++)
        {
            if (skipQueue && t.Columns[i].Name.StartsWith("pol.lic.q.", StringComparison.Ordinal))
                continue;
            if (t.Columns[i] is LongColumn l) l.Load(s.longs[i]);
            else if (t.Columns[i] is IntColumn n) n.Load(s.ints[i]);
        }
    }

    private static bool Queued(SliceSession s) => s.World.Table.GetInt("pol.lic.q.seller").Current.ToArray().Any(v => v >= 0);

    private static void WipeQueue(SliceSession s)
    {
        var q = s.World.Table.GetInt("pol.lic.q.seller");
        q.Load([.. Enumerable.Repeat(-1, q.Length)]);
    }

    /// <summary>The first (seed, month) at which the queue is non-empty at the tick boundary after the month.</summary>
    private static (ulong seed, long month) FindQueued()
    {
        for (ulong seed = 1; seed <= 12; seed++)
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed });
            for (long m = 0; m < Months; m++)
            {
                s.StepThroughMonth(m);
                if (Queued(s))
                    return (seed, m);
            }
        }
        throw new InvalidOperationException("no month with a queued licence deal for seeds 1..12: the test does not exercise the queue");
    }

    private static List<string> Run(SliceSession s, long from, int count)
    {
        var hashes = new List<string>();
        for (long m = from; m < from + count; m++)
        {
            s.StepThroughMonth(m);
            hashes.Add(s.LastHash);
        }
        return hashes;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void RestoringTheColumnsAtATickBoundaryWithAQueuedDeal_GivesTheSameMonthlyHashesAsTheUninterruptedRun(int threads)
    {
        (ulong seed, long month) = FindQueued();
        var cfg = new SliceRunConfig { Seed = seed, Threads = threads };

        SliceSession whole = SliceTestData.Create(cfg);
        Run(whole, 0, (int)month + 1);
        Assert.True(Queued(whole));
        var snap = Snapshot(whole.World.Table);
        string boundaryHash = whole.LastHash;
        long boundaryTick = whole.Tick;
        List<string> expected = Run(whole, month + 1, After);

        SliceSession restored = SliceTestData.Create(cfg);
        Run(restored, 0, (int)month + 1);
        Assert.Equal(boundaryHash, restored.LastHash);
        WipeQueue(restored);
        Assert.False(Queued(restored));
        Restore(restored.World.Table, snap, skipQueue: false);
        Assert.True(Queued(restored));
        Assert.Equal(boundaryTick, restored.Tick);   // Load does not move the tick

        Assert.Equal(expected, Run(restored, month + 1, After));
    }

    [Fact]
    public void WithoutTheQueueColumns_TheRestoredRunDiverges_SoTheQueueIsStateAndTheTestIsSensitive()
    {
        (ulong seed, long month) = FindQueued();
        var cfg = new SliceRunConfig { Seed = seed };
        SliceSession whole = SliceTestData.Create(cfg);
        Run(whole, 0, (int)month + 1);
        var snap = Snapshot(whole.World.Table);
        List<string> expected = Run(whole, month + 1, After);

        SliceSession lost = SliceTestData.Create(cfg);
        Run(lost, 0, (int)month + 1);
        WipeQueue(lost);
        Restore(lost.World.Table, snap, skipQueue: true);
        Assert.NotEqual(expected, Run(lost, month + 1, After));
    }

    [Fact]
    public void ThreadsOneAndFour_GiveTheSameMonthlyHashesAcrossTheQueuedMonth()
    {
        (ulong seed, long month) = FindQueued();
        int n = (int)month + 1 + After;
        List<string> one = Run(SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 1 }), 0, n);
        List<string> four = Run(SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 4 }), 0, n);
        Assert.Equal(one, four);
    }
}
