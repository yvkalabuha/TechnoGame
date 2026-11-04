using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// The product system step by step (spec §3.3, contract §4.4, §4.6) on the core data with scripted inputs. Expected values
/// are from the spec formulas by hand (G1 unit cost at E = 0: 217 + 1165 + 874 = 2256 raw; upkeep 29 raw per unit).
/// </summary>
public class ProductsSystemTests
{
    private const int Usa = 0, Gbr = 1, Fra = 3;
    // Design slots are the lowest free slot (contract §3.1): GBR's start fleet holds slot 0, its first new design takes slot 1.
    private const int G1Sup = 0, G1Tech = 1, G2Sup = 1;

    [Fact]
    public void StartFleets_AreG1SuperiorInService()
    {
        var h = new ProductsHarness(startLine: false);
        foreach (int a in new[] { Usa, Gbr })
        {
            Assert.Equal(DesignStage.InService, h.Products.Stage(a, G1Sup));
            Assert.Equal(Fx6.FromInt(100), h.Products.Cohort(a, G1Sup, 1945));
            Assert.Equal(["eng_g1", "hot_nicr_forged", "comp_centrifugal", "af_straight", "ab_none"], AltIds(h.Layout, h.Products.Alternatives(a, G1Sup)));
            Assert.Equal(Fx6.Zero, h.Products.InitialDefects(a, G1Sup));
        }
        for (int a = 2; a < 5; a++)
            Assert.Equal(DesignStage.None, h.Products.Stage(a, G1Sup));
    }

    [Fact]
    public void Series_FirstMonth_PaysUpkeepFirstThenBuys()
    {
        var h = new ProductsHarness(startLine: false)
        {
            Launch = (a, d, _) => a == Usa && d == G1Sup,
            Rate = (_, _, _) => Fx6.FromInt(50),
        };
        h.RunThroughMonth(0);
        ProductsSystem p = h.Products;
        Assert.Equal(DesignStage.Series, p.Stage(Usa, G1Sup));
        Assert.Equal(2256, p.UnitCost(Usa, G1Sup).Raw);
        Assert.Equal(Fx6.FromInt(50), p.Rate(Usa, G1Sup));
        Assert.Equal(Fx6.FromInt(50), p.LineE(Usa, G1Sup));
        Assert.Equal(Fx6.FromInt(150), p.Cohort(Usa, G1Sup, 1945));
        Assert.Equal(2900, p.UpkeepNeeded(Usa).Raw);
        Assert.Equal(Fx6.One, p.PaidRatio(Usa));
        Assert.Equal(2900 + 50 * 2256, p.Spent(Usa).Raw);

        // report_output per PRC of the set (units = r·n_p, hours = units·labour_p), in ascending (site, prc) after sorting.
        OutputReport[] reps = [.. h.Reports.Where(r => r.Site == Usa).OrderBy(r => r.ProcessNumber)];
        Assert.Equal([38, 39, 44, 45, 48, 49, 53, 54], reps.Select(r => r.ProcessNumber));
        Assert.All(reps, r => Assert.Equal(Fx6.FromInt(50), r.Units));
        Assert.Equal(Fx6.FromInt(50 * 3700), reps.Single(r => r.ProcessNumber == 53).LabourHours);
        // A26: one skilled request per (line, cluster): ⌈Σ v_p / 175⌉ = MET 41 250 h → 236, CHEM 18 750 → 108, MECH 60 000 → 343,
        // AERO 252 500 → 1443; requester ids 0…3 of line 0.
        WorkforceRequest[] skilled = [.. h.Requests.Where(q => q.Site == Usa && q.Category == h.Layout.SkilledCategory)];
        Assert.Equal([108, 236, 343, 1443], skilled.Select(q => q.N).Order());
        Assert.All(skilled, q => Assert.InRange(q.RequesterId, 0, 3));

        h.RunThroughMonth(1);
        Assert.True(p.UnitCost(Usa, G1Sup) < Fx6.FromRaw(2256), "line_E lowers assembly hours");
        Assert.Equal(Fx6.FromInt(200), p.Cohort(Usa, G1Sup, 1945));
    }

    [Fact]
    public void Series_IsLimitedByMoneyLeftAfterUpkeep()
    {
        var h = new ProductsHarness(startLine: false)
        {
            Launch = (a, d, _) => a == Usa && d == G1Sup,
            Rate = (_, _, _) => Fx6.FromInt(50),
            Budget = (_, _) => Fx6.FromRaw(50_000),
        };
        h.RunThroughMonth(0);
        // (50 000 − 2900)/2256 = 20.877659 units; r·unit_cost ≤ money left (spec §3.4).
        Assert.Equal(20_877_659, h.Products.Rate(Usa, G1Sup).Raw);
        Assert.True(h.Products.Spent(Usa) <= Fx6.FromRaw(50_000));
    }

    [Fact]
    public void Series_IsLimitedByAlloy()
    {
        var h = new ProductsHarness
        {
            Launch = (a, d, _) => a == Usa && d == G1Sup,
            Rate = (_, _, _) => Fx6.FromInt(50),
            Alloy = (_, _) => Fx6.FromInt(2900),
        };
        h.RunThroughMonth(0);
        Assert.Equal(Fx6.FromInt(10), h.Products.Rate(Usa, G1Sup));   // 2900 kg / alloy_kg[G1] = 290
    }

    [Fact]
    public void Series_NotAdmitted_ProducesNothing_I3()
    {
        var h = new ProductsHarness
        {
            Launch = (a, d, _) => a == Usa && d == G1Sup,
            Rate = (_, _, _) => Fx6.FromInt(50),
            Yield = (_, p) => p == 49 ? Fx6.Zero : Fx6.One,
        };
        h.RunThroughMonth(0);
        Assert.Equal(Fx6.Zero, h.Products.Rate(Usa, G1Sup));
        Assert.DoesNotContain(h.Reports, r => r.Site == Usa);
        Assert.Equal(2900, h.Products.Spent(Usa).Raw);
    }

    [Fact]
    public void Upkeep_ShortOfMoney_LowersReadiness()
    {
        var h = new ProductsHarness { Budget = (_, _) => Fx6.FromRaw(1_000) };
        h.RunThroughMonth(0);
        Assert.Equal(Fx6.FromRaw(1_000) / Fx6.FromRaw(2_900), h.Products.PaidRatio(Usa));
        Assert.Equal(h.Products.PaidRatio(Usa), h.Products.Readiness(Usa, G1Sup, 1));
    }

    [Fact]
    public void Order_DesignWorkAndBureauLag()
    {
        var h = new ProductsHarness
        {
            Knows = (a, n) => a == Gbr ? ProbeKnows(2, n) : false,
            Order = (a, t) => a == Gbr && t == 0 ? new DesignOrder(2, DesignVariant.Superior) : null,
        };
        ProductsSystem p = h.Products;
        h.RunThroughMonth(0);
        Assert.Equal(DesignStage.Project, p.Stage(Gbr, G2Sup));
        Assert.Equal(0, p.DesignBureau(Gbr, G2Sup));
        Assert.Equal(G2Sup, p.BureauDesign(Gbr, 0));
        // W = 400·37·1.11 = 16 428; D0 = 20·novelty·37 with all 5 slots new to GBR (its G1 design shares none) = 740.
        Assert.Equal(Fx6.FromInt(16_428), p.WorkLeft(Gbr, G2Sup));
        Assert.Equal(Fx6.FromInt(740), p.InitialDefects(Gbr, G2Sup));
        Assert.Contains(h.Requests, q => q.Site == Gbr && q.Category == ProductsLayout.EngineerCategory
            && q.RequesterId == h.Layout.BureauRequester(0) && q.N == 526);

        // bureau_req[G2] = ⌊500·1.11^0.5⌋ = 526 (A34). Progress in t = granted of t−1 (contract §4.6): none in month 0.
        Assert.Equal(526, p.BureauReq(2));
        h.RunThroughMonth(1);
        Assert.Equal(Fx6.FromInt(15_902), p.WorkLeft(Gbr, G2Sup));
        h.RunThroughMonth(31);
        Assert.Equal(Fx6.FromInt(122), p.WorkLeft(Gbr, G2Sup));
        h.RunThroughMonth(32);
        Assert.Equal(DesignStage.Prototype, p.Stage(Gbr, G2Sup));
        Assert.Equal(Fx6.One, p.BureauExp(Gbr, 0));
        Assert.Equal(-1, p.BureauDesign(Gbr, 0));

        // Testing starts in the month W is done: T = 200 h → found = 740·⌊0.0087·14.142135⌋ = 91.04664 (A31), open; the fix
        // team ⌈91.04664·2000/175⌉ = 1041 is capped at bureau_req = 526.
        Assert.Equal(91_046_640, p.DefectsFound(Gbr, G2Sup).Raw);
        Assert.Equal(p.DefectsFound(Gbr, G2Sup), p.DefectsOpen(Gbr, G2Sup));
        Assert.Equal(Fx6.FromInt(740) - p.DefectsFound(Gbr, G2Sup), p.DefectsHidden(Gbr, G2Sup));
        Assert.Contains(h.Requests, q => q.Site == Gbr && q.RequesterId == h.Layout.FixRequester(G2Sup) && q.N == 526);

        // Month 33: 526·175/2000 = 46.025 fixed; T = 400 h → found 740·0.174 = 128.76, the new 37.71336 join the open ones.
        h.RunThroughMonth(33);
        Assert.Equal(128_760_000, p.DefectsFound(Gbr, G2Sup).Raw);
        Assert.Equal(82_735_000, p.DefectsOpen(Gbr, G2Sup).Raw);
        // An idle bureau forgets δ_B = 0.005 of its experience (from the month after it became idle).
        Assert.Equal(Fx6.FromRaw(995_000), p.BureauExp(Gbr, 0));
    }

    [Fact]
    public void Order_PartialGrants_SlowTheBureau()
    {
        var h = new ProductsHarness
        {
            Knows = (a, n) => a == Gbr && ProbeKnows(2, n),
            Order = (a, t) => a == Gbr && t == 0 ? new DesignOrder(2, DesignVariant.Superior) : null,
            GrantShare = Fx6.FromRaw(500_000),
        };
        h.RunThroughMonth(2);
        Assert.Equal(Fx6.FromInt(16_428 - 2 * 263), h.Products.WorkLeft(Gbr, G2Sup));
    }

    [Fact]
    public void Order_ProducibleG1_IsTheSingleSuperiorDesign_FPC3()
    {
        var h = new ProductsHarness
        {
            Knows = (a, n) => a == Fra && ProbeKnows(1, n),
            Order = (a, t) => a == Fra && t == 0 ? new DesignOrder(1, DesignVariant.Producible) : null,
        };
        h.RunThroughMonth(0);
        Assert.Equal(DesignStage.Project, h.Products.Stage(Fra, G1Sup));
        Assert.Equal(DesignStage.None, h.Products.Stage(Fra, G1Tech));
    }

    [Fact]
    public void CloseLine_ResetsLineExperience()
    {
        var h = new ProductsHarness
        {
            Launch = (a, d, t) => a == Usa && d == G1Sup && t == 0,
            Close = (a, d, t) => a == Usa && d == G1Sup && t == 3,
            Rate = (_, _, _) => Fx6.FromInt(10),
        };
        h.RunThroughMonth(2);
        Assert.True(h.Products.LineE(Usa, G1Sup) > Fx6.Zero);
        h.RunThroughMonth(3);
        Assert.Equal(DesignStage.InService, h.Products.Stage(Usa, G1Sup));
        Assert.Equal(Fx6.Zero, h.Products.LineE(Usa, G1Sup));
        Assert.Equal(Fx6.Zero, h.Products.Rate(Usa, G1Sup));
    }

    [Fact]
    public void Cohorts_AreWrittenOffAtServiceLife()
    {
        // A32: cohort 1945 ages from July 1945 = month −2: written off at month 178 (1960-07); the design is then «снят».
        // «снят» is final in the full model.
        var h = new ProductsHarness(startLine: false);
        h.RunThroughMonth(177);
        Assert.Equal(Fx6.FromInt(100), h.Products.InService(Usa, G1Sup));
        h.RunThroughMonth(178);
        Assert.Equal(Fx6.Zero, h.Products.InService(Usa, G1Sup));
        Assert.Equal(DesignStage.Withdrawn, h.Products.Stage(Usa, G1Sup));
    }

    [Fact]
    public void CanonicalLp_StartsAfterTDesign_WithRhoTimesBudget()
    {
        var parameters = new ProductsParams { LpTDesign = [2, 2, 2, 2], LpRho = [Fx6.FromInt(100), Fx6.FromInt(50), Fx6.FromInt(20), Fx6.FromInt(10)] };
        var h = new ProductsHarness(mode: ProductsMode.CanonicalLp, parameters: parameters)
        {
            Knows = (a, n) => a == Usa && ProbeKnows(2, n),
            AppKnownSince = (a, g) => a == Usa && g == 1 ? 0 : a == Usa && g == 2 ? 3 : -1,
        };
        h.RunThroughMonth(1);
        Assert.Equal(DesignStage.None, h.Products.Stage(Usa, G1Tech));
        h.RunThroughMonth(2);
        // r = min(ρ[1]·0.44, alloy) = 44 units in the canonical (G1, slot 1) design; the start fleet keeps its slot.
        Assert.Equal(DesignStage.Series, h.Products.Stage(Usa, G1Tech));
        Assert.Equal(Fx6.FromInt(44), h.Products.Rate(Usa, G1Tech));
        Assert.Equal(DesignStage.InService, h.Products.Stage(Usa, G1Sup));
        Assert.Equal(Fx6.One, h.Products.Readiness(Usa, G1Tech, 3));
        // §6.1: best admitted alternative per slot; with G2 knowledge that is the G2-level set (10 PRC), each reported.
        Assert.Equal(h.Products.PrcSet(Usa, G1Tech), h.Reports.Where(r => r.Site == Usa).Select(r => r.ProcessNumber).Order());
        Assert.Equal(10, h.Products.PrcSet(Usa, G1Tech).Length);
        h.RunThroughMonth(5);
        Assert.Equal(DesignStage.InService, h.Products.Stage(Usa, G1Tech));
        Assert.Equal(2, h.Products.CanonicalSlot(Usa, 2));   // generation 2 takes the next free slot
        Assert.Equal(DesignStage.Series, h.Products.Stage(Usa, 2));
        Assert.Equal(Fx6.FromRaw(22_000_000), h.Products.Rate(Usa, 2));
    }

    [Fact]
    public void FleetValue_IsAPureFunctionOfTheSnapshot()
    {
        var h = new ProductsHarness();
        int[] alts = h.Products.Alternatives(Usa, G1Sup);
        (int n, Fx6 fv) = h.Products.FleetValue(Usa, alts, Fx6.Zero, Fx6.One, Fx6.One, Fx6.FromRaw(440_000), 0);
        Assert.Equal(n, h.Products.FleetValue(Usa, alts, Fx6.Zero, Fx6.One, Fx6.One, Fx6.FromRaw(440_000), 0).Count);
        Assert.Equal(Fx6.FromInt(n), fv);
        Assert.Equal(0, n % 10);
        Assert.True(n > 0);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 3)]
    public void L1_L2_SameHashEveryMonth(int threads, int chunkSize)
    {
        ProductsHarness reference = Busy(1, 1), run = Busy(threads, chunkSize);
        string first = "";
        for (long t = 0; t < 60; t++)
        {
            reference.RunThroughMonth(t);
            run.RunThroughMonth(t);
            Assert.Equal(reference.Hash(), run.Hash());
            first = t == 0 ? run.Hash() : first;
        }
        Assert.NotEqual(first, run.Hash());
    }

    private static ProductsHarness Busy(int threads, int chunkSize) => new(threads, chunkSize)
    {
        Knows = (a, n) => ProbeKnows(a % 2 == 0 ? 2 : 1, n),
        Order = (a, t) => t == a ? new DesignOrder(a % 2 == 0 ? 2 : 1, a % 3 == 0 ? DesignVariant.Producible : DesignVariant.Superior) : null,
        Launch = (a, d, t) => t % 7 == a,
        Close = (a, d, t) => t % 11 == a + 3,
        Rate = (a, d, t) => Fx6.FromInt(5 + (a + d + t) % 9),
        Budget = (a, t) => Fx6.FromRaw(20_000 + 10_000 * a + 500 * (t % 13)),
        Alloy = (a, _) => Fx6.FromInt(3_000 + 1_000 * a),
        Yield = (a, p) => Fx6.FromRaw(300_000 + 50_000 * ((a + p) % 15)),
        GrantShare = Fx6.FromRaw(700_000),
    };

    private static bool ProbeKnows(int gen, int number) => Probe.KnownUpTo(gen)(number);
}
