using Singularity.Core.Commands;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>TASK-0028 (T-fleet): the player's <c>retire_cohort</c> and <c>sell_cohort</c> through the session — form check, log, replay, refusals of the layer.</summary>
public class FleetPlayerCommandTests
{
    private const int Usa = 0;

    private static SliceSession Make() => SliceTestData.Create(new SliceRunConfig { Seed = 11, ProductAi = false });

    [Fact]
    public void Codec_RoundTrips_BothCommands()
    {
        var key = new CommandKey(5, SourceClass.Player, 0, 3);
        foreach (SliceCommand c in new SliceCommand[] { new RetireCohort(1, new DesignHandle(2, 5), 30), new SellCohort(4, new DesignHandle(0, 1), 7) })
        {
            LoggedCommand back = CommandCodec.Decode(CommandCodec.Encode(new LoggedCommand(key, c)));
            Assert.Equal(c, back.Command);
            Assert.Equal(key, back.Key);
        }
        Assert.Contains("\"kind\":\"retire_cohort\"", CommandCodec.Encode(new LoggedCommand(key, new RetireCohort(1, new DesignHandle(2, 5), 30))));
        Assert.Contains("\"kind\":\"sell_cohort\"", CommandCodec.Encode(new LoggedCommand(key, new SellCohort(1, new DesignHandle(2, 5), 30))));
    }

    [Fact]
    public void Submit_ChecksTheForm_AndTheAiCannotSubmit()
    {
        SliceSession s = Make();
        Assert.Equal(CommandRefusal.BadDesign, s.Submit(SourceClass.Player, 0, 0, new RetireCohort(Usa, new DesignHandle(99, 1), 1)).Reason);
        Assert.Equal(CommandRefusal.BadDesign, s.Submit(SourceClass.Player, 0, 1, new SellCohort(Usa, new DesignHandle(-1, 1), 1)).Reason);
        Assert.Equal(CommandRefusal.UnknownActor, s.Submit(SourceClass.Player, 0, 2, new SellCohort(9, new DesignHandle(0, 1), 1)).Reason);
        Assert.Equal(CommandRefusal.BadSource, s.Submit(SourceClass.Ai, 0, 0, new RetireCohort(Usa, s.HandleOf(Usa, 0), 1)).Reason);
        Assert.True(s.Submit(SourceClass.Player, 0, 3, new RetireCohort(Usa, s.HandleOf(Usa, 0), 1)).Accepted);
    }

    [Fact]
    public void ARetireCommand_RunsInTheNextMonthStep_TakesTheOldestUnits_AndTheLogReplaysToTheSameHash()
    {
        SliceSession s = Make();
        s.StepThroughMonth(0);
        Fx6 before = s.World.Products.InService(Usa, 0);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new RetireCohort(Usa, s.HandleOf(Usa, 0), 10)).Accepted);
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new RetireCohort(Usa, s.HandleOf(Usa, 0), 5)).Accepted);   // a second one for the project: refused by the layer (SLOT)
        s.StepThroughMonth(1);
        ProductsSystem p = s.World.Products;
        Assert.Equal(Fx6.FromInt(10), p.Retired(Usa, 0));
        Assert.Equal(1, p.RejectionCount(Usa, CommandRejection.BadRetireSlot));
        Assert.Equal(DesignStage.Series, p.Stage(Usa, 0));   // the start line goes on producing (E1): a retirement from a line is allowed
        Assert.Equal(Fx6.FromInt(10), p.Retired(Usa, 0));
        Assert.True(before > Fx6.Zero);
        SliceSession replay = SliceSession.Replay(SliceTestData.Data, s.World.Config, s.CommandLog, s.Tick);
        HashAssert.Equal(s.LastHash, replay.LastHash);
    }

    [Fact]
    public void ASellCommandWithoutAFrontRunner_IsRefusedByTheLayer_AsNotSurplus()
    {
        SliceSession s = Make();
        s.StepThroughMonth(0);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SellCohort(Usa, s.HandleOf(Usa, 0), 1)).Accepted);
        s.StepThroughMonth(1);
        Assert.Equal(1, s.World.Products.RejectionCount(Usa, CommandRejection.BadSellNotSurplus) + s.World.Products.RejectionCount(Usa, CommandRejection.BadSellStage));
        Assert.Equal(Fx6.Zero, s.World.Products.SaleIncome(Usa));
    }

    [Fact]
    public void MoreThanEightFleetCommandsForOneMonth_AreRefusedAtApplication()
    {
        SliceSession s = Make();
        s.StepThroughMonth(0);
        for (int i = 0; i < CommandLimits.FleetSlots + 1; i++)
            Assert.True(s.Submit(SourceClass.Player, 0, i, new RetireCohort(Usa, new DesignHandle(i % 12, 1), 1)).Accepted);
        s.StepThroughMonth(1);
        Assert.Contains(s.Outcomes, o => o.Contains("SlotOverflow"));
    }

    [Fact]
    public void MasteredRun_StartsAtSixForTheMasteredProcessesOfTheScenario_AndAtZeroForTheRest()
    {
        SliceWorld w = Make().World;
        int usa = Usa, sun = w.Scenario.ActorIndex("SUN");
        foreach (int nodeRow in w.Scenario.MasteredOf(usa))
            Assert.Equal(6, w.Industry.MasteredRun(usa, w.IndustryLayout.RowOf(w.Data.Nodes.Number[nodeRow])));
        int masteredSun = 0;
        for (int r = 0; r < w.IndustryLayout.ProcessCount; r++)
            masteredSun += w.Industry.MasteredRun(sun, r) == 6 ? 1 : 0;
        Assert.Equal(w.Scenario.MasteredOf(sun).Length, masteredSun);
        Assert.Equal(0, w.Industry.MasteredRun(sun, w.IndustryLayout.RowOf(w.Data.Nodes.Number[w.Data.Nodes.IndexOf("s8.prc.jet_engine_assembly_test")])));
    }

    // ---- TASK-0129 (#437): retiring the newest generation needs an acknowledgement

    [Fact]
    public void RetiringTheWholeBestFleet_NeedsConfirmation_AndQueuesNothing()
    {
        SliceSession s = Make();
        s.StepThroughMonth(0);
        DesignHandle h = s.HandleOf(Usa, 0);
        int all = (int)(s.World.Products.InService(Usa, 0).Raw / Fx6.Scale);
        FleetForecast f = s.World.Products.RetireForecast(Usa, 0, all, 1);
        Assert.True(f.GenBefore >= 1 && f.GenAfter < f.GenBefore, $"forecast {f.GenBefore} -> {f.GenAfter}");

        CommandVerdict v = s.Submit(SourceClass.Player, 0, 0, new RetireCohort(Usa, h, all));
        Assert.Equal(CommandRefusal.NeedsConfirmation, v.Reason);
        Assert.Equal(f.GenBefore, v.BestGenBefore);
        Assert.Equal(f.GenAfter, v.BestGenAfter);
        Assert.DoesNotContain("retire_cohort", s.CommandLog);

        // The same local_n may be used again: the refused command did not count as sent.
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new RetireCohort(Usa, h, all, Acknowledged: true)).Accepted);
        s.StepThroughMonth(1);
        Assert.True(s.World.Products.Retired(Usa, 0) >= Fx6.FromInt(all));   // the dust of the last fraction goes with it
        SliceSession replay = SliceSession.Replay(SliceTestData.Data, s.World.Config, s.CommandLog, s.Tick);
        HashAssert.Equal(s.LastHash, replay.LastHash);
    }

    [Fact]
    public void ARetirementThatKeepsTheBestGeneration_NeedsNoConfirmation()
    {
        SliceSession s = Make();
        s.StepThroughMonth(0);
        FleetForecast f = s.World.Products.RetireForecast(Usa, 0, 1, 1);
        Assert.Equal(f.GenBefore, f.GenAfter);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new RetireCohort(Usa, s.HandleOf(Usa, 0), 1)).Accepted);
    }

    [Fact]
    public void Codec_KeepsTheAcknowledgement_AndOldLogsWithoutItStillDecode()
    {
        var key = new CommandKey(5, SourceClass.Player, 0, 3);
        var ack = new RetireCohort(1, new DesignHandle(2, 5), 30, true);
        string line = CommandCodec.Encode(new LoggedCommand(key, ack));
        Assert.Contains("\"ack\":true", line);
        Assert.Equal(ack, CommandCodec.Decode(line).Command);
        string plain = CommandCodec.Encode(new LoggedCommand(key, ack with { Acknowledged = false }));
        Assert.DoesNotContain("ack", plain);
        Assert.False(((RetireCohort)CommandCodec.Decode(plain).Command).Acknowledged);
    }
}
