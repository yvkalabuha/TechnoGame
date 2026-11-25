using Singularity.Core.Data;
using Singularity.Core.Institutes;
using Singularity.Core.Knowledge;
using Singularity.Core.Tests.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Institutes;

/// <summary>TASK-0175: the safety rule slows a running project when the projects and the upkeep take more than all the research money.</summary>
public class OverloadTests
{
    private sealed class Inputs(InstituteHandle slot) : IInstituteInputs
    {
        public Fx6 Priority(int actorRow, int nodeRow, long month) => Fx6.One;
        public bool IsKnown(int actorRow, int nodeRow) => false;
        public bool InFrontier(int actorRow, int nodeRow) => false;
        public bool DiscoveredByResearch(int actorRow, int nodeRow) => false;
        public int OrderNode(InstituteHandle s) => 0;
        public long PlayerBuildMonth(InstituteHandle s) => s == slot ? 0 : -1;
        public bool PlayerControlled(int actorRow) => true;
        public Fx6 BuildFunding(int actorRow) => Fx6.One;
    }

    // Runs the project of the first candidate slot of a country for a few months; returns the paid progress (raw) and the system.
    private static (int Progress, Fx6 Take) Run(InstituteParams p)
    {
        GameData data = ModLoader.Load(TestMods.CoreDir);
        ScenarioData scenario = data.Scenario("jet_slice_1945");
        KnowledgeGraph g = KnowledgeGraph.FromData(data.Nodes, scenario.Disciplines, new KnowledgeParams());
        InstituteLayout layout = InstituteLayout.FromData(data.Institutes, scenario, g, p);
        int actor = scenario.ActorIndex("USA");
        var table = new Table(layout.ActorCount);
        var probe = new InstituteSystem(new Table(layout.ActorCount), layout, p, g, new Inputs(default));
        int slot = Enumerable.Range(0, layout.SlotCount(actor)).First(k => !probe.Standing(new InstituteHandle(actor, k)));
        var h = new InstituteHandle(actor, slot);
        var sys = new InstituteSystem(table, layout, p, g, new Inputs(h));
        var cal = new TickCalendar(new CivilDate(1945, 9, 1));
        var runner = new TickRunner(table, 1, [sys], 1, 1, cal);
        while (runner.Tick <= cal.TickOfMonth(4))
            runner.RunTick();
        Fx6 take = sys.BuildRate(h, 4);
        return (table.GetInt($"inst.prog.{slot}").Current[actor], take);
    }

    [Fact]
    public void Overload_ScalesTheProgressOfARunningProject()
    {
        (int normal, Fx6 takeNormal) = Run(new InstituteParams { AheadK = Fx6.Zero });
        Assert.True(takeNormal < Fx6.One);
        long steps = normal / Fx6.One.Raw;
        Assert.True(steps >= 3 && normal == steps * Fx6.One.Raw);
        // Half a unit of research money per level: the project of 3 levels takes 150 % of it.
        var big = new InstituteParams { AheadK = Fx6.Zero, BuildRatePerLevel = Fx6.FromRaw(500_000) };
        (int slowed, Fx6 take) = Run(big);
        Assert.True(take > Fx6.One);
        Assert.Equal(steps * (long)InstituteMath.OverloadScale(take).Raw, (long)slowed);
        Assert.True(slowed < normal);
    }
}
