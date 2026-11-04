using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Core.Tests.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Policy;

/// <summary>
/// The rule A40 on the real product layer (spec §11 T6c): a stand where the product system and the AI of the policy run together, with
/// <c>λ_D</c> = 0,003 (a stand, not a manifest: the liveness check rejects it) so that a project in О cannot reach the doctrine's bar.
/// </summary>
public class ProductAiRunTests
{
    private const int Gbr = 1;

    private sealed class RealProducts(ProductsHarness h) : IPolicyProducts
    {
        public ProductsLayout Layout => h.Layout;
        public Fx6 DRef => h.Products.DRef;

        public PolicyDesign Design(int a, int d, long month)
        {
            ProductsSystem p = h.Products;
            DesignStage st = p.Stage(a, d);
            return new PolicyDesign(st, p.PlatformGen(a, d), p.Cancelled(a, d), p.BuiltAny(a, d), p.IdleMonths(a, d), p.StageOMonth(a, d), p.Parent(a, d),
                st == DesignStage.Prototype && p.GateOpen(a, d, month), p.DefectsHidden(a, d), st == DesignStage.None ? Fx6.One : p.UnitCostFloor(a, d));
        }

        public int[] Alternatives(int a, int d) => h.Products.Alternatives(a, d);
        public bool Knows(int a, int n, long month) => h.Inputs.Knows(a, n, month);
        public bool Admitted(int a, int d, long month) => h.Inputs.Profile(a, DesignRules.Items(h.Layout, h.Products.PrcSet(a, d)), month).Admitted;
        public Fx6 AdmittedYield(int a, int p, long month) => h.Inputs.AdmittedYield(a, p, month);
        public Fx6 AlloySupply(int a, long month) => h.Inputs.AlloySupply(a, month);
        public int FreeBureaus(int a) => h.Products.FreeBureaus(a);
        public Fx6 CandidateValue(int a, ReadOnlySpan<int> alts, Fx6 budget, long month) => h.Products.CandidateValue(a, alts, budget, month).Value;
    }

    private static System.Collections.Immutable.ImmutableArray<(int Row, int Design)> StartLines(ProductsStart start)
    {
        var lines = new List<(int, int)>();
        for (int i = 0; i < start.Stage.Length; i++)
        {
            if (start.Stage[i] == DesignStage.Series)
                lines.Add((i / start.Layout.Designs, i % start.Layout.Designs));
        }
        return [.. lines];
    }

    private sealed class NoInputs : IPolicyInputs
    {
        public int NewestSeriesDesign(int actorRow, long month) => -1;
        public BottleneckLimit? Bottleneck(int actorRow, int design, long month) => null;
        public bool SeriesBelowQuality(int actorRow, Fx6 threshold, long month) => false;
    }

    private static (ProductsHarness H, PolicySystem Policy) Stand(ProductsParams parameters, Func<int, int, bool>? knows = null, int knownGen = 2)
    {
        PolicySystem? policy = null;
        var h = new ProductsHarness(parameters: parameters, extraSystems: (hh, table) =>
        {
            int n = table.RowCount;
            policy = new PolicySystem(table, new PolicyParams() with { FvBudget = Fx6.FromInt(10) }, [.. Enumerable.Range(0, n)],
                [.. Enumerable.Repeat(Fx6.One, n)], [.. Enumerable.Repeat(Fx6.Zero, n)], new CounterRng(1), new NoInputs(), new IndustryMailbox(TickRunner.ChunkCountFor(n, 1)), 99,
                [.. Enumerable.Repeat((int)PolicyProfile.Quality, n)], products: new RealProducts(hh), designs: hh.Layout.Designs,
                generations: hh.Layout.Generations, startLines: StartLines(hh.Start));
            return [policy];
        })
        {
            Knows = knows ?? ((a, nn) => a == Gbr && Probe.KnownUpTo(knownGen)(nn)),
        };
        PolicySystem p = policy!;
        h.Commands = (a, t) => p.AiDesignCommands(a, t);
        h.Series = (a, d, t) => p.AiStart(a, d, t) is { } r ? new SeriesStart(r, false) : null;
        h.Close = (a, d, t) => p.AiClose(a, d, t);
        h.Rate = (a, d, t) => p.AiLine(a, d)
            ? PolicyRules.LineRate(p.Shares(a).Procurement * p.Budget(a), h.Products.UnitCostFloor(a, d)) : null;
        p.Initialize();
        return (h, p);
    }

    [Fact]
    public void A40_StuckProjectInO_IsCancelledExactly60MonthsAfterEntering_TheBureauIsNotTouched_AndTheRequirementRepeatsAtTheNextAnnualStep()
    {
        (ProductsHarness h, PolicySystem policy) = Stand(new ProductsParams { LambdaD = Fx6.FromRaw(3_000) });
        ProductsSystem p = h.Products;
        Assert.Equal(DesignStage.None, p.Stage(Gbr, 1));
        h.RunThroughMonth(0);
        Assert.Equal(DesignStage.Project, p.Stage(Gbr, 1));            // the Requirement of the initialisation phase acted in tick 0
        long t = 1;
        while (p.Stage(Gbr, 1) != DesignStage.Prototype)
            h.RunThroughMonth(t++);
        int s = p.StageOMonth(Gbr, 1);
        int bureau = p.DesignBureau(Gbr, 1);
        // The gate opens (k_gate), the doctrine's bar (P1: 0,40·D_ref) is out of reach at λ_D = 0,003: the project stands in О.
        for (; t < s + 60; t++)
        {
            h.RunThroughMonth(t);
            Assert.Equal(DesignStage.Prototype, p.Stage(Gbr, 1));
        }
        Assert.Equal(DesignStage.Prototype, p.Stage(Gbr, 1));          // after month s + 59 …
        h.RunThroughMonth(t++);                                        // … t = s + 60: the snapshot has t − s = T_ab, the command is decided
        Assert.Equal(DesignStage.Prototype, p.Stage(Gbr, 1));
        Fx6 expBefore = p.BureauExp(Gbr, bureau);
        h.RunThroughMonth(t++);                                        // s + 61: «снят»
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 1));
        Assert.True(p.Cancelled(Gbr, 1));
        Assert.Equal(-1, p.BureauDesign(Gbr, bureau));
        Assert.Equal(expBefore - h.Params.DeltaB * expBefore, p.BureauExp(Gbr, bureau));   // only the usual forgetting of an idle bureau
        Assert.False(p.SeriesForced(Gbr, 1));
        // The Requirement repeats in the nearest annual step that sees the cancelled project.
        long annual = (s + 62 + 11) / 12 * 12;
        while (t <= annual)
            h.RunThroughMonth(t++);
        Assert.Equal(DesignStage.None, p.Stage(Gbr, 2));               // decided at `annual`, in force a month later
        h.RunThroughMonth(t++);
        Assert.Equal(DesignStage.Project, p.Stage(Gbr, 2));
        Assert.Equal(2, p.PlatformGen(Gbr, 2));
        Assert.Equal(1, policy.Repeats(Gbr, 2));
    }

    [Fact]
    public void WithTheNumbersOfSection81_AProjectInOLaunchesBeforeTAb_NothingIsCancelled_AndNeitherIsForced()
    {
        (ProductsHarness h, PolicySystem policy) = Stand(new ProductsParams());
        ProductsSystem p = h.Products;
        for (long t = 0; t < 140; t++)
            h.RunThroughMonth(t);
        Assert.Equal(DesignStage.Series, p.Stage(Gbr, 1));
        Assert.False(p.Cancelled(Gbr, 1));
        Assert.False(p.SeriesForced(Gbr, 1));
        Assert.True(p.Rate(Gbr, 1) > Fx6.Zero);
        Assert.True(policy.AiLine(Gbr, 1));
        // Launched at the doctrine's month: the decision at t − stage_o_month = the reference month for this D0, in force the month after.
        int month = PolicyLiveness.LaunchMonth(h.Params, p.InitialDefects(Gbr, 1), p.DRef, new PolicyParams().LaunchShare[(int)PolicyProfile.Quality]);
        Assert.True(month is > 0 and < 60);
        int o = p.StageOMonth(Gbr, 1);
        // The decision is made at t = s + month; the series is the state after month s + month + 1.
        ProductsHarness again = Stand(new ProductsParams()).H;
        for (long t = 0; t <= o + month; t++)
            again.RunThroughMonth(t);
        Assert.Equal(DesignStage.Prototype, again.Products.Stage(Gbr, 1));
        again.RunThroughMonth(o + month + 1);
        Assert.Equal(DesignStage.Series, again.Products.Stage(Gbr, 1));
    }

    // ---- E1 on the real layer

    [Fact]
    public void E1_StartLine_ProducesFromTickZero_AtTheAiRate()
    {
        (ProductsHarness h, PolicySystem policy) = Stand(new ProductsParams(), knownGen: 1);
        h.RunThroughMonth(0);
        Assert.Equal(DesignStage.Series, h.Products.Stage(Gbr, 0));
        Assert.True(policy.AiLine(Gbr, 0));
        Assert.True(h.Products.Rate(Gbr, 0) > Fx6.Zero);
        Assert.Equal(0, h.Products.IdleMonths(Gbr, 0));
    }

    [Fact]
    public void E1_BranchC_NoProcurementMoneyFor60Months_ClosesTheStartLineToStageВ_NotWithdrawn()
    {
        (ProductsHarness h, PolicySystem policy) = Stand(new ProductsParams(), knownGen: 1);
        h.Budget = (_, _) => Fx6.Zero;
        long t = 0;
        for (; t < 60; t++)
        {
            h.RunThroughMonth(t);
            Assert.Equal(DesignStage.Series, h.Products.Stage(Gbr, 0));
        }
        Assert.Equal(60, h.Products.IdleMonths(Gbr, 0));
        h.RunThroughMonth(t++);                                           // decided on the snapshot with idle_c = 60
        h.RunThroughMonth(t);
        Assert.Equal(DesignStage.InService, h.Products.Stage(Gbr, 0));
        Assert.False(h.Products.Cancelled(Gbr, 0));
        Assert.True(h.Products.BuiltAny(Gbr, 0));
    }

    // ---- F17 on the real layer

    [Fact]
    public void F17_AnUnknownProcessOfTheSet_IsNotAdmitted_AndAdmissionFollowsYield()
    {
        (ProductsHarness h, PolicySystem policy) = Stand(new ProductsParams(), knownGen: 1);
        var real = new RealProducts(h);
        Assert.True(real.Admitted(Gbr, 0, 0));
        h.Yield = (_, p) => p == 49 ? Fx6.Zero : Fx6.One;
        Assert.False(real.Admitted(Gbr, 0, 0));
        h.Yield = (_, _) => Fx6.One;
        h.Knows = (a, n) => a == Gbr && Probe.KnownUpTo(1)(n) && n != 49;
        Assert.False(real.Admitted(Gbr, 0, 0));
    }

    [Fact]
    public void F17_LaunchMonth_IsTheLaterOfTheDoctrineMonthAndTheMonthOfAdmission_AndTheOldLineWorksMeanwhile()
    {
        (ProductsHarness h, PolicySystem policy) = Stand(new ProductsParams());
        ProductsSystem p = h.Products;
        long t = 0;
        while (p.Stage(Gbr, 1) != DesignStage.Prototype)
            h.RunThroughMonth(t++);
        int o = p.StageOMonth(Gbr, 1);
        int doctrine = PolicyLiveness.LaunchMonth(h.Params, p.InitialDefects(Gbr, 1), p.DRef, new PolicyParams().LaunchShare[(int)PolicyProfile.Quality]);
        // From the entry into О a process of the set is not admitted, long past the doctrine's month.
        h.Yield = (_, q) => q == 40 ? Fx6.Zero : Fx6.One;
        long until = o + doctrine + 8;
        for (; t <= until; t++)
        {
            h.RunThroughMonth(t);
            Assert.Equal(DesignStage.Prototype, p.Stage(Gbr, 1));
            Assert.Equal(DesignStage.Series, p.Stage(Gbr, 0));              // the working line is not closed
        }
        Assert.True(p.Rate(Gbr, 0) > Fx6.Zero);
        h.Yield = (_, _) => Fx6.One;
        h.RunThroughMonth(t++);                                             // the first month whose snapshot is admitted: the decision
        Assert.Equal(DesignStage.Prototype, p.Stage(Gbr, 1));
        h.RunThroughMonth(t++);
        Assert.Equal(DesignStage.Series, p.Stage(Gbr, 1));
        Assert.Equal(DesignStage.InService, p.Stage(Gbr, 0));               // the previous line closed in the same bundle, built_any ⇒ В
        Assert.False(p.Cancelled(Gbr, 0));
    }

    [Fact]
    public void F17_AWaitLongerThanTAb_IsCancelledAtExactlyTheSixtiethMonthOfO_AsTheProcessClass()
    {
        (ProductsHarness h, PolicySystem policy) = Stand(new ProductsParams());
        ProductsSystem p = h.Products;
        long t = 0;
        while (p.Stage(Gbr, 1) != DesignStage.Prototype)
            h.RunThroughMonth(t++);
        int o = p.StageOMonth(Gbr, 1);
        h.Yield = (_, q) => q == 40 ? Fx6.Zero : Fx6.One;
        for (; t < o + 60; t++)
        {
            h.RunThroughMonth(t);
            Assert.Equal(DesignStage.Prototype, p.Stage(Gbr, 1));
        }
        Assert.Equal(GiveUpClass.Processes, policy.ClassifyGiveUp(Gbr, 1, t));
        h.RunThroughMonth(t++);                                             // t − stage_o_month = 60: the decision
        Assert.Equal(DesignStage.Prototype, p.Stage(Gbr, 1));
        h.RunThroughMonth(t);
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 1));
        Assert.True(p.Cancelled(Gbr, 1));
        Assert.Equal(DesignStage.Series, p.Stage(Gbr, 0));                  // the working line stayed
    }
}
