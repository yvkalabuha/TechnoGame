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
        public int Forced, MultiLine, BadClose, CancelledDesigns, SeenSeries;
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
                    if (!w.Policy.AiClose(a, d, t + 1))
                        continue;
                    // (11): a close is either a part of the launch bundle (some start in the same month) or idle_c ≥ T_ab by the snapshot.
                    bool idle = stageBefore[a, d] == DesignStage.Series && idleBefore[a, d] >= TAb;
                    if (!anyStart && !idle)
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
                o.MultiLine += lines > 1 ? 1 : 0;
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
            Assert.Equal(0, o.MultiLine);                                // one line per actor (#125)
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

    [Fact]
    public void ASeriesWithoutOutput_NeverTakesMoreThanTheStandMoneyOfAProcessWithoutOutput_V5()
    {
        foreach (ulong seed in new ulong[] { 1, 2, 3 })
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed });
            SliceWorld w = s.World;
            Fx6 cap = DetMath.MulCeil(w.IndustryParameters.Ell, w.IndustryParameters.EFull);
            int checkedLines = 0;
            for (long t = 0; t < 300; t++)
            {
                s.StepThroughMonth(t);
                for (int a = 0; a < w.Scenario.ActorCount; a++)
                {
                    for (int d = 0; d < w.ProductsLayout.Designs; d++)
                    {
                        if (w.Products.Stage(a, d) != DesignStage.Series || w.Products.Rate(a, d) > Fx6.Zero)
                            continue;
                        foreach (int p in w.Products.PrcSet(a, d))
                        {
                            int row = w.IndustryLayout.RowOf(p);
                            if (w.Industry.ProcV(a, row) > Fx6.Zero)
                                continue;
                            checkedLines++;
                            Assert.True(w.Industry.ProcE(a, row) <= cap, $"seed {seed}, month {t}, actor {a}, process {p}: E_p {w.Industry.ProcE(a, row)} above ℓ·E_full");
                        }
                    }
                }
            }
            output.WriteLine($"seed {seed}: {checkedLines} (month, process) pairs of a series without output checked");
        }
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
            var forcedByLog = log.Where(c => c.Command is StartSeries { Force: true }).Select(c => (c.Command.Actor, ((StartSeries)c.Command).Design)).ToHashSet();
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
    public void CancelDesign_IsAPlayerCommand_JournalledByTheCodec_RefusedOutOfRange_AndBadCancelWhenThereIsNoProject()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 3, ProductAi = false });
        Assert.False(s.Submit(SourceClass.Player, 0, 0, new CancelDesign(0, 99)).Accepted);
        Assert.Equal(CommandRefusal.BadDesign, s.Submit(SourceClass.Player, 0, 1, new CancelDesign(0, -1)).Reason);
        Assert.True(s.Submit(SourceClass.Player, 0, 2, new CancelDesign(0, 4)).Accepted);
        LoggedCommand logged = Assert.Single(CommandCodec.DecodeLog(s.CommandLog));
        Assert.Equal(new CancelDesign(0, 4), logged.Command);
        Assert.Contains("\"kind\":\"cancel_design\"", s.CommandLog);
        s.StepThroughMonth(0);
        Assert.Equal(1, s.World.Products.RejectionCount(0, CommandRejection.BadCancel));   // design 4 of USA is an empty slot
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
            Assert.All(runs, r => Assert.True(runs[0].LastHash == r.LastHash, $"month {t}: hash differs"));
        }
        Assert.True(Golden150 == runs[0].LastHash, $"golden hash of the AI run changed: {runs[0].LastHash}; update only with a reason written in the commit message");
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
                Assert.True(a.LastHash == b.LastHash, $"seed {seed}, month {t}: hash differs");
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
        Assert.Equal(x.LastHash, z.LastHash);
        Assert.Equal(x.LastHash, y.LastHash);
    }

    // Seed 7, the AI on, 150 months (through month 149). First recorded in T6c; re-recorded in D1 (c5867d91… before), reason: D1 — the start-data
    // package of spec v0.14 (new start knowledge and culture cells, the hot_steel_g0 substitute and the engine descent of the presets).
    // Re-recorded in T6c2 (e778e8cc… before), reason: T6c2 — E1 start line from tick 0, F17 launch at admitted, #140 engine choice, W1.
    // Re-recorded in T-fleet (513238aa… before), reason: T-fleet — contract r6: new columns and the fleet rule A42.4 (retirement by generation), schema v5.
    // D2 (#155, #159): start rule R-H-MS'; the data of the scenario changed, the model did not.
    private const string Golden150 = "951bcf96f371d409cdf03df7687e19d0dc690b47190ed137f26e7a7ee1f9b29d";
}
