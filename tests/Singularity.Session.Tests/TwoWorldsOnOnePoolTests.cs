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
    // Re-recorded in TASK-0134 (train 2: Hcap(t), mastery), reason: schema v14 (new columns prd.prog.*, pol.prog.*, pol.below.*, cmd.prog.*, prd.rejected.BadProgramme, all zero before 1978-01); the old columns are unchanged to month 387 (MasteryIdentityTests, HorizonTests, GoldenProjectionAgainstMainTests), so only the new columns change this hash.
    // Re-recorded in TASK-0147 (train 2): nodes 82-99 (G5/G6 rev 3.1 chains) and the ahead-of-time cost defaults k 0.02, pow 2, max 10 (#465) change every trajectory on the current data; explanation: docs/simulation/golden-rerecord-train2.md.
    // Re-recorded in TASK-0180 (#555): requires of 11 early G5/G6 nodes (62, 63, 82, 83, 86, 90, 91, 92, 95, 97, 99) changed, so knowledge opens later and every trajectory moves; no schema change.
    // Re-recorded in TASK-0182: schema v15, new column pol.flt.gFront (the G_front a fleet sale was priced at); the old columns are unchanged (projection tests), so only the new column changes these digests.
    // Re-recorded in TASK-0188 (#575): 57 new nodes 100-156 (9 new processes) change every trajectory on the current data; explanation in the task file.
    // Re-recorded once in the golden batch of TASK-0187 (merged state): slot model on by default + 57 new nodes (#575) + directions and research_speed effects in the hashes + FRA starts with 2 institutes (CEA, 1945-10) + the AI reads the air power index from a column (review fixes).
    // Re-recorded in TASK-0194 (#565): new columns goal.since1-3, goal.prev, goal.change (the air power goal, slot model on); the old columns are unchanged (the goal is only read out of them, nothing reads the new columns back), so only the new columns change the hash.
    private static readonly Dictionary<ulong, string> Pinned = new()
    {
        [1] = "1883dc7dd5ddfe369d5fa798f53b224bba203b9e6ea3ca7fa205750176583810", [2] = "619598d9f1c5261ada9382b863c51c39ac296e2e245f22a7492b020a8fad38ab", [3] = "98c7a6d649aeb3970e396a984f0e756d1b8ebe179e915d9de8a9cb5b79f1abd6", [4] = "2948225481eb06d3228eb37040898d55316821bd5ef430b0972fd7364f208cf6", [5] = "30e0ae70b92395de492c08f8847e4fcca333f7463ec49f95ba39ccfe5e2d9f8d", [7] = "a908d97b37f23177a3a0196a50c3c1a637dc17b7d88097f2e8646b71e3955ab3",
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
