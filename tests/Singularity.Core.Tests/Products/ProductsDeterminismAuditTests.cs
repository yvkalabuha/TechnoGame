using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Knowledge;
using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// Determinism of the product layer beyond the developer's hash tests (ADR-0002 L1/L2): storage permutations must
/// not change any value read by stable ids — actor rows of the product layer alone, and node and process storage of
/// the knowledge and capability layers in the linked three-layer run — at any thread count and chunk size; L_P runs
/// are L1/L2 too.
/// </summary>
public class ProductsDeterminismAuditTests
{
    // ---- product layer alone, permuted actor rows

    [Fact]
    public void PermutedActorRows_SameProductStateByActorEveryMonth()
    {
        ProductsHarness a = ProductsInvariantTests.Make("busy", 1, 1, null);
        ProductsHarness b = ProductsInvariantTests.Make("busy", 4, 2, [3, 0, 4, 2, 1]);
        ProductsHarness c = ProductsInvariantTests.Make("busy", 3, 1, [4, 3, 2, 1, 0]);
        for (long t = 0; t < 150; t++)
        {
            a.RunThroughMonth(t);
            b.RunThroughMonth(t);
            c.RunThroughMonth(t);
            for (int actor = 0; actor < Scenario.ActorCount; actor++)
            {
                AssertSameProducts(a.Products, actor, b.Products, b.RowOf(actor), t);
                AssertSameProducts(a.Products, actor, c.Products, c.RowOf(actor), t);
            }
            Assert.Equal(Messages(a), Messages(b));
            Assert.Equal(Messages(a), Messages(c));
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(5, 1)]
    [InlineData(16, 4)]
    public void PermutedActorRows_L1_L2_SameHashAcrossThreads(int threads, int chunkSize)
    {
        int[] perm = [2, 4, 1, 0, 3];
        ProductsHarness r1 = ProductsInvariantTests.Make("busy", 1, 1, perm), r2 = ProductsInvariantTests.Make("busy", threads, chunkSize, perm);
        for (long t = 0; t < 100; t++)
        {
            r1.RunThroughMonth(t);
            r2.RunThroughMonth(t);
            Assert.True(r1.Hash() == r2.Hash(), $"month {t}: hash differs");
        }
    }

    // ---- L_P control

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(8, 3)]
    public void CanonicalLp_L1_L2_SameHashEveryMonth(int threads, int chunkSize)
    {
        ProductsHarness r1 = ProductsLpTests.Lp(1, 1), r2 = ProductsLpTests.Lp(threads, chunkSize);
        string first = "";
        for (long t = 0; t < 100; t++)
        {
            r1.RunThroughMonth(t);
            r2.RunThroughMonth(t);
            Assert.Equal(r1.Hash(), r2.Hash());
            first = t == 0 ? r1.Hash() : first;
        }
        Assert.NotEqual(first, r1.Hash());
    }

    // ---- three linked layers, permuted node and process storage

    [Fact]
    public void Linked_PermutedNodeAndProcessStorage_SameProductsByIds_AcrossThreads()
    {
        var a = new PermutedLinked(null, null, 1, 1);
        var b = new PermutedLinked(Shuffle(CoreData.Nodes.Count, 11), Shuffle(a.ILayout.ProcessCount, 12), 4, 2);
        var c = new PermutedLinked(Shuffle(CoreData.Nodes.Count, 13), Shuffle(a.ILayout.ProcessCount, 14), 8, 1);
        Assert.NotEqual(a.ILayout.Number.ToArray(), b.ILayout.Number.ToArray());
        long reports = 0;
        for (long t = 0; t < 380; t++)
        {
            a.RunThroughMonth(t);
            b.RunThroughMonth(t);
            c.RunThroughMonth(t);
            for (int actor = 0; actor < Scenario.ActorCount; actor++)
            {
                AssertSameProducts(a.Products, actor, b.Products, actor, t);
                AssertSameProducts(a.Products, actor, c.Products, actor, t);
            }
            var ra = a.Industry.LastReports.OrderBy(r => (r.Site, r.ProcessNumber, r.Units.Raw)).ToArray();
            Assert.Equal(ra, b.Industry.LastReports.OrderBy(r => (r.Site, r.ProcessNumber, r.Units.Raw)));
            Assert.Equal(ra, c.Industry.LastReports.OrderBy(r => (r.Site, r.ProcessNumber, r.Units.Raw)));
            for (int s = 0; s < Scenario.ActorCount; s++)
            {
                for (int q = 0; q < a.ILayout.Requesters; q++)
                {
                    for (int k = 0; k < Categories; k++)
                    {
                        Assert.Equal(a.Industry.Granted(s, k, q), b.Industry.Granted(s, k, q));
                        Assert.Equal(a.Industry.Granted(s, k, q), c.Industry.Granted(s, k, q));
                    }
                }
            }
            reports += ra.Length;
        }
        Assert.True(reports > 0, "the linked run never produced");
        // Non-trivial for the bureaus too: some actor ordered a new design (FRA has no start fleet, slot G1 superior).
        Assert.True(Enumerable.Range(0, Scenario.ActorCount).Any(x => Enumerable.Range(0, a.PLayout.Designs)
            .Any(d => a.Products.InitialDefects(x, d) > Fx6.Zero)), "no new design was ever ordered: " + string.Join("; ",
                Enumerable.Range(0, Scenario.ActorCount).Select(x => string.Join(",", Enumerable.Range(0, a.PLayout.Designs).Select(d => a.Products.Stage(x, d)))
                + " app:" + string.Join(",", a.PLayout.GenApp.Select(n => a.Knows(x, n))))));
    }

    internal static void AssertSameProducts(ProductsSystem x, int rx, ProductsSystem y, int ry, long t)
    {
        ProductsLayout l = x.Layout;
        string at = $"month {t}, rows {rx}/{ry}";
        Assert.True(x.Spent(rx) == y.Spent(ry), $"{at}: spent");
        Assert.True(x.PaidRatio(rx) == y.PaidRatio(ry), $"{at}: paid ratio");
        Assert.True(x.UpkeepNeeded(rx) == y.UpkeepNeeded(ry), $"{at}: upkeep");
        for (int b = 0; b < l.Bureaus; b++)
        {
            Assert.True(x.BureauExp(rx, b) == y.BureauExp(ry, b), $"{at}: bureau_exp {b}");
            Assert.True(x.BureauDesign(rx, b) == y.BureauDesign(ry, b), $"{at}: bureau design {b}");
        }
        for (int d = 0; d < l.Designs; d++)
        {
            string ad = $"{at}, design {d}";
            Assert.True(x.Stage(rx, d) == y.Stage(ry, d), $"{ad}: stage");
            Assert.True(x.Alternatives(rx, d).SequenceEqual(y.Alternatives(ry, d)), $"{ad}: alternatives");
            Assert.True(x.Rate(rx, d) == y.Rate(ry, d), $"{ad}: rate");
            Assert.True(x.UnitCost(rx, d) == y.UnitCost(ry, d), $"{ad}: unit cost");
            Assert.True(x.LineE(rx, d) == y.LineE(ry, d), $"{ad}: line_E");
            Assert.True(x.WorkLeft(rx, d) == y.WorkLeft(ry, d), $"{ad}: work");
            Assert.True(x.InitialDefects(rx, d) == y.InitialDefects(ry, d), $"{ad}: D0");
            Assert.True(x.DefectsHidden(rx, d) == y.DefectsHidden(ry, d), $"{ad}: hidden");
            Assert.True(x.DefectsOpen(rx, d) == y.DefectsOpen(ry, d), $"{ad}: open");
            Assert.True(x.Hours(rx, d) == y.Hours(ry, d), $"{ad}: hours");
            for (int yr = l.StartYear; yr < l.StartYear + l.Years; yr++)
                Assert.True(x.Cohort(rx, d, yr) == y.Cohort(ry, d, yr), $"{ad}: cohort {yr}");
        }
    }

    /// <summary>Reports and requests of the month keyed by scenario actor, in a canonical order.</summary>
    private static string[] Messages(ProductsHarness h) =>
    [
        .. h.Reports.Select(r => $"R {h.ActorOf[r.Site]} {r.ProcessNumber} {r.Units.Raw} {r.LabourHours.Raw}")
            .Concat(h.Requests.Select(q => $"W {h.ActorOf[q.Site]} {q.Category} {q.RequesterId} {q.N}"))
            .Order(StringComparer.Ordinal),
    ];

    private static int[] Shuffle(int n, int seed)
    {
        int[] p = [.. Enumerable.Range(0, n)];
        new Random(seed).Shuffle(p);
        return p;
    }

    /// <summary>
    /// The linked run of <see cref="ProductsLinkedRunTests"/> (knowledge + capability + products, stand-in AI) with the
    /// knowledge graph stored in <c>nodePerm</c> order and the capability processes in <c>procPerm</c> order. Every input
    /// is keyed by stable numbers, so no value read by ids may change.
    /// </summary>
    private sealed class PermutedLinked
    {
        public PermutedLinked(int[]? nodePerm, int[]? procPerm, int threads, int chunkSize)
        {
            var kp = new KnowledgeParams();
            KnowledgeGraph baseGraph = KnowledgeGraph.FromData(CoreData.Nodes, Scenario.Disciplines, kp);
            KnowledgeStart baseStart = KnowledgeStart.FromScenario(Scenario, baseGraph);
            int n = baseGraph.Count, dc = baseGraph.DisciplineNames.Length;
            nodePerm ??= [.. Enumerable.Range(0, n)];
            Graph = KnowledgeGraph.Create([.. nodePerm.Select(r => SpecOf(baseGraph, r))], [.. baseGraph.DisciplineNames]);
            var kStart = new KnowledgeStart([.. baseStart.ActorNumber], n, dc);
            for (int a = 0; a < baseStart.ActorCount; a++)
            {
                for (int r = 0; r < n; r++)
                    kStart.Known[a * n + r] = baseStart.IsKnown(a, baseGraph.RowOf(Graph.Number[r]));
                Array.Copy(baseStart.KD, a * dc, kStart.KD, a * dc, dc);
                Array.Copy(baseStart.KInd, a * dc, kStart.KInd, a * dc, dc);
            }

            var ip = new IndustryParams();
            MakeLayout(out IndustryLayout baseIndustry);
            IndustryLayout dataLayout = IndustryLayout.FromData(CoreData.Nodes, Scenario, Categories, baseIndustry.Requesters);
            procPerm ??= [.. Enumerable.Range(0, dataLayout.ProcessCount)];
            var specs = procPerm.Select(r => new IndustryProcessSpec(dataLayout.Number[r], dataLayout.Clusters[dataLayout.ClusterIndex[r]],
                dataLayout.Discipline[r], dataLayout.Tier[r], dataLayout.Req[r])).ToList();
            ILayout = IndustryLayout.Create([.. dataLayout.Clusters], specs, [.. dataLayout.SiteOwner], Categories, dataLayout.SkilledCategory,
                dataLayout.Requesters, IndustryLayout.SliceAlloyGrades);
            IndustryStart iStart = IndustryStart.FromScenario(Scenario, CoreData.Nodes, ILayout, ip);
            PLayout = ProductsLayout.FromData(CoreData.Nodes, CoreData.ProductClasses, "jet_fighter", ILayout, Bureaus, Scenario.StartYear, Years, Scenario.MaxDesigns);
            ProductsStart pStart = ProductsStart.FromScenario(Scenario, CoreData.Nodes, CoreData.ProductClasses, PLayout);

            Table = new Table(Scenario.ActorCount);
            Calendar = new TickCalendar(new CivilDate(Scenario.StartYear, Scenario.StartMonth, 1));
            int chunks = TickRunner.ChunkCountFor(Table.RowCount, chunkSize);
            var rng = new CounterRng(7);
            Knowledge = new KnowledgeSystem(Table, Graph, kStart, kp, new KInputs(this, rng), rng, chunks);
            Industry = new IndustrySystem(Table, ILayout, iStart, ip, new IInputs(this), chunks, new V23CultureRule());
            Products = new ProductsSystem(Table, PLayout, pStart, new ProductsParams(), new PInputs(this),
                Industry.CreateMailbox(chunks, Cadence.Monthly));
            Runner = new TickRunner(Table, 7, [Knowledge, Industry, Products], chunkSize, threads, Calendar);
        }

        public KnowledgeGraph Graph { get; }
        public IndustryLayout ILayout { get; }
        public ProductsLayout PLayout { get; }
        public Table Table { get; }
        public TickCalendar Calendar { get; }
        public KnowledgeSystem Knowledge { get; }
        public IndustrySystem Industry { get; }
        public ProductsSystem Products { get; }
        public TickRunner Runner { get; }

        public bool Knows(int actor, int number) => Knowledge.IsKnown(actor, Graph.RowOf(number));

        public void RunThroughMonth(long month)
        {
            long last = Calendar.TickOfMonth(month);
            while (Runner.Tick <= last)
                Runner.RunTick();
        }

        private static KnowledgeNodeSpec SpecOf(KnowledgeGraph g, int r)
        {
            RequirementLists req = g.Requires;
            var groups = new int[req.GroupStart[r + 1] - req.GroupStart[r]][];
            for (int k = 0; k < groups.Length; k++)
                groups[k] = [.. req.GroupMembers(req.GroupStart[r] + k).ToArray().Select(m => g.Number[m])];
            var informed = new List<(int, Fx6)>();
            for (int e = g.InformedStart[r]; e < g.InformedStart[r + 1]; e++)
                informed.Add((g.Number[g.InformedFrom[e]], g.InformedWeight[e]));
            return new KnowledgeNodeSpec(g.Number[r], g.Type[r], g.Gen[r], g.Discipline[r], g.IsHazard[r], g.CostN[r], groups, [.. informed]);
        }
    }

    private sealed class KInputs(PermutedLinked run, CounterRng rng) : IKnowledgeInputs
    {
        public ReadOnlySpan<int> LicensedEngines(int actorRow, long month) => ReadOnlySpan<int>.Empty;

        public Fx6 ResearchBudget(int actorRow, long month) => Scenario.BudgetIndex[actorRow] * Fx6.FromRaw(280_000);

        public Fx6 Priority(int actorRow, int nodeRow, long month) => Fx6.One;

        public Fx6 J(int actorRow, int nodeRow, long month) =>
            KnowledgeRng.J(rng, month == 0 ? 0 : (int)((month - 1) / 12), actorRow, run.Graph.Number[nodeRow]);

        public Fx6 DisciplineVolume(int actorRow, int discipline, long month) => run.Industry.DisciplineVolume(actorRow, discipline);
    }

    private sealed class IInputs(PermutedLinked run) : IIndustryInputs
    {
        public Fx6 EffortBudget(int actorRow, long month) => Fx6.Zero;
        public int EffortFocus(int actorRow, int k, long month) => -1;
        public bool Planned(int siteRow, int processRow, long month) => false;
        public Fx6 PlannedVolume(int siteRow, int processRow, long month) => Fx6.Zero;

        public bool KnowsProcess(int siteRow, int processRow, long month) => run.Knows(siteRow, run.ILayout.Number[processRow]);

        public Fx6 W(int siteRow, int cluster, long month) => Fx6.One;

        public Fx6 Hcap(int siteRow, Dimension dim, long month) => Scenario.Hcap[siteRow];

        public bool QualityProgramme(int siteRow, long month) => false;

        public Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month, Fx6 restActor) => Fx6.Zero;

        public Fx6 LicenceShare(int siteRow, int processRow, long month) => Fx6.Zero;

        public long WorkforceAvailable(int siteRow, int category, long month)
        {
            long engineers = (Fx6.FromInt(20_000) * Scenario.BudgetIndex[siteRow]).Raw / Fx6.Scale;
            return category == 0 ? engineers + Scenario.TrophyEngineers[siteRow] : 10 * engineers;
        }

        public Fx6 AlloyCap(int siteRow, long month) => Scenario.AlloyCapAtStart(siteRow);

        public Fx6 AlloyDeficit(int siteRow, long month) => Fx6.Zero;

        public long KnownSince(int siteRow, int processRow, long month) => -1;
    }

    private sealed class PInputs(PermutedLinked run) : IProductsInputs
    {
        public bool Knows(int actorRow, int nodeNumber, long month) => run.Knows(actorRow, nodeNumber);

        public Fx6 AdmittedYield(int siteRow, int processNumber, long month) =>
            run.Knows(siteRow, processNumber) ? run.Industry.Yield(siteRow, run.ILayout.RowOf(processNumber), month).Yield : Fx6.Zero;

        public ProductionProfile Profile(int siteRow, ReadOnlySpan<PrcSetItem> prcSet, long month) => run.Industry.Profile(siteRow, prcSet, month);

        public Fx6 AlloySupply(int siteRow, long month) => run.Industry.Alloy(siteRow, month).Supply;

        public long Granted(int siteRow, int category, int requesterId, long month) => run.Industry.Granted(siteRow, category, requesterId);

        public Fx6 ProcurementBudget(int actorRow, long month) => Scenario.BudgetIndex[actorRow] * Fx6.FromRaw(440_000);

        public IReadOnlyList<DesignCommand> DesignCommands(int actorRow, long month) =>
            StandInCommands(run.PLayout, run.Products, actorRow, n => run.Knows(actorRow, n), p => AdmittedYield(actorRow, p, month));

        public SeriesStart? StartSeries(int actorRow, int design, long month) =>
            StandInLaunch(run.Products, actorRow, design, month) ? new SeriesStart(Fx6.FromInt(30), false) : null;

        public bool CloseLine(int actorRow, int design, long month) => StandInClose(run.PLayout, run.Products, actorRow, design);

        // The start line (E1) runs at the same 30 a month as a launched series.
        public Fx6? LineRate(int actorRow, int design, long month) => Fx6.FromInt(30);
        public Fx6 TrainingBudget(int actorRow, int design, long month) => Fx6.Zero;

        public long AppKnownSince(int actorRow, int gen, long month) => -1;
    }
}
