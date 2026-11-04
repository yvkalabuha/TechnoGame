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

    // Hashes after month 149 of seeds 1…5 (AI on, threads 2). Until TASK-0032 part 2 ADMIT (with V0) equalled the build before F17b (commit 29cd914) bit for bit;
    // re-recorded there only for the new column tool_unmet («tool_unmet column», F33): by the old columns the trajectory is unchanged (ToolingUnmetTests).
    private static readonly string[] AdmitGolden =
    [
        "92daa0419d6922f998e0458e58d0a53d86a5b334e679c668d265527a782cb573",
        "f6b133ff8193d5bdb8b8a1d95653b74c3943e7387066d0ed5998ea84fdfc8edf",
        "9cc8e665c72ee98a25c38533fddb11b4be9656c76e9381bdaf11e39deaf26ff4",
        "88e1bdcc57dedc27e09f959c0cc12d92fee69a7fcda0ee45d1f6cc8286250445",
        "51ea610b5c2d8b1a88d75645ce35a9f52554e932fae1ed303f110910f63eca23",
    ];

    // Recorded in F17b (TASK-0030); re-recorded in TASK-0032: part 1 (A39 «V5 default», #163: the default tooling target is V5), part 2 («tool_unmet column», F33).
    private static readonly string[] YProdGolden = [
        "5bf4c94e6faf72bb28a2c0dd8e4cf374312c5e5c76106e6e41bf0022e719d8fa",
        "a65bf874852f4cd36dcc18954f5ac23d3e6e28d8968937d63c3f1123271a673f",
        "5ff5c7fb8c20c1127eb4b17b6c021f13a75840cd4fbcce3fb5bba403d14ef6e9",
        "b7f5e87c02c98187d4033766d701ed1846973b66e1c27d7b0412f7019acf17b3",
        "635821a245b3bab9963a30c37bb726455f5a6c0c85e678b3f66d7785866fd455",
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
