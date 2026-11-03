using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Knowledge;
using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// ADR-0002 L1/L2 for the linked run Knowledge + Industry (rule C-B, effort budget, planned/v_exp from products) + Products v2.4:
/// 60 months, 1 and 4 threads (and odd chunk sizes), equal state hash every month, and a golden hash. The golden value
/// changes only for a reason written in the task (a formula, data or layout change), never to make the test pass.
/// </summary>
public class ProductsCbLinkedRunTests
{
    /// <summary>State hash after month 59 (tick of the last monthly step), seed 7. First recorded 2026-11-03 at contract v2.4 r2.</summary>
    // Re-frozen in the TASK-0021 review fixes: the rejection reason LineTransferred adds one product column (layout change only).
    private const string GoldenHash60 = "1792c856dd9b7cbb733d3242ee8793ef9ff6e70703b13c990a58d17680fbd4d5";

    [Fact]
    public void L2_CbIndustry_Products_60Months_SameHashEveryMonth_ForThreadsAndChunks()
    {
        var runs = new[] { new CbLinked(1, 1), new CbLinked(4, 1), new CbLinked(3, 2), new CbLinked(8, 3) };
        for (long t = 0; t < 60; t++)
        {
            string? first = null;
            foreach (CbLinked r in runs)
            {
                r.RunThroughMonth(t);
                string h = StateHash.ComputeHex(r.Table, 1, r.Runner.Tick);
                first ??= h;
                Assert.True(first == h, $"month {t}: hash differs ({r.Threads} threads)");
            }
        }
        CbLinked run = runs[0];
        Assert.True(run.EffortSeen, "no process ever received effort money: the C-B path is not exercised");
        Assert.True(run.PlannedSeen, "no project ever planned a process");
        Assert.True(run.ReportsSeen > 0);
    }

    [Fact]
    public void L1_TwoRuns_SameHash_AndMatchesGolden()
    {
        var a = new CbLinked(1, 1);
        var b = new CbLinked(4, 1);
        a.RunThroughMonth(59);
        b.RunThroughMonth(59);
        string ha = StateHash.ComputeHex(a.Table, 1, a.Runner.Tick);
        Assert.Equal(ha, StateHash.ComputeHex(b.Table, 1, b.Runner.Tick));
        Assert.True(GoldenHash60 == ha, $"golden hash changed: {ha}; update only with a reason written in the task");
    }

    [Fact]
    public void Linked_EffortConservedPerActorEveryMonth_AndInvariantsHold()
    {
        var run = new CbLinked(2, 1);
        IndustryLayout l = run.ILayout;
        for (long t = 0; t < 60; t++)
        {
            run.RunThroughMonth(t);
            for (int a = 0; a < Scenario.ActorCount; a++)
            {
                // D_a = Σ E over the actor's sites and processes + rest_a (rest goes to tooling or returns to the budget).
                Fx6 e = Fx6.Zero;
                int site0 = -1;
                for (int s = 0; s < l.SiteCount; s++)
                {
                    if (l.SiteOwner[s] != a)
                        continue;
                    site0 = site0 < 0 ? s : site0;
                    for (int r = 0; r < l.ProcessCount; r++)
                    {
                        Assert.True(run.Industry.ProcE(s, r) >= Fx6.Zero);
                        e += run.Industry.ProcE(s, r);
                    }
                }
                Fx6 budget = run.EffortBudgetOf(a);
                Assert.True(run.Industry.LastRest(site0) >= Fx6.Zero, $"month {t}, actor {a}: negative rest");
                Assert.Equal(budget, e + run.Industry.LastRest(site0));
            }
            for (int a = 0; a < Scenario.ActorCount; a++)
            {
                for (int d = 0; d < run.PLayout.Designs; d++)
                {
                    Assert.True(run.Products.RetroPool(a, d) >= Fx6.Zero);
                    Assert.True(run.Products.LossAcc(a, d) >= Fx6.Zero);
                    if (!run.Products.SeriesForced(a, d))
                    {
                        // X is defined for the stages С and В only (§4.4c); before the series d_gate is not final.
                        if (run.Products.Stage(a, d) is DesignStage.Series or DesignStage.InService && run.Products.GateOpened(a, d))
                            Assert.Equal(Fx6.Zero, run.Products.ExcessDefects(a, d));
                        Assert.Equal(Fx6.Zero, run.Products.RetroPool(a, d));
                        Assert.Equal(Fx6.Zero, run.Products.AccidentsLost(a, d));
                    }
                    Assert.True(run.Products.AccidentsLost(a, d) <= run.Products.InServiceBeforeAccidents(a, d));
                }
            }
        }
    }

    private sealed class CbLinked
    {
        public CbLinked(int threads, int chunkSize)
        {
            Threads = threads;
            var kp = new KnowledgeParams();
            Graph = KnowledgeGraph.FromData(CoreData.Nodes, Scenario.Disciplines, kp);
            KnowledgeStart kStart = KnowledgeStart.FromScenario(Scenario, Graph);
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
            PLayout = ProductsLayout.FromData(CoreData.Nodes, CoreData.ProductClasses, "jet_fighter", ILayout, Bureaus, Scenario.StartYear, Years, Scenario.MaxDesigns);
            ProductsStart pStart = ProductsStart.FromScenario(Scenario, CoreData.Nodes, CoreData.ProductClasses, PLayout);

            Table = new Table(Scenario.ActorCount);
            Calendar = new TickCalendar(new CivilDate(Scenario.StartYear, Scenario.StartMonth, 1));
            int chunks = TickRunner.ChunkCountFor(Table.RowCount, chunkSize);
            var rng = new CounterRng(7);
            Knowledge = new KnowledgeSystem(Table, Graph, kStart, kp, new KInputs(this, rng), rng, chunks);
            Industry = new IndustrySystem(Table, ILayout, iStart, ip, new IInputs(this), chunks, new CbCultureRule());
            Products = new ProductsSystem(Table, PLayout, pStart, new ProductsParams(), new PInputs(this),
                Industry.CreateMailbox(chunks, Cadence.Monthly));
            Runner = new TickRunner(Table, 7, [Knowledge, Industry, Products], chunkSize, threads, Calendar);
        }

        public int Threads { get; }
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
        public bool EffortSeen, PlannedSeen;

        public Fx6 EffortBudgetOf(int actor) => Scenario.BudgetIndex[actor] * Fx6.FromRaw(90_000);

        public bool Knows(int actor, int number) => Knowledge.IsKnown(actor, Graph.RowOf(number));

        public void RunThroughMonth(long month)
        {
            long last = Calendar.TickOfMonth(month);
            while (Runner.Tick <= last)
            {
                bool monthly = Calendar.IsDue(Cadence.Monthly, Runner.Tick);
                Runner.RunTick();
                if (!monthly)
                    continue;
                ReportsSeen += Industry.LastReports.Count;
                for (int s = 0; s < ILayout.SiteCount && !EffortSeen; s++)
                {
                    for (int r = 0; r < ILayout.ProcessCount; r++)
                        EffortSeen |= Industry.ProcE(s, r) > Fx6.Zero;
                }
                for (int a = 0; a < Scenario.ActorCount; a++)
                {
                    for (int d = 0; d < PLayout.Designs; d++)
                        PlannedSeen |= Products.Stage(a, d) is DesignStage.Project or DesignStage.Prototype;
                }
            }
        }
    }

    private sealed class KInputs(CbLinked run, CounterRng rng) : IKnowledgeInputs
    {
        public ReadOnlySpan<int> LicensedEngines(int actorRow, long month) => ReadOnlySpan<int>.Empty;

        public Fx6 ResearchBudget(int actorRow, long month) => Scenario.BudgetIndex[actorRow] * Fx6.FromRaw(280_000);

        public Fx6 Priority(int actorRow, int nodeRow, long month) => Fx6.One;

        public Fx6 J(int actorRow, int nodeRow, long month) =>
            KnowledgeRng.J(rng, month == 0 ? 0 : (int)((month - 1) / 12), actorRow, run.Graph.Number[nodeRow]);

        public Fx6 DisciplineVolume(int actorRow, int discipline, long month) => run.Industry.DisciplineVolume(actorRow, discipline);
    }

    private sealed class IInputs(CbLinked run) : IIndustryInputs
    {
        public bool KnowsProcess(int siteRow, int processRow, long month) => run.Knows(siteRow, run.ILayout.Number[processRow]);

        public Fx6 W(int siteRow, int cluster, long month) => Fx6.One;

        public Fx6 Hcap(int siteRow, Dimension dim, long month) => Scenario.Hcap[siteRow];

        public bool QualityProgramme(int siteRow, long month) => false;

        // The rest of the actor is split evenly over the cells of its site (floor): never more than rest_a in total per site.
        public Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month, Fx6 restActor)
        {
            Fx6 m = run.Industry.ToolingM(siteRow, cluster, dim);
            Fx6 wear = (run.IParams.DeltaM * m + Fx6.FromRaw(10_000)) * run.IParams.CostM(IndustryMath.CostTier(m));
            Fx6 cells = Fx6.FromInt(run.ILayout.Clusters.Length * Dims3.Count);
            return wear + Fx6.FromRaw(restActor.Raw / (cells.Raw / Fx6.Scale));
        }

        public Fx6 EffortBudget(int actorRow, long month) => run.EffortBudgetOf(actorRow);

        public int EffortFocus(int actorRow, int k, long month) => -1;

        public Fx6 PlannedVolume(int siteRow, int processRow, long month) => run.Products.PlannedVolume(siteRow, run.ILayout.Number[processRow]);

        public bool Planned(int siteRow, int processRow, long month) => run.Products.Planned(siteRow, run.ILayout.Number[processRow]);

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

    private sealed class PInputs(CbLinked run) : IProductsInputs
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

        public Fx6? LineRate(int actorRow, int design, long month) => null;

        public long AppKnownSince(int actorRow, int gen, long month) => -1;
    }
}
