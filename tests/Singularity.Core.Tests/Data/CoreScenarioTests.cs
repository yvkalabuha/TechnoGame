using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Data;

/// <summary>Scenario 1945 and class jet_fighter match TASK-0011 (scenario_1945.md, product_class.md).</summary>
public class CoreScenarioTests
{
    private static readonly GameData Data = ModLoader.Load(TestMods.CoreDir);
    private static readonly ScenarioData S = Data.Scenario("jet_slice_1945");

    private static Fx6 Fx(string text) => Fx6.FromRaw((long)(decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture) * Fx6.Scale));

    [Fact]
    public void Actors_AreInSpecOrder()
    {
        // Spec §3.5 A7: actor index in RNG keys = data order.
        Assert.Equal(["USA", "GBR", "SUN", "FRA", "SWE"], S.ActorId.ToArray());
        Assert.Equal(0, S.TechLead);
        Assert.Equal((1945, 9), (S.StartYear, S.StartMonth));
    }

    [Theory]
    [InlineData("USA", "2314.67", 1945, "16478", "1.000", "0.0299", "4.00", "3.80", "3.70", "5.50", 86)]
    [InlineData("GBR", "553.15", 1945, "11247", "0.239", "0.0220", "3.82", "3.62", "3.52", "5.32", 100)]
    [InlineData("SUN", "530.22", 1946, "3049", "0.229", "0.0548", "3.20", "3.00", "2.90", "4.70", 1385)]
    [InlineData("FRA", "162.81", 1945, "4101", "0.070", "0.0662", "3.34", "3.14", "3.04", "4.84", 120)]
    [InlineData("SWE", "54.41", 1945, "8201", "0.024", "0.0417", "3.67", "3.47", "3.37", "5.17", 0)]
    public void ActorValues_MatchScenarioTable(string id, string gdp, int gdpYear, string gdppc, string budget,
        string growth, string prec, string pur, string qc, string hcap, int trophy)
    {
        int a = S.ActorIndex(id);
        Assert.Equal(Fx(gdp), S.Gdp[a]);
        Assert.Equal(gdpYear, S.GdpYear[a]);
        Assert.Equal(Fx(gdppc), S.GdpPerCapita[a]);
        Assert.Equal(Fx(budget), S.BudgetIndex[a]);
        Assert.Equal(Fx(growth), S.GrowthPa[a]);
        foreach (Cluster c in S.Clusters)
        {
            // D1 cell edits (spec §1.1 table, items 1-4) on top of the uniform R-DIM table.
            Assert.Equal(D1Cell(id, c, Dimension.Precision, Fx(prec)), S.CultureOf(a, c, Dimension.Precision));
            Assert.Equal(D1Cell(id, c, Dimension.Purity, Fx(pur)), S.CultureOf(a, c, Dimension.Purity));
            Assert.Equal(D1Cell(id, c, Dimension.QualityControl, Fx(qc)), S.CultureOf(a, c, Dimension.QualityControl));
            // R-M per cell (spec §3.5 A20): tooling = H + 0.2, SUN +0.2 more in MECH and AERO (dismantled plants).
            Fx6 extra = id == "SUN" && c is Cluster.MECH or Cluster.AERO ? Fx("0.2") : Fx6.Zero;
            foreach (Dimension d in Enum.GetValues<Dimension>())
                Assert.Equal(S.CultureOf(a, c, d) + Fx("0.2") + extra, S.ToolingOf(a, c, d));
        }
        Assert.Equal(Fx(hcap), S.Hcap[a]);
        Assert.Equal(Fx(prec) + Fx("1.5"), S.Hcap[a]);   // R-HC
        Assert.Equal(Fx6.One, S.W[a]);
        Assert.Equal(Fx6.Zero, S.KInd[a]);
        Assert.Equal(trophy, S.TrophyEngineers[a]);
    }

    // D1 (#130, #133): GBR MECH precision 4.00 (R-H-STD-t0), USA and GBR CHEM purity 4.00 (owner), SUN AERO quality_control 3.50.
    private static Fx6 D1Cell(string actor, Cluster c, Dimension d, Fx6 uniform) => (actor, c, d) switch
    {
        ("GBR", Cluster.MECH, Dimension.Precision) => Fx("4.00"),
        ("USA" or "GBR", Cluster.CHEM, Dimension.Purity) => Fx("4.00"),
        ("SUN", Cluster.AERO, Dimension.QualityControl) => Fx("3.50"),
        _ => uniform,
    };

    [Fact]
    public void KnownAtStart_MatchesNodesCsv()
    {
        string[] lines = File.ReadAllLines(Path.Combine(TestMods.SliceDataDir, "nodes.csv"));
        int col = Array.IndexOf(lines[0].Split(','), "known_at_start");
        for (int a = 0; a < S.ActorCount; a++)
        {
            string actor = S.ActorId[a];
            var expected = lines.Skip(1).Select(l => l.Split(','))
                .Where(c => c[col] == "ALL" || c[col].Split(';').Contains(actor))
                .Select(c => c[1]).Order(StringComparer.Ordinal);
            var loaded = S.KnownOf(a).ToArray().Select(r => Data.Nodes.Id[r]).Order(StringComparer.Ordinal);
            Assert.Equal(expected, loaded);
        }
        Assert.Equal([22, 23, 15, 12, 10], Enumerable.Range(0, 5).Select(a => S.KnownOf(a).Length));
    }

    [Fact]
    public void KD_FollowsRuleRKD()
    {
        // R-KD: K_d(t0) = k_node (0.05) × number of known nodes of discipline d.
        Fx6 kNode = Fx("0.05");
        for (int a = 0; a < S.ActorCount; a++)
        {
            foreach (Discipline d in S.Disciplines)
            {
                int count = S.KnownOf(a).ToArray().Count(r => Data.Nodes.Discipline[r] == d);
                Assert.Equal(kNode * Fx6.FromInt(count), S.KDOf(a, d));
            }
        }
    }

    [Fact]
    public void Mastered_MatchesRuleRMS()
    {
        int[] g0 = [38, 44, 47, 53];
        int[] g1 = [.. g0, 39, 45, 48, 49, 54];
        for (int a = 0; a < S.ActorCount; a++)
        {
            int[] expected = a <= 1 ? g1 : g0;   // USA, GBR
            Assert.Equal(expected.Order(), S.MasteredOf(a).ToArray().Select(r => Data.Nodes.Number[r]));
        }
        Assert.Equal((-6, 3), (S.MasteredSinceMonth, S.ProcXHalfLives));
    }

    [Fact]
    public void Fleets_AreG1SuperiorForUsaAndGbr()
    {
        Assert.Equal([0, 1, 2, 2, 2, 2], S.FleetStart.ToArray());
        foreach (int f in new[] { 0, 1 })
            Assert.Equal((0, 1, DesignVariant.Superior, 100, 1945),
                (S.FleetClass[f], S.FleetGen[f], S.FleetVariant[f], S.FleetCount[f], S.FleetCohortYear[f]));
    }

    [Fact]
    public void Provenance_CoversEveryActorField()
    {
        Assert.Equal(ScenarioData.ActorFields.Order(StringComparer.Ordinal), S.ProvField.ToArray());
        int sun = S.ActorIndex("SUN");
        Assert.Equal(["gdp_1945", "gdppc_1945", "growth_pa", "known"], S.Estimated.Skip(S.EstimatedStart[sun]).Take(S.EstimatedStart[sun + 1] - S.EstimatedStart[sun]));
    }

    [Theory]
    [InlineData(new[] { "eng_g1", "hot_nicr_forged", "comp_centrifugal", "af_straight", "ab_none" }, new[] { 38, 39, 44, 45, 48, 49, 53, 54 }, 29)]
    [InlineData(new[] { "eng_g2", "hot_sa2_forged", "comp_axial", "af_swept", "ab_on" }, new[] { 38, 40, 44, 45, 49, 50, 53, 54, 55, 57 }, 37)]
    [InlineData(new[] { "eng_g2", "hot_nicr_forged", "comp_axial", "af_swept", "ab_on" }, new[] { 38, 39, 44, 45, 49, 50, 53, 54, 55, 57 }, 37)]
    [InlineData(new[] { "eng_g3", "hot_vim_cast", "comp_axial", "af_titanium", "ab_on" }, new[] { 38, 41, 42, 44, 46, 50, 51, 53, 54, 55, 56, 57 }, 48)]
    [InlineData(new[] { "eng_g3", "hot_sa2_forged", "comp_axial", "af_titanium", "ab_on" }, new[] { 38, 40, 42, 44, 45, 49, 50, 53, 54, 55, 56, 57 }, 46)]
    [InlineData(new[] { "eng_g4", "hot_vim_ds_cooled", "comp_axial", "af_titanium", "ab_on" }, new[] { 38, 41, 42, 43, 44, 50, 52, 53, 54, 55, 56, 57 }, 49)]
    [InlineData(new[] { "eng_g4", "hot_vim_cast", "comp_axial", "af_titanium", "ab_on" }, new[] { 38, 41, 42, 44, 46, 50, 51, 53, 54, 55, 56, 57 }, 48)]
    public void ReferenceVariants_MatchProductClassTable(string[] alternatives, int[] prcNumbers, int complexity)
    {
        // product_class.md §2: prc_set = union of the alternatives' PRC; complexity = Σ tier.
        ProductClassTable pc = Data.ProductClasses;
        int cls = pc.IndexOf("jet_fighter");
        string[] slots = ["engine", "hot_section", "compressor", "airframe", "afterburner"];
        var union = new SortedSet<int>();
        for (int i = 0; i < slots.Length; i++)
        {
            int alt = pc.AlternativeIndex(cls, slots[i], alternatives[i]);
            Assert.True(alt >= 0, alternatives[i]);
            foreach (int p in pc.PrcSet(alt))
                union.Add(Data.Nodes.Number[p]);
        }
        Assert.Equal(prcNumbers, union);
        Assert.Equal(complexity, union.Sum(num => Data.Nodes.Tier[Data.Nodes.Number.IndexOf(num)]));
    }

    [Fact]
    public void ProductClass_SlotsAndLightenOrder()
    {
        ProductClassTable pc = Data.ProductClasses;
        Assert.Equal(["jet_fighter"], pc.ClassId.ToArray());
        Assert.Equal(["engine", "hot_section", "compressor", "airframe", "afterburner"], pc.SlotId.ToArray());
        Assert.Equal([true, false, false, false, false], pc.SlotIsBase.ToArray());
        Assert.Equal(["hot_section", "compressor", "afterburner", "airframe"], pc.LightenOrder.Select(s => pc.SlotId[s]));
        Assert.Equal(18, pc.AltId.Length);   // 17 + the hot_steel_g0 substitute (D1)
        Assert.Equal([false, false, false, true, false], pc.SlotIsAirframe.ToArray());
        Assert.Equal(Fx("1.45"), pc.AltPerfMult[pc.AlternativeIndex(0, "hot_section", "hot_vim_ds_cooled")]);
    }
}
