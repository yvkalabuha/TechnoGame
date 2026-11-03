using System.Text.Json;
using Singularity.Core.Data;
using Singularity.Core.Products;
using Singularity.Core.Tests.Data;
using Singularity.Core.Tests.Knowledge;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// TASK-0026 (D1, spec v0.14 §1.4 «Поля альтернатив v0.14»): <c>max_eng_gen</c>, <c>alloy_free</c>, <c>upkeep_mult</c>, <c>substitute</c> in the
/// rules (presets, <c>compose_design</c>, <c>derive</c>), in the formulas (<c>upkeep</c>, <c>FV</c>, L_P and the purchases of step 7) and in the data validator.
/// </summary>
public class SubstituteTests : IDisposable
{
    private const int Usa = 0, Gbr = 1;
    private static readonly ProductsLayout L = Probe;
    private static readonly ProductsParams P = new();
    private readonly TestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    private static Func<int, bool> KnownUpToWithout(int gen, params int[] hidden) => n => L.KnownUpTo(gen)(n) && !hidden.Contains(n);

    private static string[] Ids(Span<int> alts) => AltIds(L, alts);

    // ---- the reference of the steel G1 (products_ref.py, spec §1.4 «Числа на бумаге»)

    [Fact]
    public void SteelG1_MatchesTheReference_AndThePaperNumbers()
    {
        JsonElement e = JsonDocument.Parse(File.ReadAllText(Path.Combine(JetReference.OutDir, "products.json")))
            .RootElement.GetProperty("v24").GetProperty("steel_g1");
        int[] alts = [.. e.GetProperty("alts").EnumerateArray().Select(x => Alt(L, x.GetString()!))];
        int[] prc = DesignRules.PrcSet(L, alts);
        Assert.Equal(e.GetProperty("prc_set").EnumerateArray().Select(x => x.GetInt32()), prc);
        int cx = DesignRules.Complexity(L, prc);
        Assert.Equal(20, cx);
        Assert.Equal(e.GetProperty("complexity").GetInt32(), cx);
        Assert.Equal(900_000, DesignRules.Perf(L, alts).Raw);

        int[] pu = DesignRules.Block(L, prc, airframe: false), af = DesignRules.Block(L, prc, airframe: true);
        Fx6 sumPu = DesignRules.BaseCostSum(L, pu), sumAf = DesignRules.BaseCostSum(L, af);
        int r = DesignRules.PuRank(L, alts), k = DesignRules.AfClass(L, alts);
        Assert.Equal(1, r);   // rank 0 of the substitute does not lower r_PU
        Fx6 floor = ProductsMath.UnitCostFloor(L.MaterialsFloor[k - 1], L.HFloor[k - 1], L.WageKh, sumPu, L.ProcScalePu[r - 1], sumAf, L.ProcScaleAf[k - 1]);
        Assert.Equal(1324, floor.Raw);
        Assert.Equal(e.GetProperty("unit_cost_floor").GetInt64(), floor.Raw);

        Fx6 m = DesignRules.UpkeepMult(L, alts);
        Assert.Equal(Fx6.FromRaw(2_500_000), m);
        Assert.Equal(e.GetProperty("upkeep_base").GetInt64(), ProductsMath.Upkeep(P.UpkeepShare, floor, cx, P.UpkeepQ10, L.Maintainability).Raw);
        Fx6 upkeep = ProductsMath.Upkeep(P.UpkeepShare, floor, cx, P.UpkeepQ10, L.Maintainability, m);
        Assert.Equal(57, upkeep.Raw);
        Assert.Equal(e.GetProperty("upkeep").GetInt64(), upkeep.Raw);

        Assert.True(DesignRules.AlloyFree(L, alts));
        foreach (JsonElement c in e.GetProperty("n_alloy_free").EnumerateArray())
            Assert.Equal(c.GetProperty("n_v").GetInt32(), ProductsMath.AlloyFreeCount(c.GetProperty("n_budget").GetInt32(), P.FvStep));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void UpkeepMultOne_IsBitForBitTheOldUpkeep_ForTheEightReferenceCompositions(int gen)
    {
        Span<int> alts = stackalloc int[L.SlotCount];
        foreach (bool producible in new[] { false, true })
        {
            if (!(producible ? DesignRules.Producible(L, gen, L.KnownUpTo(gen), _ => Fx6.One, P.YProd, null, alts)
                : DesignRules.Superior(L, gen, L.KnownUpTo(gen), alts)))
                continue;   // G1 producible = superior (F-PC3): one project
            Assert.Equal(Fx6.One, DesignRules.UpkeepMult(L, alts));
            Fx6 floor = ProductsMath.UnitCostFloor(L.MaterialsFloor[DesignRules.AfClass(L, alts) - 1], L.HFloor[DesignRules.AfClass(L, alts) - 1], L.WageKh,
                DesignRules.BaseCostSum(L, DesignRules.Block(L, DesignRules.PrcSet(L, alts), airframe: false)), L.ProcScalePu[DesignRules.PuRank(L, alts) - 1],
                DesignRules.BaseCostSum(L, DesignRules.Block(L, DesignRules.PrcSet(L, alts), airframe: true)), L.ProcScaleAf[DesignRules.AfClass(L, alts) - 1]);
            int cx = DesignRules.Complexity(L, DesignRules.PrcSet(L, alts));
            Assert.Equal(ProductsMath.Upkeep(P.UpkeepShare, floor, cx, P.UpkeepQ10, L.Maintainability),
                ProductsMath.Upkeep(P.UpkeepShare, floor, cx, P.UpkeepQ10, L.Maintainability, DesignRules.UpkeepMult(L, alts)));
        }
    }

    [Fact]
    public void ReferenceCompositions_AreUnchanged_TheSubstituteNeverEntersThem()
    {
        // product_class.md §2: with every node of generations <= g known and every yield 1 neither preset takes the substitute.
        Span<int> alts = stackalloc int[L.SlotCount];
        for (int gen = 1; gen <= 4; gen++)
        {
            Assert.True(DesignRules.Superior(L, gen, L.KnownUpTo(gen), alts));
            Assert.DoesNotContain("hot_steel_g0", Ids(alts));
            bool own = DesignRules.Producible(L, gen, L.KnownUpTo(gen), _ => Fx6.One, P.YProd, null, alts);
            Assert.Equal(gen > 1, own);            // G1: lightening finds nothing but the substitute, which is skipped: F-PC3
            if (own)
                Assert.DoesNotContain("hot_steel_g0", Ids(alts));
        }
    }

    // ---- max_eng_gen, the engine descent, the substitute in the presets

    [Fact]
    public void EngineLimit_Superior_StepsTheEngineDown_WhenTheAlloyIsUnknown()
    {
        Span<int> alts = stackalloc int[L.SlotCount];
        Func<int, bool> knows = KnownUpToWithout(2, 30);   // turbojet_g2 known, nickel alloy unknown
        Assert.True(DesignRules.Superior(L, 2, knows, alts));
        Assert.Equal(["eng_g1", "hot_steel_g0", "comp_axial", "af_swept", "ab_on"], Ids(alts));
        Assert.True(DesignRules.EngineLimitsHold(L, alts));
        // eng_g2 with the steel is refused by the composition rule, eng_g1 with the steel is not.
        int[] bad = [Alt(L, "eng_g2"), Alt(L, "hot_steel_g0"), Alt(L, "comp_axial"), Alt(L, "af_swept"), Alt(L, "ab_on")];
        Assert.False(DesignRules.EngineLimitsHold(L, bad));
        Assert.True(DesignRules.Available(L, Alt(L, "hot_steel_g0"), knows));
        Assert.False(DesignRules.AvailableWith(L, Alt(L, "hot_steel_g0"), Alt(L, "eng_g2"), knows));
    }

    [Fact]
    public void EngineLimit_Producible_StepsDown_AndAnActorWithoutAnyHotSectionHasNoProject()
    {
        Span<int> alts = stackalloc int[L.SlotCount];
        Func<int, bool> knows = KnownUpToWithout(2, 30);
        Assert.True(DesignRules.Producible(L, 2, knows, _ => Fx6.One, P.YProd, null, alts));
        Assert.Equal("eng_g1", L.AltId[alts[L.BaseSlot]]);
        Assert.Equal("hot_steel_g0", L.AltId[alts[L.AlloySlot]]);
        // No substitute known either (PRC 47 unknown): nothing fits the hot section at any engine.
        Assert.False(DesignRules.Superior(L, 2, KnownUpToWithout(2, 30, 47), alts));
        Assert.False(DesignRules.Producible(L, 2, KnownUpToWithout(2, 30, 47), _ => Fx6.One, P.YProd, null, alts));
    }

    [Fact]
    public void OnlySteel_IsTheProject_ForBothPresets_EvenWithoutF_PC3()
    {
        // SUN at the start: the alloy chain unknown, the substitute the only hot section.
        Func<int, bool> sun = KnownUpToWithout(1, 30, 33, 39, 45, 49);
        Span<int> sup = stackalloc int[L.SlotCount];
        Span<int> prod = stackalloc int[L.SlotCount];
        Assert.True(DesignRules.Superior(L, 1, sun, sup));
        Assert.Equal(["eng_g1", "hot_steel_g0", "comp_centrifugal", "af_straight", "ab_none"], Ids(sup));
        // producible equals superior (F-PC3) for an actor with a full choice, but here it is the same single project: the result is false, superior is used.
        Assert.False(DesignRules.Producible(L, 1, sun, _ => Fx6.One, P.YProd, null, prod));
        // Yields far under y_prod do not turn the only candidate into «no project» for the fallback.
        Assert.False(DesignRules.Producible(L, 1, sun, _ => Fx6.FromRaw(10_000), P.YProd, null, prod));
    }

    [Fact]
    public void Producible_TakesTheSteel_WhenRank1FailsYieldOrAlloy_ButNotAsTheLightestFallback()
    {
        Span<int> alts = stackalloc int[L.SlotCount];
        Func<int, bool> knows = L.KnownUpTo(1);
        // Rank 1 (PRC 39, 45, 49) under y_prod, the steel (47) fine: the main rule takes the steel (it takes part).
        Fx6 Low(int p) => p is 39 or 45 or 49 ? Fx6.FromRaw(100_000) : Fx6.One;
        Assert.True(DesignRules.Producible(L, 1, knows, Low, P.YProd, null, alts));
        Assert.Equal("hot_steel_g0", L.AltId[alts[L.AlloySlot]]);
        // The alloy test rejects every kilogram-bound alternative; alloy_free passes it.
        Assert.True(DesignRules.Producible(L, 1, knows, _ => Fx6.One, P.YProd, hot => false, alts));
        Assert.Equal("hot_steel_g0", L.AltId[alts[L.AlloySlot]]);
        // Everything under y_prod, the steel too: the fallback is the lightest PLAIN alternative, not the substitute (a plain one exists).
        // The result is a project equal to superior (F-PC3): false.
        Assert.False(DesignRules.Producible(L, 1, knows, _ => Fx6.FromRaw(100_000), P.YProd, null, alts));
        // Lightening never lands on the steel: G2 producible with the superalloy melt failing gives nicr, not steel.
        Fx6 NoSa(int p) => p == 40 ? Fx6.FromRaw(100_000) : Fx6.One;
        Assert.True(DesignRules.Producible(L, 2, L.KnownUpTo(2), NoSa, P.YProd, null, alts));
        Assert.Equal("hot_nicr_forged", L.AltId[alts[L.AlloySlot]]);
    }

    [Fact]
    public void Producible_WithAdmitted_UsesTheSubstituteWhenItIsTheOnlyAdmittedHotSection()
    {
        // L_P (spec §6.1): rank 1 not admitted (a PRC with yield 0) leaves the steel as the only candidate of the slot.
        Span<int> alts = stackalloc int[L.SlotCount];
        Assert.True(DesignRules.Producible(L, 1, L.KnownUpTo(1), _ => Fx6.One, P.YProd, null, alts, alt => !L.PrcOf(alt).Contains(45)));
        Assert.Equal("hot_steel_g0", L.AltId[alts[L.AlloySlot]]);
    }

    // ---- compose_design and derive

    private static ProductsHarness Gb(Func<int, long, IReadOnlyList<DesignCommand>> commands) => new()
    {
        Knows = (a, n) => a == Gbr && L.KnownUpTo(2)(n),
        Commands = commands,
    };

    [Fact]
    public void Compose_RefusesAnEngineAboveMaxEngGen_AndAcceptsTheSteelWithG1()
    {
        var h = Gb((a, t) => a != Gbr || t != 0 ? [] :
        [
            Compose(L, 2, "eng_g2", "hot_steel_g0", "comp_axial", "af_swept", "ab_on"),     // max_eng_gen 1 < eng_gen 2
            Compose(L, 2, "eng_g1", "hot_steel_g0", "comp_axial", "af_swept", "ab_on"),     // allowed
        ]);
        h.RunThroughMonth(0);
        Assert.Equal(1, h.Products.RejectionCount(Gbr, CommandRejection.SlotUnavailable));
        Assert.Equal(DesignStage.Project, h.Products.Stage(Gbr, 1));
        Assert.Equal(DesignStage.None, h.Products.Stage(Gbr, 2));
        Assert.Equal(["eng_g1", "hot_steel_g0", "comp_axial", "af_swept", "ab_on"], AltIds(L, h.Products.Alternatives(Gbr, 1)));
        Assert.Equal(2, h.Products.Gen(Gbr, 1));   // gen = min(g_p 2, eng_gen 1 + 1): the formula does not change
    }

    [Fact]
    public void Derive_JudgesTheResultingComposition()
    {
        DesignCommand[] Derive(int parent, params (int Slot, string Alt)[] changes)
        {
            var alts = new int[L.SlotCount];
            Array.Fill(alts, -1);
            foreach ((int slot, string alt) in changes)
                alts[slot] = Alt(L, alt);
            return [new DesignCommand(DesignCommandKind.Derive, parent, [.. alts])];
        }
        int engine = L.BaseSlot, hot = L.AlloySlot;
        var h = Gb((a, t) => a != Gbr || t != 0 ? [] :
        [
            .. Derive(0, (engine, "eng_g2"), (hot, "hot_steel_g0")),   // engine G2 + steel: refused
            .. Derive(0, (hot, "hot_steel_g0")),                        // the parent has eng_g1: allowed
        ]);
        h.RunThroughMonth(0);
        Assert.Equal(1, h.Products.RejectionCount(Gbr, CommandRejection.SlotUnavailable));
        Assert.Equal(DesignStage.Project, h.Products.Stage(Gbr, 1));
        Assert.Equal("hot_steel_g0", L.AltId[h.Products.Alternatives(Gbr, 1)[hot]]);
    }

    // ---- alloy_free

    [Fact]
    public void AlloyFree_LpLineProduces_WithZeroSupply_AndTouchesNoAlloy()
    {
        // GBR's rank-1 chain is not admitted (yield 0), so the canonical project is the steel; the alloy supply is zero.
        var h = new ProductsHarness(mode: ProductsMode.CanonicalLp, parameters: ProductsLpTests.LpParams)
        {
            Knows = (_, n) => L.KnownUpTo(1)(n),
            AppKnownSince = (a, g) => g == 1 ? 0 : -1,
            Budget = (a, _) => Fx6.FromRaw(100_000 * (a + 1)),
            Alloy = (a, _) => Fx6.Zero,
            Yield = (a, p) => a == Gbr && p is 39 or 45 or 49 ? Fx6.Zero : Fx6.One,
        };
        h.RunThroughMonth(2);
        ProductsSystem p = h.Products;
        int steel = p.CanonicalSlot(Gbr, 1), nicr = p.CanonicalSlot(Usa, 1);
        Assert.Equal("hot_steel_g0", L.AltId[p.Alternatives(Gbr, steel)[L.AlloySlot]]);
        Assert.Equal(Fx6.FromInt(20), p.Rate(Gbr, steel));                     // ρ·budget = 100·0.2, no alloy term
        Assert.Equal("hot_nicr_forged", L.AltId[p.Alternatives(Usa, nicr)[L.AlloySlot]]);
        Assert.Equal(Fx6.Zero, p.Rate(Usa, nicr));                             // the control: kg-bound, no alloy, no output
    }

    [Fact]
    public void AlloyFree_SeriesLine_ProducesWithZeroSupply_WhereTheNickelLineCannot()
    {
        var h = new ProductsHarness
        {
            Knows = (a, n) => a == Gbr && L.KnownUpTo(1)(n),
            Alloy = (_, _) => Fx6.Zero,
            Commands = (a, t) => a == Gbr && t == 0
                ? [Compose(L, 1, "eng_g1", "hot_steel_g0", "comp_centrifugal", "af_straight", "ab_none"),
                   Compose(L, 1, "eng_g1", "hot_nicr_forged", "comp_centrifugal", "af_straight", "ab_none")]
                : [],
        };
        ProductsSystem p = h.Products;
        h.Series = (a, d, t) => a == Gbr && d is 1 or 2 && p.Stage(a, d) == DesignStage.Prototype && p.GateOpen(a, d, t) ? new SeriesStart(Fx6.FromInt(10), false) : null;
        Fx6 steelOut = Fx6.Zero, nicrOut = Fx6.Zero;
        for (long t = 0; t < 90 && steelOut == Fx6.Zero; t++)
        {
            h.RunThroughMonth(t);
            steelOut += p.Rate(Gbr, 1);
            nicrOut += p.Rate(Gbr, 2);
        }
        Assert.True(steelOut > Fx6.Zero, "the steel line never produced with zero alloy supply");
        Assert.Equal(Fx6.Zero, nicrOut);
    }

    [Fact]
    public void AlloyFree_FleetValue_HasNoAlloyCap()
    {
        var h = new ProductsHarness { Alloy = (_, _) => Fx6.Zero, Knows = (a, n) => L.KnownUpTo(1)(n) };
        int[] steel = [Alt(L, "eng_g1"), Alt(L, "hot_steel_g0"), Alt(L, "comp_centrifugal"), Alt(L, "af_straight"), Alt(L, "ab_none")];
        int[] nicr = [Alt(L, "eng_g1"), Alt(L, "hot_nicr_forged"), Alt(L, "comp_centrifugal"), Alt(L, "af_straight"), Alt(L, "ab_none")];
        Fx6 budget = Fx6.FromInt(10);
        var (nSteel, _) = h.Products.FleetValue(Usa, steel, Fx6.Zero, Fx6.One, Fx6.One, budget, 0);
        var (nNicr, _) = h.Products.FleetValue(Usa, nicr, Fx6.Zero, Fx6.One, Fx6.One, budget, 0);
        Assert.True(nSteel > 0);
        Assert.Equal(0, nNicr);   // zero alloy supply caps the kilogram-bound design at zero units
    }

    // ---- the data validator (every violation of the §1.4 table is a load error with a clear message)

    private string LoadFail(string from, string to)
    {
        string dir = _mods.CopyCore((name, text) => name == "product_classes.yaml" ? TestMods.ReplaceOnce(text, from, to) : text);
        return Assert.Throws<DataLoadException>(() => ModLoader.Load(dir)).Message;
    }

    [Theory]
    [InlineData("hot_steel_g0, rank: 0, substitute: true, alloy_free: true,", "hot_steel_g0, rank: 0, substitute: true, alloy_free: true, alloy_kg: 5,", "alloy_free and alloy_kg are mutually exclusive")]
    [InlineData("alloy_kg: 290,", "alloy_kg: 0,", "alloy_kg must be positive")]
    [InlineData("substitute: true, alloy_free: true,", "substitute: true,", "alloy_kg or alloy_free must be on every alternative of the alloy slot or on none")]
    [InlineData("{id: comp_axial, rank: 2,", "{id: comp_axial, rank: 2, alloy_free: true,", "alloy_kg or alloy_free must be on every alternative of the alloy slot or on none")]
    [InlineData("{id: comp_axial, rank: 2,", "{id: comp_axial, rank: 0,", "rank 0 is for a substitute only")]
    [InlineData("{id: eng_g2, rank: 2, eng_gen: 2,", "{id: eng_g2, rank: 2, substitute: true, eng_gen: 2,", "substitute is not allowed in the engine (base) slot")]
    [InlineData("hot_steel_g0, rank: 0,", "hot_steel_g0, rank: 5,", "a substitute must rank strictly below every non-substitute alternative")]
    [InlineData("{id: eng_g2, rank: 2, eng_gen: 2,", "{id: eng_g2, rank: 2, max_eng_gen: 1, eng_gen: 2,", "max_eng_gen is not allowed in the engine (base) slot")]
    [InlineData("max_eng_gen: 1,", "max_eng_gen: 0,", "max_eng_gen must be at least 1")]
    [InlineData("max_eng_gen: 1,", "max_eng_gen: 5,", "max_eng_gen must be in 1..4 (G_max)")]
    [InlineData("upkeep_mult: 2.5,", "upkeep_mult: 0,", "upkeep_mult must be positive")]
    public void FieldRules_AreEnforced(string from, string to, string message) => Assert.Contains(message, LoadFail(from, to));

    [Fact]
    public void ASlotOfSubstitutesOnly_IsRejected()
    {
        string dir = _mods.CopyCore((name, text) => name != "product_classes.yaml" ? text :
            TestMods.ReplaceOnce(TestMods.ReplaceOnce(text, "{id: ab_none, rank: 1,", "{id: ab_none, rank: 1, substitute: true,"),
                "{id: ab_on, rank: 2,", "{id: ab_on, rank: 2, substitute: true,"));
        Assert.Contains("needs a non-substitute alternative", Assert.Throws<DataLoadException>(() => ModLoader.Load(dir)).Message);
    }

    [Fact]
    public void AlloyFreeWithoutAnyKilogramAlternative_IsNotAnAlloySlot()
    {
        // alloy_kg removed from every alternative of the hot section, alloy_free kept on the substitute: the class has no alloy slot with alloy_kg.
        string dir = _mods.CopyCore((name, text) => name != "product_classes.yaml" ? text :
            text.Replace("alloy_kg: 290, ", "").Replace("alloy_kg: 670, ", "").Replace("alloy_kg: 2120, ", "").Replace("alloy_kg: 2980, ", "")
                .Replace("{id: hot_nicr_forged, rank: 1,", "{id: hot_nicr_forged, rank: 1, alloy_free: true,")
                .Replace("{id: hot_sa2_forged, rank: 2,", "{id: hot_sa2_forged, rank: 2, alloy_free: true,")
                .Replace("{id: hot_vim_cast, rank: 3,", "{id: hot_vim_cast, rank: 3, alloy_free: true,")
                .Replace("{id: hot_vim_ds_cooled, rank: 4,", "{id: hot_vim_ds_cooled, rank: 4, alloy_free: true,"));
        Assert.Contains("at least one alternative with alloy_kg", Assert.Throws<DataLoadException>(() => ModLoader.Load(dir)).Message);
    }

    [Fact]
    public void TheNewFields_AreInTheDataHash()
    {
        string baseHash = ModLoader.Load(TestMods.CoreDir).DataHash;
        foreach ((string from, string to) in new[] { ("upkeep_mult: 2.5", "upkeep_mult: 2.4"), ("max_eng_gen: 1", "max_eng_gen: 2") })
        {
            string dir = _mods.CopyCore((name, text) => name == "product_classes.yaml" ? TestMods.ReplaceOnce(text, from, to) : text);
            Assert.NotEqual(baseHash, ModLoader.Load(dir).DataHash);
        }
    }
}
