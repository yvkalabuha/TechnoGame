using Singularity.Core.Data;
using Singularity.Core.Knowledge;
using Singularity.Core.Products;
using Singularity.Core.Tests.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// TASK-0131 (train 2): the Core accepts data with six generations: product classes with generations 5-6, airframe class 5, airframe ranks 6-7,
/// pu_ranks 5-6 (test fixture, not mod data), and the node cost of generations 5 and 6.
/// </summary>
public class SixGenerationDataTests : IDisposable
{
    private readonly TestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    private const string Engines = "requires_nodes: [s8.eng.turbofan_g4]}";
    private const string Airframes = "requires_nodes: [s8.eng.mach2_airframe_g4, s5.cmp.titanium_alloy]}";
    private const string Prc = "prc_set: [s5.prc.light_alloy_rolling, s8.prc.stressed_skin_assembly, s8.prc.swept_wing_fabrication, s8.prc.machined_integral_skins, s5.prc.titanium_reduction_forging]";

    private GameData LoadSix() => ModLoader.Load(_mods.CopyCore((name, text) => name switch
    {
        "nodes.yaml" => text.TrimEnd() + @"
  - id: s8.app.jet_fighter_g5
    number: 70
    type: APP
    gen: 5
    discipline: aero
    cluster: AERO
    requires: [s8.app.jet_fighter_g4]
    hazard: none
    anchor: {year: 1990, source: ""fixture"", estimated: true}
  - id: s8.app.jet_fighter_g6
    number: 71
    type: APP
    gen: 6
    discipline: aero
    cluster: AERO
    requires: [s8.app.jet_fighter_g5]
    hazard: none
    anchor: {year: 2010, source: ""fixture"", estimated: true}
",
        "product_classes.yaml" => Edit(text),
        _ => text,
    }));

    private static string Edit(string text)
    {
        text = TestMods.ReplaceOnce(text, Engines, Engines + @"
          - {id: eng_g5, rank: 5, eng_gen: 5, perf_mult: 2.8, prc_set: [s8.prc.jet_engine_assembly_test, s5.prc.alloying_element_refining], requires_nodes: [s8.eng.turbofan_g4]}
          - {id: eng_g6, rank: 6, eng_gen: 6, perf_mult: 3.4, prc_set: [s8.prc.jet_engine_assembly_test, s5.prc.alloying_element_refining], requires_nodes: [s8.eng.turbofan_g4]}");
        text = TestMods.ReplaceOnce(text, Airframes, Airframes + $@"
          - {{id: af_g6, rank: 6, af_class: 5, perf_mult: 2.0, {Prc}, requires_nodes: [s8.eng.mach2_airframe_g4]}}
          - {{id: af_g7, rank: 7, af_class: 5, perf_mult: 2.2, {Prc}, requires_nodes: [s8.eng.mach2_airframe_g4]}}");
        text = TestMods.ReplaceOnce(text, "{rank: 4, proc_scale_pu: 0.827491}", @"{rank: 4, proc_scale_pu: 0.827491}
      - {rank: 5, proc_scale_pu: 1.2}
      - {rank: 6, proc_scale_pu: 1.2}");
        text = TestMods.ReplaceOnce(text, "proc_scale_af: 3.754003}", @"proc_scale_af: 3.754003}
      - {af_class: 5, h_first: 125920, h_floor: 31480, materials_floor: 0.002098, design_scale: 3.12, proc_scale_af: 3.754003}");
        return TestMods.ReplaceOnce(text, "{gen: 4, app: s8.app.jet_fighter_g4, crew_req: 1, platform_min_rank: 5}", @"{gen: 4, app: s8.app.jet_fighter_g4, crew_req: 1, platform_min_rank: 5}
      - {gen: 5, app: s8.app.jet_fighter_g5, crew_req: 1, platform_min_rank: 6}
      - {gen: 6, app: s8.app.jet_fighter_g6, crew_req: 1, platform_min_rank: 7}");
    }

    [Fact]
    public void ProductClassTable_Accepts_Generations5And6_AfClass5_AirframeRanks6And7_PuRanks5And6()
    {
        GameData six = LoadSix();
        ProductClassTable t = six.ProductClasses;
        IndustryLayoutFor(six, out var industry);
        ProductsLayout l = ProductsLayout.FromData(six.Nodes, t, "jet_fighter", industry, ProductTestData.Bureaus, six.Scenario("jet_slice_1945").StartYear, ProductTestData.Years, six.Scenario("jet_slice_1945").MaxDesigns);
        Assert.Equal(6, l.Generations);
        Assert.Equal(5, l.AltAfClass[l.AltId.IndexOf("af_g6")]);
        Assert.Equal(6, l.AltEngGen[l.AltId.IndexOf("eng_g6")]);
    }

    private static void IndustryLayoutFor(GameData d, out Singularity.Core.Industry.IndustryLayout industry) =>
        industry = Singularity.Core.Industry.IndustryLayout.FromData(d.Nodes, d.Scenario("jet_slice_1945"), ProductTestData.Categories, 64);

    [Fact]
    public void NodeCost_OfGenerations5And6_IsDefined_G7IsStillLoud()
    {
        var p = new KnowledgeParams();
        Assert.True(p.CostN(NodeType.APP, 5) > p.CostN(NodeType.APP, 4));
        Assert.True(p.CostN(NodeType.APP, 6) > p.CostN(NodeType.APP, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.CostN(NodeType.APP, 7));
    }

    [Fact]
    public void KnowledgeGraph_OfSixGenerationData_PricesTheNewNodes()
    {
        GameData six = LoadSix();
        KnowledgeGraph g = KnowledgeGraph.FromData(six.Nodes, six.Scenario("jet_slice_1945").Disciplines, new KnowledgeParams());
        Assert.True(g.RowOf(70) >= 0 && g.RowOf(71) >= 0);
        Assert.Equal(new KnowledgeParams().CostN(NodeType.APP, 5), g.CostN[g.RowOf(70)]);
    }
}
