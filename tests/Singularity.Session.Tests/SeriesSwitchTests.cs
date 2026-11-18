using Singularity.Core.Knowledge;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>TASK-0046, SLICE_COMPOSITION §12.4: the switches of the series. A default value is main (same hash, same schema, no run column); every other one changes the run.</summary>
public class SeriesSwitchTests
{
    // Hash of seed 1 after month 149 under the default config (F17b YProdGolden[0], recorded before the switches existed).
    private const string MainSeed1Month149 = "6e49185f4bf45c314f3b5981aa9e5b9ea21bd498d9381e591289970411b68e75";

    private static SliceRunConfig Base(ulong seed = 1) => new() { Seed = seed, Threads = 2 };

    // Synthetic moments of the full model (the real ones come from the calibration build, T12): clocks of G1…G4, a zero offset of every node.
    private static LkTables Tables(int clockShift = 0)
    {
        SliceWorld w = SliceTestData.Create().World;
        var t = new LkTables(w.Scenario.ActorCount, w.Graph.Number.Max());
        for (int a = 0; a < t.ActorCount; a++)
        {
            for (int g = 1; g <= 4; g++)
            {
                t.Mean[a * 4 + g - 1] = 30 * g * g + clockShift;
                t.HalfWidth[a * 4 + g - 1] = 6;
            }
            foreach (int number in w.Graph.Number)
                t.Delta[t.DeltaIndex(a, number)] = 0;
        }
        return t;
    }

    private static SliceSession Run(SliceRunConfig c, int months) => ViewSupport.Run(c, months);

    [Fact]
    public void Defaults_GiveTheHashOfMain_TheSchemaOfMain_AndNoRunColumn()
    {
        SliceRunConfig explicitDefaults = Base() with
        {
            Layer = LayerControl.Full, LkTables = null, Grid = new GridCell(GridStep.Mid, GridStep.Mid, GridStep.Mid), EffortShare = EffortShareArm.X1,
            ViewMode = SliceViewMode.Calibration,
        };
        SliceSession s = Run(explicitDefaults, 150);
        HashAssert.Equal(MainSeed1Month149, s.LastHash);
        HashAssert.Equal(MainSeed1Month149, Run(Base() with { ViewMode = SliceViewMode.Full }, 150).LastHash);
        Assert.DoesNotContain(s.World.Table.Columns, c => c.Name.StartsWith("run.layer", StringComparison.Ordinal) || c.Name is "run.grid" or "run.effort_share");
        Assert.Equal(SliceTestData.Create(Base()).World.SchemaFingerprint(), SliceTestData.Create(explicitDefaults).World.SchemaFingerprint());
        Assert.Equal(9, SliceWorld.SchemaVersion);
    }

    [Fact]
    public void Defaults_384Months_AreEqualAcrossViewModeAndExplicitValues()
    {
        SliceSession a = Run(Base(3), 384);
        SliceSession b = Run(Base(3) with { Layer = LayerControl.Full, Grid = default, EffortShare = EffortShareArm.X1, ViewMode = SliceViewMode.Calibration, Threads = 4 }, 384);
        HashAssert.Equal(a.LastHash, b.LastHash);
        Assert.Equal(11_658, a.Tick);
    }

    // ---- layer

    [Fact]
    public void Layer_Lk_NeedsTables_AndTablesNeedLk()
    {
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(Base() with { Layer = LayerControl.Lk }));
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(Base() with { LkTables = Tables() }));
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(Base() with { Layer = LayerControl.Lc0, LkTables = Tables() }));
    }

    [Fact]
    public void Layer_Lk_RunsTheScheduleOfTheTables_AndKeepsI5()
    {
        SliceSession main = Run(Base(), 120);
        SliceSession lk = SliceTestData.Create(Base() with { Layer = LayerControl.Lk, LkTables = Tables() });
        SliceWorld w = lk.World;
        bool[,] startKnown = new bool[w.Scenario.ActorCount, w.Graph.Count];
        for (int a = 0; a < w.Scenario.ActorCount; a++)
        {
            for (int r = 0; r < w.Graph.Count; r++)
                startKnown[a, r] = w.Knowledge.IsKnown(a, r);
        }
        Fx6[] kd0 = [.. Enumerable.Range(0, w.Scenario.ActorCount).Select(a => w.Knowledge.KD(a, 0))];
        int opened = 0;
        for (long t = 0; t < 120; t++)
        {
            lk.StepThroughMonth(t);
            for (int a = 0; a < w.Scenario.ActorCount; a++)
            {
                for (int r = 0; r < w.Graph.Count; r++)
                {
                    bool known = w.Knowledge.IsKnown(a, r);
                    Assert.True(!startKnown[a, r] || known, $"month {t}: a start node was lost");
                    opened += known && !startKnown[a, r] ? 1 : 0;
                    if (!known)
                        continue;
                    // I5: a known node has its requirements known (every group has a known member).
                    for (int g = w.Graph.Requires.GroupStart[r]; g < w.Graph.Requires.GroupStart[r + 1]; g++)
                        Assert.Contains(w.Graph.Requires.GroupMembers(g).ToArray(), m => w.Knowledge.IsKnown(a, m));
                }
            }
        }
        Assert.True(opened > 0, "the schedule opened no node in 120 months");
        // The model of knowledge is off: K_d and progress are not updated.
        Assert.Equal(kd0, Enumerable.Range(0, w.Scenario.ActorCount).Select(a => w.Knowledge.KD(a, 0)));
        HashAssert.NotEqual(main.LastHash, lk.LastHash);
        Assert.Contains(w.Table.Columns, c => c.Name == "run.layer");
        Assert.Contains(w.Table.Columns, c => c.Name == "run.layer.tables");
        Assert.NotEqual(ViewSupport.TrajectoryDigest(main), ViewSupport.TrajectoryDigest(lk));
    }

    [Fact]
    public void Layer_Lk_TablesAreHashed_AndTheRunIsDeterministic()
    {
        string H(LkTables t, int threads = 1) => Run(Base() with { Layer = LayerControl.Lk, LkTables = t, Threads = threads }, 60).LastHash;
        Assert.Equal(H(Tables()), H(Tables(), 4));
        Assert.NotEqual(H(Tables()), H(Tables(clockShift: 1)));
        string fresh = SliceTestData.Create(Base() with { Layer = LayerControl.Lk, LkTables = Tables() }).LastHash;
        HashAssert.Equal(fresh, SliceTestData.Create(Base() with { Layer = LayerControl.Lk, LkTables = Tables() }).LastHash);
        HashAssert.NotEqual(fresh, SliceTestData.Create(Base() with { Layer = LayerControl.Lk, LkTables = Tables(1) }).LastHash);
    }

    [Fact]
    public void Layer_Lk0_OpensTheNodesAtTheWindowMiddles_AndKeepsI5()
    {
        SliceSession main = Run(Base(), 100);
        SliceSession lk0 = SliceTestData.Create(Base() with { Layer = LayerControl.Lk0 });
        SliceWorld w = lk0.World;
        int[] known = new int[400];
        for (long t = 0; t < 360; t++)
        {
            lk0.StepThroughMonth(t);
            known[t] = Enumerable.Range(0, w.Graph.Count).Sum(r => w.Knowledge.IsKnown(0, r) ? 1 : 0);
            for (int r = 0; r < w.Graph.Count; r++)
            {
                if (!w.Knowledge.IsKnown(0, r))
                    continue;
                for (int g = w.Graph.Requires.GroupStart[r]; g < w.Graph.Requires.GroupStart[r + 1]; g++)
                    Assert.Contains(w.Graph.Requires.GroupMembers(g).ToArray(), m => w.Knowledge.IsKnown(0, m));
            }
        }
        // G2 lag 24 after month 64 = 88, G3 36 after 172 = 208, G4 48 after 292 = 340: the count jumps there and only there.
        Assert.Equal(known[0], known[86]);
        Assert.True(known[89] > known[86], "no node opened at month 88");
        Assert.Equal(known[89], known[205]);
        Assert.True(known[209] > known[205], "no node opened at month 208");
        Assert.Equal(known[209], known[338]);
        Assert.True(known[341] > known[338], "no node opened at month 340");
        Assert.Contains(w.Table.Columns, c => c.Name == "run.layer");
        Assert.NotEqual(ViewSupport.TrajectoryDigest(main), ViewSupport.TrajectoryDigest(Run(Base() with { Layer = LayerControl.Lk0 }, 100)));
    }

    [Fact]
    public void Layer_Lc0_HasConstantYieldAndCost_ForEveryProcess()
    {
        SliceSession main = Run(Base(), 100);
        SliceSession lc0 = Run(Base() with { Layer = LayerControl.Lc0 }, 100);
        foreach (ActorView a in lc0.View.Actors)
        {
            foreach (ProcessView p in a.Nodes.Where(n => n.Process is not null).Select(n => n.Process!.Value))
            {
                Assert.Equal(Fx6.One, p.Yield);
                Assert.Equal(Fx6.One, p.UnitCostMult);
            }
        }
        Assert.Contains(main.View.Actors.SelectMany(a => a.Nodes).Where(n => n.Process is not null), n => n.Process!.Value.Yield != Fx6.One);
        HashAssert.NotEqual(main.LastHash, lc0.LastHash);
        Assert.NotEqual(ViewSupport.TrajectoryDigest(main), ViewSupport.TrajectoryDigest(lc0));
        HashAssert.Equal(lc0.LastHash, Run(Base() with { Layer = LayerControl.Lc0, Threads = 4 }, 100).LastHash);
    }

    // ---- grid

    [Fact]
    public void Grid_AppliesTheStepToItsAxisOnly()
    {
        KnowledgeParams k = new();
        Singularity.Core.Industry.IndustryParams i = new();
        (KnowledgeParams k0, Singularity.Core.Industry.IndustryParams i0) = SliceWorld.ApplyGrid(default, k, i);
        Assert.True(ReferenceEquals(k, k0) || k == k0);
        Assert.Equal(i, i0);
        foreach (GridStep step in new[] { GridStep.Lo, GridStep.Hi })
        {
            int factor = step == GridStep.Hi ? 2 : 1;
            int divisor = step == GridStep.Lo ? 2 : 1;
            (KnowledgeParams kc, Singularity.Core.Industry.IndustryParams ic) = SliceWorld.ApplyGrid(new GridCell(step, GridStep.Mid, GridStep.Mid), k, i);
            Assert.Equal(i.KCatch.Raw * factor / divisor, ic.KCatch.Raw);
            Assert.Equal(k.KDiff, kc.KDiff);
            Assert.Equal(k.HMax, kc.HMax);
            // k_diff axis (#190): the price share r = 1 - k_diff is scaled; frozen r* = 0,5 gives k_diff 0,75 / 0,5 / 0 exactly.
            (kc, ic) = SliceWorld.ApplyGrid(new GridCell(GridStep.Mid, GridStep.Mid, GridStep.Mid) with { KDiff = step }, k, i);
            Assert.Equal(step == GridStep.Hi ? 0 : 750_000, kc.KDiff.Raw);
            Assert.Equal(k with { KDiff = kc.KDiff }, kc);
            Assert.Equal(i, ic);
            (kc, ic) = SliceWorld.ApplyGrid(new GridCell(GridStep.Mid, GridStep.Mid, step), k, i);
            Assert.Equal(k.HMax.Raw * factor / divisor, kc.HMax.Raw);
            Assert.Equal(k with { HMax = kc.HMax }, kc);
        }
        // The frozen values halve exactly: no truncation on the current numbers.
        Assert.Equal(0, k.KDiff.Raw % 2 + k.HMax.Raw % 2 + i.KCatch.Raw % 2);
        Assert.Throws<OverflowException>(() => SliceWorld.ApplyGrid(new GridCell(GridStep.Mid, GridStep.Mid, GridStep.Hi), k with { HMax = Fx6.FromRaw(long.MaxValue / 2 + 1) }, i));
    }

    [Fact]
    public void Grid_CodeIsNineKcPlusThreeKdPlusHm_ForAll27Points()
    {
        var codes = new List<int>();
        foreach (GridStep kc in Enum.GetValues<GridStep>())
        {
            foreach (GridStep kd in Enum.GetValues<GridStep>())
            {
                foreach (GridStep hm in Enum.GetValues<GridStep>())
                    codes.Add(new GridCell(kc, kd, hm).Code);
            }
        }
        Assert.Equal(Enumerable.Range(0, 27), codes.OrderBy(c => c));
        Assert.Equal(0, default(GridCell).Code);
    }

    [Theory]
    [InlineData(GridStep.Lo, GridStep.Mid, GridStep.Mid)]
    [InlineData(GridStep.Hi, GridStep.Mid, GridStep.Mid)]
    [InlineData(GridStep.Mid, GridStep.Lo, GridStep.Mid)]
    [InlineData(GridStep.Mid, GridStep.Mid, GridStep.Lo)]
    [InlineData(GridStep.Mid, GridStep.Mid, GridStep.Hi)]
    [InlineData(GridStep.Hi, GridStep.Lo, GridStep.Hi)]
    public void Grid_EachAxisChangesTheRun(GridStep kc, GridStep kd, GridStep hm)
    {
        SliceSession main = Run(Base(), 120);
        var cell = new GridCell(kc, kd, hm);
        SliceSession s = Run(Base() with { Grid = cell }, 120);
        HashAssert.NotEqual(main.LastHash, s.LastHash);
        Assert.NotEqual(ViewSupport.TrajectoryDigest(main), ViewSupport.TrajectoryDigest(s));
        Assert.Contains(s.World.Table.Columns, c => c.Name == "run.grid");
        // The parameters of the world are the stepped ones.
        (KnowledgeParams k, Singularity.Core.Industry.IndustryParams i) = SliceWorld.ApplyGrid(cell, new KnowledgeParams(), new Singularity.Core.Industry.IndustryParams());
        Assert.Equal((k.KDiff, k.HMax, i.KCatch), (s.World.KnowledgeParameters.KDiff, s.World.KnowledgeParameters.HMax, s.World.IndustryParameters.KCatch));
        Assert.NotEqual((new KnowledgeParams().KDiff, new KnowledgeParams().HMax, new Singularity.Core.Industry.IndustryParams().KCatch),
            (k.KDiff, k.HMax, i.KCatch));
    }

    [Theory]
    [InlineData(99)]
    [InlineData(-1)]
    [InlineData(6)]
    public void Layer_UnsupportedValue_IsRefusedAtAssembly(int value)
    {
        Assert.False(Enum.IsDefined((LayerControl)value));
        Assert.Throws<InvalidOperationException>(() => SliceTestData.Create(Base() with { Layer = (LayerControl)value }));
    }

    [Theory]
    [InlineData(LayerControl.Lc)]
    [InlineData(LayerControl.Lp)]
    public void Layer_LcAndLp_AreRefusedWithoutTheirTables(LayerControl layer)
    {
        var e = Assert.Throws<ArgumentException>(() => SliceTestData.Create(Base() with { Layer = layer }));
        Assert.Contains("CapabilityTables", e.Message);
        Assert.Equal(4, (int)LayerControl.Lc);
        Assert.Equal(5, (int)LayerControl.Lp);
    }

    [Theory]
    [InlineData(3, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, 99)]
    public void Grid_UndefinedStep_IsRefusedAtAssembly(int kCatch, int kDiff, int hMax)
    {
        var cell = new GridCell((GridStep)kCatch, (GridStep)kDiff, (GridStep)hMax);
        Assert.Throws<ArgumentOutOfRangeException>(() => SliceWorld.ApplyGrid(cell, new KnowledgeParams(), new Singularity.Core.Industry.IndustryParams()));
        Assert.Throws<ArgumentOutOfRangeException>(() => SliceTestData.Create(Base() with { Grid = cell }));
    }

    [Fact]
    public void Layer_EveryValueExceptLcAndLp_IsSupported()
    {
        foreach (LayerControl l in Enum.GetValues<LayerControl>().Where(l => l is not (LayerControl.Lc or LayerControl.Lp)))
        {
            SliceRunConfig c = Base() with { Layer = l, LkTables = l == LayerControl.Lk ? new LkTables(5, SliceTestData.Create().World.Graph.Number.Max()) : null };
            Assert.NotNull(SliceTestData.Create(c));
        }
    }

    [Fact]
    public void Grid_KDiffAxis_IsExact_AndAll27CellsAssembleAndLive()
    {
        var kd = new List<long>();
        foreach (GridStep step in Enum.GetValues<GridStep>())
            kd.Add(SliceWorld.ApplyGrid(new GridCell(GridStep.Mid, step, GridStep.Mid), new KnowledgeParams(), new Singularity.Core.Industry.IndustryParams()).K.KDiff.Raw);
        Assert.Equal(new long[] { 500_000, 750_000, 0 }, kd.ToArray());
        foreach (GridStep c in Enum.GetValues<GridStep>())
        {
            foreach (GridStep d in Enum.GetValues<GridStep>())
            {
                foreach (GridStep h in Enum.GetValues<GridStep>())
                    Assert.NotNull(Run(Base() with { Grid = new GridCell(c, d, h) }, 6).LastHash);
            }
        }
    }

    [Fact]
    public void Grid_RunColumnHoldsTheCode_AndTheRunIsDeterministic()
    {
        var cell = new GridCell(GridStep.Hi, GridStep.Lo, GridStep.Hi);
        SliceSession a = SliceTestData.Create(Base() with { Grid = cell });
        var column = (IntColumn)a.World.Table.Columns.Single(c => c.Name == "run.grid");
        Assert.All(column.Current.ToArray(), v => Assert.Equal(9 * 2 + 3 * 1 + 2, v));
        HashAssert.Equal(Run(Base() with { Grid = cell }, 60).LastHash, Run(Base() with { Grid = cell, Threads = 4, ChunkSize = 2 }, 60).LastHash);
    }

    // ---- effort share

    [Fact]
    public void EffortShare_X1_ReturnsTheSameParameters()
    {
        var p = new PolicyParams();
        Assert.Same(p, SliceWorld.ApplyEffortShare(EffortShareArm.X1, p));
    }

    [Theory]
    [InlineData(EffortShareArm.X0, 0)]
    [InlineData(EffortShareArm.X05, 1)]
    [InlineData(EffortShareArm.X2, 2)]
    [InlineData(EffortShareArm.X4, 4)]
    public void EffortShare_ScalesD_AndKeepsTheSumExact_ForEveryProfileAndTheProgramme(EffortShareArm arm, int times)
    {
        var p = new PolicyParams();
        PolicyParams q = SliceWorld.ApplyEffortShare(arm, p);
        BudgetShares[] before = [.. p.SharesByProfile, p.MassProgrammeShares], after = [.. q.SharesByProfile, q.MassProgrammeShares];
        for (int i = 0; i < before.Length; i++)
        {
            long d = before[i].Effort.Raw;
            Assert.Equal(arm == EffortShareArm.X05 ? d / 2 : d * times, after[i].Effort.Raw);
            Assert.Equal(Fx6.One, after[i].Sum);
            Assert.True(after[i].IsValid);
            // The other shares scale by (1 − D′)/(1 − D) (floor); procurement takes the residue, so it is within a few raw units of the exact value.
            Int128 scale = Fx6.Scale - after[i].Effort.Raw;
            Assert.Equal((long)((Int128)before[i].Research.Raw * scale / (Fx6.Scale - d)), after[i].Research.Raw);
            Assert.Equal((long)((Int128)before[i].Quality.Raw * scale / (Fx6.Scale - d)), after[i].Quality.Raw);
            long exact = (long)((Int128)before[i].Procurement.Raw * scale / (Fx6.Scale - d));
            Assert.InRange(after[i].Procurement.Raw - exact, 0, 3);
        }
        Assert.Equal(p.SharesByProfile.Length, q.SharesByProfile.Length);
        Assert.Equal(0, p.SharesByProfile[1].Quality.Raw);   // P2 has no programme outside it: stays 0
        Assert.Equal(0, q.SharesByProfile[1].Quality.Raw);
    }

    [Theory]
    [InlineData(EffortShareArm.X0)]
    [InlineData(EffortShareArm.X05)]
    [InlineData(EffortShareArm.X2)]
    [InlineData(EffortShareArm.X4)]
    public void EffortShare_EachArmChangesTheRun(EffortShareArm arm)
    {
        SliceSession main = Run(Base(), 100);
        SliceSession s = Run(Base() with { EffortShare = arm }, 100);
        HashAssert.NotEqual(main.LastHash, s.LastHash);
        Assert.NotEqual(ViewSupport.TrajectoryDigest(main), ViewSupport.TrajectoryDigest(s));
        var column = (IntColumn)s.World.Table.Columns.Single(c => c.Name == "run.effort_share");
        Assert.All(column.Current.ToArray(), v => Assert.Equal((int)arm, v));
        // The profile shares in force are the arm's.
        Assert.Equal(s.World.PolicyParameters.Shares(s.View.Actors[0].Profile).Effort, s.World.Policy.Shares(0).Effort);
        // fv_budget stays the constant of main: the arm changes the shares only.
        Assert.Equal(main.World.PolicyParameters.FvBudget, s.World.PolicyParameters.FvBudget);
    }

    [Fact]
    public void EffortShare_FiveArmsGiveFiveRuns_AndAreDeterministic()
    {
        var hashes = new List<string>();
        foreach (EffortShareArm arm in Enum.GetValues<EffortShareArm>())
        {
            string h = Run(Base() with { EffortShare = arm }, 60).LastHash;
            HashAssert.Equal(h, Run(Base() with { EffortShare = arm, Threads = 4, ChunkSize = 2 }, 60).LastHash);
            hashes.Add(h);
        }
        Assert.Equal(5, hashes.Distinct().Count());
    }

    /// <summary>R-12-2: EFFORT_SHARE ×4 lowers the procurement share to 0,52 of its value; the liveness checks L-C1…L-C3a of the build must hold for every arm on the scenario.</summary>
    [Theory]
    [InlineData(EffortShareArm.X0)]
    [InlineData(EffortShareArm.X05)]
    [InlineData(EffortShareArm.X1)]
    [InlineData(EffortShareArm.X2)]
    [InlineData(EffortShareArm.X4)]
    public void EffortShare_EveryArm_PassesPolicyLiveness_OnTheScenario(EffortShareArm arm)
    {
        SliceSession s = SliceTestData.Create(Base() with { EffortShare = arm });   // the world constructor runs Validate and ValidateStartUpkeep
        Assert.NotNull(s.View);
        // And the run lives: some line produces and some design reaches service in 150 months under the arm.
        for (long t = 0; t < 150; t++)
            s.StepThroughMonth(t);
        Assert.Contains(s.View.Actors.SelectMany(a => a.Designs), d => d.Stage is Singularity.Core.Products.DesignStage.Series or Singularity.Core.Products.DesignStage.InService);
    }
}
