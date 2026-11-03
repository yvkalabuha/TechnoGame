using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;
using Xunit.Abstractions;

namespace Singularity.Session.Tests;

/// <summary>T6b on the assembled slice: tooling plan (A39, A30), quality programme (A41), training money (F15), economy over 384 months (DEBT-0018).</summary>
public class T6bAiCapabilityTests(ITestOutputHelper output)
{
    [Fact]
    public void Run384Months_ScriptedPlayer_ProductionDoesNotCollapse_PerActorPrint()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 3, ProductAi = false });
        var player = new PlayerScript(s);
        SliceWorld w = s.World;
        int actors = w.Scenario.ActorCount;
        var window = new long[actors];
        var windows = new List<long[]>();
        for (long t = 0; t < 384; t++)
        {
            player.Issue(t);
            s.StepThroughMonth(t);
            for (int a = 0; a < actors; a++)
            {
                for (int d = 0; d < w.ProductsLayout.Designs; d++)
                    window[a] += w.Products.Rate(a, d).Raw / Fx6.Scale;
            }
            if (t % 48 == 47)
            {
                windows.Add((long[])window.Clone());
                output.WriteLine($"months {t - 47}–{t}: " + string.Join("  ", Enumerable.Range(0, actors)
                    .Select(a => $"{w.Scenario.ActorId[a]}({w.Policy.Profile(a)}) {window[a]}")));
                Array.Clear(window);
            }
        }
        // Windows rewritten in T6c, reason contract v2.4 r4 (#128: v_exp with stage С and the max form) and the player's A41 line rule (a new
        // line takes over only at yield ≥ y_prod). Seed 3, scripted player (ProductAi off); T6b printed USA 690/1440/1440/1440/1470/1440/1440/1440 and
        // GBR 276/561/672/751/792/1025/1128/1180, now USA 690/1440/1440/1440/2250/2880/2880/2880 and GBR 276/561/672/751/792/1025/1128/1323.
        // The two actors with a working start (USA, GBR) produce in every 4-year window; FRA, SWE and SUN have no producing start
        // (no project, or a melt that is never admitted) and are printed, not asserted.
        foreach (string id in new[] { "USA", "GBR" })
        {
            int a = w.Scenario.ActorIndex(id);
            for (int i = 1; i < windows.Count; i++)
                Assert.True(windows[i][a] > 0, $"{id}: no output in the window {i}");
        }
        Assert.Equal(0, player.Refused);
    }

    [Fact]
    public void A30_KeepsTheTopMechProcessAdmitted_ForTheFirst120Months_WithNoPlayer()
    {
        var s = SliceTestData.Create(new SliceRunConfig { Seed = 5 });
        SliceWorld w = s.World;
        IndustryLayout l = w.IndustryLayout;
        int mech = l.Clusters.IndexOf(Cluster.MECH);
        int checkedMonths = 0;
        for (long t = 0; t < 120; t++)
        {
            // p* of the snapshot (A30): the admitted MECH process of the highest tier, the lower number on a tie.
            var pStar = new int[w.Scenario.ActorCount];
            for (int a = 0; a < pStar.Length; a++)
            {
                pStar[a] = -1;
                int tier = 0;
                for (int r = 0; r < l.ProcessCount; r++)
                {
                    if (l.ClusterIndex[r] == mech && w.IndustryInputs.KnowsProcess(a, r, t) && w.Industry.Yield(a, r, t).Yield > Fx6.Zero && l.Tier[r] > tier)
                    {
                        tier = l.Tier[r];
                        pStar[a] = r;
                    }
                }
            }
            s.StepThroughMonth(t);
            for (int a = 0; a < pStar.Length; a++)
            {
                if (pStar[a] < 0)
                    continue;
                // With no player the only wear of M that nothing counters is on p*'s dimensions: the support keeps it admitted a month later.
                Assert.True(w.Industry.Yield(a, pStar[a], t + 1).Yield > Fx6.Zero, $"month {t}, actor {a}: p* {l.Number[pStar[a]]} lost admission");
                checkedMonths++;
            }
        }
        Assert.True(checkedMonths > 100);
    }

    [Fact]
    public void TheToolingPlan_IsTheSameInEveryCallOfTheMonth_AndMStaysUnderTheCap()
    {
        var s = SliceTestData.Create(new SliceRunConfig { Seed = 2 });
        var player = new PlayerScript(s);
        SliceWorld w = s.World;
        IndustryLayout l = w.IndustryLayout;
        for (long t = 0; t < 120; t++)
        {
            player.Issue(t);
            // Before the step: the plan of month t from the snapshot, as every ToolingInvest call of the month will see it.
            var plans = new ToolingPlanResult[w.Scenario.ActorCount];
            for (int a = 0; a < plans.Length; a++)
            {
                Fx6 rest = w.Industry.RestActor(a, t);
                plans[a] = w.IndustryInputs.Plan(a, t, rest);
                ToolingPlanResult again = w.IndustryInputs.Plan(a, t, rest);
                Assert.Equal(plans[a].Total, again.Total);
                Assert.True(plans[a].Total <= rest + plans[a].CapBudget);
                Assert.Equal(rest + plans[a].CapBudget, plans[a].Total + plans[a].RestReturned + plans[a].TrainingBudget);
            }
            s.StepThroughMonth(t);
            for (int a = 0; a < plans.Length; a++)
            {
                Assert.Equal(plans[a].Rest, w.Industry.LastRest(a));
                for (int c = 0; c < l.Clusters.Length; c++)
                {
                    for (int d = 0; d < Dims3.Count; d++)
                    {
                        ToolingStep step = w.Industry.LastTooling(a, c, (Dimension)d);
                        Assert.Equal(plans[a].Invest(a, c, (Dimension)d) / step.CostM, step.Gain);
                        Assert.True(w.Industry.ToolingM(a, c, (Dimension)d) <= step.MCap || step.MNew <= step.MDec + Fx6.FromRaw(1));
                    }
                }
            }
        }
    }

    [Fact]
    public void QualityProgramme_P1ActsFromMonth1_WhenTheGrantIsFull_AndEndsAfter84Months()
    {
        var s = SliceTestData.Create(new SliceRunConfig { Seed = 1, ProfileCell = PolicyRules.CellOf(PolicyProfile.Quality, PolicyProfile.Mass, PolicyProfile.Adaptive) });
        SliceWorld w = s.World;
        int usa = w.Scenario.ActorIndex("USA"), gbr = w.Scenario.ActorIndex("GBR");
        Assert.True(w.Policy.QualityProgrammeActive(usa));
        Assert.False(w.IndustryInputs.QualityProgramme(usa, 0));                  // granted(t−1) = 0 at t0
        Assert.False(w.IndustryInputs.QualityProgramme(gbr, 0));
        s.StepThroughMonth(0);
        Assert.True(w.IndustryInputs.QualityProgramme(usa, 1));
        Assert.Equal(w.Policy.QualityEngineers(usa), w.Industry.Granted(usa, 0, w.QualityRequester));
        Assert.False(w.IndustryInputs.QualityProgramme(gbr, 1));                  // P2: no programme yet
        for (long t = 1; t <= 84; t++)
            s.StepThroughMonth(t);
        Assert.False(w.IndustryInputs.QualityProgramme(usa, 85));
        Assert.False(w.Policy.QualityProgrammeActive(usa));
    }

    [Fact]
    public void TrainingBudget_IsZeroWithoutATarget_AndEqualsTheUnspentCapBudgetForTheTarget()
    {
        // USA is P1 in this cell: the only doctrine with a training target before the why_not triggers (T6d).
        var s = SliceTestData.Create(new SliceRunConfig { Seed = 4, ProfileCell = 0 });
        var player = new PlayerScript(s);
        SliceWorld w = s.World;
        var adapter = new ProductsInputsAdapter(w);
        bool seenTarget = false;
        int spentMonths = 0;
        for (long t = 0; t < 384; t++)
        {
            player.Issue(t);
            // Before the step: the training money of month t from the snapshot, as step 7 will see it.
            var tbSum = new Fx6[w.Scenario.ActorCount];
            for (int a = 0; a < tbSum.Length; a++)
            {
                int target = w.Policy.TrainingTarget(a);
                for (int d = 0; d < w.ProductsLayout.Designs; d++)
                {
                    Fx6 tb = adapter.TrainingBudget(a, d, t);
                    if (d != target)
                        Assert.Equal(Fx6.Zero, tb);
                    else
                    {
                        seenTarget = true;
                        Assert.Equal(w.IndustryInputs.Plan(a, t, w.Industry.RestActor(a, t)).TrainingBudget, tb);
                    }
                    Assert.True(tb >= Fx6.Zero);
                    tbSum[a] += tb;
                }
            }
            s.StepThroughMonth(t);
            for (int a = 0; a < tbSum.Length; a++)
            {
                Assert.True(w.Products.TrainingSpent(a) <= tbSum[a], $"month {t}, actor {a}: tb_spent above the training budget");
                if (w.Products.TrainingSpent(a) > Fx6.Zero)
                    spentMonths++;
            }
        }
        Assert.True(seenTarget, "P1 never named a training target in 384 months");
        output.WriteLine($"actor-months with training money spent: {spentMonths}");
    }

    [Fact]
    public void WhyNotToolingRate_FollowsThePlan_AndTheEconomyLinkBudgetIncludesTrainingMoney()
    {
        var s = SliceTestData.Create(new SliceRunConfig { Seed = 3 });
        var player = new PlayerScript(s);
        SliceWorld w = s.World;
        var inputs = new WhyNotInputsAdapter(w);
        bool positive = false;
        for (long t = 0; t < 60; t++)
        {
            player.Issue(t);
            s.StepThroughMonth(t);
            for (int a = 0; a < w.Scenario.ActorCount; a++)
            {
                Assert.Equal(w.ProcurementBudget(a), inputs.ProcurementBudget(a, t + 1));
                for (int c = 0; c < w.IndustryLayout.Clusters.Length; c++)
                {
                    for (int d = 0; d < Dims3.Count; d++)
                    {
                        Fx6 rate = inputs.ToolingRate(a, c, (Dimension)d, t + 1);
                        Assert.True(rate >= Fx6.Zero);
                        positive |= rate > Fx6.Zero;
                    }
                }
            }
        }
        Assert.True(positive, "the tooling plan never raised M anywhere: ToolingRate is still a constant");
    }
}
