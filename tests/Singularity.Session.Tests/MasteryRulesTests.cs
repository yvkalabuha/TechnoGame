using Singularity.Core.Commands;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;
using Singularity.TestSupport;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0134 (#438-#440): Hcap(t) in the session, the series gate, the candidate close, the foresight and the mastery programme with its commands.
/// «Without the fix» is the arm of the same run with the rule off (<see cref="MasteryRules.None"/> or the one rule removed): each invariant test also
/// asserts that this arm breaks it, so the test fails on the old behaviour by construction.
/// </summary>
public class MasteryRulesTests
{
    private static SliceRunConfig Cfg(ulong seed) => HorizonConfigs.Config("Main", seed);

    private static string Digest(SliceSession s) => ColumnProjection.Hash(s, n => !n.StartsWith("run.", StringComparison.Ordinal));

    // ---- Hcap(t) in the session

    [Fact]
    public void Hcap_BeforeTheOnset_EqualsTheScenario_ForEveryActorAndDimension_AndTheOnsetIs1978_01()
    {
        SliceWorld w = SliceTestData.Create(Cfg(1)).World;
        Assert.Equal(388, w.IndustryParameters.HcapGrowthOnset);   // 1945-09 + 388 months
        for (int a = 0; a < w.Scenario.ActorCount; a++)
        {
            foreach (long m in new long[] { 0, 1, 200, 387, 388 })
            {
                for (int d = 0; d < Dims3.Count; d++)
                    Assert.Equal(w.Scenario.Hcap[a], w.IndustryInputs.Hcap(a, (Dimension)d, m));
            }
        }
    }

    [Fact]
    public void Hcap_AfterTheOnset_GrowsLinearly_ByTheSameAmountForEveryActor_UpToTheLimit()
    {
        SliceWorld w = SliceTestData.Create(Cfg(1)).World;
        IndustryParams p = w.IndustryParameters;
        for (int a = 0; a < w.Scenario.ActorCount; a++)
        {
            Fx6 h0 = w.Scenario.Hcap[a];
            Assert.Equal(h0 + p.HcapGrowthPerYear, w.IndustryInputs.Hcap(a, Dimension.Purity, 388 + 12));
            Assert.Equal(h0 + Fx6.FromRaw(p.HcapGrowthPerYear.Raw / 12), w.IndustryInputs.Hcap(a, Dimension.Precision, 389));
            Assert.Equal(h0 + p.HcapGrowthMax, w.IndustryInputs.Hcap(a, Dimension.QualityControl, 1023));
            Fx6 last = h0;
            for (long m = 380; m < 1024; m++)
            {
                Fx6 h = w.IndustryInputs.Hcap(a, Dimension.Purity, m);
                Assert.True(h >= last && h <= h0 + p.HcapGrowthMax);
                last = h;
            }
        }
    }

    [Fact]
    public void HcapGrowth_ZeroParameters_GiveTheScenarioEverywhere_AndNegativeOnesAreRefused()
    {
        SliceWorld off = SliceTestData.Create(Cfg(1) with { HcapGrowthPerYear = Fx6.Zero }).World;
        SliceWorld off2 = SliceTestData.Create(Cfg(1) with { HcapGrowthMax = Fx6.Zero }).World;
        foreach (SliceWorld w in new[] { off, off2 })
            Assert.Equal(w.Scenario.Hcap[0], w.IndustryInputs.Hcap(0, Dimension.Purity, 900));
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(Cfg(1) with { HcapGrowthPerYear = Fx6.FromRaw(-1) }));
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(Cfg(1) with { HcapGrowthMax = Fx6.FromRaw(-1) }));
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(Cfg(1) with { MasteryUpkeepMult = Fx6.FromRaw(999_999) }));
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(Cfg(1) with { MasteryXProg = Fx6.Zero }));
    }

    [Fact]
    public void MasteryParameters_AreInTheRunColumns_OnlyWhenGiven_AndChangeTheFingerprint()
    {
        SliceWorld plain = SliceTestData.Create(Cfg(1)).World;
        Assert.DoesNotContain(plain.Table.Columns, c => c.Name is "run.k.hcap_k" or "run.k.hcap_max" or "run.k.x_prog" or "run.k.upkeep_mult" or "run.mastery");
        SliceRunConfig given = Cfg(1) with
        {
            HcapGrowthPerYear = Fx6.FromRaw(200_000), HcapGrowthMax = Fx6.FromRaw(1_500_000), MasteryXProg = Fx6.FromRaw(800_000),
            MasteryUpkeepMult = Fx6.FromRaw(2_000_000), MasteryRules = MasteryRules.Gate,
        };
        SliceWorld w = SliceTestData.Create(given).World;
        foreach (string name in new[] { "run.k.hcap_k", "run.k.hcap_max", "run.k.x_prog", "run.k.upkeep_mult", "run.mastery" })
            Assert.Contains(w.Table.Columns, c => c.Name == name);
        Assert.Equal(Fx6.FromRaw(1_500_000), w.IndustryParameters.HcapGrowthMax);
        Assert.Equal(Fx6.FromRaw(800_000), w.ProductsParameters.Mastery.XProg);
        Assert.Equal(Fx6.FromRaw(2_000_000), w.IndustryParameters.UpkeepMult);
        Assert.Equal(MasteryRules.Gate, w.PolicyParameters.Mastery.Rules);
        Assert.NotEqual(Convert.ToHexString(Cfg(1).SnapshotFingerprint()), Convert.ToHexString(given.SnapshotFingerprint()));
    }

    [Fact]
    public void CalibrationWindow_IsBitwiseUnchanged_BeforeMonth388_AndHcapAndTheRulesAreConnectedAfterIt()
    {
        SliceSession main = SliceTestData.Create(Cfg(1));
        SliceSession noGrowth = SliceTestData.Create(Cfg(1) with { HcapGrowthPerYear = Fx6.Zero });                                   // the rules without Hcap(t)
        SliceSession none = SliceTestData.Create(Cfg(1) with { HcapGrowthPerYear = Fx6.Zero, MasteryRules = MasteryRules.None });      // the build before TASK-0134
        foreach (SliceSession s in new[] { main, noGrowth, none })
            s.StepThroughMonth(387);
        Assert.Equal(Digest(none), Digest(main));      // 1945-09 … 1977-12: the state is that of a run without Hcap(t) and without the rules
        Assert.Equal(Digest(none), Digest(noGrowth));
        for (long m = 388; m <= 500; m++)
        {
            foreach (SliceSession s in new[] { main, noGrowth, none })
                s.StepThroughMonth(m);
        }
        Assert.NotEqual(Digest(noGrowth), Digest(main));   // Hcap(t) reaches the culture rule
        Assert.NotEqual(Digest(none), Digest(main));
    }

    // ---- the invariants of the rules, by observation of whole runs

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    public void SeriesGate_NoSeriesStartsOnACultureYieldBelowYStart_AndTheOldBuildDoes(ulong seed)
    {
        MasteryRun with = MasteryRun.Execute(Cfg(seed), 1020);
        MasteryRun without = MasteryRun.Execute(Cfg(seed) with { MasteryRules = MasteryRules.None }, 1020);
        Assert.True(with.GateStarts > 0, "no series of a tier ≥ 5 design started after 1978: the gate is not exercised");
        Assert.Equal(0, with.GateViolations);
        Assert.True(without.GateViolations > 0, "the run without the rules never started a series below y_start: the test cannot tell the gate from its absence");
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(3UL)]
    public void CandidateLine_StaysBelowYProdForNoMoreThanTAbMonths_AndWithoutTheRuleItDoes(ulong seed)
    {
        int limit = SliceTestData.Create(Cfg(seed)).World.PolicyParameters.TAb + 2;   // the close is decided one month and applied the next
        MasteryRun with = MasteryRun.Execute(Cfg(seed), 1020);
        MasteryRun without = MasteryRun.Execute(Cfg(seed) with { MasteryRules = MasteryRules.All & ~MasteryRules.CandidateClose }, 1020);
        Assert.True(with.MaxMonthsBelow <= limit, $"a candidate line stayed below y_prod for {with.MaxMonthsBelow} months");
        Assert.True(without.MaxMonthsBelow > limit, $"without the close the longest stay was {without.MaxMonthsBelow} months: the test is vacuous");
    }

    [Fact]
    public void Programme_TakesAProcessToReadyForTheSeries_AndTheProgrammesAreTheOnlyDifference()
    {
        MasteryRun with = MasteryRun.Execute(Cfg(2), 1020);
        MasteryRun without = MasteryRun.Execute(Cfg(2) with { MasteryRules = MasteryRules.All & ~MasteryRules.Programme }, 1020);
        Assert.True(with.ProgrammeStarts > 0);
        Assert.Equal(with.ProgrammeStarts, with.ProgrammeReady + with.ProgrammeTimeouts);
        Assert.True(with.ProgrammeReady > 0, "no programme reached «ready for the series» (culture yield ≥ y_start)");
        Assert.Equal(0, without.ProgrammeStarts);
        Assert.Equal(0, without.ProgrammeReady);
    }

    [Fact]
    public void Foresight_NoProjectIsComposedWhoseTier5ProcessWillNotFitUnderHcap_AndRequirementVariantsAgree()
    {
        // Hcap frozen at the scenario value: a process with r_d above it can never be mastered; the foresight must not compose a project with it.
        SliceRunConfig frozen = Cfg(1) with { HcapGrowthMax = Fx6.Zero };
        SliceSession on = SliceTestData.Create(frozen), off = SliceTestData.Create(frozen with { MasteryRules = MasteryRules.None });
        SliceWorld w = on.World;
        long first = w.ProductsParameters.Mastery.FromMonth;
        int designs = w.ProductsLayout.Designs, actors = w.Scenario.ActorCount;
        var stageOn = new DesignStage[actors * designs];
        var stageOff = new DesignStage[actors * designs];
        int violationsOn = 0, violationsOff = 0, blocked = 0, shown = 0, shownOff = 0;
        // A project that appears in a design slot after the rules start, with a tier ≥ 5 process the actor cannot master by t + T_look.
        int Violations(SliceWorld x, DesignStage[] before, long t)
        {
            int n = 0;
            for (int a = 0; a < actors; a++)
            {
                for (int d = 0; d < designs; d++)
                {
                    DesignStage st = x.Products.Stage(a, d);
                    bool newProject = st == DesignStage.Project && before[a * designs + d] is DesignStage.None or DesignStage.Withdrawn;
                    before[a * designs + d] = st;
                    if (!newProject || t < first + 2)
                        continue;
                    n += x.Products.PrcSet(a, d).Count(p => x.ProductsLayout.ProcTier[x.ProductsLayout.ProcIndex(p)] >= MasteryParams.MinTier
                        && !MasteryChecks.Masterable(x, a, p, t - 1, x.ProductsParameters.Mastery.TLook));
                }
            }
            return n;
        }
        for (long t = 0; t < 700; t++)
        {
            on.StepThroughMonth(t);
            off.StepThroughMonth(t);
            violationsOn += Violations(on.World, stageOn, t);
            violationsOff += Violations(off.World, stageOff, t);
            for (int a = 0; a < actors && t >= first && t % 12 == 0; a++)
            {
                PresetVariants v = on.World.Policy.RequirementVariants(a, t + 1), vOff = off.World.Policy.RequirementVariants(a, t + 1);
                shown += v.Count > 0 ? 1 : 0;
                shownOff += vOff.Count > 0 ? 1 : 0;
                blocked += v.Gen > 0 && v.Count == 0 && vOff.Count > 0 ? 1 : 0;
            }
        }
        Assert.Equal(0, violationsOn);
        Assert.True(violationsOff > 0, "the run without the foresight composed no project with an unmasterable process: the test cannot tell the foresight from its absence");
        Assert.True(blocked > 0, "RequirementVariants never hid a Requirement the foresight blocks (the run without the rules shows it)");
        Assert.True(shown < shownOff);
    }

    [Fact]
    public void OneAndFourThreads_TwoRuns_GiveTheSameHashOver1020Months_WithProgrammesRunning()
    {
        MasteryRun one = MasteryRun.Execute(Cfg(2) with { Threads = 1, ChunkSize = 1 }, 1020);
        MasteryRun again = MasteryRun.Execute(Cfg(2) with { Threads = 1, ChunkSize = 1 }, 1020);
        MasteryRun four = MasteryRun.Execute(Cfg(2) with { Threads = 4, ChunkSize = 2 }, 1020);
        Assert.True(one.ProgrammeStarts > 0);
        HashAssert.Equal(one.Hash, again.Hash, "L1: two runs of 1020 months");
        HashAssert.Equal(one.Hash, four.Hash, "L2: threads 1 vs 4 over 1020 months");
    }
}
