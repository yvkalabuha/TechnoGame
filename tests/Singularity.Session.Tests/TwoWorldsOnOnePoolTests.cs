using System.Security.Cryptography;
using System.Text;
using Singularity.Core.Products;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0125 (review 2026-11-19, A6): two independent worlds share the process and the .NET thread pool. Each world's monthly hashes must
/// equal the PINNED reference of its seed, whether the worlds are stepped in turn or at the same time, with 1, 2 and 4 threads each.
/// A shared static buffer (or a thread-bound one that carries world data) breaks this.
/// The reference is pinned, not recomputed in this process: a process-lifetime leak (a static cache of the first world's config) would
/// poison a reference run in the same process as well. The digest is SHA-256 of the 37 monthly hashes joined by '\n'; it is the same
/// for 1, 2 and 4 threads (L2). Re-record it with the goldens on a schema train (a failure prints the actual digest and all hashes).
/// </summary>
public class TwoWorldsOnOnePoolTests
{
    private const long Months = 36;

    public static TheoryData<ulong, ulong, int> Cases
    {
        get
        {
            var d = new TheoryData<ulong, ulong, int>();
            foreach (int threads in new[] { 1, 2, 4 })
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
            ChunkSize = 1, // 5 actors give 5 chunks, at least as many as Threads, so every thread count really runs in parallel
            HashEveryTick = true,
            // Different arms make the two worlds differ in parameters too, not only in seed.
            SaleArm = seed % 2 == 0 ? SaleArm.ChiOff : SaleArm.None,
        });

    // Seed -> digest of the monthly hashes of that world run alone (recorded on the unmodified code, threads 2 and 4 agree).
    private static readonly Dictionary<ulong, string> Pinned = new()
    {
        [1] = "dd295a0d3073990a038e9282357c47bf1b0d35e2e86d3d8ecc7f2170b70a9142", [2] = "f084e313a7f7b4ddbed5490d3672fc73827e1e7790a07c356f04cd7545d13591", [3] = "f6e47b9abb84a198ce7831e7e6470505f5836455610606f5a1984ce200c0d5a2", [4] = "418d215c5c100a381b004ef8acd7e4962d5ff15943328749e96758c24436c365", [5] = "b20711e98d563d6059d42f00ca562c6314822443b4940639382f38afba008e78", [7] = "b898c0f56e8e64ba9c960eca665d477737290175115843033e7c209ab86c06fa",
    };

    private static void AssertPinned(ulong seed, string[] actual, string what)
    {
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(string.Join('\n', actual)))).ToLowerInvariant();
        Assert.True(Pinned[seed] == digest, $"{what}: digest {digest}, pinned {Pinned[seed]}; hashes:\n{string.Join('\n', actual)}");
    }

    [Theory]
    [InlineData(1UL, 1)]
    [InlineData(4UL, 1)]
    [InlineData(1UL, 2)]
    [InlineData(2UL, 2)]
    [InlineData(4UL, 4)]
    public void OneWorldAlone_MatchesItsPinnedDigest(ulong seed, int threads)
    {
        SliceSession s = Make(seed, threads);
        var hashes = new string[Months + 1];
        for (long m = 0; m <= Months; m++)
        {
            s.StepThroughMonth(m);
            hashes[m] = s.LastHash;
        }
        AssertPinned(seed, hashes, $"seed {seed}, {threads} threads");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TwoWorlds_StepInTurn_KeepTheirOwnHashes(ulong seedA, ulong seedB, int threads)
    {
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
        AssertPinned(seedA, ha, $"A seed {seedA}, {threads} threads");
        AssertPinned(seedB, hb, $"B seed {seedB}, {threads} threads");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TwoWorlds_StepAtTheSameTimeOnOnePool_KeepTheirOwnHashes(ulong seedA, ulong seedB, int threads)
    {
        var ha = new string[Months + 1];
        var hb = new string[Months + 1];
        // Both worlds run their Parallel.For on the same ThreadPool at once; a barrier per month keeps them overlapping.
        using var barrier = new Barrier(2);
        var gate = new Overlap();
        Task ta = Task.Run(() => Run(seedA, threads, ha, barrier, gate));
        Task tb = Task.Run(() => Run(seedB, threads, hb, barrier, gate));
        await Task.WhenAll(ta, tb);
        // The pinned digests alone would also pass if the two worlds never ran together. At least once a world entered its step while the
        // other was inside its own (the barrier releases them together and a step takes far longer than the release skew). This proves the
        // overlap of the whole step; the overlap inside the compute phase needs a production hook (see TASK-0127, Ход работы).
        Assert.True(gate.Overlapped >= 1, $"overlapped in {gate.Overlapped} of {Months + 1} months; the test did not exercise the shared pool");
        AssertPinned(seedA, ha, $"A seed {seedA}, {threads} threads");
        AssertPinned(seedB, hb, $"B seed {seedB}, {threads} threads");
    }

    // A session is created and stepped on one thread (ADR-0017 p. 7); its own Parallel.For still uses the shared pool.
    private sealed class Overlap
    {
        private int _active;
        private int _overlapped;
        public int Overlapped => Volatile.Read(ref _overlapped);

        // A month counts once: when this world entered while the other was inside its step.
        public void Step(Action step)
        {
            bool met = Interlocked.Increment(ref _active) == 2;
            step();
            Interlocked.Decrement(ref _active);
            if (met)
                Interlocked.Increment(ref _overlapped);
        }
    }

    private static void Run(ulong seed, int threads, string[] hashes, Barrier barrier, Overlap gate)
    {
        try
        {
            SliceSession s = Make(seed, threads);
            for (long m = 0; m <= Months; m++)
            {
                if (!barrier.SignalAndWait(TimeSpan.FromMinutes(2)))
                    throw new TimeoutException("the other world did not reach the month barrier");
                long month = m;
                gate.Step(() => s.StepThroughMonth(month));
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
