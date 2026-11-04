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
        if (DesignRules.Producible(L, gen, L.KnownUpTo(gen), _ => Fx6.One, P.YProd, null, prod))
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
    public void StartLine_IsHeldByTheNewestStartProject_AndLineEIsTheFleet()
    {
        // A scenario with a G1 and a G2 fleet of one actor is outside the D1 data; the rule is checked on the start object itself.
        ProductsStart start = ProductsStart.FromScenario(Scenario, CoreData.Nodes, CoreData.ProductClasses, L);
        Assert.Equal(DesignStage.Series, start.Stage[0]);
        Assert.Equal(Fx6.FromInt(100), start.LineE[0]);
        int lines = 0;
        for (int i = 0; i < start.Stage.Length; i++)
            lines += start.Stage[i] == DesignStage.Series ? 1 : 0;
        Assert.Equal(2, lines);   // USA and GBR
    }
}
