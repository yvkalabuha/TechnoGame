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
            SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = seed, Threads = 2 });
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
                    Assert.Equal(candidate, new PolicyInputsAdapter(w).NewestSeriesDesign(a, t)!.Value.Slot);   // training target = N (A41)
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
            SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = seed, Threads = 2, LineClose = LineCloseRule.Admit });
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
    // Re-recorded in TASK-0058 step 5 (all hashes below), reason: I-OC + A41 SLOT as the build (#242, #243, #245), fielded_L and the column prd.d*.q (#247), schema v8: the trajectory changed by decision, so ADMIT/V0 no longer equals the build before F17b.
    // Hashes after month 149 of seeds 1…5 (AI on, threads 2). Until TASK-0032 part 2 ADMIT (with V0) equalled the build before F17b (commit 29cd914) bit for bit;
    // re-recorded there only for the new column tool_unmet («tool_unmet column», F33): by the old columns the trajectory is unchanged (ToolingUnmetTests).
    // Re-recorded in TASK-0110 (DEBT-0054), reason: schema v10 (new columns prd.altReached.*, novelty memory per actor); the old columns are unchanged per the projection tests in this branch (T6dOff, FleetAblation, ToolingUnmet), so only the new columns change the hash.
    // Re-recorded in TASK-0099 (ADR-0005 p. 3), reason: schema v9 (new columns prd.d*.generation, parentGen, prd.lp.g*.gen, StaleHandle counter); the run ends before the first slot reuse, the old columns are unchanged (SlotReuseL2Tests.NoSlotIsReused_BeforeTheMonthOfAnyRewrittenGolden, projection tests).
    // Re-recorded in TASK-0115 (ADR-0005 p. 3, DEBT-0052), reason: schema v11 (new columns cmd.dsn.*.gen, cmd.flt.*.gen, cmd.series.*.gen, cmd.close.*.gen, pol.dsn.*.gen, pol.flt.*.gen, pol.start.*.gen, pol.close.*.gen); the run ends before the first slot reuse, so no handle is stale and the old columns are unchanged (projection tests T6dOff, FleetAblation, ToolingUnmet; SlotReuseL2Tests), only the new columns change the hash.
    // Re-recorded in TASK-0121 (#423), reason: schema v12 (new columns prd.d*.cohort.1978…2030 and prd.d*.q.1: the cohort years to 2030, the second block of the q mask); the old columns are unchanged (HorizonTests projection at 48 points of the build before the change, GoldenProjectionAgainstMainTests), so only the new columns change the hash.
    // Re-recorded in TASK-0129 (fleet ageing, #431/#435-#437), reason: schema v13 (new column prd.d*.replCredit, in the hash from month 0); the old columns are unchanged to month 176 (projection tests: HorizonTests, T6dTests, FleetAblationTests, ToolingUnmetTests, GoldenProjectionAgainstMainTests), so only the new column changes these hashes.
    // Re-recorded in TASK-0134 (train 2: Hcap(t), mastery), reason: schema v14 (new columns prd.prog.*, pol.prog.*, pol.below.*, cmd.prog.*, prd.rejected.BadProgramme, all zero before 1978-01); the old columns are unchanged to month 387 (MasteryIdentityTests, HorizonTests, GoldenProjectionAgainstMainTests), so only the new columns change this hash.
    // Re-recorded in TASK-0180 (#555): requires of 11 early G5/G6 nodes (62, 63, 82, 83, 86, 90, 91, 92, 95, 97, 99) changed, so knowledge opens later and every trajectory moves; no schema change.
    // Re-recorded in TASK-0182: schema v15 (new column pol.flt.gFront, the G_front a fleet sale was priced at); the old columns are unchanged (the identity and projection tests pass with the column left out of the projection).
    // Re-recorded in TASK-0193: 55 new nodes 160-214 (1990-2030) and 3 new processes change every trajectory; old columns and schema layout rules unchanged.
    private static readonly string[] AdmitGolden =
    [
        "38375bd7d0b88f90ec4316d6f02194bb5f11a7b064344499b2f475ecddea2ad6",
        "b3bac879983f52b74761b0907822ffaa224b165e1f42c7efc65a35c57edda712",
        "05f9f1633fb3de323525eb29888d9ea0629119c267785f959a4da525262d9da7",
        "de9939ddf8a6037a0f02398e325fba758645ae8e0615ff829bac39ecfb3a9766",
        "ae6a6d41c6b9177506268bbc03879c226b02de290dd03a6987cec6f9ea200805",
    ];

    // Recorded in F17b (TASK-0030); re-recorded in TASK-0033, reason: T6d (columns of the licence and the disruption, run.t6d, the events themselves; both arms), then «exposure to data» (the scenario hash is in the run columns); in TASK-0032: part 1 (A39 «V5 default», #163: the default tooling target is V5), part 2 («tool_unmet column», F33).
    // Re-recorded in TASK-0110 (DEBT-0054), reason: schema v10 (prd.altReached.*); the old columns are unchanged per the projection tests in this branch (T6dOff, FleetAblation, ToolingUnmet), so only the new columns change the hash.
    // Re-recorded in TASK-0115 (ADR-0005 p. 3, DEBT-0052), reason: schema v11 (new columns cmd.dsn.*.gen, cmd.flt.*.gen, cmd.series.*.gen, cmd.close.*.gen, pol.dsn.*.gen, pol.flt.*.gen, pol.start.*.gen, pol.close.*.gen); the run ends before the first slot reuse, so no handle is stale and the old columns are unchanged (projection tests T6dOff, FleetAblation, ToolingUnmet; SlotReuseL2Tests), only the new columns change the hash.
    // Re-recorded in TASK-0121 (#423), reason: schema v12 (new columns prd.d*.cohort.1978…2030 and prd.d*.q.1: the cohort years to 2030, the second block of the q mask); the old columns are unchanged (HorizonTests projection at 48 points of the build before the change, GoldenProjectionAgainstMainTests), so only the new columns change the hash.
    // Re-recorded in TASK-0129 (fleet ageing, #431/#435-#437), reason: schema v13 (new column prd.d*.replCredit, in the hash from month 0); the old columns are unchanged to month 176 (projection tests: HorizonTests, T6dTests, FleetAblationTests, ToolingUnmetTests, GoldenProjectionAgainstMainTests), so only the new column changes these hashes.
    // Re-recorded in TASK-0133 (train 2: G5/G6 data + fleet ageing): nodes 62-81 and the G5/G6 alternatives change every trajectory; reason and list in the task file.
    // Re-recorded in TASK-0134 (train 2: Hcap(t), mastery), reason: schema v14 (new columns prd.prog.*, pol.prog.*, pol.below.*, cmd.prog.*, prd.rejected.BadProgramme, all zero before 1978-01); the old columns are unchanged to month 387 (MasteryIdentityTests, HorizonTests, GoldenProjectionAgainstMainTests), so only the new columns change this hash.
    // Re-recorded in TASK-0147 (train 2, schema train of the day): nodes 82-99 of the G5/G6 rev 3.1 chains and the ahead-of-time cost defaults (k 0.02, pow 2, max 10, #465) change every trajectory on the current data; the old columns stay identical on the 61-node data with k = 0 (identity tests, CreateLegacyNeutral); explanation: docs/simulation/golden-rerecord-train2.md.
    // Re-recorded in TASK-0180 (#555): requires of 11 early G5/G6 nodes (62, 63, 82, 83, 86, 90, 91, 92, 95, 97, 99) changed, so knowledge opens later and every trajectory moves; no schema change.
    // Re-recorded in TASK-0188 (#575): 57 new nodes 100-156 (computing, electronics, materials, rockets, space, nuclear) and 9 new processes change every trajectory; old columns and schema layout rules unchanged.
    private static readonly string[] YProdGolden = [
        "1f80dcf09c3ecac873b9d3e9dc1e3bc74fc8f167e110cbe4e98268d4f1710f6a",
        "d71060a91fc82457bbeb023aeb2ab061354ec0fbca5be052acf0ab8360e22cc8",
        "c81a155e4dc496e03b4018f710306d007a7e8472b102347e0560f37395e7f426",
        "1348425efd72919a619e8b4551844b41f4e03c68ed85446671087783aa18faab",
        "437f06dbafa423768b82e7f5dec8705d109f14130ad71e0a2d9ead1902b49afb",
    ];

    private static string HashOf(ulong seed, LineCloseRule rule)
    {
        // ADMIT is the pre-F17b build, whose tooling pass 2 was V0; YPROD runs under the default V5 (A39 «V5 default», #163).
        ToolingTargetSpec target = rule == LineCloseRule.Admit ? ToolingTargetSpec.Parse("V0") : new ToolingTargetSpec();
        SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = seed, Threads = 2, LineClose = rule, ToolingTarget = target });
        for (long t = 0; t < 150; t++)
            s.StepThroughMonth(t);
        // StaleHandle is outside the old-column projections: prove here that the golden runs never hit it.
        for (int a = 0; a < s.World.Scenario.ActorCount; a++)
            Assert.Equal(0, s.World.Products.RejectionCount(a, CommandRejection.StaleHandle));
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
            SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = seed, Threads = 2 });
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
        Assert.Equal(LineCloseRule.YProd, SliceRunConfig.Legacy.LineClose);
    }
}
