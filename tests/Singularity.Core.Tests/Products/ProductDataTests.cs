using Singularity.Core.Data;
using Singularity.Core.Products;
using Singularity.Core.Tests.Data;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>Product inputs r3 in mod core (product_inputs.md §4, §5, §6, §9, §11; decision #78; spec §3.5 A18, A28, A29).</summary>
public class ProductDataTests : IDisposable
{
    private readonly TestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    [Theory]
    [InlineData(38, 600, 75)]
    [InlineData(53, 3700, 462)]
    [InlineData(55, 3450, 431)]
    [InlineData(56, 7500, 937)]
    [InlineData(42, 6750, 843)]
    [InlineData(44, 150, 18)]
    [InlineData(54, 1350, 168)]
    [InlineData(39, 225, 28)]
    [InlineData(40, 600, 75)]
    [InlineData(41, 1800, 225)]
    [InlineData(45, 225, 28)]
    [InlineData(46, 1200, 150)]
    [InlineData(49, 750, 93)]
    [InlineData(51, 2250, 281)]
    [InlineData(43, 3750, 468)]
    [InlineData(52, 5250, 656)]
    [InlineData(48, 450, 56)]
    [InlineData(50, 1200, 150)]
    [InlineData(57, 900, 112)]
    public void ProcessCosts_FollowRBC(int number, int labour, long baseRaw)
    {
        // R-BC r3: base_cost = ⌊labour·c_h⌋ raw with c_h = 1.25e-7 index/h, i.e. ⌊labour·0.125⌋ raw; n_p = 1 (R-NP).
        int k = Probe.ProcIndex(number);
        Assert.True(k >= 0);
        Assert.Equal(Fx6.FromInt(labour), Probe.ProcLabour[k]);
        Assert.Equal(Fx6.One, Probe.ProcN[k]);
        Assert.Equal(baseRaw, Probe.ProcBaseCost[k].Raw);
        Assert.Equal(labour * 125L / 1000, baseRaw);
    }

    [Fact]
    public void Class_HasNineteenCostRows_NoneForUnusedGearBearing()
    {
        Assert.Equal(19, Probe.ProcNumber.Length);
        Assert.Equal(-1, Probe.ProcIndex(47));
        Assert.Equal(175, Probe.HPm.Raw / Fx6.Scale);
        Assert.Equal(125, Probe.WageKh.Raw);
        Assert.Equal(Fx6.One, Probe.Maintainability);
        Assert.Equal(Fx6.FromInt(25), Probe.RRef);
    }

    [Theory]
    [InlineData(1, 58, 9320, 2330, 217, 290, 942_188, 1_000_000)]
    [InlineData(2, 59, 14760, 3690, 329, 670, 858_987, 1_110_000)]
    [InlineData(3, 60, 41960, 10490, 919, 2120, 1_021_118, 1_820_000)]
    [InlineData(4, 61, 125920, 31480, 2098, 2980, 2_597_030, 3_120_000)]
    public void Generations_MatchR3(int gen, int app, int hFirst, int hFloor, long materialsRaw, int alloyKg, long procScaleRaw, long designScaleRaw)
    {
        int i = gen - 1;
        Assert.Equal(app, Probe.GenApp[i]);
        Assert.Equal(Fx6.FromInt(hFirst), Probe.HFirst[i]);
        Assert.Equal(Fx6.FromInt(hFloor), Probe.HFloor[i]);
        Assert.Equal(materialsRaw, Probe.MaterialsFloor[i].Raw);
        Assert.Equal(Fx6.FromInt(alloyKg), Probe.AlloyKg[i]);
        Assert.Equal(procScaleRaw, Probe.ProcScale[i].Raw);
        Assert.Equal(designScaleRaw, Probe.DesignScale[i].Raw);
        Assert.Equal(1, Probe.CrewReq[i]);
        Assert.Equal(500, Probe.BureauEngineers[i]);
    }

    [Theory]
    [InlineData(1, 1382)]
    [InlineData(2, 2174)]
    [InlineData(3, 6163)]
    [InlineData(4, 17836)]
    public void UnitCostFloor_OfSuperiorDesigns_MatchesR3Check(int gen, long expectedRaw)
    {
        // product_inputs.md r3 §9.1: materials + h_floor·wage (A28) + proc_scale·Σ base_cost = 0.82 of the historical price.
        Span<int> alts = stackalloc int[Probe.SlotCount];
        Assert.True(DesignRules.Superior(Probe, gen, Probe.KnownUpTo(gen), alts));
        int[] prc = DesignRules.PrcSet(Probe, alts);
        Fx6 floor = ProductsMath.UnitCostFloor(Probe.MaterialsFloor[gen - 1], Probe.HFloor[gen - 1], Probe.WageKh,
            DesignRules.BaseCostSum(Probe, prc), Probe.ProcScale[gen - 1]);
        Assert.Equal(expectedRaw, floor.Raw);
    }

    [Fact]
    public void ReferenceVRef_IsLowerMedianOfG2_18750()
    {
        // Spec §3.5 A18, A29: v_p = r_ref·n_p·labour_p over the superior G2 prc_set (10 PRC); lower median 750·25.
        Assert.Equal(Fx6.FromInt(18_750), Probe.ReferenceVRef());
    }

    [Theory]
    [InlineData("USA", 92_300)]
    [InlineData("GBR", 22_059)]
    [InlineData("SUN", 21_136)]
    [InlineData("FRA", 6_461)]
    [InlineData("SWE", 2_215)]
    public void AlloyCapAtStart_FollowsRAC(string actor, long kg)
    {
        Assert.Equal(Fx6.FromInt(92_300), Scenario.AlloyRefKg);
        Assert.Equal(Fx6.FromInt(kg), Scenario.AlloyCapAtStart(Scenario.ActorIndex(actor)));
    }

    [Theory]
    [InlineData("      - {prc: s5.prc.light_alloy_rolling, n_p: 1, labour_hours: 600, base_cost: 0.000075}\n", "", "no cost row for 's5.prc.light_alloy_rolling'")]
    [InlineData("{prc: s5.prc.light_alloy_rolling,", "{prc: s6.prc.precision_gear_bearing,", "is in no prc_set of the class")]
    [InlineData("{gen: 2, app: s8.app.jet_fighter_g2,", "{gen: 3, app: s8.app.jet_fighter_g2,", "generations must be listed as 1, 2, ...")]
    [InlineData("{gen: 2, app: s8.app.jet_fighter_g2,", "{gen: 2, app: s8.app.jet_fighter_g3,", "is not an APP node of generation 2")]
    [InlineData("h_first: 9320, h_floor: 2330,", "h_first: 2000, h_floor: 2330,", "h_floor must not exceed h_first")]
    [InlineData("crew_req: 1, proc_scale: 0.942188", "crew_req: 1, proc_scale: 0", "proc_scale must be positive")]
    public void BrokenProductInputs_Fail(string from, string to, string message)
    {
        string dir = _mods.CopyCore((name, text) => name == "product_classes.yaml" ? TestMods.ReplaceOnce(text, from, to) : text);
        var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(dir));
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void ProductInputChange_ChangesDataHash()
    {
        string dir = _mods.CopyCore((name, text) =>
            name == "product_classes.yaml" ? TestMods.ReplaceOnce(text, "wage_kh: 0.000125", "wage_kh: 0.000126") : text);
        Assert.NotEqual(CoreData.DataHash, ModLoader.Load(dir).DataHash);
    }
}
