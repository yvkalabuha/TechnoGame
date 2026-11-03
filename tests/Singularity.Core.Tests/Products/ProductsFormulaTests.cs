using System.Text.Json;
using Singularity.Core.Products;
using Singularity.Core.Tests.Knowledge;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// D-tests of the product layer against the independent reference lab/reference/jet_slice/out/products.json
/// (products_ref.py, spec v0.10 §3.3, §3.5 A28–A34, §6.1): every raw value compared exactly. The reference's
/// <c>generation_cap</c> cases (A32 with every node known) are not checked: A32 was cancelled by the owner (#81).
/// </summary>
public class ProductsFormulaTests
{
    private static readonly Lazy<JsonDocument> Doc = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(JetReference.OutDir, "products.json"))));

    private static JsonElement Ref => Doc.Value.RootElement;
    private static readonly ProductsParams P = new();
    private static Fx6 R(JsonElement e) => Fx6.FromRaw(e.GetInt64());

    public static TheoryData<int, string> Designs => new()
    {
        { 1, "superior" }, { 1, "producible" }, { 2, "superior" }, { 2, "producible" },
        { 3, "superior" }, { 3, "producible" }, { 4, "superior" }, { 4, "producible" },
    };

    [Fact]
    public void Params_MatchTheReference()
    {
        JsonElement p = Ref.GetProperty("params");
        Assert.Equal(p.GetProperty("lr_q10").GetInt32(), P.LearningQ10);
        Assert.Equal(p.GetProperty("d0").GetInt64(), P.D0Factor.Raw);
        Assert.Equal(p.GetProperty("lambda_d").GetInt64(), P.LambdaD.Raw);
        Assert.Equal(p.GetProperty("beta_q10").GetInt32(), P.BetaQ10);
        Assert.Equal(p.GetProperty("psi_d").GetInt64(), P.PsiD.Raw);
        Assert.Equal(p.GetProperty("fix_hours").GetInt64(), P.FixHours.Raw);
        Assert.Equal(p.GetProperty("upkeep_share").GetInt64(), P.UpkeepShare.Raw);
        Assert.Equal(p.GetProperty("alpha_u_q10").GetInt32(), P.UpkeepQ10);
        Assert.Equal(p.GetProperty("service_life").GetInt32(), P.ServiceLife);
        Assert.Equal(p.GetProperty("W0").GetInt64(), P.W0.Raw);
        Assert.Equal(p.GetProperty("E_B").GetInt64(), P.EB.Raw);
        Assert.Equal(p.GetProperty("test_rate").GetInt64(), P.TestRate.Raw);
        Assert.Equal(p.GetProperty("y_prod").GetInt64(), P.YProd.Raw);
        Assert.Equal(p.GetProperty("B0").GetInt32(), P.B0);
        Assert.Equal(p.GetProperty("h_pm").GetInt64(), Probe.HPm.Raw);
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Design_KnowingGenerationsUpToG_MatchesReference(int gen, string variant)
    {
        JsonElement g = Ref.GetProperty("designs_know_all_up_to_g").GetProperty($"G{gen}");
        JsonElement e = g.GetProperty(variant);
        Span<int> alts = stackalloc int[Probe.SlotCount];
        bool lightened = variant == "superior"
            ? DesignRules.Superior(Probe, gen, Probe.KnownUpTo(gen), alts)
            : DesignRules.Producible(Probe, gen, Probe.KnownUpTo(gen), _ => Fx6.One, P.YProd, alts);
        Assert.True(variant == "superior" ? lightened : lightened == (g.GetProperty("producible_kind").GetString() != "single"));
        Assert.Equal(e.GetProperty("alts").EnumerateArray().Select(x => x.GetString()!), AltIds(Probe, alts));

        int[] prc = DesignRules.PrcSet(Probe, alts);
        Assert.Equal(e.GetProperty("prc_set").EnumerateArray().Select(x => x.GetInt32()), prc);
        int cx = DesignRules.Complexity(Probe, prc);
        Assert.Equal(e.GetProperty("complexity").GetInt32(), cx);
        Assert.Equal(e.GetProperty("perf").GetInt64(), DesignRules.Perf(Probe, alts).Raw);
        Fx6 bs = DesignRules.BaseCostSum(Probe, prc);
        Assert.Equal(e.GetProperty("base_cost_sum").GetInt64(), bs.Raw);
        Fx6 part = ProductsMath.ProcessPart(bs, Fx6.One, Probe.ProcScale[gen - 1]);
        Assert.Equal(e.GetProperty("process_part").GetInt64(), part.Raw);

        var card = new CostCard(Probe.HFirst[gen - 1], Probe.HFloor[gen - 1], Probe.WageKh, Probe.MaterialsFloor[gen - 1], part, P.LearningQ10);
        foreach (JsonProperty u in e.GetProperty("unit_cost_by_E").EnumerateObject())
            Assert.Equal(u.Value.GetInt64(), ProductsMath.UnitCost(card, Fx6.FromInt(int.Parse(u.Name))).Raw);
        foreach (JsonProperty h in e.GetProperty("assembly_hours_by_E").EnumerateObject())
            Assert.Equal(h.Value.GetInt64(), ProductsMath.AssemblyHours(Fx6.FromInt(int.Parse(h.Name)), card.HFirst, card.HFloor, P.LearningQ10).Raw);
        Fx6 floor = ProductsMath.UnitCostFloor(card.MaterialsFloor, card.HFloor, card.WageKh, bs, Probe.ProcScale[gen - 1]);
        Assert.Equal(e.GetProperty("unit_cost_floor").GetInt64(), floor.Raw);
        Assert.Equal(e.GetProperty("upkeep").GetInt64(), ProductsMath.Upkeep(P.UpkeepShare, floor, cx, P.UpkeepQ10, Probe.Maintainability).Raw);
        Assert.Equal(e.GetProperty("W_exp0").GetInt64(), ProductsMath.DesignWork(P.W0, cx, Probe.DesignScale[gen - 1], Fx6.Zero, P.EB).Raw);
        Assert.Equal(e.GetProperty("W_exp_g_minus_1").GetInt64(),
            ProductsMath.DesignWork(P.W0, cx, Probe.DesignScale[gen - 1], Fx6.FromInt(gen - 1), P.EB).Raw);
        Assert.Equal(e.GetProperty("D0_novelty1_exp0").GetInt64(), ProductsMath.InitialDefects(P.D0Factor, Fx6.One, cx, Fx6.Zero, P.EB).Raw);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void BureauReq_MatchesReferenceForKappa0_Half_1(int gen)
    {
        JsonElement b = Ref.GetProperty("designs_know_all_up_to_g").GetProperty($"G{gen}").GetProperty("bureau_req");
        Fx6 ds = Probe.DesignScale[gen - 1];
        Assert.Equal(b.GetProperty("kappa0").GetInt32(), (P with { KappaQ10 = 0 }).BureauReq(ds));
        Assert.Equal(b.GetProperty("kappa0.5").GetInt32(), P.BureauReq(ds));
        Assert.Equal(b.GetProperty("kappa1").GetInt32(), (P with { KappaQ10 = 1024 }).BureauReq(ds));
    }

    [Fact]
    public void ProducibleFallback_AndCanonicalWithoutSuperalloy()
    {
        JsonElement cap = Ref.GetProperty("generation_cap");
        // A32 fallback: no hot-section or compressor alternative reaches y_prod → the lightest available one.
        var low = new Dictionary<int, long> { [40] = 300_000, [39] = 200_000, [45] = 900_000, [49] = 900_000, [50] = 100_000, [48] = 400_000 };
        Fx6 Low(int p) => low.TryGetValue(p, out long v) ? Fx6.FromRaw(v) : Fx6.One;
        Span<int> alts = stackalloc int[Probe.SlotCount];
        Assert.True(DesignRules.Producible(Probe, 2, Probe.KnownUpTo(2), Low, P.YProd, alts));
        Assert.Equal(cap.GetProperty("G2_producible_fallback").GetProperty("alts").EnumerateArray().Select(x => x.GetString()!), AltIds(Probe, alts));
        // L_P: superalloy melt (40) not admitted → the hot section falls to the NiCr melt.
        Assert.True(DesignRules.Canonical(Probe, 2, Probe.KnownUpTo(2), p => p == 40 ? Fx6.Zero : Fx6.One, alts));
        Assert.Equal(cap.GetProperty("G2_canonical_40_not_admitted").EnumerateArray().Select(x => x.GetString()!), AltIds(Probe, alts));
    }

    [Fact]
    public void LpPrice_HasProcScale_A33()
    {
        Span<int> alts = stackalloc int[Probe.SlotCount];
        Assert.True(DesignRules.Superior(Probe, 2, Probe.KnownUpTo(2), alts));
        Fx6 bs = DesignRules.BaseCostSum(Probe, DesignRules.PrcSet(Probe, alts));
        Assert.Equal(Ref.GetProperty("lp_price_G2_ucm_1.25").GetInt64(), ProductsMath.ProcessPart(bs, Fx6.FromRaw(1_250_000), Probe.ProcScale[1]).Raw);
    }

    [Fact]
    public void Detection_A31_MatchesReference()
    {
        JsonElement d = Ref.GetProperty("detection");
        Fx6 d0 = R(d.GetProperty("D0"));
        foreach (JsonProperty h in d.GetProperty("by_hours").EnumerateObject())
            Assert.Equal(h.Value.GetInt64(), ProductsMath.DetectedTotal(Fx6.FromInt(int.Parse(h.Name)), d0, P.LambdaD, P.BetaQ10).Raw);
        foreach (JsonProperty s in d.GetProperty("months_tests_only_to_share").EnumerateObject())
        {
            Fx6 target = d0 * Fx6.FromRaw((long)(decimal.Parse(s.Name, System.Globalization.CultureInfo.InvariantCulture) * Fx6.Scale));
            int m = 0;
            Fx6 t = Fx6.Zero;
            while (ProductsMath.DetectedTotal(t, d0, P.LambdaD, P.BetaQ10) < target && m < 1000)
            {
                m++;
                t += P.TestRate;
            }
            Assert.Equal(s.Value.GetInt32(), m);
        }
        // 24 months of tests, then a series of 20 units a month, 20 flight hours per unit, k = 1.
        Fx6 hours = Fx6.FromInt(24 * 200), units = Fx6.Zero;
        foreach (JsonElement expected in d.GetProperty("series_20_per_month_after_24m").EnumerateArray())
        {
            units += Fx6.FromInt(20);
            hours += P.TestRate + units * Fx6.FromInt(20);
            Assert.Equal(expected.GetInt64(), ProductsMath.DetectedTotal(hours, d0, P.LambdaD, P.BetaQ10).Raw);
        }
    }

    [Fact]
    public void Readiness_MatchesReference()
    {
        JsonElement r = Ref.GetProperty("readiness");
        Fx6 d0 = R(Ref.GetProperty("detection").GetProperty("D0"));
        Fx6 half = Fx6.FromRaw(d0.Raw / 2);
        Assert.Equal(r.GetProperty("new_design_D_eq_D0").GetInt64(), ProductsMath.Readiness(Fx6.One, Fx6.One, d0, d0, P.PsiD).Raw);
        Assert.Equal(r.GetProperty("half_open").GetInt64(), ProductsMath.Readiness(Fx6.One, Fx6.FromRaw(900_000), half, d0, P.PsiD).Raw);
        Assert.Equal(r.GetProperty("unpaid_0.8_no_defects").GetInt64(), ProductsMath.Readiness(Fx6.FromRaw(800_000), Fx6.One, Fx6.Zero, d0, P.PsiD).Raw);
        Assert.Equal(r.GetProperty("start_fleet_D0_0").GetInt64(), ProductsMath.Readiness(Fx6.One, Fx6.One, Fx6.Zero, Fx6.Zero, P.PsiD).Raw);
    }

    [Theory]
    [InlineData(1945)]
    [InlineData(1946)]
    [InlineData(1960)]
    public void CohortWriteOffMonth_MatchesReference(int year)
    {
        // A32 (kept by #81): a cohort of year Y ages from July; written off in the first month t with t − m0 ≥ service_life.
        long m0 = (long)(year - Scenario.StartYear) * 12 + (7 - Scenario.StartMonth);
        Assert.Equal(Ref.GetProperty("cohort_retire_month").GetProperty($"{year}").GetInt64(), m0 + P.ServiceLife);
    }

    [Fact]
    public void AssemblyCost_IsOneRoundingPer1000Hours_A28()
    {
        // ⌊hours_raw·wage_kh_raw/10⁹⌋: 2330 h → 291 raw; 1 h → 0; 8 h → 1 raw.
        Assert.Equal(291, ProductsMath.AssemblyCost(Fx6.FromInt(2330), Fx6.FromRaw(125)).Raw);
        Assert.Equal(0, ProductsMath.AssemblyCost(Fx6.FromInt(1), Fx6.FromRaw(125)).Raw);
        Assert.Equal(1, ProductsMath.AssemblyCost(Fx6.FromInt(8), Fx6.FromRaw(125)).Raw);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(175, 1)]
    [InlineData(176, 2)]
    [InlineData(18_750, 108)]
    public void People_IsCeilingOfHoursOverHpm(int hours, long people) =>
        Assert.Equal(people, ProductsMath.People(Fx6.FromInt(hours), Fx6.FromInt(175)));

    [Theory]
    [InlineData(0)]
    [InlineData(500_000)]
    [InlineData(5_000_000)]
    public void FleetCount_IsTheLargestBlockCountWithinBudget(long budgetRaw)
    {
        var card = new CostCard(Fx6.FromInt(9320), Fx6.FromInt(2330), Fx6.FromRaw(125), Fx6.FromRaw(217), Fx6.FromRaw(874), 330);
        Fx6 budget = Fx6.FromRaw(budgetRaw), upkeep = Fx6.FromRaw(29);
        int n = ProductsMath.FleetCount(card, Fx6.Zero, upkeep, 180, budget, 10, 20_000);
        Assert.Equal(0, n % 10);
        Assert.True(Cost(n) <= budget);
        Assert.True(n == 20_000 || Cost(n + 10) > budget);

        Fx6 Cost(int units)
        {
            Fx6 sum = Fx6.Zero;
            for (int k = 0; k < units; k += 10)
                sum += Fx6.FromInt(10) * ProductsMath.UnitCost(card, Fx6.FromInt(k + 1));
            return sum + Fx6.FromInt(units) * upkeep * Fx6.FromInt(180);
        }
    }
}
