using Singularity.Core.Data;
using Singularity.Core.Institutes;
using Singularity.Core.Knowledge;
using Singularity.Core.Tests.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Institutes;

/// <summary>TASK-0199 (#589): the AI surplus building rule against a stub that returns a fixed surplus share.</summary>
public class SurplusBuildTests
{
    private sealed class Inputs(Fx6 surplus) : IInstituteInputs
    {
        public Fx6 Priority(int actorRow, int nodeRow, long month) => Fx6.One;
        public bool IsKnown(int actorRow, int nodeRow) => false;
        public bool InFrontier(int actorRow, int nodeRow) => false;
        public bool DiscoveredByResearch(int actorRow, int nodeRow) => false;
        public int OrderNode(InstituteHandle s) => 0;
        public long PlayerBuildMonth(InstituteHandle s) => -1;
        public bool PlayerControlled(int actorRow) => false;
        public Fx6 BuildFunding(int actorRow) => Fx6.One;
        public void GoalPath(int actorRow, long month, Span<byte> tier) { }
        public Fx6 SurplusShare(int actorRow, long month) => surplus;
    }

    // Three USA candidates of 2030 are added to a copy of the data; with a 40-year window they open in 1990 and the historical rule is far from starting them.
    private static GameData Data()
    {
        string dir = Directory.CreateTempSubdirectory("surplus-mods").FullName;
        foreach (string f in Directory.GetFiles(TestMods.CoreDir, "*", SearchOption.AllDirectories))
        {
            string to = Path.Combine(dir, Path.GetRelativePath(TestMods.CoreDir, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to);
        }
        string rows = string.Concat(Enumerable.Range(1, 3).Select(i =>
            $"\n  - {{id: usa_surplus{i}, country: USA, founded: 2030, ref_year: 2030, areas: {{mech: 2, metal: 1}}, directions: {{ctrl: 2, elec: 2, mat: 1}}, source: \"test\"}}"));
        File.AppendAllText(Path.Combine(dir, "institutes.yaml"), rows + "\n");
        try
        {
            return ModLoader.Load(dir);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // First month each of the three candidates is seen building or standing, and the most of them building at once (months 0..720, 1945-09 .. 2005-08).
    private static (long[] Starts, int MostAtOnce) Run(SlotParams slots, Fx6 surplus)
    {
        GameData data = Data();
        ScenarioData scenario = data.Scenario("jet_slice_1945");
        var p = new InstituteParams { WindowYears = 40 };
        KnowledgeGraph g = KnowledgeGraph.FromData(data.Nodes, scenario.Disciplines, new KnowledgeParams());
        InstituteLayout layout = InstituteLayout.FromData(data.Institutes, scenario, g, p);
        int usa = scenario.ActorIndex("USA");
        var table = new Table(layout.ActorCount);
        var sys = new InstituteSystem(table, layout, p, g, new Inputs(surplus), slots: slots);
        var handles = Enumerable.Range(1, 3).Select(i => new InstituteHandle(usa, Enumerable.Range(0, layout.SlotCount(usa)).Single(k => layout.IdOf(new InstituteHandle(usa, k)) == "usa_surplus" + i))).ToArray();
        var cal = new TickCalendar(new CivilDate(1945, 9, 1));
        var runner = new TickRunner(table, 1, [sys], 1, 1, cal);
        long[] starts = [-1, -1, -1];
        int most = 0;
        for (long m = 0; m < 720; m++)
        {
            while (runner.Tick <= cal.TickOfMonth(m))
                runner.RunTick();
            int building = 0;
            for (int i = 0; i < 3; i++)
            {
                bool running = sys.BuildStart(handles[i]) >= 0 && !sys.Standing(handles[i]);
                building += running ? 1 : 0;
                if ((running || sys.Standing(handles[i])) && starts[i] < 0)
                    starts[i] = m;
            }
            most = Math.Max(most, building);
        }
        return ([.. starts.Where(s => s >= 0).Order()], most);
    }

    private static SlotParams Rule(int every = 1, Fx6? ceiling = null) =>
        new() { EraPrice = [], SurplusBuildFromYear = 1945, SurplusBuildEveryMonths = every, UpkeepCeilingShare = ceiling ?? new SlotParams().UpkeepCeilingShare };

    private static readonly Fx6 Half = Fx6.FromRaw(500_000);

    [Fact]
    public void WithoutASurplus_NothingIsBuilt_AndWithOne_TheCandidatesStartAheadOfTheirYear()
    {
        Assert.Empty(Run(Rule(), Fx6.Zero).Starts);
        (long[] starts, _) = Run(Rule(), Half);
        Assert.NotEmpty(starts);
    }

    [Fact]
    public void TheSecondBuild_StartsNoEarlierThanThePeriodAfterTheFirst()
    {
        (long[] free, _) = Run(Rule(every: 1), Half);
        (long[] spaced, _) = Run(Rule(every: 150), Half);
        Assert.True(free.Length >= 2 && spaced.Length >= 2, $"starts {free.Length} / {spaced.Length}");
        long freeGap = free[1] - free[0], spacedGap = spaced[1] - spaced[0];
        Assert.True(freeGap < 150, $"control gap {freeGap}");
        Assert.True(spacedGap >= 150, $"gap {spacedGap}");
    }

    [Fact]
    public void NothingIsStarted_WhileAnotherInstituteOfTheCountryIsBuilding()
    {
        (_, int most) = Run(Rule(), Half);
        Assert.Equal(1, most);
    }

    [Fact]
    public void NothingIsStarted_WhenTheUpkeepAfterTheProjectIsAboveTheCeiling()
    {
        Assert.NotEmpty(Run(Rule(), Half).Starts);
        Assert.Empty(Run(Rule(ceiling: Fx6.Zero), Half).Starts);
    }
}
