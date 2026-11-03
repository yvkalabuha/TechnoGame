using System.Collections.Immutable;
using Singularity.Core.Commands;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

public class CommandTests
{
    private static SliceSession Make() => SliceTestData.Create(new SliceRunConfig { Seed = 3 });

    private static BudgetShares Shares(long r, long c, long d, long p, long q) =>
        new(Fx6.FromRaw(r), Fx6.FromRaw(c), Fx6.FromRaw(d), Fx6.FromRaw(p), Fx6.FromRaw(q));

    private static ImmutableArray<int> Alts(SliceWorld w, int gen)
    {
        var alts = new int[w.ProductsLayout.SlotCount];
        Assert.True(DesignRules.Superior(w.ProductsLayout, gen, n => true, alts));
        return [.. alts];
    }

    // ---- Submit: form and right, with a reason

    [Fact]
    public void Submit_FocusRepeat_IsRefusedWithAReason_AndNeverReachesTheAllocator()
    {
        SliceSession s = Make();
        CommandVerdict v = s.Submit(SourceClass.Player, 0, 0, new SetEffortFocus(0, [47, 47]));
        Assert.Equal(CommandRefusal.FocusRepeat, v.Reason);
        Assert.Contains("47", v.Message);
        Assert.Equal("", s.CommandLog);                  // refused commands are not logged
        for (long m = 0; m < 24; m++)
            s.StepThroughMonth(m);                       // EffortAllocator would throw on a repeat: nothing arrived
    }

    [Fact]
    public void Submit_AiSubmitsNoCommands_ButPlayerAndHarnessMayForce()
    {
        SliceSession s = Make();
        // The AI submits no commands at all (its decisions are pol.* columns): neither with nor without force.
        Assert.Equal(CommandRefusal.BadSource, s.Submit(SourceClass.Ai, 1, 0, new StartSeries(0, 0, Fx6.FromInt(5), true)).Reason);
        Assert.Equal(CommandRefusal.BadSource, s.Submit(SourceClass.Ai, 1, 1, new StartSeries(0, 0, Fx6.FromInt(5), false)).Reason);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new StartSeries(0, 0, Fx6.FromInt(5), true)).Accepted);
        Assert.True(s.Submit(SourceClass.Harness, 0, 0, new StartSeries(0, 1, Fx6.FromInt(5), true)).Accepted);
        Assert.DoesNotContain("\"source\":2", s.CommandLog);
    }

    [Fact]
    public void Submit_SharesNotSummingToOne_OrNegative_AreRefused()
    {
        SliceSession s = Make();
        Assert.Equal(CommandRefusal.BadShares, s.Submit(SourceClass.Player, 0, 0, new SetBudgetShares(0, Shares(300_000, 300_000, 300_000, 100_001, 0))).Reason);
        Assert.Equal(CommandRefusal.BadShares, s.Submit(SourceClass.Player, 0, 1, new SetBudgetShares(0, Shares(1_100_000, -100_000, 0, 0, 0))).Reason);
        Assert.True(s.Submit(SourceClass.Player, 0, 2, new SetBudgetShares(0, Shares(300_000, 300_000, 300_000, 100_000, 0))).Accepted);
    }

    [Fact]
    public void Submit_Malformed_IsRefused()
    {
        SliceSession s = Make();
        Assert.Equal(CommandRefusal.UnknownActor, s.Submit(SourceClass.Player, 0, 0, new CloseLine(9, 0)).Reason);
        Assert.Equal(CommandRefusal.BadDesign, s.Submit(SourceClass.Player, 0, 1, new CloseLine(0, 99)).Reason);
        Assert.Equal(CommandRefusal.FocusTooMany, s.Submit(SourceClass.Player, 0, 2, new SetEffortFocus(0, [47, 48, 49, 50])).Reason);
        Assert.Equal(CommandRefusal.FocusUnknownProcess, s.Submit(SourceClass.Player, 0, 3, new SetEffortFocus(0, [9999])).Reason);
        Assert.Equal(CommandRefusal.BadSource, s.Submit(SourceClass.Ai, 0, 4, new SetEffortFocus(0, [47])).Reason);
        Assert.Equal(CommandRefusal.BadAlternatives, s.Submit(SourceClass.Player, 0, 5, new ComposeDesign(0, 1, [0, 1])).Reason);
        Assert.Equal(CommandRefusal.BadRate, s.Submit(SourceClass.Player, 0, 6, new StartSeries(0, 0, Fx6.FromRaw(-1), false)).Reason);
    }

    [Fact]
    public void Submit_DuplicateAndNonMonotonicLocalN_AreRefused()
    {
        SliceSession s = Make();
        Assert.True(s.Submit(SourceClass.Player, 0, 5, new CloseLine(0, 0)).Accepted);
        Assert.Equal(CommandRefusal.DuplicateKey, s.Submit(SourceClass.Player, 0, 5, new CloseLine(0, 1)).Reason);
        Assert.Equal(CommandRefusal.LocalNotMonotonic, s.Submit(SourceClass.Player, 0, 4, new CloseLine(0, 1)).Reason);
        Assert.True(s.Submit(SourceClass.Player, 1, 0, new CloseLine(0, 1)).Accepted);   // another source has its own counter
        s.Step(1);
        Assert.Equal(CommandRefusal.LocalNotMonotonic, s.Submit(SourceClass.Player, 0, 5, new CloseLine(0, 1)).Reason);   // also across ticks
    }

    [Fact]
    public void Submit_FifthDesignCommandInOneTick_IsRefused()
    {
        SliceSession s = Make();
        ImmutableArray<int> alts = Alts(s.World, 1);
        for (int i = 0; i < CommandLimits.DesignSlots; i++)
            Assert.True(s.Submit(SourceClass.Player, 0, i, new ComposeDesign(0, 1, alts)).Accepted);
        Assert.Equal(CommandRefusal.TooManyDesignCommands, s.Submit(SourceClass.Player, 0, 4, new ComposeDesign(0, 1, alts)).Reason);
        Assert.True(s.Submit(SourceClass.Player, 0, 5, new ComposeDesign(1, 1, alts)).Accepted);   // another actor has its own slots
    }

    // ---- application

    [Fact]
    public void Focus_OfAKnownProcess_IsWrittenToTheColumns_AnUnknownOneIsRefusedAtApplication()
    {
        SliceSession s = Make();
        int sun = s.World.Scenario.ActorIndex("SUN");
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetEffortFocus(sun, [47])).Accepted);   // known to SUN at t0
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new SetEffortFocus(0, [40])).Accepted);        // 40 is not known to USA at t0: stage 2
        s.Step(1);
        Assert.Equal([47, -1, -1], new[] { s.World.Commands.Focus(sun, 0), s.World.Commands.Focus(sun, 1), s.World.Commands.Focus(sun, 2) });
        Assert.Equal(-1, s.World.Commands.Focus(0, 0));
        Assert.Contains(s.Outcomes, o => o.Contains("rejected") && o.Contains("FocusNotKnown") && o.Contains("process 40"));
        Assert.Contains(s.Outcomes, o => o.Contains("accepted"));
        // Clearing the focus.
        Assert.True(s.Submit(SourceClass.Player, 0, 2, new SetEffortFocus(sun, [])).Accepted);
        s.Step(1);
        Assert.Equal(-1, s.World.Commands.Focus(sun, 0));
    }

    [Fact]
    public void PlayerShares_OverrideTheProfile_ForBudgetAdapters()
    {
        SliceSession s = Make();
        BudgetShares custom = Shares(100_000, 100_000, 300_000, 400_000, 100_000);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetBudgetShares(2, custom)).Accepted);
        s.Step(1);
        Assert.Equal(custom, s.World.SharesOf(2));
        Assert.Equal(s.World.Policy.Shares(0), s.World.SharesOf(0));
        s.StepThroughMonth(14);                              // the annual AI decision does not undo the player's setting
        Assert.Equal(custom, s.World.SharesOf(2));
    }

    [Fact]
    public void OneShotCommand_IsSeenByExactlyOneMonthlyStep()
    {
        SliceSession s = Make();
        ImmutableArray<int> alts = Alts(s.World, 1);
        s.StepThroughMonth(3);                               // now on a mid-month day: the next monthly step is month 4
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new ComposeDesign(1, 1, alts)).Accepted);
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new StartSeries(1, 2, Fx6.FromInt(7), true)).Accepted);
        Assert.True(s.Submit(SourceClass.Player, 0, 2, new CloseLine(1, 3)).Accepted);
        s.Step(1);
        CommandState c = s.World.Commands;
        Assert.Empty(c.DesignCommands(1, 3));
        Assert.Single(c.DesignCommands(1, 4));
        Assert.Empty(c.DesignCommands(1, 5));
        Assert.Equal((Fx6.FromInt(7), true), c.Series(1, 2, 4));
        Assert.Null(c.Series(1, 2, 5));
        Assert.Null(c.Series(1, 2, 3));
        Assert.True(c.CloseLine(1, 3, 4));
        Assert.False(c.CloseLine(1, 3, 5));
        Assert.Null(c.Series(0, 2, 4));
    }

    [Fact]
    public void DesignCommands_KeepCommandOrder_AcrossSourcesByKey()
    {
        SliceSession s = Make();
        ImmutableArray<int> alts = Alts(s.World, 1);
        // Harness (class 1) is submitted first but ordered after the player (class 0) at the same apply tick.
        Assert.True(s.Submit(SourceClass.Harness, 0, 0, new ComposeDesign(1, 2, alts)).Accepted);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new ComposeDesign(1, 1, alts)).Accepted);
        s.Step(1);
        var list = s.World.Commands.DesignCommands(1, 0);   // applied at tick 0, a monthly tick: seen by month 0
        Assert.Equal([1, 2], list.Select(x => x.Target));
    }

    // ---- order of sources, journal, replay

    [Fact]
    public void ThreeSources_AtOneApplyTick_GiveOneState_InAnySubmitOrder()
    {
        (SourceClass Source, int Id, SliceCommand Command)[] cmds =
        [
            (SourceClass.Player, 0, new SetBudgetShares(0, Shares(500_000, 100_000, 100_000, 200_000, 100_000))),
            (SourceClass.Harness, 0, new SetBudgetShares(0, Shares(100_000, 100_000, 500_000, 200_000, 100_000))),
            (SourceClass.Player, 1, new SetBudgetShares(0, Shares(100_000, 500_000, 100_000, 200_000, 100_000))),
        ];
        string? hash = null, log = null;
        foreach (int[] order in Permutations(3))
        {
            SliceSession s = Make();
            foreach (int i in order)
                Assert.True(s.Submit(cmds[i].Source, cmds[i].Id, 0, cmds[i].Command).Accepted);
            s.StepThroughMonth(1);
            hash ??= s.LastHash;
            log ??= s.CommandLog;
            Assert.Equal(hash, s.LastHash);
            Assert.Equal(log, s.CommandLog);
            Assert.Equal(cmds[1].Command is SetBudgetShares last ? last.Shares : default, s.World.SharesOf(0));   // the Harness key sorts after both players and applies last
        }
    }

    [Fact]
    public void Replay_OfTheLog_GivesTheSameHash_Tick_AndDate()
    {
        var config = new SliceRunConfig { Seed = 11 };
        SliceSession original = SliceSession.Create(SliceTestData.Data, config);
        var player = new PlayerScript(original);
        for (long m = 0; m < 48; m++)
        {
            player.Issue(m);
            if (m == 5)
                Assert.True(original.Submit(SourceClass.Harness, 2, 0, new SetBudgetShares(1, Shares(200_000, 200_000, 200_000, 300_000, 100_000))).Accepted);
            original.StepThroughMonth(m);
        }
        Assert.True(player.Accepted > 0);
        string log = original.CommandLog;
        SliceSession replay = SliceSession.Replay(SliceTestData.Data, config, log, original.Tick);
        Assert.Equal(original.LastHash, replay.LastHash);
        Assert.Equal(original.Tick, replay.Tick);
        Assert.Equal(original.Date, replay.Date);
        Assert.Equal(log, replay.CommandLog);
    }

    [Fact]
    public void StateHash_IsTakenOnNonEmptyTicksOnly()
    {
        SliceSession s = Make();
        s.StepThroughMonth(0);
        long n = s.HashComputations;
        s.Step(1);                                           // an empty day: no system, no command
        Assert.Equal(n, s.HashComputations);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetEffortFocus(0, [47])).Accepted);
        s.Step(1);                                           // a day with a command only
        Assert.Equal(n + 1, s.HashComputations);
        s.Step(1);
        Assert.Equal(n + 1, s.HashComputations);
        string h = s.LastHash;
        s.StepThroughMonth(1);                               // a monthly step
        Assert.NotEqual(h, s.LastHash);
        Assert.True(s.HashComputations > n + 1);
    }

    [Fact]
    public void CommandApplication_ChangesTheHash_EvenWithoutASystemRunning()
    {
        SliceSession a = Make(), b = Make();
        a.StepThroughMonth(0);
        b.StepThroughMonth(0);
        Assert.True(a.Submit(SourceClass.Player, 0, 0, new SetEffortFocus(0, [47])).Accepted);
        a.Step(1);
        b.Step(1);
        Assert.NotEqual(a.LastHash, b.LastHash);
    }

    // ---- codec

    [Fact]
    public void Codec_RoundTripsEveryCommand_WithOneFormat()
    {
        ImmutableArray<int> alts = [3, -1, 7, 0, 12];
        LoggedCommand[] all =
        [
            new(new CommandKey(10, SourceClass.Player, 0, 0), new SetBudgetShares(1, Shares(350_000, 120_000, 80_000, 400_000, 50_000))),
            new(new CommandKey(10, SourceClass.Player, 0, 1), new SetEffortFocus(2, [47, 40])),
            new(new CommandKey(11, SourceClass.Harness, 3, 0), new ComposeDesign(0, 2, [1, 2, 3, 4, 5])),
            new(new CommandKey(12, SourceClass.Harness, 3, 1), new DeriveDesign(0, 4, alts)),
            new(new CommandKey(13, SourceClass.Player, 0, 2), new StartSeries(4, 1, Fx6.FromRaw(25_500_001), true)),
            new(new CommandKey(14, SourceClass.Player, 0, 3), new CloseLine(4, 2)),
            new(new CommandKey(15, SourceClass.Player, 0, 4), new SetEffortFocus(2, [])),
        ];
        string text = CommandCodec.EncodeLog(all);
        List<LoggedCommand> back = CommandCodec.DecodeLog(text);
        Assert.Equal(all.Length, back.Count);
        Assert.Equal(text, CommandCodec.EncodeLog(back));
        for (int i = 0; i < all.Length; i++)
        {
            Assert.Equal(all[i].Key, back[i].Key);
            Assert.Equal(all[i].Command.Kind, back[i].Command.Kind);
            Assert.Equal(CommandCodec.Encode(all[i]), CommandCodec.Encode(back[i]));
        }
        Assert.Equal(new Fx6().Raw + 25_500_001, ((StartSeries)back[4].Command).Rate.Raw);
        Assert.True(((StartSeries)back[4].Command).Force);
    }

    [Fact]
    public void Codec_Format_IsStable_RawValuesAreStrings()
    {
        var c = new LoggedCommand(new CommandKey(10, SourceClass.Harness, 3, 1), new StartSeries(4, 1, Fx6.FromRaw(25_500_001), true));
        Assert.Equal("{\"apply_tick\":10,\"source\":1,\"source_id\":3,\"local_n\":1,\"kind\":\"start_series\",\"actor\":4,\"design\":1,\"rate\":\"25500001\",\"force\":true}",
            CommandCodec.Encode(c));
        var s = new LoggedCommand(new CommandKey(0, SourceClass.Player, 0, 0), new SetBudgetShares(0, Shares(1, 2, 3, 4, 999_990)));
        Assert.Equal("{\"apply_tick\":0,\"source\":0,\"source_id\":0,\"local_n\":0,\"kind\":\"set_budget_shares\",\"actor\":0,\"shares\":[\"1\",\"2\",\"3\",\"4\",\"999990\"]}",
            CommandCodec.Encode(s));
    }

    [Fact]
    public void Codec_RejectsUnknownKind()
    {
        Assert.Throws<FormatException>(() => CommandCodec.Decode("{\"apply_tick\":0,\"source\":0,\"source_id\":0,\"local_n\":0,\"kind\":\"nope\",\"actor\":0}"));
    }

    [Fact]
    public void KeyOrder_IsTickThenSourceClassThenSourceThenLocal()
    {
        var keys = new[]
        {
            new CommandKey(1, SourceClass.Ai, 0, 0), new CommandKey(1, SourceClass.Player, 9, 9), new CommandKey(0, SourceClass.Ai, 5, 5),
            new CommandKey(1, SourceClass.Harness, 0, 0), new CommandKey(1, SourceClass.Player, 0, 1), new CommandKey(1, SourceClass.Player, 0, 0),
        };
        Array.Sort(keys);
        Assert.Equal(
            [new CommandKey(0, SourceClass.Ai, 5, 5), new CommandKey(1, SourceClass.Player, 0, 0), new CommandKey(1, SourceClass.Player, 0, 1),
             new CommandKey(1, SourceClass.Player, 9, 9), new CommandKey(1, SourceClass.Harness, 0, 0), new CommandKey(1, SourceClass.Ai, 0, 0)], keys);
    }

    private static IEnumerable<int[]> Permutations(int n)
    {
        if (n == 1)
        {
            yield return [0];
            yield break;
        }
        foreach (int[] p in Permutations(n - 1))
        {
            for (int at = 0; at < n; at++)
            {
                var q = new List<int>(p);
                q.Insert(at, n - 1);
                yield return [.. q];
            }
        }
    }
}
