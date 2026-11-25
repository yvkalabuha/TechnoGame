using Singularity.Core.Commands;
using Singularity.Core.Products;
using Singularity.Core.WhyNot;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0115 (H2, H4, H5): the design handle <c>(slot, generation)</c> end to end. A client names a design by the handle it read from the view; the
/// layer alone decides at application whether it still resolves (ADR-0005 p. 3). The log carries the generation (<c>commands.log</c> v2) so that
/// a replay resolves every command exactly as the original run did.
/// </summary>
public class DesignHandleTests
{
    private const int Usa = 0;

    private static SliceSession Make() => SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 3, ProductAi = false });

    private static int _local;

    private static void Send(SliceSession s, SliceCommand c) =>
        Assert.True(s.Submit(SourceClass.Player, 0, _local++, c).Accepted);

    private static ComposeDesign NewestSuperior(SliceSession s)
    {
        SliceWorld w = s.World;
        ProductsLayout l = w.ProductsLayout;
        bool Knows(int n) { int r = w.Graph.RowOf(n); return r >= 0 && w.Knowledge.IsKnown(Usa, r); }
        int g = 0;
        for (int i = 1; i <= l.Generations; i++)
            g = Knows(l.GenApp[i - 1]) ? i : g;
        Assert.True(g > 0, "the actor knows no APP node");
        var alts = new int[l.SlotCount];
        Assert.True(DesignRules.Superior(l, g, Knows, alts));
        return new ComposeDesign(Usa, g, [.. alts]);
    }

    private static int FirstSlotIn(SliceSession s, DesignStage stage)
    {
        for (int d = 0; d < s.World.ProductsLayout.Designs; d++)
        {
            if (s.World.Products.Stage(Usa, d) == stage)
                return d;
        }
        throw new InvalidOperationException($"no design in {stage}");
    }

    /// <summary>
    /// Compose → project (generation 1) → cancel → compose again: the slot holds a new project of generation 2. Returns the handle the client read
    /// from the view while the first project stood there.
    /// </summary>
    private static (int Slot, DesignHandle Old) ReusedSlot(SliceSession s)
    {
        s.StepThroughMonth(0);
        Send(s, NewestSuperior(s));
        s.StepThroughMonth(1);
        int slot = FirstSlotIn(s, DesignStage.Project);
        DesignHandle old = s.HandleOf(Usa, slot);
        Assert.Equal(1, old.Generation);
        Send(s, new CancelDesign(Usa, old));
        s.StepThroughMonth(2);
        Assert.Equal(DesignStage.Withdrawn, s.World.Products.Stage(Usa, slot));
        Send(s, NewestSuperior(s));
        s.StepThroughMonth(3);
        Assert.Equal(DesignStage.Project, s.World.Products.Stage(Usa, slot));
        Assert.Equal(2, s.World.Products.Generation(Usa, slot));
        return (slot, old);
    }

    // ---- H2: the log

    [Fact]
    public void Log_StartsWithItsVersionHeader_AndAnEmptyLogIsOnlyTheHeader()
    {
        Assert.Equal("{\"commands_log\":2}\n", CommandCodec.EncodeLog([]));
        Assert.Equal(2, CommandCodec.LogVersion);
        Assert.Empty(CommandCodec.DecodeLog(CommandCodec.EncodeLog([])));
        SliceSession s = Make();
        Assert.StartsWith("{\"commands_log\":2}\n", s.CommandLog);
    }

    [Fact]
    public void Codec_RoundTripsEveryHandleCommand_AndWritesTheGenerationRightAfterTheSlot()
    {
        var key = new CommandKey(7, SourceClass.Player, 0, 0);
        var h = new DesignHandle(3, 5);
        SliceCommand[] all =
        [
            new DeriveDesign(1, h, [-1, 2, -1, -1, 4]), new StartSeries(1, h, Fx6.FromInt(3), true), new CloseLine(1, h), new CancelDesign(1, h),
            new RetireCohort(1, h, 4), new SellCohort(1, h, 2),
        ];
        foreach (SliceCommand c in all)
        {
            string line = CommandCodec.Encode(new LoggedCommand(key, c));
            Assert.Equal(line, CommandCodec.Encode(new LoggedCommand(key, CommandCodec.Decode(line).Command)));
            Assert.Equal(c.Kind, CommandCodec.Decode(line).Command.Kind);
        }
        Assert.Contains("\"design\":3,\"design_gen\":5", CommandCodec.Encode(new LoggedCommand(key, all[1])));
        Assert.Contains("\"parent\":3,\"parent_gen\":5", CommandCodec.Encode(new LoggedCommand(key, all[0])));
    }

    [Fact]
    public void ALogOfVersion1_AHeaderlessLog_ALaterVersion_AndAMissingGeneration_AreRejectedLoudly_NeverMigrated()
    {
        const string v1Line = "{\"apply_tick\":1,\"source\":0,\"source_id\":0,\"local_n\":0,\"kind\":\"cancel_design\",\"actor\":0,\"design\":1}";
        var e = Assert.Throws<FormatException>(() => CommandCodec.DecodeLog(v1Line + "\n"));
        Assert.Contains("v1", e.Message);
        Assert.Throws<FormatException>(() => CommandCodec.DecodeLog(""));
        Assert.Throws<FormatException>(() => CommandCodec.DecodeLog("{\"commands_log\":1}\n" + v1Line + "\n"));
        Assert.Throws<FormatException>(() => CommandCodec.DecodeLog("{\"commands_log\":3}\n"));
        Assert.Throws<FormatException>(() => CommandCodec.DecodeLog("{\"commands_log\":2}\n" + v1Line + "\n"));   // v2 line without design_gen
        Assert.Throws<FormatException>(() => CommandCodec.Decode(v1Line));   // a single line of a v2 queue without design_gen
        // Replay takes the same road: a v1 log never reaches the world.
        SliceSession s = Make();
        Assert.Throws<FormatException>(() => SliceSession.Replay(SliceTestData.Data, s.World.Config, v1Line + "\n", 5));
    }

    [Fact]
    public void Submit_AHandleWithGenerationBelow1_IsBadHandle_AndNeverReachesTheLog()
    {
        SliceSession s = Make();
        Assert.Equal(CommandRefusal.BadHandle, s.Submit(SourceClass.Player, 0, 0, new CancelDesign(Usa, new DesignHandle(1, 0))).Reason);
        Assert.Equal(CommandRefusal.BadHandle, s.Submit(SourceClass.Player, 0, 1, new CloseLine(Usa, default)).Reason);
        Assert.Equal(CommandRefusal.BadHandle, s.Submit(SourceClass.Player, 0, 2, new SellCohort(Usa, new DesignHandle(0, -1), 1)).Reason);
        Assert.Empty(CommandCodec.DecodeLog(s.CommandLog));
    }

    // ---- stale from the UI and from replay

    [Fact]
    public void StaleFromTheUi_AViewOfMonth1_CancelAfterTheSlotWasRetaken_IsJournalled_ThenRejectedByTheLayer_AndTheNewProjectIsUntouched()
    {
        SliceSession s = Make();
        (int slot, DesignHandle old) = ReusedSlot(s);
        long before = s.World.Products.RejectionCount(Usa, CommandRejection.StaleHandle);
        Send(s, new CancelDesign(Usa, old));   // the client still holds the handle of the view it read while the first project stood in the slot
        Assert.Equal(old, CommandCodec.DecodeLog(s.CommandLog).Last().Command is CancelDesign c ? c.Design : default);   // accepted into the journal
        s.StepThroughMonth(4);
        Assert.Equal(DesignStage.Project, s.World.Products.Stage(Usa, slot));
        Assert.Equal(2, s.World.Products.Generation(Usa, slot));
        Assert.False(s.World.Products.Cancelled(Usa, slot));
        Assert.Equal(before + 1, s.World.Products.RejectionCount(Usa, CommandRejection.StaleHandle));
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: StaleHandle"));
        // The view the client reads now carries the new handle, taken from the view and not computed by the client.
        Assert.Equal(new DesignHandle(slot, 2), s.HandleOf(Usa, slot));
    }

    [Fact]
    public void StaleFromReplay_TheLogKeepsTheGenerationTheSenderSaw_SoTheReplayRejectsTheSameCommand_AndGivesTheSameHash()
    {
        SliceSession s = Make();
        (int slot, DesignHandle old) = ReusedSlot(s);
        Send(s, new CancelDesign(Usa, old));
        s.StepThroughMonth(4);
        long stale = s.World.Products.RejectionCount(Usa, CommandRejection.StaleHandle);
        Assert.Equal(1, stale);
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: StaleHandle"));
        SliceSession replay = SliceSession.Replay(SliceTestData.Data, s.World.Config, s.CommandLog, s.Tick);
        HashAssert.Equal(s.LastHash, replay.LastHash);
        Assert.Equal(stale, replay.World.Products.RejectionCount(Usa, CommandRejection.StaleHandle));
        Assert.Equal(DesignStage.Project, replay.World.Products.Stage(Usa, slot));
        Assert.Equal(2, replay.World.Products.Generation(Usa, slot));
        Assert.Equal(s.Outcomes, replay.Outcomes);   // the replay publishes the same `rejected: StaleHandle`
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 3)]
    public void L2_TheStaleScenario_GivesTheSameHashAtEveryTick_ForAnyThreadsAndChunks(int threads, int chunkSize)
    {
        List<string> Run(int th, int chunk)
        {
            SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 3, ProductAi = false, Threads = th, ChunkSize = chunk });
            _local = 0;
            (_, DesignHandle old) = ReusedSlot(s);
            Send(s, new CancelDesign(Usa, old));
            var hashes = new List<string>();
            for (long t = 0; t < 40; t++)
            {
                s.Step(1);
                hashes.Add(s.LastHash);
            }
            return hashes;
        }
        Assert.Equal(Run(1, 1), Run(threads, chunkSize));
    }

    [Fact]
    public void ATamperedGenerationInTheLog_IsRejectedByEveryReplayTheSameWay()
    {
        SliceSession s = Make();
        ReusedSlot(s);
        s.StepThroughMonth(4);
        // The first cancel (month 1 → 2) carried generation 1; the log now says 9: it names no project, so it is rejected, the slot is never freed and the
        // second compose finds another one. The run differs from the original, but every replay of the tampered log is the same run.
        string tampered = s.CommandLog.Replace("\"design_gen\":1", "\"design_gen\":9");
        Assert.NotEqual(s.CommandLog, tampered);
        SliceSession a = SliceSession.Replay(SliceTestData.Data, s.World.Config, tampered, s.Tick);
        SliceSession b = SliceSession.Replay(SliceTestData.Data, s.World.Config, tampered, s.Tick);
        HashAssert.Equal(a.LastHash, b.LastHash);
        HashAssert.NotEqual(s.LastHash, a.LastHash);
        Assert.Equal(1, a.World.Products.RejectionCount(Usa, CommandRejection.StaleHandle));
        Assert.Equal(1, a.World.Products.Generation(Usa, FirstSlotIn(a, DesignStage.Project)));   // nothing was retaken
    }

    // ---- H4: the FORCE proxy

    [Fact]
    public void ForceProxy_AProjectInAReusedSlot_GetsItsOwnForce_BecauseTheProxyKeysByHandle()
    {
        SliceSession s = Make();
        var proxy = new ForceProxy(s);
        s.StepThroughMonth(0);
        Send(s, NewestSuperior(s));
        long m = 1;
        int slot = -1;
        for (; m < 200 && proxy.Sent == 0; m++)
        {
            s.StepThroughMonth(m);
            proxy.Issue(m);
        }
        Assert.Equal(1, proxy.Sent);   // the first project reached О with the gate closed and was forced (the command is queued)
        slot = FirstSlotIn(s, DesignStage.Prototype);
        DesignHandle first = s.HandleOf(Usa, slot);
        // The player withdraws it at the same boundary: the player's command goes first in key order, so the queued forced start finds a dead project.
        Send(s, new CancelDesign(Usa, first));
        s.StepThroughMonth(m++);
        Assert.Equal(DesignStage.Withdrawn, s.World.Products.Stage(Usa, slot));
        Assert.False(s.World.Products.SeriesForced(Usa, slot));
        Send(s, NewestSuperior(s));
        for (; m < 400 && s.World.Products.Stage(Usa, slot) != DesignStage.Prototype; m++)
        {
            s.StepThroughMonth(m);
            proxy.Issue(m);
        }
        Assert.Equal(DesignStage.Prototype, s.World.Products.Stage(Usa, slot));
        Assert.Equal(2, s.World.Products.Generation(Usa, slot));
        Assert.Equal(2, proxy.Sent);   // keyed by (actor, slot) the proxy would have stayed at 1
        s.StepThroughMonth(m);
        Assert.Equal(DesignStage.Series, s.World.Products.Stage(Usa, slot));
        Assert.True(s.World.Products.SeriesForced(Usa, slot));
    }

    // ---- H5: why_not

    [Fact]
    public void WhyNot_OfAStaleHandle_NamesNoProject_AndReadsNothingOfTheNewOne()
    {
        SliceSession s = Make();
        (int slot, DesignHandle old) = ReusedSlot(s);
        for (long m = 4; s.World.Products.Stage(Usa, slot) != DesignStage.Prototype; m++)
        {
            Assert.True(m < 200, "the second project never reached stage О");
            s.StepThroughMonth(m);
        }
        DesignHandle now = s.HandleOf(Usa, slot);
        Assert.Equal(new DesignHandle(slot, 2), now);
        Assert.NotEmpty(s.WhyNot(Usa, WhyNotTarget.Design(now)));   // the closed gate of the new project, at least
        IReadOnlyList<Link> stale = s.WhyNot(Usa, WhyNotTarget.Design(old));
        Link only = Assert.Single(stale);   // H5: one StaleHandle link, none of the new project's links
        Assert.Equal(WhyNotCause.StaleHandle, only.Cause);
        Assert.Equal(slot, only.SubjectId);
    }
}
