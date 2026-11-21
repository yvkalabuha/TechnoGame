using Singularity.Core.Commands;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0124 (review A4): save → load → continue gives the same state hash at every month as the run that never stopped.
/// Anything that lives outside the table and decides the next tick (a cache, a queue, a counter in a field) shows up here as a hash difference.
/// </summary>
public class SnapshotContinuityTests
{
    private const long Months = 40;

    private static SliceRunConfig Cfg(ulong seed, int threads) => new() { Seed = seed, Threads = threads, ChunkSize = 4 };

    private static byte[] Save(SliceSession s)
    {
        using var m = new MemoryStream();
        s.WriteSnapshot(m);
        return m.ToArray();
    }

    private static SliceSession Load(SliceRunConfig cfg, byte[] snapshot)
    {
        SliceSession s = SliceTestData.Create(cfg);
        s.LoadSnapshot(new MemoryStream(snapshot));
        return s;
    }

    private static string[] Reference(ulong seed, int threads)
    {
        SliceSession s = SliceTestData.Create(Cfg(seed, threads));
        var hashes = new string[Months];
        for (long m = 0; m < Months; m++)
        {
            s.StepThroughMonth(m);
            hashes[m] = s.LastHash;
        }
        return hashes;
    }

    /// <summary>Runs to the end of month k, saves, loads into a new session and returns the hashes of months k+1… of the continued run.</summary>
    private static string[] Continued(ulong seed, int threads, long k, Action<SliceSession>? tamper = null)
    {
        SliceSession first = SliceTestData.Create(Cfg(seed, threads));
        first.StepThroughMonth(k);
        SliceSession s = Load(Cfg(seed, threads), Save(first));
        tamper?.Invoke(s);
        var hashes = new List<string>();
        for (long m = k + 1; m < Months; m++)
        {
            s.StepThroughMonth(m);
            hashes.Add(s.LastHash);
        }
        return [.. hashes];
    }

    [Theory]
    [InlineData(1UL, 5L)]
    [InlineData(1UL, 23L)]
    [InlineData(2UL, 5L)]
    [InlineData(2UL, 23L)]
    [InlineData(3UL, 5L)]
    [InlineData(3UL, 23L)]
    public void Continued_EqualsUninterrupted_L1_AndL2(ulong seed, long k)
    {
        string[] reference = Reference(seed, 1);
        string[] tail = reference[(int)(k + 1)..];
        foreach (int threads in new[] { 1, 2, 4 })
        {
            string[] got = Continued(seed, threads, k);
            Assert.Equal(tail.Length, got.Length);
            for (int i = 0; i < tail.Length; i++)
                HashAssert.Equal(tail[i], got[i], $"seed {seed}, K {k}, threads {threads}, month {k + 1 + i}");
        }
    }

    [Fact]
    public void LoadedSession_ReportsTheSavedStateAndClock()
    {
        SliceSession a = SliceTestData.Create(Cfg(1, 1));
        a.StepThroughMonth(11);
        SliceSession b = Load(Cfg(1, 1), Save(a));
        Assert.Equal(a.Tick, b.Tick);
        Assert.Equal(a.Date, b.Date);
        HashAssert.Equal(a.LastHash, b.LastHash, "hash right after the load");
        Assert.Equal(a.World.SchemaFingerprint(), b.World.SchemaFingerprint());
    }

    // Mutation: one column is not restored exactly (every value off by one raw unit after the load). The continuity check must see it.
    [Theory]
    [InlineData("pol.budget")]
    [InlineData("run.seed.ai")]
    public void Continuity_Fails_WhenOneColumnIsNotRestored(string name)
    {
        const ulong seed = 1;
        const long k = 12;
        string[] tail = Reference(seed, 1)[(int)(k + 1)..];
        string[] got = Continued(seed, 1, k, s =>
        {
            LongColumn c = s.World.Table.GetLong(name);
            c.Load([.. c.Current.ToArray().Select(v => v + 1)]);
        });
        Assert.NotEqual(tail, got);
    }

    [Fact]
    public void Journal_IsRestored_AndLocalNStaysMonotonic()
    {
        SliceSession a = SliceTestData.Create(new SliceRunConfig { Seed = 7, ProductAi = false });
        var player = new PlayerScript(a);
        for (long m = 0; m < 24; m++)
        {
            player.Issue(m);
            a.StepThroughMonth(m);
        }
        Assert.True(player.Accepted > 0, "the script must have issued commands");
        SliceSession b = Load(new SliceRunConfig { Seed = 7, ProductAi = false }, Save(a));
        Assert.Equal(a.CommandLog, b.CommandLog);
        HashAssert.Equal(a.LastHash, b.LastHash, "hash after the load");
        for (long m = 24; m < 30; m++)
        {
            a.StepThroughMonth(m);
            b.StepThroughMonth(m);
            HashAssert.Equal(a.LastHash, b.LastHash, $"month {m}");
        }
        // The first logged command of source/id: its local_n is not above the last one, so a replayed number is refused after the load too.
        LoggedCommand last = CommandCodec.DecodeLog(b.CommandLog).Last();
        CommandVerdict v = b.Submit(last.Key.Source, last.Key.SourceId, last.Key.LocalN, last.Command);
        Assert.False(v.Accepted);
    }

    [Fact]
    public void Snapshot_IsRefused_WhileACommandIsPending_AndLoadNeedsANewSession()
    {
        SliceSession a = SliceTestData.Create(new SliceRunConfig { Seed = 7, ProductAi = false });
        var player = new PlayerScript(a);
        player.Issue(0);
        Assert.True(player.Accepted > 0);
        Assert.Throws<InvalidOperationException>(() => Save(a));
        a.StepThroughMonth(0);
        byte[] snap = Save(a);
        Assert.Throws<InvalidOperationException>(() => a.LoadSnapshot(new MemoryStream(snap)));
    }

    [Fact]
    public void Load_OfASnapshotFromAnotherLayout_IsRefused()
    {
        SliceSession a = SliceTestData.Create(Cfg(1, 1));
        a.StepThroughMonth(2);
        byte[] snap = Save(a);
        // The bytes of the schema version sit at offset 4 (length prefix) + 8.
        snap[12] ^= 1;
        Assert.Throws<InvalidDataException>(() => Load(Cfg(1, 1), snap));
    }
}
