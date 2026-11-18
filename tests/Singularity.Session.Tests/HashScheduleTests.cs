using Singularity.Core.Commands;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>ADR-0017 «Проверка» п. 1-3, 5, 6, 8, 10: hash schedule, lazy <c>LastHash</c>, table version, copy-on-write against the full copy.</summary>
public class HashScheduleTests
{
    private const int Days = 180;

    private static SliceSession Create(SliceRunConfig? config = null) =>
        SliceSession.Create(SliceTestData.Data, config ?? new SliceRunConfig { Seed = 7 });

    private static string Fresh(SliceSession s) => StateHash.ComputeHex(s.World.Table, SliceWorld.SchemaVersion);

    // Deterministic pseudo-random days (no System.Random): a command on some days, a lazy read on others.
    private static bool Pick(int day, int salt) => (day * 2654435761u + (uint)salt * 40503u) % 7 < 2;

    [Fact]
    public void LazyLastHash_EqualsAFreshComputation_OnIrregularDays_WithLoadsAndCommandsBetween()
    {
        SliceSession s = Create();
        int local = 0;
        for (int day = 0; day < Days; day++)
        {
            if (Pick(day, 1))
                Assert.True(s.Submit(SourceClass.Player, 0, local++, new SetEffortFocus(0, [47])).Accepted);
            if (Pick(day, 2))
                s.World.Table.GetLong("run.seed.know").Load(s.World.Table.GetLong("run.seed.know").Current.ToArray());   // a write between ticks
            s.Step(1);
            if (Pick(day, 3))
                HashAssert.Equal(Fresh(s), s.LastHash, $"day {day}");
        }
        HashAssert.Equal(Fresh(s), s.LastHash);
    }

    [Fact]
    public void Version_GrowsOnLoad_NotOnAnEmptyTick_NotOnAProbeOnlyTick()
    {
        SliceSession s = Create();
        s.StepThroughMonth(0);
        s.Step(1);                                           // an empty day
        Table table = s.World.Table;
        long v = table.Version;
        s.Step(1);
        Assert.Equal(v, table.Version);

        string before = s.LastHash;
        LongColumn seed = table.GetLong("run.seed.know");
        seed.Load([.. Enumerable.Repeat(123L, table.RowCount)]);
        Assert.True(table.Version > v);
        HashAssert.NotEqual(before, s.LastHash);
        HashAssert.Equal(Fresh(s), s.LastHash);

        SliceSession probe = Create(new SliceRunConfig { Seed = 7, DailyTickProbe = true });
        probe.StepThroughMonth(0);
        long pv = probe.World.Table.Version;
        long computations = probe.HashComputations;
        probe.Step(5);                                       // ticks that run only the probe: no column opens
        Assert.Equal(pv, probe.World.Table.Version);
        Assert.Equal(computations, probe.HashComputations);
    }

    [Fact]
    public void LastHash_FromAnotherThread_Throws_AndTheViewNeverComputes()
    {
        SliceSession s = Create();
        s.StepThroughMonth(0);
        long n = s.HashComputations;
        Exception? thrown = null;
        // A dedicated thread: Task.Run could be inlined on the calling thread when it is waited for.
        var other = new Thread(() =>
        {
            try { _ = s.LastHash; }
            catch (Exception e) { thrown = e; }
        });
        other.Start();
        other.Join();
        Assert.IsType<InvalidOperationException>(thrown);
        _ = s.View.StateHash;
        Assert.Equal(n, s.HashComputations);
    }

    [Fact]
    public void ViewStateHash_IsNullOffTheSchedule_AndTheComputedHashOnIt()
    {
        SliceSession s = Create(new SliceRunConfig { Seed = 7, DailyTickProbe = true });
        for (int day = 0; day < 70; day++)
        {
            s.Step(1);
            bool monthly = s.World.Calendar.IsDue(Cadence.Monthly, s.Tick - 1);
            if (monthly)
                HashAssert.Equal(s.LastHash, s.View.StateHash, $"day {day}");
            else
                Assert.Null(s.View.StateHash);
        }
        SliceSession off = Create(new SliceRunConfig { Seed = 7, HashCheckpoints = false });
        off.StepThroughMonth(2);
        Assert.Null(off.View.StateHash);
        HashAssert.Equal(Fresh(off), off.LastHash);
    }

    [Fact]
    public void WithoutCheckpoints_AndWithoutReads_OnlyTheStartHashIsComputed()
    {
        SliceSession s = Create(new SliceRunConfig { Seed = 7, HashCheckpoints = false });
        Assert.Equal(1, s.HashComputations);
        int local = 0;
        for (int day = 0; day < 100; day++)
        {
            if (day % 9 == 0)
                Assert.True(s.Submit(SourceClass.Player, 0, local++, new SetEffortFocus(0, [47])).Accepted);
            s.Step(1);
        }
        Assert.Equal(1, s.HashComputations);
        _ = s.LastHash;
        Assert.Equal(2, s.HashComputations);
        _ = s.LastHash;                                      // cached
        Assert.Equal(2, s.HashComputations);
    }

    [Fact]
    public void CopyOnWrite_EqualsTheFullCopyMode_OnEveryDay_WithCommands()
    {
        var config = new SliceRunConfig { Seed = 7, HashEveryTick = true, DailyTickProbe = true };
        SliceSession cow = Create(config), full = Create(config);
        full.World.Table.FullCopyMode = true;
        int local = 0;
        for (int day = 0; day < Days; day++)
        {
            if (Pick(day, 1))
            {
                Assert.True(cow.Submit(SourceClass.Player, 0, local, new SetEffortFocus(0, [47])).Accepted);
                Assert.True(full.Submit(SourceClass.Player, 0, local++, new SetEffortFocus(0, [47])).Accepted);
            }
            cow.Step(1);
            full.Step(1);
            HashAssert.Equal(full.View.StateHash, cow.View.StateHash, $"day {day}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void L2_HashesAtCheckpointsAndAtTheEnd_AreEqualAcrossThreadsAndChunkSizes(bool everyTick)
    {
        List<string?>? reference = null;
        foreach (int threads in new[] { 1, 4, 8, 16 })
        {
            foreach (int chunk in new[] { 1, 8, 32 })
            {
                SliceSession s = Create(new SliceRunConfig { Seed = 7, Threads = threads, ChunkSize = chunk, HashEveryTick = everyTick, DailyTickProbe = everyTick });
                var hashes = new List<string?>();
                for (int day = 0; day < 65; day++)
                {
                    s.Step(1);
                    hashes.Add(s.View.StateHash);
                }
                hashes.Add(s.LastHash);
                if (reference is null)
                {
                    reference = hashes;
                    continue;
                }
                Assert.Equal(reference.Count, hashes.Count);
                for (int i = 0; i < hashes.Count; i++)
                    HashAssert.Equal(reference[i], hashes[i], $"threads {threads}, chunk {chunk}, index {i}");
            }
        }
    }

    [Fact]
    public void Replay_EndsWithAHash_AndEqualsTheOriginalRun()
    {
        SliceSession original = Create(new SliceRunConfig { Seed = 7, HashCheckpoints = false });
        original.Step(10);
        Assert.True(original.Submit(SourceClass.Player, 0, 0, new SetEffortFocus(0, [47])).Accepted);
        original.Step(80);
        string log = original.CommandLog;
        string expected = original.LastHash;

        SliceSession replay = SliceSession.Replay(SliceTestData.Data, new SliceRunConfig { Seed = 7, HashCheckpoints = false }, log, 90);
        Assert.Equal(2, replay.HashComputations);            // the start and the end of the replay
        HashAssert.Equal(expected, replay.LastHash);
        Assert.Equal(2, replay.HashComputations);
    }

    [Fact]
    public void Step_FromAnotherThread_Throws_AndChangesNothing()
    {
        SliceSession s = Create();
        long tick = s.Tick;
        Exception? thrown = null;
        var other = new Thread(() =>
        {
            try { s.Step(1); }
            catch (Exception e) { thrown = e; }
        });
        other.Start();
        other.Join();
        Assert.IsType<InvalidOperationException>(thrown);
        Assert.Equal(tick, s.Tick);
    }

    [Fact]
    public void HashEveryTick_HashesEveryNonEmptyTick_EvenWithoutAPublishedView()
    {
        // No probe: a day on which nothing runs is empty and computes nothing; every other day is hashed.
        SliceSession s = Create(new SliceRunConfig { Seed = 7, HashEveryTick = true });
        long expected = s.HashComputations;
        for (int day = 0; day < 70; day++)
        {
            s.Step(1);
            bool nonEmpty = s.World.Runner.LastTickRan;
            if (nonEmpty)
                expected++;
            Assert.Equal(expected, s.HashComputations);
            if (nonEmpty)
                HashAssert.Equal(Fresh(s), s.LastHash, $"day {day}");
        }
    }
}
