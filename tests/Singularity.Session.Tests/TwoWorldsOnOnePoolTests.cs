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
    // Re-recorded in TASK-0129 (fleet ageing, #431/#435-#437), reason: schema v13 (new column prd.d*.replCredit, in the hash from month 0); the old columns are unchanged to month 176 (projection tests: HorizonTests, T6dTests, FleetAblationTests, ToolingUnmetTests, GoldenProjectionAgainstMainTests), so only the new column changes these hashes.
// Re-recorded in TASK-0133 (train 2: G5/G6 data + fleet ageing): nodes 62-81 and the G5/G6 alternatives change every trajectory; reason and list in the task file.
    private static readonly Dictionary<ulong, string> Pinned = new()
    {
        [1] = "636d250379a12c85923255f5bff68dadcb606568fcde10d99350e2f600823d8a", [2] = "b07686420557f696031732be3200e46b5130839e42438f29f677e404fbd2a04c", [3] = "d9f360075f5c2e1a1af9e7120f311eb9914dbb851b8e3f054dbcb88ee1f37d7f", [4] = "9b71cdaca46e57d39b4eb17affe0f1e68c96a01bfcd092627140f9596df74863", [5] = "2bbd0f6c280a820faeaffb71bb3d636097bc7cadb896b62484bcc17cfdbffee3", [7] = "bf02b423f2d66d6f193fe9d293e70433aed034fa20200e54b981ef1a624d3c0f",
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
        // Diagnostic only: whether the two calls met depends on the thread-pool scheduler. It was 0 of 37 on a
        // windows-latest CI runner (run 45114226799), so it cannot gate the test; the hashes above are the check.
        _ = gate.EnteredWhileOtherInCall;
        AssertPinned(seedA, ha, $"A seed {seedA}, {threads} threads");
        AssertPinned(seedB, hb, $"B seed {seedB}, {threads} threads");
    }

    // A session is created and stepped on one thread (ADR-0017 p. 7); its own Parallel.For still uses the shared pool.
    private sealed class Overlap
    {
        private int _active;
        private int _overlapped;
        public int EnteredWhileOtherInCall => Volatile.Read(ref _overlapped);

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
