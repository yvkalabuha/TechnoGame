using System.Collections.Immutable;
using Singularity.Client.Spike;
using Singularity.Core.Commands;
using Singularity.Core.Data;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Client.Audio.Tests;

/// <summary>TASK-0192: the pure rules behind the institute strip, the node card, the path and the queue panel.</summary>
public class ScienceMapLogicTests
{
    static InstituteView Inst(int slot, bool standing = true, int active = 0, int[]? queue = null, long buildStart = -1, int buildMonths = 0, int focus = 0, double progress = 0, double left = 0) =>
        new(slot, "I" + slot, standing, 0, [], buildStart, buildMonths, Fx6.Zero, 1950, 3, Active: active, Queue: queue is null ? ImmutableArray<int>.Empty : [.. queue],
            FocusLevel: focus, ActiveProgress: Fx6.FromRaw((long)(progress * Fx6.Scale)), MonthsLeft: Fx6.FromRaw((long)(left * Fx6.Scale)));

    [Fact]
    public void Strip_ClassifiesFreeBusyBuildingAndUnavailable()
    {
        Assert.Equal(SlotKind.Free, ScienceMapLogic.Strip(Inst(0), 10).Kind);
        var busy = ScienceMapLogic.Strip(Inst(1, active: 7, queue: [8, 9], focus: 1, progress: 0.5, left: 5), 10);
        Assert.Equal((SlotKind.Busy, 2, 1, 5, 5), (busy.Kind, busy.Queued, busy.FocusLevel, busy.MonthsLeft, busy.ProgressMonths));
        var build = ScienceMapLogic.Strip(Inst(2, false, buildStart: 4, buildMonths: 24), 10);
        Assert.Equal((SlotKind.Building, 6, 18), (build.Kind, build.BuildDone, build.BuildLeft));
        Assert.Equal(SlotKind.Unavailable, ScienceMapLogic.Strip(Inst(3, false), 10).Kind);
    }

    [Fact]
    public void Page_ShowsSixAndPagesTheRest()
    {
        Assert.Equal((0, 6), ScienceMapLogic.Page(8, 0));
        Assert.Equal((6, 2), ScienceMapLogic.Page(8, 1));
        Assert.Equal((6, 2), ScienceMapLogic.Page(8, 9));
        Assert.Equal((0, 3), ScienceMapLogic.Page(3, 0));
        Assert.Equal(2, ScienceMapLogic.PageCount(8));
    }

    [Fact]
    public void CanAssign_GivesTheReasonInTheOrderOfTheSpec()
    {
        var insts = ImmutableArray.Create(Inst(0, active: 5, queue: [1, 2, 3, 4, 6]), Inst(1, active: 6), Inst(2, false));
        int[] order = [0, 1];
        Assert.Equal(AssignWhy.Known, ScienceMapLogic.CanAssign(true, true, 0, -1, insts, order, out _).Why);
        Assert.Equal(AssignWhy.AlreadyAssigned, ScienceMapLogic.CanAssign(false, true, 0, 1, insts, order, out _).Why);
        var nf = ScienceMapLogic.CanAssign(false, false, 42, -1, insts, order, out _);
        Assert.Equal((AssignWhy.NotInFrontier, 42), (nf.Why, nf.Arg));
        Assert.Equal(AssignWhy.NoInstitute, ScienceMapLogic.CanAssign(false, true, 0, -1, insts, [], out _).Why);
        // Slot 0 has a full queue, slot 1 has room: the node goes to slot 1's queue.
        Assert.True(ScienceMapLogic.CanAssign(false, true, 0, -1, insts, order, out int t).Enabled);
        Assert.Equal(1, t);
        var full = ImmutableArray.Create(Inst(0, active: 5, queue: [1, 2, 3, 4, 6]));
        Assert.Equal(AssignWhy.QueueFull, ScienceMapLogic.CanAssign(false, true, 0, -1, full, [0], out _).Why);
        // A free institute wins over a queue even when it is worse in order.
        var free = ImmutableArray.Create(Inst(0, active: 5), Inst(1));
        ScienceMapLogic.CanAssign(false, true, 0, -1, free, [0, 1], out t);
        Assert.Equal(1, t);
    }

    [Fact]
    public void FocusRows_ShowExtraPriceOverNoFocus()
    {
        Fx6 F(double v) => Fx6.FromRaw((long)(v * Fx6.Scale));
        var rows = ScienceMapLogic.FocusRows([new FocusQuoteView(0, F(13.2), F(3), F(48)), new FocusQuoteView(1, F(10.1), F(5), F(168)), new FocusQuoteView(2, F(8.0), F(9), F(308))]);
        Assert.Equal([(0, 14, 0.0), (1, 11, 120.0), (2, 8, 260.0)], rows.Select(r => (r.Level, r.Months, r.Extra)));
        Assert.Empty(ScienceMapLogic.FocusRows(default));
    }

    [Fact]
    public void Move_SwapsNeighboursOnly_AndRefusesTheEdges()
    {
        Assert.Equal([2, 1, 3], ScienceMapLogic.Move([1, 2, 3], 1, -1)!);
        Assert.Equal([1, 3, 2], ScienceMapLogic.Move([1, 2, 3], 1, 1)!);
        Assert.Null(ScienceMapLogic.Move([1, 2, 3], 0, -1));
        Assert.Null(ScienceMapLogic.Move([1, 2, 3], 2, 1));
    }

    [Fact]
    public void Reassign_MovesOrRemovesOneNode_KeepsEmptiedLists()
    {
        ImmutableArray<QueuePlan> plan = [new QueuePlan(0, [1, 2]), new QueuePlan(1, [3])];
        var moved = ScienceMapLogic.Reassign(plan, 2, 1);
        Assert.Equal([1], moved[0].Nodes.ToArray());
        Assert.Equal([3, 2], moved[1].Nodes.ToArray());
        var gone = ScienceMapLogic.Reassign(plan, 3, -1);
        Assert.Equal(2, gone.Length);
        Assert.Empty(gone[1].Nodes);
    }

    [Fact]
    public void PlanGroups_TotalsAreSumsOfTheirLines_AndSummaryIsTheSumOfThePath()
    {
        Fx6 F(double v) => Fx6.FromRaw((long)(v * Fx6.Scale));
        var path = new PathView([new PathNodeView(10, F(8), F(40), true), new PathNodeView(11, F(10), F(52), false), new PathNodeView(12, F(12), F(50), true)], F(20), F(30), 12);
        var groups = ScienceMapLogic.PlanGroups([new QueuePlan(0, [10, 11]), new QueuePlan(2, [12])], path);
        Assert.Equal((18, 92.0), (groups[0].Months, groups[0].Price));
        Assert.True(groups[0].Lines[1].WaitsForPrevious);
        Assert.False(groups[1].Lines[0].WaitsForPrevious);
        Assert.Equal(142.0, groups.Sum(g => g.Price));
        var sum = ScienceMapLogic.Summary(path);
        Assert.Equal((3, 142.0, 20, 30), (sum.Nodes, sum.Price, sum.MonthsMin, sum.MonthsMax));
    }

    [Fact]
    public void Tags_AreThreeLettersForEveryDirection()
    {
        Assert.Equal(Directions.Ids.Length, Enumerable.Range(0, 13).Select(ScienceMapLogic.Tag).Distinct().Count());
        Assert.All(Enumerable.Range(0, 13), d => Assert.Equal(3, ScienceMapLogic.Tag(d).Length));
        Assert.Equal("???", ScienceMapLogic.Tag(40));
    }

    [Fact]
    public void MilKey_NoneGivesNoLine()
    {
        Assert.Null(ScienceMapLogic.MilKey(MilValue.None));
        Assert.Equal("mil.direct", ScienceMapLogic.MilKey(MilValue.Direct));
    }
}
