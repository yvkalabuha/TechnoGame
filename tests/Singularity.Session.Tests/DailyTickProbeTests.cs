namespace Singularity.Session.Tests;

/// <summary>ADR-0016 T1a: <c>DailyTickProbe</c> is execution-only. Off = the build before it; on = every day a non-empty tick, the same outcome.</summary>
public class DailyTickProbeTests
{
    private const long Months = 14;

    private static SliceSession Run(bool probe, int threads = 1, int chunkSize = 1, ulong seed = 7)
    {
        SliceSession s = SliceSession.Create(SliceTestData.Data,
            new SliceRunConfig { Seed = seed, Threads = threads, ChunkSize = chunkSize, DailyTickProbe = probe });
        s.StepThroughMonth(Months);
        return s;
    }

    [Fact]
    public void FlagIsOffByDefault_AndTheDefaultRunEqualsTheExplicitOffRun()
    {
        Assert.False(new SliceRunConfig().DailyTickProbe);
        SliceSession def = SliceTestData.Create();
        def.StepThroughMonth(Months);
        Assert.Equal(def.LastHash, Run(false).LastHash);
    }

    [Fact]
    public void FlagOn_GivesTheSameStateHashAndTheSameViewAsFlagOff()
    {
        SliceSession off = Run(false), on = Run(true);
        Assert.Equal(off.Tick, on.Tick);
        Assert.Equal(off.LastHash, on.LastHash);
        Assert.Equal(off.View.StateHash, on.View.StateHash);
    }

    [Fact]
    public void FlagOn_HashesAndSnapshotsEveryDay_FlagOffOnlyMonthly()
    {
        SliceSession off = SliceTestData.Create(), on = SliceSession.Create(SliceTestData.Data, new SliceRunConfig { Seed = 7, DailyTickProbe = true });
        long off0 = off.HashComputations, on0 = on.HashComputations;
        off.Step(90);
        on.Step(90);
        Assert.Equal(90, on.HashComputations - on0);
        Assert.True(off.HashComputations - off0 < 10, $"flag off hashed {off.HashComputations - off0} times in 90 days");
        Assert.Equal(off.LastHash, on.LastHash);
    }

    [Fact]
    public void FlagOn_IsDeterministic_AcrossThreadsAndChunkSizes()
    {
        string h1 = Run(true, 1, 1).LastHash;
        Assert.Equal(h1, Run(true, 4, 1).LastHash);
        Assert.Equal(h1, Run(true, 4, 3).LastHash);
        Assert.Equal(h1, Run(true, 1, 1).LastHash);
    }
}
