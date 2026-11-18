using Singularity.Kernel;

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
        HashAssert.Equal(def.LastHash, Run(false).LastHash);
    }

    [Fact]
    public void FlagOn_GivesTheSameStateHashAndTheSameViewAsFlagOff()
    {
        SliceSession off = Run(false), on = Run(true);
        Assert.Equal(off.Tick, on.Tick);
        HashAssert.Equal(off.LastHash, on.LastHash);
        HashAssert.Equal(off.View.StateHash, on.View.StateHash);
    }

    [Fact]
    public void FlagOn_SnapshotsEveryDay_HashesEveryDayOnlyWithHashEveryTick()
    {
        var every = SliceSession.Create(SliceTestData.Data, new SliceRunConfig { Seed = 7, DailyTickProbe = true, HashEveryTick = true });
        var checkpoints = SliceSession.Create(SliceTestData.Data, new SliceRunConfig { Seed = 7, DailyTickProbe = true });
        for (int day = 0; day < 90; day++)
        {
            every.Step(1);
            checkpoints.Step(1);
            Assert.Equal(every.Tick, every.View.Tick);
            Assert.Equal(checkpoints.Tick, checkpoints.View.Tick);
            HashAssert.Equal(every.LastHash, every.View.StateHash, $"day {day}");
            bool monthly = checkpoints.World.Calendar.IsDue(Cadence.Monthly, checkpoints.Tick - 1);
            bool hashed = checkpoints.View.StateHash is not null;
            Assert.Equal(monthly, hashed);
        }
        HashAssert.Equal(every.LastHash, checkpoints.LastHash);
    }

    [Fact]
    public void FlagOn_IsDeterministic_AcrossThreadsAndChunkSizes()
    {
        string h1 = Run(true, 1, 1).LastHash;
        HashAssert.Equal(h1, Run(true, 4, 1).LastHash);
        HashAssert.Equal(h1, Run(true, 4, 3).LastHash);
        HashAssert.Equal(h1, Run(true, 1, 1).LastHash);
    }
}
