using System.Collections.Immutable;
using System.Security.Cryptography;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Knowledge;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>TASK-0055 (test API v4): the parameters of class «К» (spec §8.2) and the L_C/L_P tables of <see cref="SliceRunConfig"/>. A default is main; every other value changes the run.</summary>
public class CalibrationParamsTests
{
    // Hash of seed 1 after month 149 under the default config (as in SeriesSwitchTests).
    // Re-recorded in TASK-0110 (DEBT-0054), reason: schema v10 (new columns prd.altReached.*, novelty memory per actor); the old columns are unchanged per the projection tests in this branch (T6dOff, FleetAblation, ToolingUnmet), so only the new columns change the hash.
    // Re-recorded in TASK-0115: schema v11 (cmd.*.gen, pol.*.gen columns); the old columns are unchanged (projection tests).
    // Re-recorded in TASK-0121 (#423), reason: schema v12 (new columns prd.d*.cohort.1978…2030 and prd.d*.q.1: the cohort years to 2030, the second block of the q mask); the old columns are unchanged (HorizonTests projection at 48 points of the build before the change, GoldenProjectionAgainstMainTests), so only the new columns change the hash.
    // Re-recorded in TASK-0129 (fleet ageing, #431/#435-#437), reason: schema v13 (new column prd.d*.replCredit, in the hash from month 0); the old columns are unchanged to month 176 (projection tests: HorizonTests, T6dTests, FleetAblationTests, ToolingUnmetTests, GoldenProjectionAgainstMainTests), so only the new column changes these hashes.
    // Re-recorded in TASK-0133 (train 2: G5/G6 data + fleet ageing): nodes 62-81 and the G5/G6 alternatives change every trajectory; reason and list in the task file.
    private const string MainSeed1Month149 = "0dc571a6bffaef40e6922578270f39f48f7917e48a862c38af918ce5cceb78a1";

    private static SliceRunConfig Base(ulong seed = 1, int threads = 2) => new() { Seed = seed, Threads = threads };

    private static Fx6 F(long raw) => Fx6.FromRaw(raw);

    // Synthetic control tables: flat curves over the whole τ range, the same shape the Lab check uses.
    private static CapabilityControlTables Tables(long yield = 500_000) => new(
        [.. Enumerable.Repeat(0, 3).Select(_ => new CapabilityCurve([0, 767], [F(yield), F(yield)], [F(1_500_000), F(1_500_000)]))],
        [24, 36, 40, 60], [F(100_000), F(40_000), F(20_000), F(1_000)]);

    public static TheoryData<string> Parameters => new() { "k", "theta", "hmax", "kappa", "alpha", "xhalf", "deltah", "cm" };

    private static SliceRunConfig With(string name) => name switch
    {
        "k" => Base() with { CostDivisor = F(2_000_000) },
        "theta" => Base() with { ThetaStart = F(300_000) },
        "hmax" => Base() with { HMax = F(120_000) },
        "kappa" => Base() with { KappaQ10 = 256 },
        "alpha" => Base() with { Alpha = F(110_000) },
        "xhalf" => Base() with { XHalfByTier = [Fx6.FromInt(100), Fx6.FromInt(400), Fx6.FromInt(1250)] },
        "deltah" => Base() with { DeltaH = F(1_700) },
        "cm" => Base() with { CM = F(200_000) },
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    // Digest of the simulated state only: the run.* columns (the configuration) are left out, so a difference is a difference of the trajectory.
    private static string SimulationDigest(SliceSession s)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (Column c in s.World.Table.Columns)
        {
            if (!c.Name.StartsWith("run.", StringComparison.Ordinal))
            {
                hash.AppendData(System.Text.Encoding.UTF8.GetBytes(c.Name));
                switch (c)
                {
                    case LongColumn l:
                        foreach (long v in l.Current)
                            hash.AppendData(BitConverter.GetBytes(v));
                        break;
                    case IntColumn i:
                        foreach (int v in i.Current)
                            hash.AppendData(BitConverter.GetBytes(v));
                        break;
                    default:
                        throw new InvalidOperationException($"Column {c.Name}: unknown type.");
                }
            }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    [Fact]
    public void Defaults_GiveTheHashOfMain_AndNoRunKColumn()
    {
        SliceRunConfig explicitDefaults = Base() with { CostDivisor = Fx6.One, ThetaStart = null, HMax = null, KappaQ10 = null, Alpha = null, XHalfByTier = default, DeltaH = null, CM = null, CapabilityTables = null };
        SliceSession s = ViewSupport.Run(explicitDefaults, 150);
        HashAssert.Equal(MainSeed1Month149, s.LastHash);
        Assert.DoesNotContain(s.World.Table.Columns, c => c.Name.StartsWith("run.k.", StringComparison.Ordinal) || c.Name == "run.layer.capability");
        Assert.Equal(SliceTestData.Create(Base()).World.SchemaFingerprint(), SliceTestData.Create(explicitDefaults).World.SchemaFingerprint());
    }

    [Theory]
    [MemberData(nameof(Parameters))]
    public void EachKParameter_ChangesTheTrajectory_AndIsInTheRunColumns(string name)
    {
        SliceSession s = ViewSupport.Run(With(name), 150);
        // The run.* columns alone change LastHash, so the trajectory is compared without them.
        Assert.NotEqual(SimulationDigest(ViewSupport.Run(Base(), 150)), SimulationDigest(s));
        Assert.Contains(s.World.Table.Columns, c => c.Name.StartsWith("run.k.", StringComparison.Ordinal));
    }

    [Fact]
    public void CostMultG5AndG6_ChangeCostN_OnlyForTheseGenerations_AndAreInTheRunColumns()
    {
        var plain = new KnowledgeParams();
        SliceWorld w = SliceTestData.Create(Base() with { CostMultG5 = F(5_000_000), CostMultG6 = F(8_000_000) }).World;
        KnowledgeParams k = w.KnowledgeParameters;
        Assert.Equal(F(5_000_000) * plain.CostByType[(int)NodeType.APP], k.CostN(NodeType.APP, 5));
        Assert.Equal(F(8_000_000) * plain.CostByType[(int)NodeType.APP], k.CostN(NodeType.APP, 6));
        Assert.NotEqual(plain.CostN(NodeType.APP, 5), k.CostN(NodeType.APP, 5));
        for (int g = 0; g <= 4; g++)
            Assert.Equal(plain.CostN(NodeType.APP, g), k.CostN(NodeType.APP, g));   // G0…G4 are never touched
        Assert.Contains(w.Table.Columns, c => c.Name == "run.k.cost_mult_g5");
        Assert.Contains(w.Table.Columns, c => c.Name == "run.k.cost_mult_g6");
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(Base() with { CostMultG5 = Fx6.Zero }));
        // Only one of the two set: the other keeps its default.
        KnowledgeParams only5 = SliceTestData.Create(Base() with { CostMultG5 = F(5_000_000) }).World.KnowledgeParameters;
        Assert.Equal(plain.CostN(NodeType.APP, 6), only5.CostN(NodeType.APP, 6));
    }

    [Fact]
    public void KParameters_ReachTheCoreRecords_AtTheValueGiven()
    {
        SliceWorld w = SliceTestData.Create(Base() with
        {
            CostDivisor = F(2_000_000), ThetaStart = F(300_000), HMax = F(120_000), KappaQ10 = 256, Alpha = F(110_000),
            XHalfByTier = [Fx6.FromInt(1), Fx6.FromInt(2), Fx6.FromInt(3)], DeltaH = F(1_700), CM = F(200_000),
        }).World;
        Assert.Equal(F(750_000), w.KnowledgeParameters.CostByType[1]);      // 1.5 / 2
        Assert.Equal(F(1_000_000), w.KnowledgeParameters.CostByType[4]);    // 2 / 2
        Assert.Equal(F(300_000), w.KnowledgeParameters.ThetaStart);
        Assert.Equal(F(120_000), w.KnowledgeParameters.HMax);
        Assert.Equal(256, w.ProductsParameters.KappaQ10);
        Assert.Equal(F(110_000), w.IndustryParameters.Alpha);
        Assert.Equal(Fx6.FromInt(3), w.IndustryParameters.XHalfByTier[2]);
        Assert.Equal(F(1_700), w.IndustryParameters.DeltaH);
        Assert.Equal(F(200_000), w.IndustryParameters.CM);
    }

    [Fact]
    public void TheGridAndTheAlphaChannel_ActOnTheCalibratedValue()
    {
        SliceWorld w = SliceTestData.Create(Base() with
        {
            HMax = F(120_000), Alpha = F(110_000), Grid = new GridCell(GridStep.Mid, GridStep.Mid, GridStep.Lo), Channels = SliceChannels.Alpha,
        }).World;
        Assert.Equal(F(60_000), w.KnowledgeParameters.HMax);
        Assert.Equal(Fx6.Zero, w.IndustryParameters.Alpha);
    }

    [Fact]
    public void BadKParameters_AreRefused()
    {
        foreach (SliceRunConfig bad in new[]
        {
            Base() with { CostDivisor = Fx6.Zero }, Base() with { ThetaStart = F(-1) }, Base() with { HMax = F(2_000_000) }, Base() with { KappaQ10 = 2000 },
            Base() with { Alpha = F(-1) }, Base() with { XHalfByTier = [Fx6.One] }, Base() with { XHalfByTier = [Fx6.One, Fx6.One, Fx6.Zero] },
            Base() with { DeltaH = F(-5) }, Base() with { CM = Fx6.Zero },
        })
            Assert.ThrowsAny<ArgumentException>(() => SliceTestData.Create(bad));
    }

    [Theory]
    [InlineData(LayerControl.Lc)]
    [InlineData(LayerControl.Lp)]
    public void CapabilityLayers_RunOnTheirTables_L1L2_AndDifferFromMain(LayerControl layer)
    {
        SliceRunConfig c = Base(1, 1) with { Layer = layer, CapabilityTables = Tables() };
        SliceSession a = SliceTestData.CreateLegacy(c), b = SliceTestData.CreateLegacy(c with { Threads = 4, ChunkSize = 3 }), again = SliceTestData.CreateLegacy(c);
        for (long t = 0; t < 60; t++)
        {
            a.StepThroughMonth(t);
            b.StepThroughMonth(t);
            again.StepThroughMonth(t);
            HashAssert.Equal(a.LastHash, b.LastHash);          // L2
            HashAssert.Equal(a.LastHash, again.LastHash);      // L1
        }
        Assert.NotEqual(SimulationDigest(ViewSupport.RunLegacy(Base(1, 1), 60)), SimulationDigest(a));
        Assert.Contains(a.World.Table.Columns, col => col.Name == "run.layer.capability");
        Assert.Contains(a.World.Table.Columns, col => col.Name == "run.layer");
    }

    // Regression (TASK-0055): a control yield below 0.7 made WhyNot divide by X½_eff = 0 at tick 3287 (PolicySystem programme trigger).
    [Theory]
    [InlineData(10001UL)]
    [InlineData(10002UL)]
    public void Lc_RunsTheFullHorizon_WithAYieldBelowTheThreshold(ulong seed)
    {
        SliceSession s = ViewSupport.Run(Base(seed, 1) with { Layer = LayerControl.Lc, CapabilityTables = Tables() }, 384);
        Assert.True(s.Tick >= 11658);   // StepThroughMonth(383) ends at the month start; no crash through the horizon
    }

    [Fact]
    public void Lc_PastTheCrashTick_IsL1L2()
    {
        SliceRunConfig c = Base(10002, 1) with { Layer = LayerControl.Lc, CapabilityTables = Tables() };
        SliceSession a = ViewSupport.Run(c, 130), b = ViewSupport.Run(c with { Threads = 4, ChunkSize = 3 }, 130), again = ViewSupport.Run(c, 130);
        HashAssert.Equal(a.LastHash, b.LastHash);
        HashAssert.Equal(a.LastHash, again.LastHash);
    }

    [Fact]
    public void TheCurvesAndTheDesignTimes_AreInTheHash()
    {
        string h(CapabilityControlTables t) => ViewSupport.Run(Base(1, 1) with { Layer = LayerControl.Lc, CapabilityTables = t }, 12).LastHash;
        Assert.NotEqual(h(Tables(500_000)), h(Tables(600_000)));
        Assert.NotEqual(h(Tables()), h(Tables() with { TDesign = [24, 36, 40, 61] }));
    }

    [Fact]
    public void CapabilityTables_AreNeededExactlyWithLcOrLp_AndMustBeWellFormed()
    {
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(Base() with { Layer = LayerControl.Lc }));
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(Base() with { Layer = LayerControl.Lp }));
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(Base() with { CapabilityTables = Tables() }));
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(Base() with { Layer = LayerControl.Lc0, CapabilityTables = Tables() }));
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(Base() with { Layer = LayerControl.Lc, CapabilityTables = Tables() with { Curves = Tables().Curves.RemoveAt(2) } }));
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(Base() with { Layer = LayerControl.Lc, CapabilityTables = Tables() with { Rho = [F(1)] } }));
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(Base() with { Layer = LayerControl.Lp, CapabilityTables = Tables() with { TDesign = [24, 36, 40] , Rho = [F(1), F(1), F(1)] } }));
    }
}
