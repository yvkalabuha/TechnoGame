using Singularity.Core.Data;
using Singularity.Core.Institutes;
using Singularity.Core.Knowledge;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Knowledge;

/// <summary>TASK-0151: the priority path of the knowledge layer on a small graph with fake inputs.</summary>
public class PriorityTests
{
    private sealed class Inputs : IKnowledgeInputs
    {
        public Fx6 Budget = Fx6.FromInt(100_000);
        public Fx6 Weight = Fx6.One;
        public int[] Licences = [];
        public long LicenceMonth = -1;
        public int GoalRow = -1;

        public Fx6 ResearchBudget(int actorRow, long month) => Budget;
        public Fx6 Priority(int actorRow, int nodeRow, long month) => nodeRow == 2 || Weight == Fx6.Zero ? Weight : Fx6.FromRaw(1);   // the cheap node 3 takes the money
        public Fx6 J(int actorRow, int nodeRow, long month) => Fx6.One;
        public Fx6 DisciplineVolume(int actorRow, int discipline, long month) => Fx6.Zero;
        public ReadOnlySpan<int> LicensedEngines(int actorRow, long month) => month == LicenceMonth ? Licences : [];

        public int PriorityGoals(int actorRow, long month, Span<int> nodeRows, Span<Fx6> multipliers)
        {
            if (GoalRow < 0)
                return 0;
            nodeRows[0] = GoalRow;
            multipliers[0] = Fx6.FromRaw(1_200_000);
            return 1;
        }
    }

    private static (KnowledgeSystem Sys, TickRunner Runner, TickCalendar Cal, KnowledgeGraph G) Rig(Inputs inputs, bool licences = false)
    {
        KnowledgeGraph g = KnowledgeGraph.Create(
        [
            new KnowledgeNodeSpec(1, NodeType.FND, 1, 0, false, Fx6.FromInt(1_000_000), []),
            new KnowledgeNodeSpec(2, NodeType.ENG, 1, 0, false, Fx6.FromInt(1_000_000), [[1]]),
            new KnowledgeNodeSpec(3, NodeType.FND, 1, 0, false, Fx6.FromInt(1), []),
        ], ["prop"]);
        var start = new KnowledgeStart([0], 3, 1);
        var table = new Table(1);
        var cal = new TickCalendar(new CivilDate(1945, 9, 1));
        var sys = new KnowledgeSystem(table, g, start, new KnowledgeParams(), inputs, new CounterRng(1), 1, licenceTransfersKnowledge: licences,
            priorities: new InstituteParams());
        return (sys, new TickRunner(table, 1, [sys], 1, 1, cal), cal, g);
    }

    private static void Through(TickRunner runner, TickCalendar cal, long month)
    {
        while (runner.Tick <= cal.TickOfMonth(month))
            runner.RunTick();
    }

    [Fact]
    public void AllWeightsZero_WithAGoal_SpendsNothingAndDoesNotDivideByZero()
    {
        var inputs = new Inputs { Weight = Fx6.Zero };
        var (sys, runner, cal, g) = Rig(inputs);
        inputs.GoalRow = g.RowOf(3);
        Through(runner, cal, 5);
        Assert.Equal(Fx6.Zero, sys.Prog(0, g.RowOf(3)));
        Assert.Equal(Fx6.Zero, sys.ProgressRate(0, g.RowOf(3), 6));
    }

    [Fact]
    public void ResearchDiscoveries_AreMarked_LicenceGrantsAreNot()
    {
        var inputs = new Inputs { Budget = Fx6.FromInt(100_000) };
        var (sys, runner, cal, g) = Rig(inputs, licences: true);
        inputs.Licences = [g.RowOf(2)];
        inputs.LicenceMonth = 1;
        Through(runner, cal, 0);
        Through(runner, cal, 1);
        Through(runner, cal, 12);
        Assert.True(sys.IsKnown(0, g.RowOf(2)));
        Assert.False(sys.DiscoveredByResearch(0, g.RowOf(2)), "the engine came with the licence");
        Assert.True(sys.IsKnown(0, g.RowOf(3)));
        Assert.True(sys.DiscoveredByResearch(0, g.RowOf(3)), "the node was opened by research");
    }
}
