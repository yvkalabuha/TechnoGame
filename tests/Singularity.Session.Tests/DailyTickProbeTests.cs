using System.Text.Json;
using Singularity.Core.Commands;
using Singularity.Core.Policy;
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

    // ---- F7a: the view is reused on a day with no step and no command

    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    private static string FreshJson(SliceSession s)
    {
        SliceView v = s.View;
        return JsonSerializer.Serialize(ViewBuilder.Snapshot(s.World, v.Tick, v.StateHash, v.Month, null, null), Json);
    }

    [Fact]
    public void ReusedView_EqualsAFreshSnapshot_OnEveryProbeDay()
    {
        // Seed 3 with the A40 stress arm first closes a line in month 220 (found by a scan): the window below holds that monthly view.
        SliceSession s = SliceSession.Create(SliceTestData.Data, new SliceRunConfig
        {
            Seed = 3, LicKnows = true, EffortShare = EffortShareArm.X0, DailyTickProbe = true, HashEveryTick = true,
        });
        Assert.True(s.World.DailyViewNeutral);
        s.StepThroughMonth(219);
        int reused = 0;
        bool monthlyReasons = false;
        for (int day = 0; day < 75; day++)
        {
            var before = s.View.Actors;
            s.Step(1);
            SliceView v = s.View;
            if (v.Inputs is not null && v.Actors.Any(a => a.Decisions.Closes.Any(c => c.Reason != LineCloseReason.None)))
                monthlyReasons = true;
            Assert.Equal(s.Tick, v.Tick);
            HashAssert.Equal(s.LastHash, v.StateHash, $"day {day}");
            if (v.Inputs is not null)
                continue;   // a monthly tick builds the view from scratch
            Assert.Null(v.Step);
            Assert.Equal(FreshJson(s), JsonSerializer.Serialize(v, Json));
            if (before == v.Actors)
                reused++;
        }
        Assert.True(reused > 40, $"the actors were reused on {reused} days only");
        Assert.True(monthlyReasons, "no monthly view had a close reason: the WithoutCloseReasons trap is not exercised");
    }

    [Fact]
    public void GameSchedule_ReusedDaysCarryNoStateHash()
    {
        var s = SliceSession.Create(SliceTestData.Data,
            new SliceRunConfig { Seed = 7, DailyTickProbe = true, HashCheckpoints = true, HashEveryTick = false });
        int reused = 0;
        for (int day = 0; day < 75; day++)
        {
            s.Step(1);
            if (s.View.Inputs is not null)
                continue;
            Assert.Null(s.View.StateHash);
            reused++;
        }
        Assert.True(reused > 40);
    }

    private sealed class WeeklySystem : ITickSystem
    {
        public int SystemId => 99;
        public Cadence Cadence => Cadence.Weekly;
        public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd) { }
        public void Merge(in TickContext context, int chunkCount) { }
        public void Post(in TickContext context) { }
    }

    [Fact]
    public void AWeeklySystem_IsNotViewNeutral()
    {
        Assert.True(SliceWorld.IsDailyViewNeutral([new DailyProbeSystem()]));
        Assert.False(SliceWorld.IsDailyViewNeutral([new DailyProbeSystem(), new WeeklySystem()]));
    }

    [Fact]
    public void ATickWithACommand_RebuildsTheView()
    {
        SliceSession s = SliceSession.Create(SliceTestData.Data, new SliceRunConfig { Seed = 7, DailyTickProbe = true });
        s.Step(5);
        var before = s.View.Actors;
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new StartSeries(0, 0, Fx6.FromInt(5), true)).Accepted);
        s.Step(1);
        Assert.False(before == s.View.Actors, "a tick with a command must build the actors anew");
        Assert.Equal(FreshJson(s), JsonSerializer.Serialize(s.View, Json));
    }
}
