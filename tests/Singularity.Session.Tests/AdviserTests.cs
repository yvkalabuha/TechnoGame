using System.Collections.Immutable;
using Singularity.Core.Commands;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0153 (#479): for the player actor the product AI only advises. Off = main (no column, same hash); on = no AI line decision reaches the layer for
/// the actor, the proposals are in the view, accepting is the player's own command, a refusal silences the proposal for a year, the line limit holds.
/// </summary>
public class AdviserTests
{
    private const int Horizon = 400;

    private static SliceRunConfig Base => new() { Seed = 1, Threads = 2 };

    private sealed record Found(int Actor, int Design, long Month, Fx6 Rate, List<(long Month, int Actor, DesignCommand Command)> Designs);

    // The first month and actor in which the AI of an unmodified run starts a series; the decision of month m is in the columns before the step of month m.
    // The design commands the AI issued before are kept: under an adviser the player has to send them himself, and then the trajectory is the same.
    private static Found FirstAiStart()
    {
        SliceSession s = SliceTestData.Create(Base);
        SliceWorld w = s.World;
        var designs = new List<(long, int, DesignCommand)>();
        for (long m = 0; m < Horizon; m++)
        {
            for (int a = 0; a < w.Scenario.ActorCount; a++)
            {
                foreach (DesignCommand c in w.Policy.AiDesignCommands(a, m))
                    designs.Add((m, a, c));
                for (int d = 0; d < w.ProductsLayout.Designs; d++)
                {
                    if (w.Policy.AiStart(a, d, m) is { } start)
                        return new Found(a, d, m, start.Rate, designs);
                }
            }
            s.StepThroughMonth(m);
        }
        throw new Xunit.Sdk.XunitException("no AI start in the horizon");
    }

    // The player of the adviser run: sends the design commands the AI would have sent for the months before step `untilMonth`, and stops before that step.
    private sealed class Player(SliceSession s, Found f)
    {
        private int _n;
        public long Month { get; private set; }

        public void StepBefore(long untilMonth)
        {
            while (Month < untilMonth)
            {
                foreach ((long m, int a, DesignCommand c) in f.Designs)
                {
                    if (m != Month || a != f.Actor)
                        continue;
                    SliceCommand command = c.Kind switch
                    {
                        DesignCommandKind.Compose => new ComposeDesign(a, c.Target, c.Alts),
                        DesignCommandKind.Derive => new DeriveDesign(a, c.Design, c.Alts),
                        _ => new CancelDesign(a, c.Design),
                    };
                    Assert.True(s.Submit(SourceClass.Player, 0, ++_n, command).Accepted);
                }
                s.StepThroughMonth(Month);
                Month++;
            }
        }

        public int NextLocal() => ++_n;
    }

    private static SliceSession Advised(int actor, int maxLines = 2) =>
        SliceTestData.Create(Base with { AdviserActors = [actor], AdviserMaxLines = maxLines });

    [Fact]
    public void Off_IsMain_NoAdviserColumns_SameHashAndSchema()
    {
        SliceSession plain = SliceTestData.Create(Base);
        SliceSession off = SliceTestData.Create(Base with { AdviserActors = ImmutableArray<int>.Empty });
        Assert.DoesNotContain(off.World.Table.Columns, c => c.Name.StartsWith("pol.adv.", StringComparison.Ordinal) || c.Name.StartsWith("cmd.refuse.", StringComparison.Ordinal));
        Assert.Equal(plain.World.SchemaFingerprint(), off.World.SchemaFingerprint());
        plain.StepThroughMonth(150);
        off.StepThroughMonth(150);
        HashAssert.Equal(plain.LastHash, off.LastHash);
        Assert.Equal(Base.SnapshotFingerprint(), (Base with { AdviserActors = ImmutableArray<int>.Empty }).SnapshotFingerprint());
        Assert.Empty(off.View.Actors.SelectMany(a => a.Decisions.Proposals));
    }

    [Fact]
    public void Off_RefuseProposalIsRefused_NoAdviser()
    {
        SliceSession s = SliceTestData.Create(Base);
        s.StepThroughMonth(0);
        s.Submit(SourceClass.Player, 0, 1, new RefuseProposal(0, new DesignHandle(0, 1), false));
        s.StepThroughMonth(1);
        Assert.Contains("NoAdviser", string.Join("|", s.Outcomes));
    }

    [Fact]
    public void AdviserActor_AiNeverStartsClosesOrDesigns_ButOthersStillDo()
    {
        Found first = FirstAiStart();
        SliceSession s = Advised(first.Actor);
        var player = new Player(s, first);
        SliceWorld w = s.World;
        bool othersStarted = false;
        for (long m = 0; m <= first.Month + 60; m++)
        {
            player.StepBefore(m);
            for (int d = 0; d < w.ProductsLayout.Designs; d++)
            {
                Assert.Null(w.Policy.AiStart(first.Actor, d, m));
                Assert.Null(w.Policy.AiClose(first.Actor, d, m));
                for (int a = 0; a < w.Scenario.ActorCount && !othersStarted; a++)
                    othersStarted |= a != first.Actor && w.Policy.AiStart(a, d, m) is not null;
            }
            Assert.Empty(w.Policy.AiDesignCommands(first.Actor, m));
        }
        Assert.True(othersStarted || first.Month < 0, "the AI of the other actors keeps leading their lines");
    }

    [Fact]
    public void Proposal_IsInTheView_AtTheMonthOfTheDecision_WithTheRateOfTheAi()
    {
        Found first = FirstAiStart();
        SliceSession s = Advised(first.Actor);
        var player = new Player(s, first);
        player.StepBefore(first.Month);
        ProposalView p = Assert.Single(s.View.Actors[first.Actor].Decisions.Proposals, x => x.Kind == ProposalKind.Open && x.Design == first.Design);
        Assert.Equal(first.Rate, p.Rate);
        Assert.Equal(DesignStage.Prototype, s.World.Products.Stage(first.Actor, first.Design));
        Assert.Equal(p.Handle, s.View.Actors[first.Actor].Designs[first.Design].Handle);
        Assert.Empty(s.View.Actors.Where(a => a.Actor != first.Actor).SelectMany(a => a.Decisions.Proposals));
    }

    [Fact]
    public void Accept_IsAnOrdinaryStartSeries_AndTheLineRunsAtTheProposedRate()
    {
        Found first = FirstAiStart();
        SliceSession s = Advised(first.Actor);
        var player = new Player(s, first);
        player.StepBefore(first.Month);
        ProposalView p = Assert.Single(s.View.Actors[first.Actor].Decisions.Proposals, x => x.Kind == ProposalKind.Open && x.Design == first.Design);
        Assert.IsType<StartSeries>(p.Accept(first.Actor));
        Assert.True(s.Submit(SourceClass.Player, 0, player.NextLocal(), p.Accept(first.Actor)).Accepted);
        s.StepThroughMonth(first.Month);
        Assert.Equal(DesignStage.Series, s.World.Products.Stage(first.Actor, first.Design));
        Assert.Contains("accepted", s.Outcomes[^1]);
        // The line was started by a command: it keeps the rate of the command and is not an AI line.
        Assert.False(s.World.Policy.AiLine(first.Actor, first.Design, s.World.Products.Generation(first.Actor, first.Design)));
    }

    [Fact]
    public void Refuse_SilencesTheProposalForAYear_AndItComesBack()
    {
        Found first = FirstAiStart();
        bool Offered(SliceSession s, long month) => s.World.Policy.AdviserProposals(first.Actor, month).Any(x => x.Kind == ProposalKind.Open && x.Design == first.Design);

        SliceSession control = Advised(first.Actor);
        SliceSession refused = Advised(first.Actor);
        var controlPlayer = new Player(control, first);
        var refusedPlayer = new Player(refused, first);
        controlPlayer.StepBefore(first.Month);
        refusedPlayer.StepBefore(first.Month);
        Assert.True(Offered(control, first.Month));
        Assert.True(Offered(refused, first.Month));
        ProposalView p = Assert.Single(refused.View.Actors[first.Actor].Decisions.Proposals, x => x.Kind == ProposalKind.Open && x.Design == first.Design);
        Assert.True(refused.Submit(SourceClass.Player, 0, refusedPlayer.NextLocal(), p.Refuse(first.Actor)).Accepted);
        control.StepThroughMonth(first.Month);
        refused.StepThroughMonth(first.Month);
        Assert.True(Offered(control, first.Month + 1), "without a refusal the AI proposes again");
        Assert.Contains("accepted", refused.Outcomes[^1]);
        for (long m = first.Month + 1; m < first.Month + 12; m++)
        {
            Assert.False(Offered(refused, m), $"month {m}: the refused proposal must stay silent");
            refused.StepThroughMonth(m);
        }
        Assert.Equal(first.Month + 12, refused.World.Policy.ProposalSilentUntil(first.Actor, first.Design, ProposalKind.Open));
        // The refusal is in the commands log, so a replay gives the same state.
        SliceSession replay = SliceSession.Replay(SliceTestData.Data, Base with { AdviserActors = [first.Actor] }, refused.CommandLog, refused.Tick);
        HashAssert.Equal(refused.LastHash, replay.LastHash);
    }

    [Fact]
    public void LineLimit_RefusesAThirdLine_ButNotARateChangeOfARunningLine()
    {
        SliceSession probe = SliceTestData.Create(Base);
        SliceWorld w = probe.World;
        int actor = -1, line = -1;
        for (int a = 0; a < w.Scenario.ActorCount && actor < 0; a++)
        {
            for (int d = 0; d < w.ProductsLayout.Designs; d++)
            {
                if (w.Products.Stage(a, d) == DesignStage.Series)
                {
                    (actor, line) = (a, d);
                    break;
                }
            }
        }
        Assert.True(actor >= 0, "the scenario has a start line");
        int other = Enumerable.Range(0, w.ProductsLayout.Designs).First(d => d != line);
        var handle = new DesignHandle(other, 1);

        SliceSession limited = Advised(actor, maxLines: 1);
        CommandVerdict v = limited.Submit(SourceClass.Player, 0, 1, new StartSeries(actor, handle, Fx6.One, false));
        Assert.Equal(CommandRefusal.LineLimit, v.Reason);
        Assert.True(limited.Submit(SourceClass.Player, 0, 2, new StartSeries(actor, new DesignHandle(line, 1), Fx6.One, false)).Accepted);

        Assert.True(Advised(actor, maxLines: 2).Submit(SourceClass.Player, 0, 1, new StartSeries(actor, handle, Fx6.One, false)).Accepted);
        // Other actors and runs without an adviser are not limited.
        Assert.True(SliceTestData.Create(Base).Submit(SourceClass.Player, 0, 1, new StartSeries(actor, handle, Fx6.One, false)).Accepted);
    }

    [Fact]
    public void RefuseProposal_RoundTripsThroughTheCommandLog()
    {
        var logged = new LoggedCommand(new CommandKey(5, SourceClass.Player, 0, 1), new RefuseProposal(2, new DesignHandle(3, 4), true));
        LoggedCommand back = CommandCodec.Decode(CommandCodec.Encode(logged));
        Assert.Equal(logged.Command, back.Command);
    }
}
