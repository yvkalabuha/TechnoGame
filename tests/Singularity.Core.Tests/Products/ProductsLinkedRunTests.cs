using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Knowledge;
using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// Knowledge, capability and product layers on one Kernel runner with the slice data and calendar: products read
/// <c>known</c>, <c>production_profile</c>, alloy and <c>granted</c> from the snapshots and post <c>report_output</c> and
/// <c>reserve_workforce</c> to the capability merge (which rejects output at yield 0, I3, and splits workforce, I6). A
/// scripted stand-in AI (T6 does not exist) orders the newest known APP, launches series once tested, closes old lines.
/// </summary>
public class ProductsLinkedRunTests
{
    [Fact]
    public void L2_ThreeLayers_SameHashEveryMonth_ForThreadCountsAndChunkSizes()
    {
        // The whole horizon (384 months); USA and GBR start knowing every node of G1–G2, so G2 orders, bureau work, testing,
        // defect detection and fixing and a producing G2 series all happen inside the run.
        var runs = new[] { new Linked(1, 1), new Linked(4, 1), new Linked(8, 2), new Linked(3, 3) };
        Linked run = runs[0];
        bool project = false, prototype = false, found = false, fixing = false, seriesG2 = false;
        for (long t = 0; t < 384; t++)
        {
            string? first = null;
            foreach (Linked r in runs)
            {
                r.RunThroughMonth(t);
                string h = StateHash.ComputeHex(r.Table, 1, r.Runner.Tick);
                first ??= h;
                Assert.True(first == h, $"month {t}: hash differs");
            }
            for (int a = 0; a < 2; a++)
            {
                for (int d = 2; d < 4; d++)
                {
                    DesignStage st = run.Products.Stage(a, d);
                    project |= st == DesignStage.Project;
                    prototype |= st == DesignStage.Prototype;
                    seriesG2 |= st == DesignStage.Series && run.Products.Rate(a, d) > Fx6.Zero;
                    found |= run.Products.DefectsFound(a, d) > Fx6.Zero;
                    fixing |= run.Industry.Granted(a, ProductsLayout.EngineerCategory, run.PLayout.FixRequester(d)) > 0;
                }
            }
        }
        Assert.True(run.ReportsSeen > 0);
        Assert.True(project && prototype && found && fixing && seriesG2,
            $"project {project}, prototype {prototype}, found {found}, fixing {fixing}, G2 series {seriesG2}");
        Assert.Contains(Enumerable.Range(0, 2), a => run.Products.BureauExp(a, 0) > Fx6.Zero);
    }

    [Fact]
    public void L1_TwoRuns_SameHash()
    {
        Linked a = new(1, 1), b = new(1, 1);
        a.RunThroughMonth(60);
        b.RunThroughMonth(60);
        Assert.Equal(StateHash.ComputeHex(a.Table, 1, a.Runner.Tick), StateHash.ComputeHex(b.Table, 1, b.Runner.Tick));
    }

    private sealed class Linked
    {
        public Linked(int threads, int chunkSize)
        {
            var kp = new KnowledgeParams();
            Graph = KnowledgeGraph.FromData(CoreData.Nodes, Scenario.Disciplines, kp);
            KnowledgeStart kStart = KnowledgeStart.FromScenario(Scenario, Graph);
            // USA and GBR know every node of generations ≤ 2 at t0, so the run reaches G2 projects.
            for (int a = 0; a < 2; a++)
            {
                for (int r = 0; r < Graph.Count; r++)
                {
                    if (Graph.Gen[r] <= 2)
                        kStart.Known[a * Graph.Count + r] = true;
                }
            }
            var ip = new IndustryParams();
            IParams = ip;
            var probe = MakeLayout(out _);
            ILayout = IndustryLayout.FromData(CoreData.Nodes, Scenario, Categories, probe.Requesters);
            IndustryStart iStart = IndustryStart.FromScenario(Scenario, CoreData.Nodes, ILayout, ip);
            PLayout = ProductsLayout.FromData(CoreData.Nodes, CoreData.ProductClasses, "jet_fighter", ILayout, Bureaus, Scenario.StartYear, Years);
            ProductsStart pStart = ProductsStart.FromScenario(Scenario, CoreData.Nodes, CoreData.ProductClasses, PLayout);

            Table = new Table(Scenario.ActorCount);
            Calendar = new TickCalendar(new CivilDate(Scenario.StartYear, Scenario.StartMonth, 1));
            int chunks = TickRunner.ChunkCountFor(Table.RowCount, chunkSize);
            var rng = new CounterRng(7);
            Knowledge = new KnowledgeSystem(Table, Graph, kStart, kp, new KInputs(this, rng), rng, chunks);
            Industry = new IndustrySystem(Table, ILayout, iStart, ip, new IInputs(this), chunks);
            Products = new ProductsSystem(Table, PLayout, pStart, new ProductsParams(), new PInputs(this),
                Industry.CreateMailbox(chunks, Cadence.Monthly));
            Runner = new TickRunner(Table, 7, [Knowledge, Industry, Products], chunkSize, threads, Calendar);
        }

        public KnowledgeGraph Graph { get; }
        public IndustryParams IParams { get; }
        public IndustryLayout ILayout { get; }
        public ProductsLayout PLayout { get; }
        public Table Table { get; }
        public TickCalendar Calendar { get; }
        public KnowledgeSystem Knowledge { get; }
        public IndustrySystem Industry { get; }
        public ProductsSystem Products { get; }
        public TickRunner Runner { get; }
        public long ReportsSeen;

        public bool Knows(int actor, int number) => Knowledge.IsKnown(actor, Graph.RowOf(number));

        public void RunThroughMonth(long month)
        {
            long last = Calendar.TickOfMonth(month);
            while (Runner.Tick <= last)
            {
                bool monthly = Calendar.IsDue(Cadence.Monthly, Runner.Tick);
                Runner.RunTick();
                if (monthly)
                    ReportsSeen += Industry.LastReports.Count;
            }
        }
    }

    private sealed class KInputs(Linked run, CounterRng rng) : IKnowledgeInputs
    {
        public Fx6 ResearchBudget(int actorRow, long month) => Scenario.BudgetIndex[actorRow] * Fx6.FromRaw(280_000);

        public Fx6 Priority(int actorRow, int nodeRow, long month) => Fx6.One;

        public Fx6 J(int actorRow, int nodeRow, long month) =>
            KnowledgeRng.J(rng, month == 0 ? 0 : (int)((month - 1) / 12), actorRow, run.Graph.Number[nodeRow]);

        public Fx6 DisciplineVolume(int actorRow, int discipline, long month) => run.Industry.DisciplineVolume(actorRow, discipline);
    }

    private sealed class IInputs(Linked run) : IIndustryInputs
    {
        public bool KnowsProcess(int siteRow, int processRow, long month) => run.Knows(siteRow, run.ILayout.Number[processRow]);

        public Fx6 W(int siteRow, int cluster, long month) => Fx6.One;

        public Fx6 Hcap(int siteRow, Dimension dim, long month) => Scenario.Hcap[siteRow];

        public bool QualityProgramme(int siteRow, long month) => false;

        // Stand-in AI: replace wear and add 0.01 M a month (spec §3.5 A11 money form), so tooling does not decay to 0.
        public Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month)
        {
            Fx6 m = run.Industry.ToolingM(siteRow, cluster, dim);
            return (run.IParams.DeltaM * m + Fx6.FromRaw(10_000)) * run.IParams.CostM(IndustryMath.CostTier(m));
        }

        public Fx6 LicenceShare(int siteRow, int processRow, long month) => Fx6.Zero;

        // Spec §1.1: engineer = ⌊20 000·budget_index⌋ + trophy, skilled = 10·engineer base.
        public long WorkforceAvailable(int siteRow, int category, long month)
        {
            long engineers = (Fx6.FromInt(20_000) * Scenario.BudgetIndex[siteRow]).Raw / Fx6.Scale;
            return category == 0 ? engineers + Scenario.TrophyEngineers[siteRow] : 10 * engineers;
        }

        public Fx6 AlloyCap(int siteRow, long month) => Scenario.AlloyCapAtStart(siteRow);

        public Fx6 AlloyDeficit(int siteRow, long month) => Fx6.Zero;

        public long KnownSince(int siteRow, int processRow, long month) => -1;
    }

    private sealed class PInputs(Linked run) : IProductsInputs
    {
        public bool Knows(int actorRow, int nodeNumber, long month) => run.Knows(actorRow, nodeNumber);

        public Fx6 AdmittedYield(int siteRow, int processNumber, long month) =>
            run.Knows(siteRow, processNumber) ? run.Industry.Yield(siteRow, run.ILayout.RowOf(processNumber), month).Yield : Fx6.Zero;

        public ProductionProfile Profile(int siteRow, ReadOnlySpan<PrcSetItem> prcSet, long month) => run.Industry.Profile(siteRow, prcSet, month);

        public Fx6 AlloySupply(int siteRow, long month) => run.Industry.Alloy(siteRow, month).Supply;

        public long Granted(int siteRow, int category, int requesterId, long month) => run.Industry.Granted(siteRow, category, requesterId);

        public Fx6 ProcurementBudget(int actorRow, long month) => Scenario.BudgetIndex[actorRow] * Fx6.FromRaw(440_000);

        // Stand-in AI: the newest known APP without a design; even actors superior, odd producible.
        public DesignOrder? Order(int actorRow, long month)
        {
            for (int g = run.PLayout.Generations; g >= 1; g--)
            {
                if (!run.Knows(actorRow, run.PLayout.GenApp[g - 1]))
                    continue;
                bool any = run.Products.Stage(actorRow, ProductsLayout.DesignId(g, DesignVariant.Superior)) != DesignStage.None
                    || run.Products.Stage(actorRow, ProductsLayout.DesignId(g, DesignVariant.Producible)) != DesignStage.None;
                return any ? null : new DesignOrder(g, actorRow % 2 == 0 ? DesignVariant.Superior : DesignVariant.Producible);
            }
            return null;
        }

        public bool LaunchSeries(int actorRow, int design, long month) =>
            run.Products.Stage(actorRow, design) == DesignStage.Prototype || (month == 0 && design == 0);

        public bool CloseLine(int actorRow, int design, long month)
        {
            for (int d = design + 1; d < run.PLayout.Designs; d++)
            {
                if (ProductsLayout.GenOf(d) > ProductsLayout.GenOf(design) && run.Products.Stage(actorRow, d) == DesignStage.Series)
                    return true;
            }
            return false;
        }

        public Fx6 LineRate(int actorRow, int design, long month) => Fx6.FromInt(30);

        public long AppKnownSince(int actorRow, int gen, long month) => -1;
    }
}
