using Singularity.Core.Commands;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0130 (ADR-0025 п. 12, Проверки п. 7): two worlds with <see cref="ViewScope.Subscribed"/> views and different subscriptions share the process and the thread pool.
/// Each keeps its own hashes and its own views, equal to those of the same world run alone. The subscription is an instance field: nothing leaks between the worlds.
/// </summary>
public class TwoWorldsSubscribedViewsTests
{
    private const long Months = 14;

    private static SliceSession Make(ulong seed, int threads) =>
        SliceTestData.Create(SliceRunConfig.Legacy with { Seed = seed, Threads = threads, ChunkSize = 1, HashEveryTick = true, ViewScope = ViewScope.Subscribed });

    // Every month: the subscription changes, a command goes in, the hash and the dump of the view are recorded.
    private static string[] Run(ulong seed, int threads, int[] first, int[] second, Barrier? barrier)
    {
        SliceSession s = Make(seed, threads);
        s.SetViewSubscription(first);
        s.RebuildView();
        var record = new string[Months + 1];
        for (long m = 0; m <= Months; m++)
        {
            barrier?.SignalAndWait(TimeSpan.FromMinutes(2));
            if (m == Months / 2)
            {
                s.SetViewSubscription(second);
                s.RebuildView();
            }
            if (m % 4 == 1)
                Assert.True(s.Submit(SourceClass.Player, 0, (int)m, new SetEffortFocus(1, [47])).Accepted);
            s.StepThroughMonth(m);
            record[m] = s.LastHash + "|" + ViewSupport.Dump(s.View);
        }
        return record;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task TwoWorldsWithDifferentSubscriptions_AtTheSameTime_EqualTheirSoloRuns(int threads)
    {
        int[] aFirst = [0], aSecond = [2, 3], bFirst = [1, 4], bSecond = [4];
        string[] soloA = Run(1, threads, aFirst, aSecond, null);
        string[] soloB = Run(2, threads, bFirst, bSecond, null);
        using var barrier = new Barrier(2);
        Task<string[]> ta = Task.Run(() => Run(1, threads, aFirst, aSecond, barrier));
        Task<string[]> tb = Task.Run(() => Run(2, threads, bFirst, bSecond, barrier));
        string[][] both = await Task.WhenAll(ta, tb);
        Assert.Equal(soloA, both[0]);
        Assert.Equal(soloB, both[1]);
    }
}
