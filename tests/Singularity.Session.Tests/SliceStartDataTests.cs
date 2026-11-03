using Singularity.Core.Data;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Core.WhyNot;
using Singularity.Kernel;
using Xunit.Abstractions;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0026 (D1, spec v0.14 §11): the start-data package on the assembled slice at t0 — K1 (every known PRC has yield &gt; 0), K4
/// (the steel is worth less than the rank-1 hot section for USA and GBR), the presets and the <c>ENGINE_GEN_LIMIT</c> link.
/// </summary>
public class SliceStartDataTests(ITestOutputHelper output)
{
    private static SliceSession New() => SliceTestData.Create();

    private static int Actor(SliceSession s, string id) => s.World.Scenario.ActorIndex(id);

    private static void Learn(SliceSession s, int actor, int node, int value)
    {
        IntColumn c = s.World.Table.GetInt($"know.known.{node}");
        int[] all = c.Current.ToArray();
        all[actor] = value;
        c.Load(all);
    }

    [Fact]
    public void K1_EveryKnownProcessOfEveryActorHasPositiveYieldAtT0()
    {
        SliceSession s = New();
        SliceWorld w = s.World;
        var bad = new List<string>();
        for (int a = 0; a < w.Scenario.ActorCount; a++)
        {
            foreach (int row in w.Scenario.KnownOf(a))
            {
                if (w.Data.Nodes.Type[row] != NodeType.PRC)
                    continue;
                int number = w.Data.Nodes.Number[row];
                Fx6 y = w.ProductsInputs.AdmittedYield(a, number, 0);
                output.WriteLine($"{w.Scenario.ActorId[a]} PRC {number}: yield {y}");
                if (y <= Fx6.Zero)
                    bad.Add($"{w.Scenario.ActorId[a]}/{number}");
            }
        }
        Assert.True(bad.Count == 0, "yield = 0 at t0 for: " + string.Join(", ", bad));
    }

    private static int[] Composition(ProductsLayout l, params string[] ids) =>
        [.. ids.Select(id => Array.IndexOf(l.AltId.ToArray(), id))];

    [Theory]
    [InlineData("USA")]
    [InlineData("GBR")]
    public void K4_TheSteelHotSectionIsWorthLessThanRank1AtEqualBudget(string actorId)
    {
        SliceSession s = New();
        SliceWorld w = s.World;
        ProductsLayout l = w.ProductsLayout;
        int a = Actor(s, actorId);
        int[] nicr = Composition(l, "eng_g1", "hot_nicr_forged", "comp_centrifugal", "af_straight", "ab_none");
        int[] steel = Composition(l, "eng_g1", "hot_steel_g0", "comp_centrifugal", "af_straight", "ab_none");
        Fx6 budget = w.PolicyParameters.FvBudget;
        var (nNicr, fvNicr) = w.Products.CandidateValue(a, nicr, budget, 0);
        var (nSteel, fvSteel) = w.Products.CandidateValue(a, steel, budget, 0);
        output.WriteLine($"K4 {actorId}: FV(steel) = {fvSteel} (n = {nSteel}), FV(rank 1) = {fvNicr} (n = {nNicr}), ratio = {fvSteel / fvNicr}");
        Assert.True(fvSteel < fvNicr, $"K4 failed for {actorId}: the owner decides, upkeep_mult is not to be tuned (spec §6.9)");
    }

    [Fact]
    public void SunPreset_IsTheSteelG1_BecauseItKnowsNoAlloy()
    {
        SliceSession s = New();
        SliceWorld w = s.World;
        ProductsLayout l = w.ProductsLayout;
        int sun = Actor(s, "SUN");
        Learn(s, sun, 48, 1);   // the centrifugal impeller process: SUN still has to learn it (spec §6.9 K-SUN, «48 (до 48 мес.)»)
        var alts = new int[l.SlotCount];
        PolicyParams p = w.PolicyParameters;
        foreach (PolicyProfile profile in new[] { PolicyProfile.Quality, PolicyProfile.Mass, PolicyProfile.Adaptive })
        {
            Assert.True(ProductPresets.Compose(profile, new PolicyProductsAdapter(w), p, sun, 1, 0, alts));
            Assert.Equal(["eng_g1", "hot_steel_g0", "comp_centrifugal", "af_straight", "ab_none"], alts.Select(a => l.AltId[a]));
        }
    }

    [Fact]
    public void KnowingG2WithoutTheAlloy_StepsTheEngineDown_AndWhyNotNamesEngineGenLimit()
    {
        SliceSession s = New();
        SliceWorld w = s.World;
        ProductsLayout l = w.ProductsLayout;
        int sun = Actor(s, "SUN");
        Learn(s, sun, 48, 1);
        Learn(s, sun, 19, 1);   // turbojet_g2 without the alloy (30)
        var alts = new int[l.SlotCount];
        Assert.True(DesignRules.Superior(l, 1, n => w.ProductsInputs.Knows(sun, n, 0), alts));
        Assert.Equal(["eng_g1", "hot_steel_g0"], alts.Take(2).Select(a => l.AltId[a]));

        IReadOnlyList<Link> links = s.WhyNot(sun, WhyNotTarget.Requirement(1));
        Link limit = Assert.Single(links, k => k.Cause == WhyNotCause.EngineGenLimit);
        Assert.Equal(WhyNotLayer.Product, limit.Layer);
        Assert.Equal(30, limit.SubjectId);                                      // the lever: learn the alloy node
        Assert.Equal(Fx6.FromInt(1), limit.Have);                               // max_eng_gen
        Assert.Equal(Fx6.FromInt(2), limit.Need);                               // eng_gen of the highest known engine
        Assert.Equal(l.AlloySlot, limit.Slot);
        Assert.Equal([Lever.ResearchPriority], limit.Levers.ToArray());
        Assert.DoesNotContain(links, k => k.Cause == WhyNotCause.AlloyDeficit);

        // The lever moves along the requirements of the lowest-rank plain alternative (30, then 33, then its processes) and disappears with the last one.
        Learn(s, sun, 30, 1);
        Assert.Equal(33, Assert.Single(s.WhyNot(sun, WhyNotTarget.Requirement(1)), k => k.Cause == WhyNotCause.EngineGenLimit).SubjectId);
        foreach (int node in new[] { 33, 39, 45, 49 })
            Learn(s, sun, node, 1);
        Assert.DoesNotContain(s.WhyNot(sun, WhyNotTarget.Requirement(1)), k => k.Cause == WhyNotCause.EngineGenLimit);
    }

    [Fact]
    public void WithoutAnyKnowledgeOfG2_ThereIsNoEngineGenLimitLink()
    {
        SliceSession s = New();
        Assert.DoesNotContain(s.WhyNot(Actor(s, "SUN"), WhyNotTarget.Requirement(1)), k => k.Cause == WhyNotCause.EngineGenLimit);
    }
}
