using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>Presets «superior»/«producible» (contract §4.4a, COMPONENT_SELECTION §6; spec §1.4; product_class.md §2, F-PC1–3), platform threshold, g_p and gen.</summary>
public class DesignRulesTests
{
    private static readonly ProductsLayout L = Probe;

    [Theory]
    // product_class.md §2: every node known, every yield ≥ y_prod; perf to 4 digits from the table.
    [InlineData(2, false, new[] { "eng_g2", "hot_sa2_forged", "comp_axial", "af_swept", "ab_on" }, 37, 2_458_527)]
    [InlineData(2, true, new[] { "eng_g2", "hot_nicr_forged", "comp_axial", "af_swept", "ab_on" }, 37, 2_235_025)]
    [InlineData(4, false, new[] { "eng_g4", "hot_vim_ds_cooled", "comp_axial", "af_mach2", "ab_on" }, 49, 7_593_795)]
    [InlineData(4, true, new[] { "eng_g4", "hot_vim_cast", "comp_axial", "af_mach2", "ab_on" }, 48, 6_546_375)]
    public void AllKnown_GivesReferenceVariants(int gen, bool producible, string[] expected, int complexity, long perfRaw)
    {
        Span<int> alts = stackalloc int[L.SlotCount];
        bool ok = producible
            ? DesignRules.Producible(L, gen, L.KnownUpTo(gen), _ => Fx6.One, Fx6.FromRaw(500_000), null, alts)
            : DesignRules.Superior(L, gen, L.KnownUpTo(gen), alts);
        Assert.True(ok);
        Assert.Equal(expected, AltIds(L, alts));
        Assert.Equal(complexity, DesignRules.Complexity(L, DesignRules.PrcSet(L, alts)));
        Assert.Equal(perfRaw, DesignRules.Perf(L, alts).Raw);
    }

    [Fact]
    public void G3_KnowingGenerationsUpTo3_MatchesReferenceTable()
    {
        // product_class.md §2 assumes the actor knows the nodes of generations ≤ g only.
        Func<int, bool> Knows = L.KnownUpTo(3);
        Span<int> sup = stackalloc int[L.SlotCount], tech = stackalloc int[L.SlotCount];
        Assert.True(DesignRules.Superior(L, 3, Knows, sup));
        Assert.Equal(["eng_g3", "hot_vim_cast", "comp_axial", "af_titanium", "ab_on"], AltIds(L, sup));
        Assert.Equal(4_496_500, DesignRules.Perf(L, sup).Raw);
        Assert.True(DesignRules.Producible(L, 3, Knows, _ => Fx6.One, Fx6.FromRaw(500_000), null, tech));
        Assert.Equal(["eng_g3", "hot_sa2_forged", "comp_axial", "af_titanium", "ab_on"], AltIds(L, tech));
        Assert.Equal(46, DesignRules.Complexity(L, DesignRules.PrcSet(L, tech)));
    }

    [Fact]
    public void G1_HasOneVariant_FPC3()
    {
        // USA at t0: nothing lighter in any slot, «технологичный» = «превосходящий» (F-PC3).
        var known = Scenario.KnownOf(Scenario.ActorIndex("USA")).ToArray().Select(r => CoreData.Nodes.Number[r]).ToHashSet();
        Span<int> sup = stackalloc int[L.SlotCount], tech = stackalloc int[L.SlotCount];
        Assert.True(DesignRules.Superior(L, 1, known.Contains, sup));
        Assert.Equal(["eng_g1", "hot_nicr_forged", "comp_centrifugal", "af_straight", "ab_none"], AltIds(L, sup));
        Assert.False(DesignRules.Producible(L, 1, known.Contains, _ => Fx6.One, Fx6.FromRaw(500_000), null, tech));
        Assert.Equal(AltIds(L, sup), AltIds(L, tech));
    }

    [Fact]
    public void Producible_TakesTheBestSlotAlternativeThatTheSiteCanMake()
    {
        // Superalloy melt (40) at yield 0.3 < y_prod: the hot section drops to hot_nicr_forged, sets differ, no lightening.
        Span<int> tech = stackalloc int[L.SlotCount];
        Fx6 Yield(int p) => p == 40 ? Fx6.FromRaw(300_000) : Fx6.One;
        Assert.True(DesignRules.Producible(L, 2, L.KnownUpTo(2), Yield, Fx6.FromRaw(500_000), null, tech));
        Assert.Equal(["eng_g2", "hot_nicr_forged", "comp_axial", "af_swept", "ab_on"], AltIds(L, tech));
    }

    [Fact]
    public void Superior_HasNoEngineOfTheGenerationRequirement_81()
    {
        // #81: no generation cap and no «engine >= g» rule: without turbojet_g2 (19) the G2 airframe still gets a design
        // (the best known engine is the G1 one); gen is then bounded by eng_gen + 1.
        Span<int> alts = stackalloc int[L.SlotCount];
        Assert.True(DesignRules.Superior(L, 2, n => n != 19 && n != 23 && n != 28 && L.KnownUpTo(2)(n), alts));
        Assert.Equal("eng_g1", L.AltId[alts[L.BaseSlot]]);
        Assert.Equal(2, DesignRules.Gen(L, DesignRules.PlatformGen(L, alts, L.KnownUpTo(2)), alts));
    }

    [Fact]
    public void Superior_FailsBelowThePlatformThreshold()
    {
        // G4 needs airframe rank 5 (af_mach2, node 29): with the generation-4 nodes except 29 known, the best airframe is rank 4.
        Span<int> alts = stackalloc int[L.SlotCount];
        Assert.False(DesignRules.Superior(L, 4, n => n != 29 && L.KnownUpTo(4)(n), alts));
        Assert.True(DesignRules.Superior(L, 3, n => n != 29 && L.KnownUpTo(4)(n), alts));
    }

    [Fact]
    public void Producible_Admitted_TakesTheBestAdmittedAlternative()
    {
        // L_P (§6.1, T-13): the producible preset over admitted alternatives only (yield > 0), else the next lower.
        Span<int> alts = stackalloc int[L.SlotCount];
        Func<int, Fx6> yield = p => p is 41 or 56 ? Fx6.Zero : Fx6.One;
        Assert.True(DesignRules.Producible(L, 2, L.KnownUpTo(2), yield, Fx6.FromRaw(500_000), null, alts,
            alt => L.PrcOf(alt).ToArray().All(p => yield(p) > Fx6.Zero)));
        Assert.Equal(["eng_g2", "hot_sa2_forged", "comp_axial", "af_swept", "ab_on"], AltIds(L, alts));
        // A slot without an admitted alternative: no project.
        Assert.False(DesignRules.Producible(L, 2, L.KnownUpTo(2), yield, Fx6.FromRaw(500_000), null, alts, alt => L.AltId[alt] != "eng_g1" && L.AltId[alt] != "eng_g2"));
    }

    [Fact]
    public void Producible_AlloyOk_SkipsAnAlloyShortHotSection()
    {
        // Doctrine P2 (COMPONENT_SELECTION §6): alloy_ok(h) false for the VIM section → the next lower hot section.
        Span<int> alts = stackalloc int[L.SlotCount];
        Assert.True(DesignRules.Producible(L, 3, L.KnownUpTo(3), _ => Fx6.One, Fx6.FromRaw(500_000), alt => L.AltId[alt] != "hot_vim_cast", alts));
        Assert.Equal("hot_sa2_forged", L.AltId[alts[L.AlloySlot]]);
    }

    [Theory]
    // Platform generation from the airframe rank and the known APP nodes (contract §4.4a): thresholds 1 / 2 / 3 / 5.
    [InlineData("af_straight", 4, 1)]
    [InlineData("af_swept", 4, 2)]
    [InlineData("af_milled", 4, 3)]
    [InlineData("af_titanium", 4, 3)]
    [InlineData("af_mach2", 4, 4)]
    [InlineData("af_mach2", 2, 2)]
    public void PlatformGen_FollowsRankAndKnownApp(string airframe, int knownUpTo, int expected)
    {
        int[] alts = [Alt(L, "eng_g1"), Alt(L, "hot_nicr_forged"), Alt(L, "comp_centrifugal"), Alt(L, airframe), Alt(L, "ab_none")];
        Assert.Equal(expected, DesignRules.PlatformGen(L, alts, L.KnownUpTo(knownUpTo)));
    }

    [Fact]
    public void Novelty_IsShareOfSlotsNewToTheActor()
    {
        int[] g1 = [Alt(L, "eng_g1"), Alt(L, "hot_nicr_forged"), Alt(L, "comp_centrifugal"), Alt(L, "af_straight"), Alt(L, "ab_none")];
        int[] g2 = [Alt(L, "eng_g2"), Alt(L, "hot_nicr_forged"), Alt(L, "comp_axial"), Alt(L, "af_swept"), Alt(L, "ab_on")];
        Assert.Equal(Fx6.FromRaw(800_000), DesignRules.Novelty(L, g2, g1));
        Assert.Equal(Fx6.One, DesignRules.Novelty(L, g2, []));
        Assert.Equal(Fx6.Zero, DesignRules.Novelty(L, g1, g1));
    }
}
