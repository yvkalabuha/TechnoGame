using Singularity.Core.Commands;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;
using Xunit.Abstractions;

namespace Singularity.Session.Tests;

/// <summary>
/// The product AI on the assembled slice (TASK-0025, spec §11 T6c): the invariants of the AI's decisions over 384 months, the print of the
/// give-up rule on seeds 1…20 (acceptance 10: an input to the comparison with the expectations of A40 «Живость ветки С», not a check), the arms
/// FORCE and DERIV (smoke on two seeds), the line switch of the player proxy (A41), determinism with the AI on.
/// </summary>
public class ProductAiSessionTests(ITestOutputHelper output)
{
    private const int TAb = 60;

    private sealed class Observed
    {
        public readonly int Actors;
        public int OFirings, CFirings;
        public readonly int[] OByActor, CByActor, CultureByActor, OtherByActor;
        public int Forced, MultiLine, BadClose, CancelledDesigns, SeenSeries, TwoLines;
        /// <summary>Stage-О give-ups by class (A40 v0.15) and launches of the AI whose processes were not admitted in the decision snapshot (F17: 0).</summary>
        public int ODefects, OProcesses, StartsNotAdmitted;
        public readonly List<string> Notes = new();

        public Observed(int actors)
        {
            Actors = actors;
            OByActor = new int[actors];
            CByActor = new int[actors];
            CultureByActor = new int[actors];
            OtherByActor = new int[actors];
        }
    }

    /// <summary>Runs <paramref name="months"/> months of the AI-driven world; observes the decisions and the invariants of the AI month by month.</summary>
    private static Observed Observe(SliceRunConfig config, int months, Action<SliceSession, long>? beforeStep = null, Action<SliceSession, long>? afterStep = null)
    {
        SliceSession s = SliceTestData.Create(config);
        SliceWorld w = s.World;
        int n = w.Scenario.ActorCount, designs = w.ProductsLayout.Designs;
        var o = new Observed(n);
        // Per line in С: for each month of С whether its zero output has a cultural cause (min yield of the prc_set is 0, or no alloy and no smelt is admitted).
        var culture = new List<bool>[n, designs];
        var idleBefore = new int[n, designs];
        var stageBefore = new DesignStage[n, designs];
        var admittedBefore = new bool[n, designs];
        var classBefore = new GiveUpClass[n, designs];
        var yMinBefore = new Fx6[n, designs];
        for (long t = 0; t < months; t++)
        {
            beforeStep?.Invoke(s, t);
            for (int a = 0; a < n; a++)
            {
                for (int d = 0; d < designs; d++)
                {
                    idleBefore[a, d] = w.Products.IdleMonths(a, d);
                    stageBefore[a, d] = w.Products.Stage(a, d);
                    if (stageBefore[a, d] == DesignStage.Prototype)
                    {
                        admittedBefore[a, d] = new PolicyProductsAdapter(w).Admitted(a, d, t);
                        classBefore[a, d] = w.Policy.ClassifyGiveUp(a, d, t);
                    }
                    if (stageBefore[a, d] != DesignStage.Series)
                        continue;
                    culture[a, d] ??= new List<bool>();
                    Fx6 min = Fx6.One;
                    foreach (int p in w.Products.PrcSet(a, d))
                    {
                        Fx6 y = w.ProductsInputs.AdmittedYield(a, p, t);
                        if (y < min)
                            min = y;
                    }
                    bool noSmelt = new[] { 39, 40, 41 }.All(p => w.ProductsInputs.AdmittedYield(a, p, t) == Fx6.Zero);
                    yMinBefore[a, d] = min;
                    culture[a, d].Add(min == Fx6.Zero || w.Industry.Alloy(a, t).Supply == Fx6.Zero && noSmelt);
                }
            }
            s.StepThroughMonth(t);
            afterStep?.Invoke(s, t);
            for (int a = 0; a < n; a++)
            {
                bool anyStart = false;
                for (int d = 0; d < designs; d++)
                {
                    anyStart |= w.Policy.AiStart(a, d, t + 1) is not null;
                    if (w.Policy.AiStart(a, d, t + 1) is not null && w.Products.SeriesForced(a, d))
                        o.Forced++;
                    if (w.Policy.AiStart(a, d, t + 1) is not null && !admittedBefore[a, d])
                        o.StartsNotAdmitted++;
                }
                foreach (DesignCommand c in w.Policy.AiDesignCommands(a, t + 1))
                {
                    if (c.Kind != DesignCommandKind.Cancel)
                        continue;
                    o.OFirings++;
                    o.OByActor[a]++;
                    if (classBefore[a, c.Target] == GiveUpClass.Processes)
                        o.OProcesses++;
                    else
                        o.ODefects++;
                    // (6): the AI cancels only a project in О for T_ab months or more.
                    if (stageBefore[a, c.Target] != DesignStage.Prototype || t - w.Products.StageOMonth(a, c.Target) < TAb)
                        o.BadClose++;
                }
                for (int d = 0; d < designs; d++)
                {
                    if (w.Policy.AiClose(a, d, t + 1) is null)
                        continue;
                    // (11): a close is either a part of the launch bundle (some start in the same month) or idle_c ≥ T_ab by the snapshot.
                    bool idle = stageBefore[a, d] == DesignStage.Series && idleBefore[a, d] >= TAb;
                    // F17b: a close of the working line by y_prod needs a second line in С whose y_min reached y_prod in the snapshot.
                    bool handover = stageBefore[a, d] == DesignStage.Series
                        && Enumerable.Range(0, designs).Any(e => e != d && stageBefore[a, e] == DesignStage.Series && yMinBefore[a, e] >= w.ProductsParameters.YProd);
                    if (!anyStart && !idle && !handover)
                        o.BadClose++;
                    if (!idle)
                        continue;
                    o.CFirings++;
                    o.CByActor[a]++;
                    List<bool> history = culture[a, d]!;
                    bool cultural = history.Count >= TAb && history.TakeLast(TAb).All(x => x);
                    (cultural ? o.CultureByActor : o.OtherByActor)[a]++;
                    o.Notes.Add($"t={t} {w.Scenario.ActorId[a]}({w.Policy.Profile(a)}) d{d} g{w.Products.PlatformGen(a, d)} " +
                        $"built={w.Products.BuiltAny(a, d)} forced={w.Products.SeriesForced(a, d)} cause={(cultural ? "culture" : "OTHER")}");
                }
                int lines = 0;
                for (int d = 0; d < designs; d++)
                {
                    lines += w.Products.Stage(a, d) == DesignStage.Series ? 1 : 0;
                    o.CancelledDesigns += w.Products.Cancelled(a, d) ? 1 : 0;
                }
                o.MultiLine += lines > 2 ? 1 : 0;      // F17b: at most two lines (working and candidate)
                o.TwoLines += lines == 2 ? 1 : 0;
                o.SeenSeries += lines;
            }
        }
        return o;
    }

    [Fact]
    public void Seeds1To20_384Months_TheAiHoldsItsInvariants_AndTheGiveUpPrintIsMade()
    {
        var total = new Observed(5);
        string[] ids = [];
        for (ulong seed = 1; seed <= 20; seed++)
        {
            Observed o = Observe(new SliceRunConfig { Seed = seed, Threads = 4, ChunkSize = 2 }, 384);
            ids = [.. SliceTestData.Create(new SliceRunConfig { Seed = seed }).World.Scenario.ActorId];
            Assert.Equal(0, o.Forced);                                   // the AI never forces a series
            Assert.Equal(0, o.MultiLine);                                // F17b: at most two lines per actor
            Assert.Equal(0, o.BadClose);                                 // invariants (6) and (11)
            Assert.Equal(0, o.ODefects);                                 // L-O0 (P0, A40 v0.15): no give-up of the class «defects» in main
            Assert.Equal(0, o.StartsNotAdmitted);                        // F17: every AI start has admitted = true in the decision snapshot
            total.OFirings += o.OFirings;
            total.OProcesses += o.OProcesses;
            total.ODefects += o.ODefects;
            total.CFirings += o.CFirings;
            for (int a = 0; a < 5; a++)
            {
                total.OByActor[a] += o.OByActor[a];
                total.CByActor[a] += o.CByActor[a];
                total.CultureByActor[a] += o.CultureByActor[a];
                total.OtherByActor[a] += o.OtherByActor[a];
            }
            total.Notes.AddRange(o.Notes.Select(x => $"seed {seed}: {x}"));
        }
        output.WriteLine("A40 print, seeds 1…20, 384 months (cancel_design = branch О; close_line after idle_c ≥ T_ab = branch С):");
        output.WriteLine($"{"actor",-6}{"branch О",10}{"branch С",10}{"cause: culture",16}{"cause: other",14}");
        for (int a = 0; a < 5; a++)
            output.WriteLine($"{ids[a],-6}{total.OByActor[a],10}{total.CByActor[a],10}{total.CultureByActor[a],16}{total.OtherByActor[a],14}");
        output.WriteLine($"total  {total.OFirings,9} {total.CFirings,9}; branch О by class: processes {total.OProcesses}, defects {total.ODefects}");
        foreach (string note in total.Notes)
            output.WriteLine(note);
        // L-C0 (P0): a firing of branch С whose cause is not the culture is a defect of the implementation or of the estimate L-C3.
        Assert.Equal(0, total.OtherByActor.Sum());
    }

    // (13, contract §4.4e r12) the reserve branch О PROCESSES is reachable. σ = 0 and k_catch = 0 (no world_lead channel) so that neither spill nor catch admit a process without money.
    [Fact]
    public void A40ProcessesBranch_FiresWithoutEffortMoney()
    {
        int fired = 0;
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
        {
            Observed o = Observe(new SliceRunConfig
            {
                Seed = seed, Threads = 2, EffortShare = EffortShareArm.X0, Channels = SliceChannels.Sigma | SliceChannels.KCatch,
            }, 384);
            output.WriteLine($"seed {seed}: branch О {o.OFirings} (processes {o.OProcesses}, defects {o.ODefects})");
            Assert.Equal(0, o.BadClose);                                  // (6) still holds: only О for T_ab months or more
            fired += o.OProcesses;
        }
        Assert.True(fired > 0, "branch О PROCESSES never fired with share_D = 0, σ = 0 and no catch-up");
    }

    // (13, second fixture) money is there but the culture does not grow within T_ab (α = 0, σ = 0, k_catch = 0): the process stays unadmitted and the branch fires.
    [Fact]
    public void A40ProcessesBranch_FiresWhenTabExpiresBeforeAdmission()
    {
        int fired = 0;
        foreach (ulong seed in new ulong[] { 1, 2, 3 })
        {
            var config = new SliceRunConfig
            {
                Seed = seed, Threads = 2, Channels = SliceChannels.Alpha | SliceChannels.Sigma | SliceChannels.KCatch,
            };
            SliceSession s = SliceTestData.Create(config);
            Fx6 money = Fx6.Zero;
            for (long t = 0; t < 120; t++)
            {
                s.StepThroughMonth(t);
                for (int site = 0; site < s.World.IndustryLayout.SiteCount; site++)
                {
                    for (int row = 0; row < s.World.IndustryLayout.ProcessCount; row++)
                        money += s.World.Industry.ProcE(site, row);
                }
            }
            Assert.True(money > Fx6.Zero, "no process-development money was distributed: the fixture does not test «T_ab expires with money»");
            Observed o = Observe(config, 384);
            output.WriteLine($"seed {seed}: branch О {o.OFirings} (processes {o.OProcesses})");
            fired += o.OProcesses;
        }
        Assert.True(fired > 0, "branch О PROCESSES never fired while the culture could not grow within T_ab");
    }

    // (V5′, contract §4.1a r12) without an own prototype or series line on home and without output, the money of a process stays within the laboratory share.
    // The snapshot of month t is the state after t−1, so v_p and V_OC are read before the step.
    [Fact]
    public void WithoutOwnPrototypeOrSeries_NoOutputProcessTakesAtMostTheStandMoney_V5Prime()
    {
        int bounded = 0, above = 0;
        foreach (ulong seed in new ulong[] { 1, 2, 3 })
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed });
            SliceWorld w = s.World;
            var il = w.IndustryLayout;
            Fx6 cap = DetMath.MulCeil(w.IndustryParameters.Ell, w.IndustryParameters.EFull);
            var free = new bool[il.SiteCount, il.ProcessCount];
            for (long t = 0; t < 300; t++)
            {
                for (int site = 0; site < il.SiteCount; site++)
                {
                    for (int row = 0; row < il.ProcessCount; row++)
                        free[site, row] = w.Industry.ProcV(site, row) == Fx6.Zero && w.IndustryInputs.GrowthNominal(site, row, t) == Fx6.Zero;
                }
                s.StepThroughMonth(t);
                for (int site = 0; site < il.SiteCount; site++)
                {
                    for (int row = 0; row < il.ProcessCount; row++)
                    {
                        Fx6 e = w.Industry.ProcE(site, row);
                        if (free[site, row])
                        {
                            bounded++;
                            Assert.True(e <= cap, $"seed {seed}, month {t}, site {site}, process row {row}: E_p {e} above ℓ·E_full without V_OC and output");
                        }
                        else if (w.Industry.ProcV(site, row) == Fx6.Zero && e > cap)
                        {
                            above++;
                        }
                    }
                }
            }
        }
        output.WriteLine($"{bounded} (month, site, process) cells bounded; {above} cells without output above the laboratory share (own О/С line)");
        Assert.True(above > 0, "no own О/С line ever took more than the laboratory share: the I-OC channel is dead or the test vacuous");
    }

    [Fact]
    public void ForceArm_SendsForceOnlyFromTheHarness_SmokeOnTwoSeeds_BothRates()
    {
        foreach ((ulong seed, int divisor) in new[] { (1UL, 1), (2UL, 4) })
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed });
            var proxy = new ForceProxy(s, divisor);
            SliceWorld w = s.World;
            for (long t = 0; t < 240; t++)
            {
                s.StepThroughMonth(t);
                proxy.Issue(t);
            }
            List<LoggedCommand> log = CommandCodec.DecodeLog(s.CommandLog);
            Assert.All(log, c => Assert.Equal(SourceClass.Harness, c.Key.Source));
            var forcedByLog = log.Where(c => c.Command is StartSeries { Force: true }).Select(c => (c.Command.Actor, ((StartSeries)c.Command).Design.Slot)).ToHashSet();
            int forcedInState = 0;
            for (int a = 0; a < w.Scenario.ActorCount; a++)
            {
                for (int d = 0; d < w.ProductsLayout.Designs; d++)
                {
                    if (!w.Products.SeriesForced(a, d))
                        continue;
                    forcedInState++;
                    Assert.Contains((a, d), forcedByLog);                 // series_forced ⇒ a forced start in the journal (P0)
                }
            }
            output.WriteLine($"FORCE seed {seed} rate/{divisor}: {proxy.Sent} forced starts sent, {forcedInState} series forced in the state");
            Assert.True(proxy.Sent > 0, "the proxy never found a project in О with a closed gate");
            Assert.True(forcedInState > 0);
        }
    }

    [Fact]
    public void DerivArm_SmokeOnTwoSeeds_DerivedDesignsAreMadeOnlyWithTheFlag()
    {
        int derivedWith = 0;
        foreach (ulong seed in new ulong[] { 1, 2 })
        {
            foreach (bool flag in new[] { false, true })
            {
                SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, Derive = flag });
                SliceWorld w = s.World;
                int derived = 0;
                for (long t = 0; t < 384; t++)
                {
                    s.StepThroughMonth(t);
                }
                for (int a = 0; a < w.Scenario.ActorCount; a++)
                {
                    for (int d = 0; d < w.ProductsLayout.Designs; d++)
                        derived += w.Products.Parent(a, d) >= 0 ? 1 : 0;
                }
                output.WriteLine($"DERIV seed {seed}, flag {flag}: {derived} derived designs");
                if (!flag)
                    Assert.Equal(0, derived);                              // main has no derive (spec §4)
                else
                    derivedWith += derived;
            }
        }
        Assert.True(derivedWith > 0, "the DERIV arm never derived a design on two seeds");
    }

    [Fact]
    public void PlayerProxy_ClosesTheOldLineOnlyByTheYieldRuleOfA41()
    {
        int switches = 0, held = 0;
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5, 6 })
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, ProductAi = false });
            var player = new PlayerScript(s);
            for (long t = 0; t < 384; t++)
            {
                player.Issue(t);
                s.StepThroughMonth(t);
            }
            // Spec A41 «Переключение линии у прокси игрока»: the new line's smallest yield over its prc_set is at least y_prod (0,5).
            Assert.All(player.Switches, x => Assert.True(x.MinYield >= new ProductsParams().YProd, $"seed {seed}, month {x.Month}: switched at yield {x.MinYield}"));
            Assert.Equal(0, player.Refused);
            Assert.All(player.Held, x => Assert.True(x.MinYield < new ProductsParams().YProd));
            switches += player.Switches.Count;
            held += player.Held.Count;
        }
        output.WriteLine($"{switches} line switches, {held} months with the old line kept (new line below y_prod), seeds 1…6");
        // The rule is exercised: a newer line that does not reach y_prod does not take the old line away.
        Assert.True(switches + held > 0, "the player never had a newer line in series on six seeds");
    }

    [Fact]
    public void CancelDesign_IsAPlayerCommand_JournalledByTheCodec_RefusedOutOfRange_BadCancelForAFleet_StaleHandleForAnEmptySlot()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 3, ProductAi = false });
        Assert.False(s.Submit(SourceClass.Player, 0, 0, new CancelDesign(0, new DesignHandle(99, 1))).Accepted);
        Assert.Equal(CommandRefusal.BadDesign, s.Submit(SourceClass.Player, 0, 1, new CancelDesign(0, new DesignHandle(-1, 1))).Reason);
        Assert.Equal(CommandRefusal.BadHandle, s.Submit(SourceClass.Player, 0, 2, new CancelDesign(0, new DesignHandle(4, 0))).Reason);
        Assert.True(s.Submit(SourceClass.Player, 0, 3, new CancelDesign(0, new DesignHandle(4, 1))).Accepted);
        DesignHandle fleet = s.HandleOf(0, 0);
        Assert.True(s.Submit(SourceClass.Player, 0, 4, new CancelDesign(0, fleet)).Accepted);
        List<LoggedCommand> log = CommandCodec.DecodeLog(s.CommandLog);
        Assert.Equal(2, log.Count);
        Assert.Equal(new CancelDesign(0, new DesignHandle(4, 1)), log[0].Command);
        Assert.Equal(new CancelDesign(0, fleet), log[1].Command);
        Assert.Contains("\"kind\":\"cancel_design\"", s.CommandLog);
        s.StepThroughMonth(0);
        Assert.Equal(1, s.World.Products.RejectionCount(0, CommandRejection.StaleHandle));   // slot 4 of USA is an empty slot: the handle names nothing
        Assert.Equal(1, s.World.Products.RejectionCount(0, CommandRejection.BadCancel));     // slot 0 is a fleet in В/С, not a project in П or О
    }

    [Fact]
    public void WithTheAiOn_L2_ThreadsAndChunksGiveOneHashEveryMonth_AndAnAiRunHasAFixedGolden()
    {
        var runs = new[]
        {
            SliceTestData.Create(new SliceRunConfig { Seed = 7, Threads = 1, ChunkSize = 1 }),
            SliceTestData.Create(new SliceRunConfig { Seed = 7, Threads = 4, ChunkSize = 1 }),
            SliceTestData.Create(new SliceRunConfig { Seed = 7, Threads = 8, ChunkSize = 3 }),
        };
        for (long t = 0; t < 150; t++)
        {
            foreach (SliceSession r in runs)
                r.StepThroughMonth(t);
            Assert.All(runs, r => HashAssert.Equal(runs[0].LastHash, r.LastHash, $"month {t}: hash differs"));
        }
        for (int a = 0; a < runs[0].World.Scenario.ActorCount; a++)
            Assert.Equal(0, runs[0].World.Products.RejectionCount(a, CommandRejection.StaleHandle));   // outside the old-column projections
        HashAssert.Equal(Golden150, runs[0].LastHash, $"golden hash of the AI run changed: {runs[0].LastHash}; update only with a reason written in the commit message");
    }

    [Fact]
    public void WithTheAiOn_L2Seeds1To20_Threads1And8_SameHash_AndL1_384Months()
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            SliceSession a = SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 1, ChunkSize = 1 });
            SliceSession b = SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 8, ChunkSize = 2 });
            for (long t = 0; t < 60; t++)
            {
                a.StepThroughMonth(t);
                b.StepThroughMonth(t);
                HashAssert.Equal(a.LastHash, b.LastHash, $"seed {seed}, month {t}: hash differs");
            }
        }
        SliceSession x = SliceTestData.Create(new SliceRunConfig { Seed = 3, Threads = 1 });
        SliceSession y = SliceTestData.Create(new SliceRunConfig { Seed = 3, Threads = 8, ChunkSize = 1 });
        SliceSession z = SliceTestData.Create(new SliceRunConfig { Seed = 3, Threads = 1 });
        for (long t = 0; t < 384; t++)
        {
            x.StepThroughMonth(t);
            y.StepThroughMonth(t);
            z.StepThroughMonth(t);
        }
        HashAssert.Equal(x.LastHash, z.LastHash);
        HashAssert.Equal(x.LastHash, y.LastHash);
    }

    // Seed 7, the AI on, 150 months (through month 149). First recorded in T6c; re-recorded in D1 (c5867d91… before), reason: D1 — the start-data
    // package of spec v0.14 (new start knowledge and culture cells, the hot_steel_g0 substitute and the engine descent of the presets).
    // Re-recorded in T6c2 (e778e8cc… before), reason: T6c2 — E1 start line from tick 0, F17 launch at admitted, #140 engine choice, W1.
    // Re-recorded in T-fleet (513238aa… before), reason: T-fleet — contract r6: new columns and the fleet rule A42.4 (retirement by generation), schema v5.
    // D2 (#155, #159): start rule R-H-MS'; the data of the scenario changed, the model did not.
    // Re-recorded in F17b (951bcf96… before), reason: F17b — LINE_CLOSE YPROD bit 12 of run.fleet in the hash of an AI run (ADMIT keeps the old hash, see F17bTests).
    // Re-recorded in T6d (409293e4… before), reason: T6d — licences, alloy disruption and the why_not triggers of the AI, new policy columns, schema v7; then «exposure to data» (1d6a59a9… before; the scenario hash is in the run columns).
    // Re-recorded in TASK-0058 step 5 (e99b1c3e… before), reason: I-OC + A41 SLOT as the build, fielded_L and the column prd.d*.q (#242–#247), schema v8.
    // Re-recorded in TASK-0110 (DEBT-0054), reason: schema v10 (new columns prd.altReached.*, novelty memory per actor); the old columns are unchanged per the projection tests in this branch (T6dOff, FleetAblation, ToolingUnmet), so only the new columns change the hash.
    // Re-recorded in TASK-0099 (ADR-0005 p. 3), reason: schema v9 (new columns prd.d*.generation, parentGen, prd.lp.g*.gen, StaleHandle counter); the run ends before the first slot reuse, the old columns are unchanged (SlotReuseL2Tests.NoSlotIsReused_BeforeTheMonthOfAnyRewrittenGolden, projection tests).
    // Re-recorded in TASK-0115: schema v11 (cmd.*.gen, pol.*.gen columns); the run ends before the first slot reuse, the AI's handles are all current, the old columns are unchanged.
    // Re-recorded in TASK-0121 (#423), reason: schema v12 (new columns prd.d*.cohort.1978…2030 and prd.d*.q.1: the cohort years to 2030, the second block of the q mask); the old columns are unchanged (HorizonTests projection at 48 points of the build before the change, GoldenProjectionAgainstMainTests), so only the new columns change the hash.
    // Re-recorded in TASK-0129 (fleet ageing, #431/#435-#437), reason: schema v13 (new column prd.d*.replCredit, in the hash from month 0); the old columns are unchanged to month 176 (projection tests: HorizonTests, T6dTests, FleetAblationTests, ToolingUnmetTests, GoldenProjectionAgainstMainTests), so only the new column changes these hashes.
    private const string Golden150 = "e32429bdd1dacca2c4f1f81e6064fac4f83b84eb8bdbebafa95c63ff880faf96";
}
