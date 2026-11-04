using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Core.Tests.Data;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>TASK-0027 (T6c2, spec v0.15 §1.4 «Выбор двигателя в пресетах», #140) and the start-line validator of E1 (A32).</summary>
public class EngineStepDownTests : IDisposable
{
    private static readonly ProductsLayout L = Probe;
    private static readonly ProductsParams P = new();
    private readonly TestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    private static string[] Ids(ProductsLayout l, Span<int> alts) => AltIds(l, alts);

    [Fact]
    public void Producible_WhenNoEngineMeetsYProd_TakesTheNewestEngine_AndNotTheSteel()
    {
        // USA P2 knowing eng_g1…eng_g4 and the alloys; the engine processes (44, 54) are under y_prod for every engine.
        Fx6 Y(int p) => p is 44 or 54 ? Fx6.FromRaw(100_000) : Fx6.One;
        Span<int> alts = stackalloc int[L.SlotCount];
        Assert.True(DesignRules.Producible(L, 4, L.KnownUpTo(4), Y, P.YProd, null, alts));
        Assert.Equal("eng_g4", L.AltId[alts[L.BaseSlot]]);
        Assert.NotEqual("hot_steel_g0", L.AltId[alts[L.AlloySlot]]);
        Assert.True(DesignRules.EngineLimitsHold(L, alts));
    }

    [Fact]
    public void SunWithoutNode30_KnowingTurbojetG2_BothPresetsStepDownToEngG1WithSteel()
    {
        Func<int, bool> knows = n => L.KnownUpTo(2)(n) && n != 30;
        Span<int> sup = stackalloc int[L.SlotCount];
        Span<int> prod = stackalloc int[L.SlotCount];
        Assert.True(DesignRules.Superior(L, 2, knows, sup));
        Assert.Equal("eng_g1", L.AltId[sup[L.BaseSlot]]);
        Assert.Equal("hot_steel_g0", L.AltId[sup[L.AlloySlot]]);
        Fx6 Low(int p) => p is 44 or 54 ? Fx6.FromRaw(100_000) : Fx6.One;
        Assert.True(DesignRules.Producible(L, 2, knows, Low, P.YProd, null, prod));
        Assert.Equal("eng_g1", L.AltId[prod[L.BaseSlot]]);
        Assert.Equal("hot_steel_g0", L.AltId[prod[L.AlloySlot]]);
    }

    [Fact]
    public void DifferentPrcSetsOfEngines_ALowerEngineThatPassesYProd_IsTakenByProducible_NotTheNewest()
    {
        // Synthetic data: eng_g1 uses only the assembly process 54, the others also 44; 44 is under y_prod (#122 stays).
        string dir = _mods.CopyCore((name, text) => name != "product_classes.yaml" ? text :
            TestMods.ReplaceOnce(text, "{id: eng_g1, rank: 1, eng_gen: 1, perf_mult: 1.0, prc_set: [s8.prc.jet_engine_assembly_test, s5.prc.alloying_element_refining]",
                "{id: eng_g1, rank: 1, eng_gen: 1, perf_mult: 1.0, prc_set: [s8.prc.jet_engine_assembly_test]"));
        GameData data = ModLoader.Load(dir);
        IndustryLayout industry = IndustryLayout.FromData(data.Nodes, data.Scenario("jet_slice_1945"), Categories, 64);
        ProductsLayout l = ProductsLayout.FromData(data.Nodes, data.ProductClasses, "jet_fighter", industry, Bureaus, 1945, Years, 12);
        Fx6 Low(int p) => p == 44 ? Fx6.FromRaw(100_000) : Fx6.One;
        Span<int> alts = stackalloc int[l.SlotCount];
        Assert.True(DesignRules.Producible(l, 4, l.KnownUpTo(4), Low, P.YProd, null, alts));
        Assert.Equal("eng_g1", l.AltId[alts[l.BaseSlot]]);
        // Nothing passes: the newest engine again.
        Assert.True(DesignRules.Producible(l, 4, l.KnownUpTo(4), _ => Fx6.FromRaw(100_000), P.YProd, null, alts));
        Assert.Equal("eng_g4", l.AltId[alts[l.BaseSlot]]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void TheReferenceCompositions_AreUnchanged_ForFullKnowledgeAndFullYield(int gen)
    {
        Span<int> sup = stackalloc int[L.SlotCount];
        Span<int> prod = stackalloc int[L.SlotCount];
        Assert.True(DesignRules.Superior(L, gen, L.KnownUpTo(gen), sup));
        Assert.Equal(gen, L.AltRank[sup[L.BaseSlot]]);
        // G1: producible equals superior (F-PC3) and is not a second project; G2…G4 at full yield differ from superior in the lightened slot.
        bool built = DesignRules.Producible(L, gen, L.KnownUpTo(gen), _ => Fx6.One, P.YProd, null, prod);
        Assert.Equal(gen > 1, built);
        if (built)
            Assert.Equal(gen, L.AltRank[prod[L.BaseSlot]]);
    }

    // ---- E1: validator of the start state

    [Fact]
    public void TwoStartLinesOfOneActor_AreRejected()
    {
        var start = new ProductsStart(L, Scenario.ActorCount);
        for (int d = 0; d < 2; d++)
        {
            start.Stage[d] = DesignStage.Series;
            start.PlatformGen[d] = 1;
        }
        var table = new Table(Scenario.ActorCount);
        var ex = Assert.Throws<ArgumentException>(() => new ProductsSystem(table, L, start, P, new ProductsHarness().Inputs, new IndustryMailbox(1)));
        Assert.Contains("start lines", ex.Message);
    }

    [Fact]
    public void AProducibleStartFleet_IsRejected()
    {
        string dir = _mods.CopyCore((name, text) => name != "scenario_1945.yaml" ? text : text.Replace("variant: superior", "variant: producible"));
        GameData data = ModLoader.Load(dir);
        IndustryLayout industry = IndustryLayout.FromData(data.Nodes, data.Scenario("jet_slice_1945"), Categories, 64);
        ProductsLayout l = ProductsLayout.FromData(data.Nodes, data.ProductClasses, "jet_fighter", industry, Bureaus, 1945, Years, 12);
        Assert.Throws<ArgumentException>(() => ProductsStart.FromScenario(data.Scenario("jet_slice_1945"), data.Nodes, data.ProductClasses, l));
    }

    [Fact]
    public void StartLine_IsHeldByTheNewestStartProject_LineEIsItsFleet_OthersAreInStageВ_AndTheScenarioLoads()
    {
        // GBR knows every node of generations ≤ 2 and has a G1 fleet (100) and a G2 fleet (10): the G2 project holds the line.
        string known = string.Join(", ", Enumerable.Range(0, CoreData.Nodes.Count).Where(i => CoreData.Nodes.Gen[i] <= 2).Select(i => CoreData.Nodes.Id[i]));
        string dir = _mods.CopyCore((name, text) =>
        {
            if (name != "scenario_1945.yaml")
                return text;
            int gbr = text.IndexOf("id: GBR", StringComparison.Ordinal);
            int k = text.IndexOf("known: [", gbr, StringComparison.Ordinal);
            int kEnd = text.IndexOf(']', k);
            text = text[..(k + 8)] + known + text[kEnd..];
            return TestMods.ReplaceOnce(text, "fleet:\n          - {product_class: jet_fighter, gen: 1, variant: superior, count: 100, cohort_year: 1945}\n        trophy_engineers: 100\n        estimated: []\n      - id: SUN",
                "fleet:\n          - {product_class: jet_fighter, gen: 1, variant: superior, count: 100, cohort_year: 1945}\n          - {product_class: jet_fighter, gen: 2, variant: superior, count: 10, cohort_year: 1945}\n        trophy_engineers: 100\n        estimated: []\n      - id: SUN");
        });
        GameData data = ModLoader.Load(dir);
        ScenarioData scenario = data.Scenario("jet_slice_1945");
        IndustryLayout industry = IndustryLayout.FromData(data.Nodes, scenario, Categories, 64);
        ProductsLayout l = ProductsLayout.FromData(data.Nodes, data.ProductClasses, "jet_fighter", industry, Bureaus, 1945, Years, scenario.MaxDesigns);
        ProductsStart start = ProductsStart.FromScenario(scenario, data.Nodes, data.ProductClasses, l);
        int gbrRow = scenario.ActorIndex("GBR");
        int d1 = gbrRow * l.Designs, d2 = gbrRow * l.Designs + 1;
        Assert.Equal(1, start.PlatformGen[d1]);
        Assert.Equal(2, start.PlatformGen[d2]);
        Assert.Equal(DesignStage.InService, start.Stage[d1]);
        Assert.Equal(DesignStage.Series, start.Stage[d2]);
        Assert.Equal(Fx6.Zero, start.LineE[d1]);
        Assert.Equal(Fx6.FromInt(10), start.LineE[d2]);
    }
}
