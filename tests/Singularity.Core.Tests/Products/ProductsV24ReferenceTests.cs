using System.Text.Json;
using Singularity.Core.Products;
using Singularity.Core.Tests.Knowledge;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// Bit-equality of the product layer with section <c>v24</c> of lab/reference/jet_slice/out/products.json (products_ref.py;
/// contract v2.4 r2 §4.4a–c, §9.1; spec v0.11 A33, A35, A36): composition, <c>g_p</c> and <c>gen</c>, R-PS2m, the gate,
/// accidents, retrofit (F9), <c>FV</c>. Every raw value is compared exactly.
/// </summary>
public class ProductsV24ReferenceTests
{
    private static readonly Lazy<JsonDocument> Doc = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(JetReference.OutDir, "products.json"))));

    private static JsonElement V24 => Doc.Value.RootElement.GetProperty("v24");
    private static readonly ProductsParams P = new();
    private static readonly ProductsLayout L = Probe;
    private static Fx6 DRef => ProductsMath.DRef(P.D0Factor, L.CRef);

    public static TheoryData<string> References => [.. V24.GetProperty("references").EnumerateObject().Select(o => o.Name)];

    [Fact]
    public void Params_AndDRef_MatchTheReference()
    {
        JsonElement p = V24.GetProperty("params");
        Assert.Equal(p.GetProperty("c_ref").GetInt32(), L.CRef);
        Assert.Equal(V24.GetProperty("D_ref").GetInt64(), DRef.Raw);
        Assert.Equal(p.GetProperty("k_gate").GetInt64(), P.KGate.Raw);
        Assert.Equal(p.GetProperty("t_test_min").GetInt32(), P.TTestMin);
        Assert.Equal(p.GetProperty("a_acc").GetInt64(), P.AAcc.Raw);
        Assert.Equal(p.GetProperty("rho_retro").GetInt64(), P.RhoRetro.Raw);
        Assert.Equal(p.GetProperty("fleet_hours").GetInt64(), P.FleetHoursPerUnit.Raw);
        Assert.Equal(p.GetProperty("gamma_q10").GetInt32(), P.GammaQ10);
        Assert.Equal(p.GetProperty("H_build").GetInt32(), P.HBuild);
    }

    [Theory]
    [MemberData(nameof(References))]
    public void Reference_Composition_Cost_Gate_MatchTheReference(string name)
    {
        JsonElement e = V24.GetProperty("references").GetProperty(name);
        int gen = name[1] - '0';
        bool producible = name.EndsWith("producible", StringComparison.Ordinal);
        Span<int> alts = stackalloc int[L.SlotCount];
        // product_class.md §2: the actor knows every node of generations <= g, every yield is 1.
        Assert.True(producible
            ? DesignRules.Producible(L, gen, L.KnownUpTo(gen), _ => Fx6.One, P.YProd, null, alts)
            : DesignRules.Superior(L, gen, L.KnownUpTo(gen), alts));
        Assert.Equal(e.GetProperty("alts").EnumerateArray().Select(x => x.GetString()!), AltIds(L, alts));

        int[] prc = DesignRules.PrcSet(L, alts);
        Assert.Equal(e.GetProperty("prc_set").EnumerateArray().Select(x => x.GetInt32()), prc);
        int cx = DesignRules.Complexity(L, prc);
        Assert.Equal(e.GetProperty("complexity").GetInt32(), cx);
        Assert.Equal(e.GetProperty("perf").GetInt64(), DesignRules.Perf(L, alts).Raw);

        // R-PS2m blocks at U = 1.
        int[] pu = DesignRules.Block(L, prc, airframe: false), af = DesignRules.Block(L, prc, airframe: true);
        Fx6 sumPu = DesignRules.BaseCostSum(L, pu), sumAf = DesignRules.BaseCostSum(L, af);
        int r = DesignRules.PuRank(L, alts), k = DesignRules.AfClass(L, alts);
        Assert.Equal(e.GetProperty("sum_PU").GetInt64(), sumPu.Raw);
        Assert.Equal(e.GetProperty("sum_AF").GetInt64(), sumAf.Raw);
        Assert.Equal(e.GetProperty("r_PU").GetInt32(), r);
        Assert.Equal(e.GetProperty("af_class").GetInt32(), k);
        Assert.Equal(e.GetProperty("PU").GetInt64(), (sumPu * Fx6.One * L.ProcScalePu[r - 1]).Raw);
        Assert.Equal(e.GetProperty("AF").GetInt64(), (sumAf * Fx6.One * L.ProcScaleAf[k - 1]).Raw);
        Fx6 part = ProductsMath.ProcessPart(sumPu, Fx6.One, L.ProcScalePu[r - 1], sumAf, Fx6.One, L.ProcScaleAf[k - 1]);
        Assert.Equal(e.GetProperty("proc_part").GetInt64(), part.Raw);
        Fx6 floor = ProductsMath.UnitCostFloor(L.MaterialsFloor[k - 1], L.HFloor[k - 1], L.WageKh, sumPu, L.ProcScalePu[r - 1], sumAf, L.ProcScaleAf[k - 1]);
        Assert.Equal(e.GetProperty("unit_cost_floor").GetInt64(), floor.Raw);
        Assert.Equal(e.GetProperty("upkeep").GetInt64(), ProductsMath.Upkeep(P.UpkeepShare, floor, cx, P.UpkeepQ10, L.Maintainability).Raw);

        // Work, defects and the gate by the airframe class.
        Assert.Equal(e.GetProperty("W_exp0").GetInt64(), ProductsMath.DesignWork(P.W0, cx, L.DesignScale[k - 1], Fx6.Zero, P.EB).Raw);
        Fx6 d0 = ProductsMath.InitialDefects(P.D0Factor, Fx6.One, cx, Fx6.Zero, P.EB);
        Assert.Equal(e.GetProperty("D0_novelty1_exp0").GetInt64(), d0.Raw);
        Assert.Equal(e.GetProperty("alloy_kg").GetInt64(), L.AltAlloyKg[alts[L.AlloySlot]].Raw / Fx6.Scale);
        Assert.Equal(e.GetProperty("d_gate_preliminary").GetInt64(),
            ProductsMath.PreliminaryGate(P.KGate, DRef, d0, P.TTestMin, P.TestRate, P.LambdaD, P.BetaQ10).Raw);
        Assert.Equal(e.GetProperty("gate_month_tests_only").GetInt32(), TestsOnlyMonth(d0, P.KGate));
        foreach ((string doctrine, long kp) in new[] { ("P1", 400_000L), ("P2", 700_000L), ("P3", 550_000L) })
            Assert.Equal(e.GetProperty("ai_launch_month").GetProperty(doctrine).GetInt32(), TestsOnlyMonth(d0, Fx6.FromRaw(kp)));

        // g_p and gen: the actor knows the nodes of generations <= g (product_class.md §2).
        int gp = DesignRules.PlatformGen(L, alts, L.KnownUpTo(gen));
        Assert.Equal(e.GetProperty("g_p").GetInt32(), gp);
        Assert.Equal(e.GetProperty("gen").GetInt32(), DesignRules.Gen(L, gp, alts));
    }

    // First month m of tests only with D_hidden <= k·D_ref and m >= t_test_min (reference gate_month).
    private static int TestsOnlyMonth(Fx6 d0, Fx6 k)
    {
        Fx6 hours = Fx6.Zero;
        for (int m = 1; m < 1000; m++)
        {
            hours += P.TestRate;
            Fx6 hidden = d0 - ProductsMath.DetectedTotal(hours, d0, P.LambdaD, P.BetaQ10);
            if (ProductsMath.GateOpen(hidden, k, DRef, m, P.TTestMin))
                return m;
        }
        return -1;
    }

    [Theory]
    [InlineData("A", new[] { "eng_g4", "hot_vim_ds_cooled", "comp_axial", "af_titanium", "ab_on" })]
    [InlineData("B", new[] { "eng_g3", "hot_vim_cast", "comp_axial", "af_milled", "ab_on" })]
    [InlineData("C", new[] { "eng_g2", "hot_sa2_forged", "comp_axial", "af_milled", "ab_on" })]
    public void ComponentSelection_Examples_MatchTheReference(string name, string[] ids)
    {
        JsonElement e = V24.GetProperty("cs_examples").GetProperty(name);
        int[] alts = [.. ids.Select(id => Alt(L, id))];
        int[] prc = DesignRules.PrcSet(L, alts);
        int[] pu = DesignRules.Block(L, prc, airframe: false), af = DesignRules.Block(L, prc, airframe: true);
        int r = DesignRules.PuRank(L, alts), k = DesignRules.AfClass(L, alts);
        Fx6 sumPu = DesignRules.BaseCostSum(L, pu), sumAf = DesignRules.BaseCostSum(L, af);
        Assert.Equal(e.GetProperty("sum_PU").GetInt64(), sumPu.Raw);
        Assert.Equal(e.GetProperty("sum_AF").GetInt64(), sumAf.Raw);
        Assert.Equal(e.GetProperty("r_PU").GetInt32(), r);
        Assert.Equal(e.GetProperty("af_class").GetInt32(), k);
        Assert.Equal(e.GetProperty("proc_part").GetInt64(), ProductsMath.ProcessPart(sumPu, Fx6.One, L.ProcScalePu[r - 1], sumAf, Fx6.One, L.ProcScaleAf[k - 1]).Raw);
        Assert.Equal(e.GetProperty("unit_cost_floor").GetInt64(),
            ProductsMath.UnitCostFloor(L.MaterialsFloor[k - 1], L.HFloor[k - 1], L.WageKh, sumPu, L.ProcScalePu[r - 1], sumAf, L.ProcScaleAf[k - 1]).Raw);
        Assert.Equal(e.GetProperty("alloy_kg").GetInt64(), L.AltAlloyKg[alts[L.AlloySlot]].Raw / Fx6.Scale);
    }

    [Fact]
    public void MixedCases_GenFollowsTheEngineGeneration_G49()
    {
        // The actor knows the nodes of generations <= 3 (G3 platform) or <= 4 (G2 platform with the G4 engine).
        (string Key, string[] Ids, int KnownUpTo)[] cases =
        [
            ("G3_platform_eng_g1", ["eng_g1", "hot_nicr_forged", "comp_axial", "af_titanium", "ab_on"], 3),
            ("G3_platform_eng_g2", ["eng_g2", "hot_nicr_forged", "comp_axial", "af_titanium", "ab_on"], 3),
            ("G2_platform_eng_g4", ["eng_g4", "hot_vim_ds_cooled", "comp_axial", "af_swept", "ab_on"], 4),
        ];
        foreach ((string key, string[] ids, int upTo) in cases)
        {
            int[] alts = [.. ids.Select(id => Alt(L, id))];
            int gp = DesignRules.PlatformGen(L, alts, L.KnownUpTo(upTo));
            JsonElement e = V24.GetProperty("gen_mixed").GetProperty(key);
            Assert.Equal(e.GetProperty("g_p").GetInt32(), gp);
            Assert.Equal(e.GetProperty("gen").GetInt32(), DesignRules.Gen(L, gp, alts));
        }
    }

    [Fact]
    public void EngineGenerationData_IsExplicit_AndWithinGmax()
    {
        for (int a = L.AltStart[L.BaseSlot]; a < L.AltStart[L.BaseSlot + 1]; a++)
            Assert.InRange(L.AltEngGen[a], 1, L.Generations);
        Assert.Equal([1, 2, 3, 4], L.AltEngGen.Skip(L.AltStart[L.BaseSlot]).Take(4).ToArray());
        Assert.Equal([1, 2, 3, 5], L.PlatformMinRank.ToArray());
    }

    [Fact]
    public void ValidatorPsi_MatchesTheReference()
    {
        JsonElement v = V24.GetProperty("validator_psi");
        int cMax = ProductsSystem.MaxComplexity(L);
        Assert.Equal(v.GetProperty("c_max").GetInt32(), cMax);
        Assert.Equal(v.GetProperty("psi_c_max_over_c_ref").GetInt64(), (P.PsiD * Fx6.FromInt(cMax) / Fx6.FromInt(L.CRef)).Raw);
        ProductsSystem.ValidatePsi(L, P);
        Assert.Throws<ArgumentException>(() => ProductsSystem.ValidatePsi(L, P with { PsiD = Fx6.FromRaw(800_000) }));
    }

    [Fact]
    public void Readiness_ByDRef_MatchesTheReference()
    {
        JsonElement r = V24.GetProperty("readiness");
        Assert.Equal(r.GetProperty("D_eq_D_ref").GetInt64(), ProductsMath.Readiness(Fx6.One, Fx6.One, DRef, DRef, P.PsiD).Raw);
        Assert.Equal(r.GetProperty("D_zero").GetInt64(), ProductsMath.Readiness(Fx6.One, Fx6.One, Fx6.Zero, DRef, P.PsiD).Raw);
        Assert.Equal(r.GetProperty("D_960_new_G3").GetInt64(), ProductsMath.Readiness(Fx6.One, Fx6.One, Fx6.FromInt(960), DRef, P.PsiD).Raw);
        Assert.Equal(r.GetProperty("half_paid_q09_D_370").GetInt64(),
            ProductsMath.Readiness(Fx6.FromRaw(500_000), Fx6.FromRaw(900_000), Fx6.FromInt(370), DRef, P.PsiD).Raw);
    }

    [Fact]
    public void DevScale_IsUiOnly_AndNegativeForANewComplexDesign_P10()
    {
        Assert.Equal(Fx6.One, ProductsMath.DevScale(Fx6.Zero, DRef));
        Assert.Equal(Fx6.Zero, ProductsMath.DevScale(DRef, DRef));
        Assert.True(ProductsMath.DevScale(Fx6.FromInt(960), DRef) < Fx6.Zero);   // D0 960 over D_ref 740
    }

    [Fact]
    public void Accidents_MatchTheReference()
    {
        (string Case, Fx6 Acc, int Units, Fx6 X)[] input =
        [
            ("forced_X_0.6_Dref", Fx6.Zero, 100, Fx6.FromRaw(600_000) * DRef),
            ("carry_fraction", Fx6.FromRaw(700_000), 100, Fx6.FromRaw(600_000) * DRef),
            ("no_excess", Fx6.Zero, 300, Fx6.Zero),
            ("cap_units", Fx6.FromRaw(5_500_000), 3, DRef),
        ];
        foreach (JsonElement e in V24.GetProperty("accidents").EnumerateArray())
        {
            var c = input.Single(i => i.Case == e.GetProperty("case").GetString());
            Fx6 units = Fx6.FromInt(c.Units);
            Fx6 acc = c.Acc + ProductsMath.AccidentLoss(units, P.FleetHoursPerUnit, P.AAcc, c.X, DRef);
            long n = ProductsMath.WholeLosses(acc, units);
            Assert.Equal(e.GetProperty("loss_acc_before_write_off").GetInt64(), acc.Raw);
            Assert.Equal(e.GetProperty("written_off").GetInt64(), n);
            Assert.Equal(e.GetProperty("loss_acc").GetInt64(), (acc - Fx6.FromInt(n)).Raw);
        }
    }

    [Fact]
    public void Retrofit_F9_MatchesTheReference()
    {
        Fx6 g1 = Fx6.FromRaw(V24.GetProperty("references").GetProperty("G1.superior").GetProperty("unit_cost_floor").GetInt64());
        Fx6 g3 = Fx6.FromRaw(V24.GetProperty("references").GetProperty("G3.superior").GetProperty("unit_cost_floor").GetInt64());
        (string Case, Fx6 Floor, int Pool, int Units, Fx6 Left)[] input =
        [
            ("money_enough", g3, 442, 60, Fx6.FromInt(2)),
            ("money_short", g3, 442, 60, Fx6.FromRaw(5_000)),
            ("pool_empty", g3, 0, 60, Fx6.FromRaw(50_000)),
            ("g1_small_floor_F9", g1, 442, 60, Fx6.FromInt(2)),
        ];
        foreach (JsonElement e in V24.GetProperty("retro").EnumerateArray())
        {
            var c = input.Single(i => i.Case == e.GetProperty("case").GetString());
            Fx6 fixN = Fx6.FromInt(58), pool = Fx6.FromInt(c.Pool), units = Fx6.FromInt(c.Units);
            if (c.Pool == 0)
            {
                // The system skips the retrofit branch: all fix_n defects are fixed, the money is untouched.
                Assert.Equal(e.GetProperty("fixed").GetInt64(), fixN.Raw);
                Assert.Equal(e.GetProperty("left").GetInt64(), c.Left.Raw);
                continue;
            }
            Fx6 cU = ProductsMath.RetrofitCost(c.Floor, P.RhoRetro, units, DRef);
            Fx6 fixedNow = ProductsMath.RetrofitFixed(fixN, pool, cU, c.Left, out Fx6 kPaid);
            Assert.Equal(e.GetProperty("c_u").GetInt64(), cU.Raw);
            Assert.Equal(e.GetProperty("k_paid").GetInt64(), kPaid.Raw);
            Assert.Equal(e.GetProperty("fixed").GetInt64(), fixedNow.Raw);
            Assert.Equal(e.GetProperty("retro_pool").GetInt64(), (pool - kPaid).Raw);
            Assert.Equal(e.GetProperty("left").GetInt64(), (c.Left - kPaid * cU).Raw);
        }
        foreach (JsonProperty g in V24.GetProperty("retro_c_u_per_1000").EnumerateObject())
        {
            Fx6 floor = Fx6.FromRaw(V24.GetProperty("references").GetProperty($"{g.Name}.superior").GetProperty("unit_cost_floor").GetInt64());
            Assert.Equal(g.Value.GetInt64(), ProductsMath.RetrofitCost(floor, P.RhoRetro, Fx6.FromInt(1000), DRef).Raw);
        }
    }

    [Fact]
    public void FleetValue_GammaAndAlloy_MatchTheReference()
    {
        JsonElement fv = V24.GetProperty("fv");
        (int N, long Readiness, long PerfRel)[] terms = [(300, 950_000, 1_000_000), (200, 900_000, 1_600_000), (100, 600_000, 2_600_000)];
        int i = 0;
        foreach (JsonElement t in fv.GetProperty("terms").EnumerateArray())
        {
            Assert.Equal(terms[i].N, t.GetProperty("n").GetInt32());
            Assert.Equal(t.GetProperty("fv").GetInt64(),
                ProductsMath.FleetValue(terms[i].N, Fx6.FromRaw(terms[i].Readiness), Fx6.FromRaw(terms[i].PerfRel), P.GammaQ10).Raw);
            i++;
        }
        foreach (JsonElement c in fv.GetProperty("n_alloy").EnumerateArray())
        {
            int nv = ProductsMath.AlloyLimitedCount(c.GetProperty("n_budget").GetInt32(), Fx6.FromRaw(c.GetProperty("supply").GetInt64()),
                Fx6.FromInt(c.GetProperty("alloy_kg").GetInt32()), P.HBuild, P.FvStep);
            Assert.Equal(c.GetProperty("n_v").GetInt32(), nv);
        }
    }
}
