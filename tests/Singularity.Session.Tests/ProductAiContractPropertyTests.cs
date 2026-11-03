using Singularity.Core.Products;
using Singularity.Kernel;
using Xunit.Abstractions;

namespace Singularity.Session.Tests;

/// <summary>
/// Contract v2.4 r4 invariants (§3.1, §4.1b V1–V4, §4.4e (8)–(12)) as properties over AI-driven runs: checked every month, every actor and design,
/// on several seeds (test-engineer review of TASK-0025). Complements <see cref="ProductAiSessionTests"/>, which holds the A40 print and the arms.
/// </summary>
public class ProductAiContractPropertyTests(ITestOutputHelper output)
{
    private static readonly ulong[] Seeds = [1, 2, 3, 5, 8, 13, 21, 34];
    private const int Months = 384;

    [Fact]
    public void AiRuns_JournalHasNoCommands_NoForce_And_LineColumnsKeepTheirContractRelations()
    {
        int seriesMonths = 0, cancelled = 0, withdrawnServed = 0, inService = 0, idleSeen = 0, noOutputSeen = 0;
        foreach (ulong seed in Seeds)
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 4, ChunkSize = 2 });
            SliceWorld w = s.World;
            int n = w.Scenario.ActorCount, designs = w.ProductsLayout.Designs;
            var stage = new DesignStage[n, designs];
            var idle = new int[n, designs];
            var built = new bool[n, designs];
            var canc = new bool[n, designs];
            for (long t = 0; t < Months; t++)
            {
                s.StepThroughMonth(t);
                for (int a = 0; a < n; a++)
                {
                    int lines = 0;
                    for (int d = 0; d < designs; d++)
                    {
                        string at = $"seed {seed} month {t} actor {a} design {d}";
                        DesignStage st = w.Products.Stage(a, d);
                        bool b = w.Products.BuiltAny(a, d), c = w.Products.Cancelled(a, d);
                        int ic = w.Products.IdleMonths(a, d);
                        Fx6 rate = w.Products.Rate(a, d);
                        Assert.False(w.Products.SeriesForced(a, d), $"{at}: AI forced a series");
                        // monotone flags: built_any and cancelled never fall; a stage never goes back
                        Assert.True(!built[a, d] || b, $"{at}: built_any fell");
                        Assert.True(!canc[a, d] || c, $"{at}: cancelled fell");
                        Assert.True(st >= stage[a, d], $"{at}: stage went back {stage[a, d]} -> {st}");
                        // (8)-(10): withdrawn-with-cancelled <=> never built; В and a served withdrawal imply built_any
                        if (st == DesignStage.Withdrawn)
                        {
                            Assert.True(c != b, $"{at}: withdrawn with cancelled={c} built_any={b}");
                            cancelled += c ? 1 : 0;
                            withdrawnServed += c ? 0 : 1;
                        }
                        if (c)
                            Assert.False(b, $"{at}: cancelled design has built_any");
                        if (st == DesignStage.InService)
                        {
                            Assert.True(b, $"{at}: В without built_any");
                            Assert.False(c, $"{at}: В and cancelled");
                            inService++;
                        }
                        // idle_c: 0 once output is positive; counts the launch month and every following zero-output month; frozen outside С
                        if (st == DesignStage.Series)
                        {
                            lines++;
                            seriesMonths++;
                            if (rate > Fx6.Zero)
                            {
                                Assert.Equal(0, ic);
                                Assert.True(b, $"{at}: output without built_any");
                            }
                            else
                            {
                                noOutputSeen++;
                                int expected = stage[a, d] == DesignStage.Series ? idle[a, d] + 1 : 1;
                                Assert.True(ic == expected, $"{at}: idle_c {ic}, expected {expected}");
                                idleSeen = Math.Max(idleSeen, ic);
                            }
                            if (!b)
                                Assert.True(ic >= 1, $"{at}: series without output has idle_c 0");
                        }
                        else if (stage[a, d] != DesignStage.Series || st != DesignStage.Series)
                        {
                            // outside С the counter is frozen at the value the last month in С left (a closed line keeps it)
                            Assert.True(ic == idle[a, d] || stage[a, d] == DesignStage.Series, $"{at}: idle_c moved outside С ({idle[a, d]} -> {ic})");
                        }
                        stage[a, d] = st;
                        idle[a, d] = ic;
                        built[a, d] = b;
                        canc[a, d] = c;
                    }
                    Assert.True(lines <= 1, $"seed {seed} month {t} actor {a}: {lines} serial lines (one line per actor, #125)");
                }
            }
            // the AI sends nothing through the journal: no player/harness command, hence no start_series(force)
            Assert.DoesNotContain("force", s.CommandLog);
            Assert.Empty(Singularity.Core.Commands.CommandCodec.DecodeLog(s.CommandLog));
        }
        output.WriteLine($"series actor-design-months {seriesMonths}, zero-output months {noOutputSeen}, max idle_c {idleSeen}, withdrawn cancelled {cancelled}, withdrawn served {withdrawnServed}, В design-months {inService}");
        Assert.True(seriesMonths > 0);
        Assert.True(noOutputSeen > 0, "no series without output was ever observed: the idle_c property is vacuous");
    }

    [Fact]
    public void AiRuns_RequirementIsRepeatedAtMostOncePerActorAndGeneration_AndTheCounterIsMonotone()
    {
        int repeats = 0;
        foreach (ulong seed in Seeds)
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed });
            SliceWorld w = s.World;
            int n = w.Scenario.ActorCount, gens = w.ProductsLayout.Generations;
            var prev = new int[n, gens + 1];
            for (long t = 0; t < Months; t++)
            {
                s.StepThroughMonth(t);
                for (int a = 0; a < n; a++)
                {
                    for (int g = 1; g <= gens; g++)
                    {
                        int r = w.Policy.Repeats(a, g);
                        Assert.InRange(r, 0, 1);
                        Assert.True(r >= prev[a, g], $"seed {seed} month {t} actor {a} g{g}: repeat counter fell");
                        prev[a, g] = r;
                    }
                }
            }
            for (int a = 0; a < n; a++)
                for (int g = 1; g <= gens; g++)
                    repeats += prev[a, g];
        }
        output.WriteLine($"{repeats} Requirement repeats over {Seeds.Length} seeds");
    }

    [Fact]
    public void AiRuns_ExpectedVolume_V1toV3_OnTheSnapshotOfEveryMonth()
    {
        int plannedNoOutput = 0, firstOutput = 0, firstOutputFlat = 0;
        foreach (ulong seed in Seeds)
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 2, ChunkSize = 3 });
            SliceWorld w = s.World;
            var il = w.IndustryLayout;
            int n = w.Scenario.ActorCount, designs = w.ProductsLayout.Designs;
            var firstBefore = new bool[n, designs];
            var vBefore = new Dictionary<(int, int), Fx6>();   // (actor, process) -> v_exp(home) before the step
            var stagesBefore = new string[n];
            for (long t = 0; t < Months; t++)
            {
                // v_exp of the snapshot t-1 as the next month's inputs see it
                vBefore.Clear();
                for (int a = 0; a < n; a++)
                {
                    stagesBefore[a] = string.Join(',', Enumerable.Range(0, designs).Select(d => (int)w.Products.Stage(a, d)));
                    for (int d = 0; d < designs; d++)
                    {
                        firstBefore[a, d] = !w.Products.BuiltAny(a, d);
                        foreach (int p in w.Products.PrcSet(a, d))
                            vBefore[(a, p)] = w.IndustryInputs.PlannedVolume(il.HomeSite(a), il.RowOf(p), t);
                    }
                }
                s.StepThroughMonth(t);
                for (int a = 0; a < n; a++)
                {
                    int home = il.HomeSite(a);
                    for (int d = 0; d < designs; d++)
                    {
                        DesignStage st = w.Products.Stage(a, d);
                        foreach (int p in w.Products.PrcSet(a, d))
                        {
                            int row = il.RowOf(p);
                            string at = $"seed {seed} month {t} actor {a} design {d} p{p}";
                            Fx6 procV = w.Industry.ProcV(home, row);
                            Fx6 v = w.IndustryInputs.PlannedVolume(home, row, t);
                            Fx6 plan = w.ProductsLayout.RRef * w.ProductsLayout.ProcN[w.ProductsLayout.ProcIndex(p)] * w.ProductsLayout.ProcLabour[w.ProductsLayout.ProcIndex(p)];
                            if (st is DesignStage.Project or DesignStage.Prototype or DesignStage.Series)
                            {
                                Assert.True(w.Products.Planned(a, p), $"{at}: not planned");
                                Assert.True(v >= procV, $"{at}: v_exp below proc_v");
                            }
                            if (st == DesignStage.Series && w.Products.Rate(a, d) == Fx6.Zero)
                            {
                                plannedNoOutput++;
                                Assert.True(v >= plan, $"{at}: V1 series without output has v_exp {v} < plan {plan}");
                            }
                            // other sites: v_exp = proc_v (V3)
                            for (int site = 0; site < il.SiteCount; site++)
                            {
                                if (il.SiteOwner[site] == a && site != home)
                                    Assert.Equal(w.Industry.ProcV(site, row), w.IndustryInputs.PlannedVolume(site, row, t));
                            }
                            // V1 continuity at the first output, when nothing else about the actor's stages changed: v_exp does not jump down below its pre-output value
                            // unless proc_v itself takes the max (it can only grow v_exp into proc_v + V_PO)
                            string stagesAfter = string.Join(',', Enumerable.Range(0, designs).Select(x => (int)w.Products.Stage(a, x)));
                            if (firstBefore[a, d] && w.Products.BuiltAny(a, d) && st == DesignStage.Series && stagesBefore[a].Split(',')[d] == ((int)DesignStage.Series).ToString()
                                && stagesAfter.Split(',').Select((x, i) => (x, i)).All(z => z.i == d || z.x == stagesBefore[a].Split(',')[z.i])
                                && vBefore.TryGetValue((a, p), out Fx6 vb))
                            {
                                firstOutput++;
                                if (v == vb)
                                    firstOutputFlat++;
                                Assert.True(v >= vb, $"{at}: v_exp fell {vb} -> {v} at the first output");
                            }
                        }
                    }
                }
            }
        }
        output.WriteLine($"V1: {plannedNoOutput} (series, process) months without output checked, {firstOutput} first outputs ({firstOutputFlat} with v_exp unchanged)");
        Assert.True(plannedNoOutput > 0, "no series without output was observed: V1 is vacuous");
    }

    [Fact]
    public void WithTheAiOn_L1_TwoRunsOfOneSeed_AndL2_Threads1_4_8_SameHashEveryMonth_OnSeveralSeeds()
    {
        foreach (ulong seed in new ulong[] { 4, 11, 29 })
        {
            var runs = new[]
            {
                SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 1, ChunkSize = 1 }),
                SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 1, ChunkSize = 1 }),
                SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 4, ChunkSize = 2 }),
                SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 8, ChunkSize = 1 }),
            };
            for (long t = 0; t < 300; t++)
            {
                foreach (SliceSession r in runs)
                    r.StepThroughMonth(t);
                Assert.All(runs, r => Assert.True(runs[0].LastHash == r.LastHash, $"seed {seed} month {t}: hash differs"));
            }
        }
    }
}
