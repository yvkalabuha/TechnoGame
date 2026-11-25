using Singularity.Core.Data;
using Singularity.Core.Institutes;
using Singularity.Core.Knowledge;
using Singularity.Core.Tests.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Institutes;

/// <summary>#559, ai-institute-goals.md §2 and §7: an AI institute works only on the unknown path to the next product generation.</summary>
public class GoalPathTests
{
    // Every node is in the frontier and weighs more the higher its row (so the heaviest node is the last row); the path is the given rows (tier 1).
    private sealed class Inputs(int[] pathRows) : IInstituteInputs
    {
        public Fx6 Priority(int actorRow, int nodeRow, long month) => Fx6.FromInt(nodeRow + 1);
        public bool IsKnown(int actorRow, int nodeRow) => false;
        public bool InFrontier(int actorRow, int nodeRow) => true;
        public bool DiscoveredByResearch(int actorRow, int nodeRow) => false;
        public int OrderNode(InstituteHandle slot) => 0;
        public long PlayerBuildMonth(InstituteHandle slot) => -1;
        public bool PlayerControlled(int actorRow) => false;
        public Fx6 BuildFunding(int actorRow) => Fx6.One;

        public void GoalPath(int actorRow, long month, Span<byte> tier)
        {
            foreach (int r in pathRows)
                tier[r] = 1;
        }
    }

    private static (InstituteSystem Sys, KnowledgeGraph G, int Actor, Table Table, InstituteLayout Layout) Make(int[] pathRows)
    {
        GameData data = ModLoader.Load(TestMods.CoreDir);
        ScenarioData scenario = data.Scenario("jet_slice_1945");
        KnowledgeGraph g = KnowledgeGraph.FromData(data.Nodes, scenario.Disciplines, new KnowledgeParams());
        var p = new InstituteParams();
        InstituteLayout layout = InstituteLayout.FromData(data.Institutes, scenario, g, p);
        var table = new Table(layout.ActorCount);
        return (new InstituteSystem(table, layout, p, g, new Inputs(pathRows)), g, scenario.ActorIndex("USA"), table, layout);
    }

    private static int[] Propose(InstituteSystem sys, InstituteLayout layout, int actor)
    {
        var goals = new int[layout.SlotCount(actor)];
        sys.Propose(actor, 0, goals, new bool[goals.Length]);
        return goals;
    }

    [Fact]
    public void FreeSlot_TakesThePathNodeNotTheHeaviestNode()
    {
        var (sys, g, actor, _, layout) = Make([0]);
        int[] goals = Propose(sys, layout, actor);
        Assert.Equal(1, goals.Count(n => n == g.Number[0]));
        Assert.DoesNotContain(g.Number[g.Count - 1], goals);   // the heaviest node is off the path
        Assert.All(goals, n => Assert.True(n == 0 || n == g.Number[0]));
    }

    [Fact]
    public void EmptyPath_LeavesTheSlotsWithoutAGoal()
    {
        var (sys, _, actor, _, layout) = Make([]);
        Assert.All(Propose(sys, layout, actor), n => Assert.Equal(0, n));
    }

    [Fact]
    public void Propose_ReturnsTheGoalsTheAiTakesInTheNextTick()
    {
        var (sys, g, actor, table, layout) = Make([3, 5]);
        int[] proposed = Propose(sys, layout, actor);
        Assert.Contains(g.Number[3], proposed);
        Assert.Contains(g.Number[5], proposed);
        var cal = new TickCalendar(new CivilDate(1945, 9, 1));
        var runner = new TickRunner(table, 1, [sys], 1, 1, cal);
        runner.RunTick();
        for (int k = 0; k < proposed.Length; k++)
            Assert.Equal(proposed[k], sys.GoalNode(new InstituteHandle(actor, k)));
    }
}
