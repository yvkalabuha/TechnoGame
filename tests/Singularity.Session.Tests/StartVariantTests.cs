using Singularity.Core.Data;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>START_PERT and START_SYM (spec §6.5, v0.12).</summary>
public class StartVariantTests
{
    private static readonly GameData Data = SliceTestData.Data;
    private static readonly ScenarioData Original = Data.Scenario("jet_slice_1945");

    private static ScenarioData Pert(ulong seed, Func<int, Fx6>? multiplier = null) =>
        Original.Perturbed(multiplier ?? (i => PolicyRng.StartMultiplier(new CounterRng(seed), i)), Data.Nodes, Data.ProductClasses);

    [Fact]
    public void IdentityMultipliers_RebuildTheSameScenario_SoTheCopyIsFaithful()
    {
        ScenarioData copy = Pert(0, _ => Fx6.One);
        Assert.Equal(Original.Hash, copy.Hash);
    }

    [Fact]
    public void FieldIndex_CountsEstimatedPairs_InCanonicalOrder_WithoutGaps()
    {
        var seen = new List<int>();
        Pert(0, i => { seen.Add(i); return Fx6.One; });
        Assert.Equal(Enumerable.Range(0, seen.Count), seen);
        // 5 actors × {budget_index, culture_h, fleet, k_d, k_ind, w} (estimated for all) + SUN's gdp_1945, gdppc_1945, growth_pa
        // (known is a node set and tooling_m, hcap are derived: no multiplier).
        int expected = 5 * 6;
        for (int a = 0; a < Original.ActorCount; a++)
        {
            for (int e = Original.EstimatedStart[a]; e < Original.EstimatedStart[a + 1]; e++)
            {
                if (Original.Estimated[e] is "gdp_1945" or "gdppc_1945" or "growth_pa")
                    expected++;
            }
        }
        Assert.Equal(expected, seen.Count);
    }

    [Fact]
    public void Multipliers_AreInRange08To12_AndScaleEstimatedFieldsOnly()
    {
        var m = new List<Fx6>();
        ScenarioData p = Pert(3, i => { Fx6 x = PolicyRng.StartMultiplier(new CounterRng(3), i); m.Add(x); return x; });
        Assert.All(m, x => Assert.InRange(x.Raw, 800_000, 1_200_000));
        Assert.True(m.Distinct().Count() > 10);
        for (int a = 0; a < Original.ActorCount; a++)
        {
            Fx6 ratio = p.BudgetIndex[a] / Original.BudgetIndex[a];
            Assert.InRange(ratio.Raw, 799_000, 1_200_001);
            Assert.NotEqual(Original.BudgetIndex[a], p.BudgetIndex[a]);
        }
        // gdp_1945 is not estimated for USA (provenance false, actor list empty): unchanged. For SUN it is listed: changed.
        int usa = Original.ActorIndex("USA"), sun = Original.ActorIndex("SUN");
        Assert.Equal(Original.Gdp[usa], p.Gdp[usa]);
        Assert.NotEqual(Original.Gdp[sun], p.Gdp[sun]);
        Assert.Equal(Original.TrophyEngineers.ToArray(), p.TrophyEngineers.ToArray());   // trophy_engineers has a source, never estimated
    }

    [Fact]
    public void DerivedFields_KeepTheirRuleOffsets_ToThePerturbedCulture()
    {
        ScenarioData p = Pert(5);
        int c3 = Original.Clusters.Length * 3;
        for (int a = 0; a < Original.ActorCount; a++)
        {
            Fx6 maxOld = Original.CultureH[a * c3], maxNew = p.CultureH[a * c3];
            for (int i = 0; i < c3; i++)
            {
                Assert.Equal(Original.ToolingM[a * c3 + i] - Original.CultureH[a * c3 + i], p.ToolingM[a * c3 + i] - p.CultureH[a * c3 + i]);
                maxOld = maxOld > Original.CultureH[a * c3 + i] ? maxOld : Original.CultureH[a * c3 + i];
                maxNew = maxNew > p.CultureH[a * c3 + i] ? maxNew : p.CultureH[a * c3 + i];
            }
            Assert.Equal(Original.Hcap[a] - maxOld, p.Hcap[a] - maxNew);
            Assert.NotEqual(Original.CultureH[a * c3], p.CultureH[a * c3]);
        }
    }

    [Fact]
    public void KnownNodes_MasteredHistory_AndZeroKInd_DoNotChange()
    {
        ScenarioData p = Pert(9);
        for (int a = 0; a < Original.ActorCount; a++)
        {
            Assert.Equal(Original.KnownOf(a).ToArray(), p.KnownOf(a).ToArray());
            Assert.Equal(Original.MasteredOf(a).ToArray(), p.MasteredOf(a).ToArray());
            Assert.Equal(Original.KInd[a], p.KInd[a]);   // 0 stays 0
        }
        Assert.Equal(Original.MasteredSinceMonth, p.MasteredSinceMonth);
    }

    [Fact]
    public void SameSeed_SameScenario_DifferentSeed_Different()
    {
        Assert.Equal(Pert(7).Hash, Pert(7).Hash);
        Assert.NotEqual(Pert(7).Hash, Pert(8).Hash);
    }

    [Fact]
    public void Symmetric_UsaGbrSun_AreEqual_AndOthersUntouched()
    {
        ScenarioData s = Original.Symmetrized(Data.Nodes, Data.ProductClasses);
        int usa = Original.ActorIndex("USA"), gbr = Original.ActorIndex("GBR"), sun = Original.ActorIndex("SUN");
        int c3 = Original.Clusters.Length * 3, d = Original.Disciplines.Length;
        foreach (int r in new[] { gbr, sun })
        {
            Assert.Equal(s.BudgetIndex[usa], s.BudgetIndex[r]);
            Assert.Equal(s.GrowthPa[usa], s.GrowthPa[r]);
            Assert.Equal(s.Hcap[usa], s.Hcap[r]);
            Assert.Equal(s.W[usa], s.W[r]);
            Assert.Equal(s.KInd[usa], s.KInd[r]);
            Assert.Equal(s.TrophyEngineers[usa], s.TrophyEngineers[r]);
            Assert.Equal(s.CultureH.AsSpan().Slice(usa * c3, c3).ToArray(), s.CultureH.AsSpan().Slice(r * c3, c3).ToArray());
            Assert.Equal(s.ToolingM.AsSpan().Slice(usa * c3, c3).ToArray(), s.ToolingM.AsSpan().Slice(r * c3, c3).ToArray());
            Assert.Equal(s.KD.AsSpan().Slice(usa * d, d).ToArray(), s.KD.AsSpan().Slice(r * d, d).ToArray());
            Assert.Equal(s.KnownOf(usa).ToArray(), s.KnownOf(r).ToArray());
            Assert.Equal(s.MasteredOf(usa).ToArray(), s.MasteredOf(r).ToArray());
            Assert.Equal(s.FleetStart[usa + 1] - s.FleetStart[usa], s.FleetStart[r + 1] - s.FleetStart[r]);
        }
        // Mean of the three (floor of the sum over 3).
        Assert.Equal((Original.BudgetIndex[usa].Raw + Original.BudgetIndex[gbr].Raw + Original.BudgetIndex[sun].Raw) / 3, s.BudgetIndex[usa].Raw);
        foreach (string id in new[] { "FRA", "SWE" })
        {
            int r = Original.ActorIndex(id);
            Assert.Equal(Original.BudgetIndex[r], s.BudgetIndex[r]);
            Assert.Equal(Original.KnownOf(r).ToArray(), s.KnownOf(r).ToArray());
        }
        // The offsets of the rule are USA's, applied to the mean culture.
        Assert.Equal(Original.ToolingM[usa * c3] - Original.CultureH[usa * c3], s.ToolingM[sun * c3] - s.CultureH[sun * c3]);
    }

    [Fact]
    public void Variants_RunInTheWorld_AndEnterTheHash()
    {
        SliceSession main = SliceTestData.Create(new SliceRunConfig { Seed = 4 });
        SliceSession pert = SliceTestData.Create(new SliceRunConfig { Seed = 4, Start = StartVariant.Perturbed });
        SliceSession sym = SliceTestData.Create(new SliceRunConfig { Seed = 4, Start = StartVariant.Symmetric });
        foreach (SliceSession s in new[] { main, pert, sym })
            s.StepThroughMonth(24);
        Assert.Equal(3, new[] { main.LastHash, pert.LastHash, sym.LastHash }.Distinct().Count());
        Assert.Equal(pert.World.Scenario.BudgetIndex[0], pert.World.BudgetAtStart[0]);
        Assert.Equal(sym.World.Industry.ToolingM(0, 0, Dimension.Precision), sym.World.Industry.ToolingM(2, 0, Dimension.Precision));
    }
}
