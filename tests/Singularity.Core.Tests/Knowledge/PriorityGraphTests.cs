using Singularity.Core.Data;
using Singularity.Core.Data.Yaml;
using Singularity.Core.Institutes;
using Singularity.Core.Knowledge;
using Singularity.Core.Tests.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Knowledge;

/// <summary>TASK-0151 codex round 2: graph walk of the priority groups, the kappa cap, the canonical institute table.</summary>
public class PriorityGraphTests
{
    private sealed class Inputs : IKnowledgeInputs
    {
        public int GoalRow = -1;
        public Fx6 ResearchBudget(int actorRow, long month) => Fx6.FromInt(100_000);
        public Fx6 Priority(int actorRow, int nodeRow, long month) => Fx6.One;
        public Fx6 J(int actorRow, int nodeRow, long month) => Fx6.One;
        public Fx6 DisciplineVolume(int actorRow, int discipline, long month) => Fx6.Zero;
        public ReadOnlySpan<int> LicensedEngines(int actorRow, long month) => [];

        public int PriorityGoals(int actorRow, long month, Span<int> nodeRows, Span<Fx6> multipliers)
        {
            if (GoalRow < 0)
                return 0;
            nodeRows[0] = GoalRow;
            multipliers[0] = Fx6.FromRaw(1_200_000);
            return 1;
        }
    }

    // A chain of diamonds: number 1 is the root, every level is two parallel nodes joined by one node; paths multiply by two per level.
    [Fact]
    public async Task DiamondChain_GoalAtTheTop_StepAndRateFinishQuickly()
    {
        const int depth = 45;
        var specs = new List<KnowledgeNodeSpec> { new(1, NodeType.FND, 1, 0, false, Fx6.FromInt(1_000_000), []) };
        int join = 1, next = 2;
        for (int d = 0; d < depth; d++)
        {
            int a = next++, b = next++, j = next++;
            specs.Add(new(a, NodeType.FND, 1, 0, false, Fx6.FromInt(1_000_000), [[join]]));
            specs.Add(new(b, NodeType.FND, 1, 0, false, Fx6.FromInt(1_000_000), [[join]]));
            specs.Add(new(j, NodeType.FND, 1, 0, false, Fx6.FromInt(1_000_000), [[a], [b]]));
            join = j;
        }
        KnowledgeGraph g = KnowledgeGraph.Create(specs, ["prop"]);
        var inputs = new Inputs { GoalRow = g.RowOf(join) };
        var table = new Table(1);
        var cal = new TickCalendar(new CivilDate(1945, 9, 1));
        var sys = new KnowledgeSystem(table, g, new KnowledgeStart([0], specs.Count, 1), new KnowledgeParams(), inputs, new CounterRng(1), 1,
            priorities: new InstituteParams());
        var runner = new TickRunner(table, 1, [sys], 1, 1, cal);
        var work = Task.Run(() =>
        {
            while (runner.Tick <= cal.TickOfMonth(3))
                runner.RunTick();
            return sys.ProgressRate(0, g.RowOf(1), 4);
        });
        Assert.Same(work, await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(20))));
        Assert.True(work.IsCompletedSuccessfully, "the walk over shared prerequisites did not finish");
    }

    // Codex round 5 #1: the walk of the group path must not use the call stack (a mod graph may be a chain of any depth).
    [Fact]
    public void DeepChain_GoalAtTheTop_WalkDoesNotOverflowTheStack()
    {
        const int depth = 20_000;
        var specs = new List<KnowledgeNodeSpec> { new(1, NodeType.FND, 1, 0, false, Fx6.FromInt(1_000_000), []) };
        for (int i = 2; i <= depth; i++)
            specs.Add(new(i, NodeType.FND, 1, 0, false, Fx6.FromInt(1_000_000), [[i - 1]]));
        KnowledgeGraph g = KnowledgeGraph.Create(specs, ["prop"]);
        var sys = new KnowledgeSystem(new Table(1), g, new KnowledgeStart([0], specs.Count, 1), new KnowledgeParams(), new Inputs { GoalRow = g.RowOf(depth) },
            new CounterRng(1), 1, priorities: new InstituteParams());
        Fx6 rate = Fx6.Zero;
        // A small stack: a recursive walk over 20 000 levels cannot fit into it.
        var thread = new Thread(() => rate = sys.ProgressRate(0, g.RowOf(1), 4), 256 * 1024);
        thread.Start();
        thread.Join();
        Assert.True(rate > Fx6.Zero);
    }

    [Fact]
    public void KappaEff_TinyGroupWeightDoesNotOverflow_AndGivesTheCap()
    {
        var p = new InstituteParams();
        Fx6 k = InstituteMath.KappaEff(Fx6.FromInt(20_000_000), Fx6.FromRaw(1), p);
        Assert.Equal(p.Kappa, k);
    }

    private static InstituteTable Table(string areas)
    {
        string yaml = $"id: x\ncountry: SUN\nfounded: 1940\nref_year: 1945\nsource: s\nareas:\n{areas}\n";
        return InstituteTable.Build([("x", (YMap)StrictYaml.Parse(yaml, "t.yaml"))]);
    }

    // Codex round 5 #2: the table is shared by scenarios; a country outside the scenario must not break the layout of the others.
    [Fact]
    public void Layout_CountryOutsideTheScenario_IsSkipped()
    {
        GameData data = ModLoader.Load(TestMods.CoreDir);
        ScenarioData scenario = data.Scenario("jet_slice_1945");
        var p = new KnowledgeParams();
        KnowledgeGraph g = KnowledgeGraph.FromData(data.Nodes, scenario.Disciplines, p);
        string Row(string country) => $"id: x\ncountry: {country}\nfounded: 1940\nref_year: 1945\nsource: s\nareas:\n  {g.DisciplineNames[0]}: 2\n";
        var table = InstituteTable.Build([
            ("a", (YMap)StrictYaml.Parse(Row("SUN").Replace("id: x", "id: a"), "a.yaml")),
            ("b", (YMap)StrictYaml.Parse(Row("ZZZ").Replace("id: x", "id: b"), "b.yaml"))]);
        InstituteLayout l = InstituteLayout.FromData(table, scenario, g, new InstituteParams());
        Assert.Equal(1, l.StartingCount(scenario.ActorIndex("SUN")));
        Assert.Equal(0, l.SlotCount(scenario.ActorIndex("USA")));
        // Codex TASK-0174 #1: nothing standing at the start (founded after it) still loads, as candidates.
        var later = InstituteTable.Build([("c", (YMap)StrictYaml.Parse(Row("SUN").Replace("id: x", "id: c").Replace("founded: 1940", "founded: 1950").Replace("ref_year: 1945", "ref_year: 1950"), "c.yaml"))]);
        InstituteLayout l2 = InstituteLayout.FromData(later, scenario, g, new InstituteParams());
        Assert.Equal(0, l2.StartingCount(scenario.ActorIndex("SUN")));
        Assert.Equal(1, l2.SlotCount(scenario.ActorIndex("SUN")));
    }

    [Fact]
    public void InstituteTable_AreaKeyOrder_DoesNotChangeTheHash()
    {
        Assert.Equal(Table("  aero: 2\n  prop: 3").Hash, Table("  prop: 3\n  aero: 2").Hash);
    }
}
