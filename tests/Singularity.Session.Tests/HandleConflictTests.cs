using Singularity.Core.Commands;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0115 round 3: two commands about one slot in one month step. A stale handle is always the lower generation, so it never displaces the valid
/// command (H2); the outcome of every command agrees with the verdict of the layer's step (H2: <c>rejected: StaleHandle</c> in <c>Outcomes</c>).
/// </summary>
public class HandleConflictTests
{
    private const int Usa = 0;
    private static int _local;

    private static SliceSession Make() => SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 3, ProductAi = false });

    private static CommandVerdict Submit(SliceSession s, SliceCommand c) => s.Submit(SourceClass.Player, 0, _local++, c);

    private static void Send(SliceSession s, SliceCommand c) => Assert.True(Submit(s, c).Accepted);

    private static ComposeDesign NewestSuperior(SliceSession s)
    {
        SliceWorld w = s.World;
        ProductsLayout l = w.ProductsLayout;
        bool Knows(int n) { int r = w.Graph.RowOf(n); return r >= 0 && w.Knowledge.IsKnown(Usa, r); }
        int g = 0;
        for (int i = 1; i <= l.Generations; i++)
            g = Knows(l.GenApp[i - 1]) ? i : g;
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

    /// <summary>A project in stage П (generation 1) at the boundary of month 1.</summary>
    private static (int Slot, DesignHandle Handle) LiveProject(SliceSession s)
    {
        s.StepThroughMonth(0);
        Send(s, NewestSuperior(s));
        s.StepThroughMonth(1);
        int slot = FirstSlotIn(s, DesignStage.Project);
        return (slot, s.HandleOf(Usa, slot));
    }

    /// <summary>The slot holds a project of generation 2; the returned old handle (generation 1) is what a stale client still sends.</summary>
    private static (int Slot, DesignHandle Old, DesignHandle Now) ReusedSlot(SliceSession s)
    {
        (int slot, DesignHandle old) = LiveProject(s);
        Send(s, new CancelDesign(Usa, old));
        s.StepThroughMonth(2);
        Send(s, NewestSuperior(s));
        s.StepThroughMonth(3);
        DesignHandle now = s.HandleOf(Usa, slot);
        Assert.Equal(2, now.Generation);
        return (slot, old, now);
    }

    private static long NextMonth(SliceSession s) =>
        s.World.Calendar.MonthIndex(s.World.Calendar.NextDue(Cadence.Monthly, s.Tick));

    private static string[] LastOutcomes(SliceSession s, int n) => [.. s.Outcomes.Skip(s.Outcomes.Count - n)];

    private static bool IsStale(string outcome) => outcome.Contains("rejected: StaleHandle");

    // ---- finding 1: one entry per (actor, slot, month)

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StartSeries_AStaleHandleNeverDisplacesTheValidOne_InEitherArrivalOrder(bool staleFirst)
    {
        SliceSession s = Make();
        (int slot, DesignHandle old, DesignHandle now) = ReusedSlot(s);
        long month = NextMonth(s);
        var stale = new StartSeries(Usa, old, Fx6.FromInt(2), false);
        var valid = new StartSeries(Usa, now, Fx6.FromInt(3), true);
        foreach (StartSeries c in staleFirst ? new[] { stale, valid } : [valid, stale])
            Send(s, c);
        s.Step(1);
        (DesignHandle Design, Fx6 Rate, bool Force) kept = s.World.Commands.Series(Usa, slot, month)!.Value;
        Assert.Equal(now, kept.Design);
        Assert.Equal(Fx6.FromInt(3), kept.Rate);
        string[] outcomes = LastOutcomes(s, 2);
        Assert.Equal(staleFirst ? [true, false] : [false, true], outcomes.Select(IsStale));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CloseLine_AStaleHandleNeverDisplacesTheValidOne_InEitherArrivalOrder(bool staleFirst)
    {
        SliceSession s = Make();
        (int slot, DesignHandle old, DesignHandle now) = ReusedSlot(s);
        long month = NextMonth(s);
        foreach (DesignHandle h in staleFirst ? new[] { old, now } : [now, old])
            Send(s, new CloseLine(Usa, h));
        s.Step(1);
        Assert.Equal(now, s.World.Commands.CloseLine(Usa, slot, month));
        Assert.Equal(staleFirst ? [true, false] : [false, true], LastOutcomes(s, 2).Select(IsStale));
    }

    [Fact]
    public void TwoStartsOfTheSameGeneration_KeepTheLastWriteRule()
    {
        SliceSession s = Make();
        (int slot, _, DesignHandle now) = ReusedSlot(s);
        long month = NextMonth(s);
        Send(s, new StartSeries(Usa, now, Fx6.FromInt(2), false));
        Send(s, new StartSeries(Usa, now, Fx6.FromInt(5), false));
        s.Step(1);
        Assert.Equal(Fx6.FromInt(5), s.World.Commands.Series(Usa, slot, month)!.Value.Rate);
    }

    // ---- finding 2: the outcome agrees with the layer

    [Fact]
    public void TwoCancelsOfOneLiveHandle_FirstAccepted_SecondRejectedAsStale_LikeTheLayer_AndTheReplayAgrees()
    {
        SliceSession s = Make();
        (int slot, DesignHandle h) = LiveProject(s);
        long before = s.World.Products.RejectionCount(Usa, CommandRejection.StaleHandle);
        Send(s, new CancelDesign(Usa, h));
        Send(s, new CancelDesign(Usa, h));
        s.StepThroughMonth(2);
        Assert.Equal(DesignStage.Withdrawn, s.World.Products.Stage(Usa, slot));
        Assert.Equal(before + 1, s.World.Products.RejectionCount(Usa, CommandRejection.StaleHandle));
        Assert.Equal([false, true], LastOutcomes(s, 2).Select(IsStale));
        Assert.Contains("rejected: StaleHandle", LastOutcomes(s, 1)[0]);
        SliceSession replay = SliceSession.Replay(SliceTestData.Data, s.World.Config, s.CommandLog, s.Tick);
        Assert.Equal(s.Outcomes, replay.Outcomes);
        HashAssert.Equal(s.LastHash, replay.LastHash);
    }

    /// <summary>
    /// The model of the outcomes (a cancel ends its handle for every later command of the step, in the layer's phase order) against the layer's own
    /// counter: the number of <c>StaleHandle</c> outcomes equals the rise of <c>RejectionCount(StaleHandle)</c>, for many arrival orders of the commands.
    /// </summary>
    [Fact]
    public void TheNumberOfStaleOutcomes_EqualsTheLayersCounter_ForManyOrdersOfCommandsOnOneHandle()
    {
        for (int rotation = 0; rotation < 7; rotation++)
        {
            foreach (bool reverse in new[] { false, true })
            {
                SliceSession s = Make();
                (int slot, DesignHandle h) = LiveProject(s);
                int slotCount = s.World.ProductsLayout.SlotCount;
                SliceCommand[] all =
                [
                    new CancelDesign(Usa, h), new CloseLine(Usa, h), new CancelDesign(Usa, h), new StartSeries(Usa, h, Fx6.FromInt(2), false),
                    new RetireCohort(Usa, h, 1), new DeriveDesign(Usa, h, [.. Enumerable.Repeat(-1, slotCount)]), new SellCohort(Usa, h, 1),
                ];
                IEnumerable<SliceCommand> ordered = all.Skip(rotation).Concat(all.Take(rotation));
                if (reverse)
                    ordered = ordered.Reverse();
                SliceCommand[] batch = [.. ordered];
                long before = s.World.Products.RejectionCount(Usa, CommandRejection.StaleHandle);
                foreach (SliceCommand c in batch)
                    Send(s, c);
                s.StepThroughMonth(2);
                long layer = s.World.Products.RejectionCount(Usa, CommandRejection.StaleHandle) - before;
                string[] outcomes = LastOutcomes(s, batch.Length);
                Assert.True(layer == outcomes.Count(IsStale),
                    $"rotation {rotation} reverse {reverse}: the layer counted {layer} stale, the outcomes {outcomes.Count(IsStale)}: {string.Join(" | ", outcomes)}");
                SliceSession replay = SliceSession.Replay(SliceTestData.Data, s.World.Config, s.CommandLog, s.Tick);
                Assert.Equal(s.Outcomes, replay.Outcomes);
            }
        }
    }

    // ---- finding 4 and the reviewer's lows 7, 10: views

    [Fact]
    public void TheDecisionView_CarriesTheHandleOfEveryAiDecision()
    {
        SliceRunConfig[] configs = [new() { Seed = 1, Threads = 2, LicKnows = true }, new() { Seed = 3, Threads = 2, LicKnows = true, EffortShare = EffortShareArm.X0 }];
        int starts = 0, cancels = 0, closes = 0;
        foreach (SliceRunConfig config in configs)
        {
          SliceSession s = SliceTestData.Create(config);
          for (long m = 0; m < ViewWalk.Months; m++)
          {
            s.StepThroughMonth(m);
            foreach (ActorView a in s.View.Actors)
            {
                foreach (StartView v in a.Decisions.Starts)
                {
                    Assert.Equal(new DesignHandle(v.Design, s.World.Policy.AiStart(a.Actor, v.Design, m + 1)!.Value.Design.Generation), v.Handle);
                    Assert.True(v.Handle.Generation >= 1);
                    starts++;
                }
                foreach (CloseView v in a.Decisions.Closes)
                {
                    Assert.Equal(v.Design, v.Handle.Slot);
                    Assert.True(v.Handle.Generation >= 1);
                    closes++;
                }
                foreach (DesignCommandView v in a.Decisions.DesignCommands.Where(c => c.Kind != DesignCommandKind.Compose))
                {
                    Assert.Equal(v.Target, v.Handle.Slot);
                    Assert.True(v.Handle.Generation >= 1);
                    cancels++;
                }
            }
          }
        }
        Assert.True(starts > 0 && closes > 0 && cancels > 0, $"the runs produced {starts} starts, {closes} closes and {cancels} cancels: the test does not cross the views");
    }

    [Fact]
    public void DesignViewParent_IsTheLiveParent_NotTheRawSlotOfAProjectThatLeft()
    {
        SliceSession s = Make();
        (int child, DesignHandle h) = LiveProject(s);
        int parent = Enumerable.Range(0, s.World.ProductsLayout.Designs).First(d => d != child && s.World.Products.Stage(Usa, d) == DesignStage.None);
        // The project names the first tenant of the parent slot as its parent; the slot has been retaken since (generation 2): the client must see no parent.
        Load(s, $"prd.d{child}.parent", parent);
        Load(s, $"prd.d{child}.parentGen", 1);
        Load(s, $"prd.d{parent}.generation", 2);
        Assert.Equal(parent, s.World.Products.Parent(Usa, child));
        Assert.Equal(-1, s.World.Products.LiveParent(Usa, child));
        Assert.Equal(-1, ViewBuilder.Snapshot(s.World, s.Tick, null, 1, null, null).Actors[Usa].Designs[child].Parent);
        Load(s, $"prd.d{parent}.generation", 1);   // the parent is the first tenant again: the parent shows
        Assert.Equal(parent, ViewBuilder.Snapshot(s.World, s.Tick, null, 1, null, null).Actors[Usa].Designs[child].Parent);
        Assert.Equal(1, h.Generation);
    }

    private static void Load(SliceSession s, string column, int value)
    {
        IntColumn c = s.World.Table.Columns.OfType<IntColumn>().Single(x => x.Name == column);
        int[] values = c.Current.ToArray();
        values[Usa] = value;
        c.Load(values);
    }

    // ---- finding 6

    [Fact]
    public void ALeadingBlankLine_BeforeTheHeader_IsRefused()
    {
        Assert.Throws<FormatException>(() => CommandCodec.DecodeLog("\n{\"commands_log\":2}\n"));
        Assert.Throws<FormatException>(() => CommandCodec.DecodeLog("\n\n"));
        Assert.Empty(CommandCodec.DecodeLog("{\"commands_log\":2}\n\n"));   // a blank line after the header stays harmless
    }
}
