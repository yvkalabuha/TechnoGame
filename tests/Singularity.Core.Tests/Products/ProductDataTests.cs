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
    [InlineData(1, 58, 1, 1)]
    [InlineData(2, 59, 1, 2)]
    [InlineData(3, 60, 1, 3)]
    [InlineData(4, 61, 1, 5)]
    public void Generations_HaveAppCrewAndPlatformThreshold(int gen, int app, int crew, int platformMinRank)
    {
        int i = gen - 1;
        Assert.Equal(app, Probe.GenApp[i]);
        Assert.Equal(crew, Probe.CrewReq[i]);
        Assert.Equal(platformMinRank, Probe.PlatformMinRank[i]);
    }

    [Theory]
    [InlineData(1, 9320, 2330, 217, 1_000_000, 1_253_259)]
    [InlineData(2, 14760, 3690, 329, 1_110_000, 1_086_777)]
    [InlineData(3, 41960, 10490, 919, 1_820_000, 1_098_982)]
    [InlineData(4, 125920, 31480, 2098, 3_120_000, 3_754_003)]
    public void AirframeClasses_MatchProductInputs14(int afClass, int hFirst, int hFloor, long materialsRaw, long designScaleRaw, long procScaleAfRaw)
    {
        int i = afClass - 1;
        Assert.Equal(Fx6.FromInt(hFirst), Probe.HFirst[i]);
        Assert.Equal(Fx6.FromInt(hFloor), Probe.HFloor[i]);
        Assert.Equal(materialsRaw, Probe.MaterialsFloor[i].Raw);
        Assert.Equal(designScaleRaw, Probe.DesignScale[i].Raw);
        Assert.Equal(procScaleAfRaw, Probe.ProcScaleAf[i].Raw);
    }

    [Fact]
    public void PuScales_AreTheMonotoneEnvelope_R_PS2m()
    {
        Assert.Equal([515_528, 515_528, 827_491, 827_491], Probe.ProcScalePu.Select(x => x.Raw));
        Assert.Equal(37, Probe.CRef);
        Assert.Equal(12, Scenario.MaxDesigns);
    }

    [Fact]
    public void AlternativeFields_EngGen_AfClass_AlloyKg()
    {
        Assert.Equal([1, 2, 3, 4], Enumerable.Range(0, 4).Select(i => Probe.AltEngGen[Alt(Probe, $"eng_g{i + 1}")]));
        Assert.Equal([1, 2, 3, 3, 4],
            new[] { "af_straight", "af_swept", "af_milled", "af_titanium", "af_mach2" }.Select(id => Probe.AltAfClass[Alt(Probe, id)]));
        Assert.Equal([290, 670, 2120, 2980],
            new[] { "hot_nicr_forged", "hot_sa2_forged", "hot_vim_cast", "hot_vim_ds_cooled" }.Select(id => (int)(Probe.AltAlloyKg[Alt(Probe, id)].Raw / Fx6.Scale)));
        Assert.Equal(Probe.SlotOf(Alt(Probe, "hot_vim_cast")), Probe.AlloySlot);
        Assert.Equal(Probe.SlotOf(Alt(Probe, "af_mach2")), Probe.AirframeSlot);
    }

    [Theory]
    [InlineData(1, 1382)]
    [InlineData(2, 2174)]
    [InlineData(3, 6163)]
    [InlineData(4, 17836)]
    public void UnitCostFloor_OfSuperiorDesigns_MatchesProductInputs91(int gen, long expectedRaw)
    {
        // product_inputs.md r4 §14.6: R-PS2m reproduces the §9.1 references exactly.
        Span<int> alts = stackalloc int[Probe.SlotCount];
        Assert.True(DesignRules.Superior(Probe, gen, Probe.KnownUpTo(gen), alts));
        int[] prc = DesignRules.PrcSet(Probe, alts);
        int k = DesignRules.AfClass(Probe, alts), r = DesignRules.PuRank(Probe, alts);
        Fx6 floor = ProductsMath.UnitCostFloor(Probe.MaterialsFloor[k - 1], Probe.HFloor[k - 1], Probe.WageKh,
            DesignRules.BaseCostSum(Probe, DesignRules.Block(Probe, prc, airframe: false)), Probe.ProcScalePu[r - 1],
            DesignRules.BaseCostSum(Probe, DesignRules.Block(Probe, prc, airframe: true)), Probe.ProcScaleAf[k - 1]);
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
    [InlineData("proc_scale_af: 1.253259", "proc_scale_af: 0", "proc_scale_af must be positive")]
    [InlineData("{af_class: 2,", "{af_class: 3,", "af_classes must be listed as 1, 2, ...")]
    [InlineData("{rank: 2, proc_scale_pu: 0.515528}", "{rank: 3, proc_scale_pu: 0.515528}", "pu_ranks must be listed as 1, 2, ...")]
    [InlineData("{id: eng_g1, rank: 1, eng_gen: 1,", "{id: eng_g1, rank: 1, eng_gen: 5,", "eng_gen of an engine alternative must be in 1..4 (G_max)")]
    [InlineData("{id: eng_g1, rank: 1, eng_gen: 1,", "{id: eng_g1, rank: 1, eng_gen: 0,", "eng_gen of an engine alternative must be in 1..4 (G_max)")]
    [InlineData("{id: af_swept, rank: 2, af_class: 2,", "{id: af_swept, rank: 2, af_class: 9,", "af_class of an airframe alternative must be in 1..4")]
    [InlineData("{id: comp_axial, rank: 2,", "{id: comp_axial, rank: 2, eng_gen: 2,", "eng_gen is only allowed in the engine (base) slot")]
    [InlineData("{id: ab_on, rank: 2,", "{id: ab_on, rank: 2, af_class: 1,", "af_class is only allowed in the airframe slot")]
    [InlineData("{id: hot_sa2_forged, rank: 2, alloy_kg: 670,", "{id: hot_sa2_forged, rank: 2,", "alloy_kg must be on all or none alternatives of a slot")]
    [InlineData("      - id: airframe\n        block: af\n", "      - id: airframe\n        block: pu\n", "needs exactly one airframe (block af) slot")]
    [InlineData("      - id: engine\n        block: pu\n", "      - id: engine\n        block: af\n", "the base (engine) slot belongs to the PU block")]
    [InlineData("      - id: engine\n        block: pu\n", "      - id: engine\n        block: xx\n", "block must be 'pu' or 'af'")]
    [InlineData("    c_ref: 37\n", "    c_ref: 0\n", "c_ref must be at least 1")]
    [InlineData("platform_min_rank: 5}", "platform_min_rank: 0}", "platform_min_rank must be at least 1")]
    public void BrokenProductInputs_Fail(string from, string to, string message)
    {
        string dir = _mods.CopyCore((name, text) => name == "product_classes.yaml" ? TestMods.ReplaceOnce(text, from, to) : text);
        var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(dir));
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void OverlappingBlocks_AreRejected()
    {
        // A PRC of the airframe also in the engine slot: R-PS2m needs disjoint PU and AF blocks.
        string dir = _mods.CopyCore((name, text) => name == "product_classes.yaml"
            ? TestMods.ReplaceOnce(text, "{id: ab_on, rank: 2, perf_mult: 1.15, prc_set: [s8.prc.afterburner_fabrication]",
                "{id: ab_on, rank: 2, perf_mult: 1.15, prc_set: [s8.prc.afterburner_fabrication, s5.prc.light_alloy_rolling]")
            : text);
        var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(dir));
        Assert.Contains("both the PU and the AF block", e.Message);
    }

    [Fact]
    public void MaxDesignsBelowOne_IsRejected()
    {
        string dir = _mods.CopyCore((name, text) => name == "scenario_1945.yaml" ? TestMods.ReplaceOnce(text, "max_designs: 12", "max_designs: 0") : text);
        var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(dir));
        Assert.Contains("max_designs must be at least 1", e.Message);
    }

    [Fact]
    public void ProductInputChange_ChangesDataHash()
    {
        string dir = _mods.CopyCore((name, text) =>
            name == "product_classes.yaml" ? TestMods.ReplaceOnce(text, "wage_kh: 0.000125", "wage_kh: 0.000126") : text);
        Assert.NotEqual(CoreData.DataHash, ModLoader.Load(dir).DataHash);
    }
}
