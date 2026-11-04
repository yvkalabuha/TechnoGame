using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// ADR-0002 L1/L2 on the assembled slice: Knowledge + Industry (rule C-B, budget from the policy, planned volumes from the products) +
/// Products v2.4 + Policy, driven through <see cref="SliceSession"/> by a scripted player. The golden hash changes only for a reason
/// written in the commit message (a formula, data or layout change), never to make the test pass.
/// </summary>
public class LinkedRunTests
{
    /// <summary>
    /// State hash after month 59, seed 7, scripted player. First recorded at "composition root T6a": the world is now built by
    /// Singularity.Session (policy columns, budget growth, run.* and cmd.* columns, no V23 stand-in). It replaces the
    /// golden of ProductsCbLinkedRunTests (1792c856…), which belonged to the test-built world.
    /// Re-recorded in T6b (e495c5ab… before): the tooling plan A39/A30 now runs (before, ToolingInvest ≡ 0 and the capability decayed), the
    /// quality programme and training state are columns (schema v2), v_exp now includes proc_v (A37), and the scripted player no longer
    /// closes a working line for a newer one that does not produce yet.
    /// Re-recorded in T6c (d9e11cf5… before), reason contract v2.4 r4 (#128): three new product columns per design (schema v3), v_exp =
    /// max(proc_v, V_C) + V_PO with stage С planned (lines with output below r_ref now weigh r_ref in the allocation), and the
    /// player's line switch by the A41 rule (yield ≥ y_prod instead of Rate > 0).
    /// Re-recorded again in T6c (72768ab7… before), reason: schema v4 — the product-AI columns pol.dsn/start/close/line/rep and run.ai (the run is
    /// driven by the scripted player with ProductAi = false, so the trajectory is the same, only the layout of the state differs).
    /// Re-recorded in D1 (3d5d21be… before), reason: D1 — start-data package of spec v0.14 (SUN knows 58 and has AERO qc 3.50, FRA knows 54, USA/GBR culture
    /// cells, node 58 without 30, the hot_steel_g0 substitute in the hot-section slot and its preset rules).
    /// Re-recorded in T6c2 (7f7a499c… before), reason: T6c2 — E1: the start fleets of USA/GBR are a line in stage С producing from tick 0 (a scenario rule, so the
    /// scripted player's run changes too); the script orders its own project beside the start line.
    /// </summary>
    // Re-recorded in T-fleet (30a75d4f… before), reason: T-fleet — contract r6: new columns (fleet commands, sale money, quota, line_bind, mastered_run, run.fleet), schema v5.
    // D2 (#155, #159): start rule R-H-MS'; the data of the scenario changed, the model did not.
    private const string Golden60 = "b71072593a573f892bb2b54725b829b0f08ace4de67a0d082804479fdf51f157";

    private sealed class Run
    {
        public Run(ulong seed, int threads, int chunkSize)
        {
            Session = SliceSession.Create(SliceTestData.Data, new SliceRunConfig { Seed = seed, Threads = threads, ChunkSize = chunkSize, ProductAi = false });
            Player = new PlayerScript(Session);
        }

        public SliceSession Session { get; }
        public PlayerScript Player { get; }
        public long Reports;
        public bool EffortSeen, PlannedSeen;

        public void ThroughMonth(long month)
        {
            Player.Issue(month);
            Session.StepThroughMonth(month);
            SliceWorld w = Session.World;
            Reports += w.Industry.LastReports.Count;
            for (int s = 0; s < w.IndustryLayout.SiteCount && !EffortSeen; s++)
            {
                for (int r = 0; r < w.IndustryLayout.ProcessCount; r++)
                    EffortSeen |= w.Industry.ProcE(s, r) > Fx6.Zero;
            }
            for (int a = 0; a < w.Scenario.ActorCount; a++)
            {
                for (int d = 0; d < w.ProductsLayout.Designs; d++)
                    PlannedSeen |= w.Products.Stage(a, d) is DesignStage.Project or DesignStage.Prototype;
            }
        }
    }

    [Fact]
    public void L2_60Months_SameHashEveryMonth_ForThreadsAndChunks()
    {
        var runs = new[] { new Run(7, 1, 1), new Run(7, 4, 1), new Run(7, 3, 2), new Run(7, 8, 3) };
        for (long t = 0; t < 60; t++)
        {
            foreach (Run r in runs)
                r.ThroughMonth(t);
            Assert.All(runs, r => Assert.True(runs[0].Session.LastHash == r.Session.LastHash, $"month {t}: hash differs"));
        }
        Assert.True(runs[0].EffortSeen, "no process ever received effort money: the C-B path is not exercised");
        Assert.True(runs[0].PlannedSeen, "no project ever planned a process");
        Assert.True(runs[0].Reports > 0, "no line ever reported output");
        Assert.True(runs[0].Player.Accepted > 0);
        Assert.Equal(0, runs[0].Player.Refused);
    }

    [Fact]
    public void L2_Seeds1To20_Threads1_4_8_SameHashEveryMonth()
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var runs = new[] { new Run(seed, 1, 1), new Run(seed, 4, 1), new Run(seed, 8, 2) };
            for (long t = 0; t < 48; t++)
            {
                foreach (Run r in runs)
                    r.ThroughMonth(t);
                Assert.All(runs, r => Assert.True(runs[0].Session.LastHash == r.Session.LastHash, $"seed {seed}, month {t}: hash differs"));
            }
        }
    }

    [Fact]
    public void L1_TwoRuns_SameHash_AndMatchesGolden()
    {
        var a = new Run(7, 1, 1);
        var b = new Run(7, 4, 1);
        for (long t = 0; t < 60; t++)
        {
            a.ThroughMonth(t);
            b.ThroughMonth(t);
        }
        Assert.Equal(a.Session.LastHash, b.Session.LastHash);
        Assert.True(Golden60 == a.Session.LastHash, $"golden hash changed: {a.Session.LastHash}; update only with a reason written in the commit message");
    }

    [Fact]
    public void L1_384Months_TwoRunsAgree_AcrossThreads()
    {
        var a = new Run(3, 1, 1);
        var b = new Run(3, 8, 2);
        for (long t = 0; t < 384; t++)
        {
            a.ThroughMonth(t);
            b.ThroughMonth(t);
        }
        Assert.Equal(a.Session.LastHash, b.Session.LastHash);
        Assert.Equal(11_658, a.Session.Tick);                       // 1945-09-01 + 384 months: the month-383 step is on day 11 657
        Assert.Equal(new CivilDate(1977, 8, 2), a.Session.Date);
    }

    [Fact]
    public void Run_EffortIsConservedPerActorEveryMonth_AndInvariantsHold()
    {
        var run = new Run(2, 2, 1);
        SliceWorld w = run.Session.World;
        for (long t = 0; t < 60; t++)
        {
            // D_a(t) = share_D·budget_a(t), read before the monthly step that spends it.
            Fx6[] d = [.. Enumerable.Range(0, w.Scenario.ActorCount).Select(a => w.SharesOf(a).Effort * w.Policy.Budget(a))];
            run.ThroughMonth(t);
            for (int a = 0; a < w.Scenario.ActorCount; a++)
            {
                Fx6 e = Fx6.Zero;
                for (int r = 0; r < w.IndustryLayout.ProcessCount; r++)
                {
                    Assert.True(w.Industry.ProcE(a, r) >= Fx6.Zero);
                    e += w.Industry.ProcE(a, r);
                }
                Assert.True(w.Industry.LastRest(a) >= Fx6.Zero, $"month {t}, actor {a}: negative rest");
                Assert.Equal(d[a], e + w.Industry.LastRest(a));
                for (int g = 0; g < w.ProductsLayout.Designs; g++)
                {
                    Assert.True(w.Products.RetroPool(a, g) >= Fx6.Zero);
                    Assert.True(w.Products.LossAcc(a, g) >= Fx6.Zero);
                    if (!w.Products.SeriesForced(a, g))
                    {
                        Assert.Equal(Fx6.Zero, w.Products.RetroPool(a, g));
                        Assert.Equal(Fx6.Zero, w.Products.AccidentsLost(a, g));
                    }
                    Assert.True(w.Products.AccidentsLost(a, g) <= w.Products.InServiceBeforeAccidents(a, g));
                }
                Assert.True(w.Products.Spent(a) <= w.Policy.Budget(a));
            }
        }
    }

    [Fact]
    public void Run_PlannedVolumesFromProducts_ReachTheCapabilityLayer()
    {
        var run = new Run(7, 1, 1);
        SliceWorld w = run.Session.World;
        bool plannedSeen = false, volumeSeen = false;
        for (long t = 0; t < 30; t++)
        {
            run.ThroughMonth(t);
            for (int a = 0; a < w.Scenario.ActorCount; a++)
            {
                foreach (int r in w.IndustryLayout.Order)
                {
                    int number = w.IndustryLayout.Number[r];
                    plannedSeen |= w.Products.Planned(a, number);
                    volumeSeen |= w.Products.ExpectedVolume(a, number, Fx6.Zero, true) > Fx6.Zero;
                }
            }
        }
        Assert.True(plannedSeen && volumeSeen);
    }

    [Fact]
    public void Run_WorkforceAndAlloyFollowTheBudget()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 1 });
        SliceWorld w = s.World;
        var inputs = new IndustryInputsAdapter(w);
        int usa = w.Scenario.ActorIndex("USA");
        Assert.Equal(20_000 + 86, inputs.WorkforceAvailable(usa, 0, 0));
        Assert.Equal(200_000, inputs.WorkforceAvailable(usa, 1, 0));
        Assert.Equal(w.Scenario.AlloyCapAtStart(usa), inputs.AlloyCap(usa, 0));
        s.StepThroughMonth(59);
        Fx6 grown = w.Policy.Budget(usa);
        Assert.True(grown > w.BudgetAtStart[usa]);
        Assert.Equal(20_000 * grown.Raw / w.BudgetAtStart[usa].Raw + 86, inputs.WorkforceAvailable(usa, 0, 60));
        Assert.True(inputs.AlloyCap(usa, 60) > w.Scenario.AlloyCapAtStart(usa));
    }
}
