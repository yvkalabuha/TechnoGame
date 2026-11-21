using Singularity.Core.Products;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0125 (review 2026-11-19, A6): two independent worlds share the process and the .NET thread pool. Each world's monthly hashes must
/// equal those of the same world run alone, whether the worlds are stepped in turn or at the same time, with 2 and 4 threads each.
/// A shared static buffer (or a thread-bound one that carries world data) breaks this.
/// </summary>
public class TwoWorldsOnOnePoolTests
{
    private const long Months = 36;

    public static TheoryData<ulong, ulong, int> Cases
    {
        get
        {
            var d = new TheoryData<ulong, ulong, int>();
            foreach (int threads in new[] { 2, 4 })
            {
                d.Add(1, 2, threads);
                d.Add(3, 4, threads);
                d.Add(5, 7, threads);
            }
            return d;
        }
    }

    private static SliceSession Make(ulong seed, int threads) =>
        SliceTestData.Create(new SliceRunConfig
        {
            Seed = seed,
            Threads = threads,
            ChunkSize = threads == 2 ? 1 : 4,
            HashEveryTick = true,
            // Different arms make the two worlds differ in parameters too, not only in seed.
            SaleArm = seed % 2 == 0 ? SaleArm.ChiOff : SaleArm.None,
        });

    private static string[] Alone(ulong seed, int threads)
    {
        SliceSession s = Make(seed, threads);
        var hashes = new string[Months + 1];
        for (long m = 0; m <= Months; m++)
        {
            s.StepThroughMonth(m);
            hashes[m] = s.LastHash;
        }
        return hashes;
    }

    private static void AssertSame(string[] expected, string[] actual, string what)
    {
        for (int m = 0; m < expected.Length; m++)
            HashAssert.Equal(expected[m], actual[m], $"{what}: month {m}");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TwoWorlds_StepInTurn_KeepTheirOwnHashes(ulong seedA, ulong seedB, int threads)
    {
        string[] a0 = Alone(seedA, threads), b0 = Alone(seedB, threads);
        SliceSession a = Make(seedA, threads), b = Make(seedB, threads);
        var ha = new string[Months + 1];
        var hb = new string[Months + 1];
        for (long m = 0; m <= Months; m++)
        {
            a.StepThroughMonth(m);
            ha[m] = a.LastHash;
            b.StepThroughMonth(m);
            hb[m] = b.LastHash;
        }
        AssertSame(a0, ha, $"A seed {seedA}");
        AssertSame(b0, hb, $"B seed {seedB}");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TwoWorlds_StepAtTheSameTimeOnOnePool_KeepTheirOwnHashes(ulong seedA, ulong seedB, int threads)
    {
        string[] a0 = Alone(seedA, threads), b0 = Alone(seedB, threads);
        var ha = new string[Months + 1];
        var hb = new string[Months + 1];
        // Both worlds run their Parallel.For on the same ThreadPool at once; a barrier per month keeps them overlapping.
        using var barrier = new Barrier(2);
        Task ta = Task.Run(() => Run(seedA, threads, ha, barrier));
        Task tb = Task.Run(() => Run(seedB, threads, hb, barrier));
        await Task.WhenAll(ta, tb);
        AssertSame(a0, ha, $"A seed {seedA}");
        AssertSame(b0, hb, $"B seed {seedB}");
    }

    // A session is created and stepped on one thread (ADR-0017 p. 7); its own Parallel.For still uses the shared pool.
    private static void Run(ulong seed, int threads, string[] hashes, Barrier barrier)
    {
        try
        {
            SliceSession s = Make(seed, threads);
            for (long m = 0; m <= Months; m++)
            {
                if (!barrier.SignalAndWait(TimeSpan.FromMinutes(2)))
                    throw new TimeoutException("the other world did not reach the month barrier");
                s.StepThroughMonth(m);
                hashes[m] = s.LastHash;
            }
        }
        catch
        {
            // A world that fails must not leave the other one waiting at the barrier for ever.
            barrier.RemoveParticipant();
            throw;
        }
    }
}
