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
        public Fx6 AdmittedYield(int a, int p, long month) => h.Inputs.AdmittedYield(a, p, month);
        public Fx6 AlloySupply(int a, long month) => h.Inputs.AlloySupply(a, month);
        public int FreeBureaus(int a) => h.Products.FreeBureaus(a);
        public Fx6 CandidateValue(int a, ReadOnlySpan<int> alts, Fx6 budget, long month) => h.Products.CandidateValue(a, alts, budget, month).Value;
    }

    private sealed class NoInputs : IPolicyInputs
    {
        public int NewestSeriesDesign(int actorRow, long month) => -1;
        public BottleneckLimit? Bottleneck(int actorRow, int design, long month) => null;
        public bool SeriesBelowQuality(int actorRow, Fx6 threshold, long month) => false;
    }

    private static (ProductsHarness H, PolicySystem Policy) Stand(ProductsParams parameters)
    {
        PolicySystem? policy = null;
        var h = new ProductsHarness(parameters: parameters, extraSystems: (hh, table) =>
        {
            int n = table.RowCount;
            policy = new PolicySystem(table, new PolicyParams() with { FvBudget = Fx6.FromInt(10) }, [.. Enumerable.Range(0, n)],
                [.. Enumerable.Repeat(Fx6.One, n)], [.. Enumerable.Repeat(Fx6.Zero, n)], new CounterRng(1), new NoInputs(), new IndustryMailbox(TickRunner.ChunkCountFor(n, 1)), 99,
                [.. Enumerable.Repeat((int)PolicyProfile.Quality, n)], products: new RealProducts(hh), designs: hh.Layout.Designs,
                generations: hh.Layout.Generations);
            return [policy];
        })
        {
            Knows = (a, nn) => a == Gbr && Probe.KnownUpTo(2)(nn),
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
    }
}
