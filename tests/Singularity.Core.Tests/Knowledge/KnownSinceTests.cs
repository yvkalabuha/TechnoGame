using Singularity.Core.Data;
using Singularity.Core.Knowledge;
using Singularity.Core.Tests.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Knowledge;

/// <summary><c>know.since.N</c> (SLICE_COMPOSITION §13.2, slice spec §6.1 v0.24 r2): t_known for the controls L_C and L_P, present only with the flag.</summary>
public class KnownSinceTests
{
    private static readonly GameData Data = ModLoader.Load(TestMods.CoreDir);
    private static readonly ScenarioData Scenario = Data.Scenario("jet_slice_1945");

    private sealed class FixedInputs : IKnowledgeInputs
    {
        public Fx6 Budget = Fx6.FromRaw(300_000);
        public int[] Licences = [];
        public long LicenceMonth = -1;

        public Fx6 ResearchBudget(int actorRow, long month) => Budget;
        public Fx6 Priority(int actorRow, int nodeRow, long month) => Fx6.One;
        public Fx6 J(int actorRow, int nodeRow, long month) => Fx6.One;
        public Fx6 DisciplineVolume(int actorRow, int discipline, long month) => Fx6.Zero;
        public ReadOnlySpan<int> LicensedEngines(int actorRow, long month) => month == LicenceMonth ? Licences : [];
    }

    private sealed record Rig(Table Table, KnowledgeSystem System, TickRunner Runner, TickCalendar Calendar, KnowledgeGraph Graph, KnowledgeStart Start)
    {
        public void Through(long month)
        {
            while (Runner.Tick <= Calendar.TickOfMonth(month))
                Runner.RunTick();
        }
    }

    private static Rig SliceRig(bool track, int threads = 1, int chunkSize = 1, long startKnownSince = KnownSinceMarks.StartKnown)
    {
        var p = new KnowledgeParams();
        KnowledgeGraph g = KnowledgeGraph.FromData(Data.Nodes, Scenario.Disciplines, p);
        KnowledgeStart s = KnowledgeStart.FromScenario(Scenario, g);
        var table = new Table(s.ActorCount);
        var cal = new TickCalendar(new CivilDate(Scenario.StartYear, Scenario.StartMonth, 1));
        var sys = new KnowledgeSystem(table, g, s, p, new FixedInputs(), new CounterRng(42), TickRunner.ChunkCountFor(s.ActorCount, chunkSize),
            trackKnownSince: track, startKnownSince: startKnownSince);
        return new Rig(table, sys, new TickRunner(table, 42, [sys], chunkSize, threads, cal), cal, g, s);
    }

    [Fact]
    public void Unknown_IsMinusOne_Start_IsMinus384_AndADiscoveryCarriesTheMonthOfItsMerge()
    {
        Rig rig = SliceRig(track: true);
        int actors = rig.Start.ActorCount, nodes = rig.Graph.Count;
        var before = new bool[actors * nodes];
        int opened = 0;
        for (long t = 0; t < 60; t++)
        {
            for (int a = 0; a < actors; a++)
            {
                for (int r = 0; r < nodes; r++)
                    before[a * nodes + r] = rig.System.IsKnown(a, r);
            }
            rig.Through(t);
            for (int a = 0; a < actors; a++)
            {
                for (int r = 0; r < nodes; r++)
                {
                    bool known = rig.System.IsKnown(a, r);
                    long since = rig.System.KnownSince(a, r);
                    if (!known)
                        Assert.Equal(KnownSinceMarks.Unknown, since);
                    else if (rig.Start.IsKnown(a, r))
                        Assert.Equal(KnownSinceMarks.StartKnown, since);
                    else if (!before[a * nodes + r])
                    {
                        Assert.Equal(t, since);
                        opened++;
                    }
                    else
                        Assert.InRange(since, 0, t - 1);
                }
            }
        }
        Assert.True(opened > 0, "the run opens nothing, the test sees no discovery");
    }

    [Fact]
    public void LicenceGrants_CarryTheMonthOfTheGrantMerge_ForTheEngineAndItsClosure()
    {
        KnowledgeGraph g = KnowledgeGraph.Create(
        [
            new KnowledgeNodeSpec(1, NodeType.FND, 1, 0, false, Fx6.FromInt(1_000_000), []),
            new KnowledgeNodeSpec(2, NodeType.ENG, 1, 0, false, Fx6.FromInt(1_000_000), [[1]]),
        ], ["prop"]);
        var start = new KnowledgeStart([0], 2, 1);
        var table = new Table(1);
        var cal = new TickCalendar(new CivilDate(1945, 9, 1));
        var inputs = new FixedInputs { Budget = Fx6.FromRaw(1), Licences = [g.RowOf(2)], LicenceMonth = 3 };
        var sys = new KnowledgeSystem(table, g, start, new KnowledgeParams(), inputs, new CounterRng(1), 1, licenceTransfersKnowledge: true, trackKnownSince: true);
        var rig = new Rig(table, sys, new TickRunner(table, 1, [sys], 1, 1, cal), cal, g, start);
        rig.Through(2);
        Assert.Equal(KnownSinceMarks.Unknown, sys.KnownSince(0, g.RowOf(2)));
        rig.Through(3);
        Assert.Equal(3, sys.KnownSince(0, g.RowOf(2)));
        Assert.Equal(3, sys.KnownSince(0, g.RowOf(1)));
        rig.Through(10);
        Assert.Equal(3, sys.KnownSince(0, g.RowOf(2)));   // the value does not move later
    }

    [Fact]
    public void WithoutTheFlag_ThereAreNoSinceColumns_AndReadingThrows()
    {
        Rig plain = SliceRig(track: false);
        Assert.DoesNotContain(plain.Table.Columns, c => c.Name.StartsWith("know.since.", StringComparison.Ordinal));
        Assert.False(plain.System.TracksKnownSince);
        Assert.Throws<InvalidOperationException>(() => plain.System.KnownSince(0, 0));
    }

    [Fact]
    public void WithTheFlag_TheColumnsFollowKIndInNodeOrder_TheHashDiffers_ButEveryOtherColumnEqualsTheUntrackedRun()
    {
        Rig plain = SliceRig(track: false), tracked = SliceRig(track: true);
        plain.Through(23);
        tracked.Through(23);
        string[] names = [.. tracked.Table.Columns.Select(c => c.Name)];
        string[] sinceNames = [.. names.Where(n => n.StartsWith("know.since.", StringComparison.Ordinal))];
        Assert.Equal(tracked.Graph.Count, sinceNames.Length);
        Assert.Equal(names.Take(plain.Table.Columns.Count()).ToArray(), plain.Table.Columns.Select(c => c.Name).ToArray());   // appended after the existing columns
        HashAssert.NotEqual(StateHash.ComputeHex(plain.Table, 1), StateHash.ComputeHex(tracked.Table, 1));
        foreach (Column c in plain.Table.Columns)
        {
            Column other = tracked.Table.Columns.Single(x => x.Name == c.Name);
            switch (c)
            {
                case IntColumn i:
                    Assert.Equal(i.Current.ToArray(), ((IntColumn)other).Current.ToArray());
                    break;
                case LongColumn l:
                    Assert.Equal(l.Current.ToArray(), ((LongColumn)other).Current.ToArray());
                    break;
            }
        }
    }

    [Fact]
    public void StartKnownSince_IsAtMostMinus384_AndOtherValuesAreABuildError()
    {
        foreach (long bad in new long[] { -383, -1, 0, 5 })
            Assert.Throws<ArgumentOutOfRangeException>(() => SliceRig(track: true, startKnownSince: bad));
        Rig rig = SliceRig(track: true, startKnownSince: -500);
        int a = Scenario.ActorIndex("USA"), r = rig.Graph.RowOf(58);
        Assert.Equal(-500, rig.System.KnownSince(a, r));
        SliceRig(track: false, startKnownSince: -1);   // without the flag the value is not used and not checked
    }

    [Fact]
    public void KnownSince_IsL1AndL2Deterministic()
    {
        string Run(int threads, int chunk)
        {
            Rig rig = SliceRig(track: true, threads, chunk);
            rig.Through(30);
            return StateHash.ComputeHex(rig.Table, 1);
        }
        string h = Run(1, 1);
        Assert.Equal(h, Run(1, 1));
        Assert.Equal(h, Run(4, 1));
        Assert.Equal(h, Run(8, 2));
    }
}
