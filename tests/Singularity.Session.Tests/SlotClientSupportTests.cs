using System.Collections.Immutable;
using Singularity.Core.Commands;
using Singularity.Core.Institutes;
using Singularity.Core.Policy;
using Singularity.Kernel;
using Singularity.TestSupport;

namespace Singularity.Session.Tests;

/// <summary>DEBT-0115 items the tech map client needs (TASK-0192): the published queue can be reordered; ApplyQueues respects the 60 % check (O-096).</summary>
public class SlotClientSupportTests
{
    private static SliceSession Make(ulong seed, int player) =>
        SliceTestData.Create(new SliceRunConfig { Seed = seed, PlayerActor = player, ScienceSlots = true });

    private static int[] Frontier(SliceSession s, int actor) =>
        [.. Enumerable.Range(0, s.World.Graph.Count).Where(r => s.World.Knowledge.InFrontier(actor, r)).Select(r => s.World.Graph.Number[r])];

    [Fact]
    public void ReorderQueue_AcceptsThePublishedQueue_WithoutTheActiveNode_AndTheActiveNodeStaysFirst()
    {
        int fra = Make(3, 0).World.Scenario.ActorIndex("FRA");
        SliceSession s = Make(3, fra);
        int[] f = Frontier(s, fra);
        int n = 0;
        for (int i = 0; i < 6; i++)   // more than the free institutes take: the rest waits in the list of slot 0
            Assert.True(s.Submit(SourceClass.Player, 0, n++, new AssignNode(fra, 0, f[i], Acknowledged: true)).Accepted);
        s.StepThroughMonth(0);
        InstituteView v = s.View.Actors[fra].Institutes[0];
        Assert.Equal(f[0], v.Active);
        int[] queue = [.. v.Queue];
        Assert.True(queue.Length >= 2, "a queue to reorder");
        int[] reversed = [.. queue.Reverse()];
        Assert.True(s.Submit(SourceClass.Player, 0, n++, new ReorderQueue(fra, 0, [.. reversed])).Accepted);
        s.StepThroughMonth(1);
        Assert.DoesNotContain(s.Outcomes, o => o.Contains("rejected"));
        v = s.View.Actors[fra].Institutes[0];
        Assert.Equal(f[0], v.Active);
        Assert.Equal(reversed, v.Queue);
        // A name outside the queue is still refused.
        Assert.True(s.Submit(SourceClass.Player, 0, n++, new ReorderQueue(fra, 0, [.. reversed.Skip(1).Append(f[0])])).Accepted);
        s.StepThroughMonth(2);
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: NotInList"));
    }

    [Fact]
    public void ApplyQueues_ThatDropsTheCoverageBelow60Percent_NeedsConfirmation_O096()
    {
        SliceSession s = Make(2, 0);
        BudgetShares sh = s.World.SharesOf(0);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetBudgetShares(0, new BudgetShares(Fx6.Zero, sh.Capability, sh.Effort, sh.Procurement, sh.Quality, sh.Security))).Accepted);
        s.StepThroughMonth(0);
        int[] f = Frontier(s, 0);
        var plan = ImmutableArray.Create(new QueuePlan(0, [f[0], f[1]]));
        CommandVerdict asked = s.Submit(SourceClass.Player, 0, 1, new ApplyQueues(0, plan));
        Assert.Equal(CommandRefusal.NeedsConfirmation, asked.Reason);
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new ApplyQueues(0, plan, Acknowledged: true)).Accepted);
    }

    [Fact]
    public void ApplyQueues_AcknowledgedFlag_SurvivesTheCommandLog()
    {
        var c = new LoggedCommand(new CommandKey(0, SourceClass.Player, 0, 1), new ApplyQueues(0, [new QueuePlan(1, [4, 5])], true));
        Assert.True(((ApplyQueues)CommandCodec.Decode(CommandCodec.Encode(c)).Command).Acknowledged);
    }
}
