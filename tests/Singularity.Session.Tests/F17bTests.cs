using Singularity.Core.Policy;
using Xunit.Abstractions;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>F17b (#160, spec A41): the working line runs at min(r_AI, r_keep) while two lines coexist; LINE_CLOSE = ADMIT keeps one line.</summary>
public class F17bTests(ITestOutputHelper output)
{
    private static readonly ulong[] Seeds = [1, 2, 3];

    [Fact]
    public void YProd_WhileTwoLinesCoexist_TheWorkingLineOutputIsAtMostRKeep_AndItIsTheOlderGeneration()
    {
        int coexistence = 0, rateBound = 0, candidateProduces = 0;
        foreach (ulong seed in Seeds)
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 2 });
            SliceWorld w = s.World;
            Fx6 rKeep = w.IndustryParameters.RKeep;
            int designs = w.ProductsLayout.Designs;
            for (long t = 0; t < 384; t++)
            {
                s.StepThroughMonth(t);
                for (int a = 0; a < w.Scenario.ActorCount; a++)
                {
                    var lines = new List<int>();
                    for (int d = 0; d < designs; d++)
                    {
                        if (w.Products.Stage(a, d) == DesignStage.Series)
                            lines.Add(d);
                    }
                    if (lines.Count != 2)
                        continue;
                    int working = w.Products.PlatformGen(a, lines[0]) <= w.Products.PlatformGen(a, lines[1]) ? lines[0] : lines[1];
                    coexistence++;
                    int candidate = lines[0] == working ? lines[1] : lines[0];
                    if (w.Products.Bind(a, working) == LineBind.Rate)
                    {
                        // acceptance p.1: LineRate(W) = min(r_AI, r_keep) = r_keep raw-exact whenever the rate (not money or alloy) binds
                        Assert.Equal(rKeep, w.Products.Rate(a, working));
                        rateBound++;
                    }
                    if (w.Products.Rate(a, candidate) > Fx6.Zero)
                        candidateProduces++;
                    Assert.Equal(candidate, new PolicyInputsAdapter(w).NewestSeriesDesign(a, t));   // training target = N (A41)
                    Assert.True(w.Products.Rate(a, working) <= rKeep, $"seed {seed} month {t} actor {a}: working line output {w.Products.Rate(a, working)} above r_keep {rKeep}");
                }
            }
        }
        Assert.True(coexistence > 0, "no two-line month in the runs: the hold was not exercised");
        Assert.True(rateBound > 0, "the working line was never bound by its rate");
        Assert.True(candidateProduces > 0, "the candidate never produced in a coexistence month (it gets the remainder of the money)");
        output.WriteLine($"coexistence months {coexistence}, W bound by rate {rateBound}, N producing {candidateProduces}");
    }

    [Fact]
    public void Admit_KeepsOneLinePerActor_AsBeforeF17b()
    {
        foreach (ulong seed in Seeds)
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 2, LineClose = LineCloseRule.Admit });
            SliceWorld w = s.World;
            for (long t = 0; t < 384; t++)
            {
                s.StepThroughMonth(t);
                for (int a = 0; a < w.Scenario.ActorCount; a++)
                {
                    int lines = 0;
                    for (int d = 0; d < w.ProductsLayout.Designs; d++)
                        lines += w.Products.Stage(a, d) == DesignStage.Series ? 1 : 0;
                    Assert.True(lines <= 1, $"seed {seed} month {t} actor {a}: {lines} lines under ADMIT");
                }
            }
        }
    }

    // TASK-0050 + TASK-0051: all hashes below re-recorded once (hash without tick; RNG key actor·2^16 + node).
    // Hashes after month 149 of seeds 1…5 (AI on, threads 2). Until TASK-0032 part 2 ADMIT (with V0) equalled the build before F17b (commit 29cd914) bit for bit;
    // re-recorded there only for the new column tool_unmet («tool_unmet column», F33): by the old columns the trajectory is unchanged (ToolingUnmetTests).
    private static readonly string[] AdmitGolden =
    [
        "5182522187ea58ccb5e0ef2e7656b485565710e5112fb71ccc560058c66ac80a",
        "80503279220d3557668e1d14344446434512f61162c8c74e30b5c4dca3a5ad79",
        "821840913e093333a7357fc92ed732eaac493108aa98ff7d96af7033026b2448",
        "e07008cd104c053bf5b4fedfdcf466249ac89f7efe848fc7cf7a1e7e3efa05f3",
        "d79add416b9be263ce95c76cb3d9592af60a00146c1efbc74923ceadc2870db5",
    ];

    // Recorded in F17b (TASK-0030); re-recorded in TASK-0033, reason: T6d (columns of the licence and the disruption, run.t6d, the events themselves; both arms), then «exposure to data» (the scenario hash is in the run columns); in TASK-0032: part 1 (A39 «V5 default», #163: the default tooling target is V5), part 2 («tool_unmet column», F33).
    private static readonly string[] YProdGolden = [
        "9171e388fb488c671d4fda694d4e6eba8b27faf5dd8329dca350c5bc39a4a151",
        "53becbd4a6ec207cab1a3428242d717c367b5d9b9d2f1787364afecbf27b586a",
        "bbb6ac79200966629b2218b05c71e125ef0246408f002c3fafe9b000b4b7a508",
        "8989f391426b621b78505f6b9e7fa1c9d88379c06b1896cead528d9b9e93f6ee",
        "ca39e8ffefd9502d200d4b03712c33b72e02bd2bae3c4cee21520be8b9363986",
    ];

    private static string HashOf(ulong seed, LineCloseRule rule)
    {
        // ADMIT is the pre-F17b build, whose tooling pass 2 was V0; YPROD runs under the default V5 (A39 «V5 default», #163).
        ToolingTargetSpec target = rule == LineCloseRule.Admit ? ToolingTargetSpec.Parse("V0") : new ToolingTargetSpec();
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 2, LineClose = rule, ToolingTarget = target });
        for (long t = 0; t < 150; t++)
            s.StepThroughMonth(t);
        return s.LastHash;
    }

    [Fact]
    public void L1_Seeds1To5_Admit_IsBitIdenticalToTheBuildBeforeF17b_AndYProdHasItsGolden()
    {
        for (int i = 0; i < 5; i++)
        {
            ulong seed = (ulong)i + 1;
            string admit = HashOf(seed, LineCloseRule.Admit);
            Assert.True(AdmitGolden[i] == admit, $"seed {seed}: ADMIT hash {admit} differs from the golden (ADMIT, V0)");
            string yprod = HashOf(seed, LineCloseRule.YProd);
            Assert.True(YProdGolden[i] == yprod, $"seed {seed}: YPROD golden changed: {yprod}; update only with a reason written in the commit message");
            Assert.NotEqual(admit, yprod);   // LINE_CLOSE is part of the run hash (bit 12 of run.fleet)
        }
    }

    [Fact]
    public void Fleet_WhileTwoLinesCoexist_NoCommandIsDecidedWithTheWorkingLineAsL()
    {
        int candidateL = 0;
        foreach (ulong seed in Seeds)
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 2 });
            SliceWorld w = s.World;
            int gens = w.ProductsLayout.Generations, designs = w.ProductsLayout.Designs;
            int n = w.Scenario.ActorCount;
            for (long t = 0; t < 384; t++)
            {
                var before = new List<int>[n];
                var pair = new long[n][,];
                for (int a = 0; a < n; a++)
                {
                    before[a] = [.. Enumerable.Range(0, designs).Where(d => w.Products.Stage(a, d) == DesignStage.Series)];
                    pair[a] = new long[gens + 1, gens + 1];
                    for (int gl = 1; gl <= gens; gl++)
                        for (int gd = 1; gd <= gens; gd++)
                            pair[a][gl, gd] = w.Policy.FleetCommandsByPair(a, gl, gd);
                }
                s.StepThroughMonth(t);
                for (int a = 0; a < n; a++)
                {
                    if (before[a].Count != 2 || before[a].Any(d => w.Products.Stage(a, d) != DesignStage.Series))
                        continue;
                    int g0 = w.Products.PlatformGen(a, before[a][0]), g1 = w.Products.PlatformGen(a, before[a][1]);
                    if (g0 == g1)
                        continue;
                    int gW = Math.Min(g0, g1), gN = Math.Max(g0, g1);
                    for (int gd = 1; gd <= gens; gd++)
                    {
                        Assert.True(w.Policy.FleetCommandsByPair(a, gW, gd) == pair[a][gW, gd], $"seed {seed} month {t} actor {a}: a fleet command with the working line as L");
                        candidateL += (int)(w.Policy.FleetCommandsByPair(a, gN, gd) - pair[a][gN, gd]);
                    }
                }
            }
        }
        output.WriteLine($"fleet commands with L = N during coexistence: {candidateL}");
    }

    [Fact]
    public void LineClose_ParsesTheTwoValues_AndAnUnknownValueIsAManifestError()
    {
        Assert.Equal(LineCloseRule.YProd, LineCloseRules.Parse("YPROD"));
        Assert.Equal(LineCloseRule.Admit, LineCloseRules.Parse("ADMIT"));
        Assert.Throws<ArgumentException>(() => LineCloseRules.Parse("yprod"));
        Assert.Equal(LineCloseRule.YProd, new SliceRunConfig().LineClose);
    }
}
