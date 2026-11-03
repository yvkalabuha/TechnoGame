using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Policy;

/// <summary>
/// The product AI of the policy (spec §3.5 A40, A41, §4) on a scripted snapshot: Requirement and presets, the launch doctrine, one line, the
/// give-up rule of both branches, the repeat counter. The snapshot is a fake of <see cref="IPolicyProducts"/> the test edits before each month;
/// the decisions of month t are read from the columns the layer would read at t+1.
/// </summary>
public class ProductAiTests
{
    private const int Designs = 12;

    private sealed class Snapshot : IPolicyProducts
    {
        public readonly PolicyDesign[] Design = new PolicyDesign[Designs];
        public readonly int[][] Alts = new int[Designs][];
        public int KnownGen = 2;
        public int Free = 2;
        public Func<int, Fx6> Yield = _ => Fx6.One;
        public Fx6 Supply = Fx6.FromInt(1_000_000);
        public Func<int[], Fx6> Value = _ => Fx6.One;

        public ProductsLayout Layout => Probe;
        public Fx6 DRef => Fx6.FromInt(740);
        public PolicyDesign Get(int d, long month) => Design[d];
        public PolicyDesign Design_(int d) => Design[d];
        PolicyDesign IPolicyProducts.Design(int actorRow, int design, long month) => Design[design];
        public int[] Alternatives(int actorRow, int design) => Alts[design] ?? [.. Enumerable.Repeat(0, Probe.SlotCount)];
        public bool Knows(int actorRow, int nodeNumber, long month) => Probe.KnownUpTo(KnownGen)(nodeNumber);
        public Fx6 AdmittedYield(int actorRow, int processNumber, long month) => Knows(0, processNumber, month) ? Yield(processNumber) : Fx6.Zero;
        public Fx6 AlloySupply(int actorRow, long month) => Supply;
        public int FreeBureaus(int actorRow) => Free;
        public Fx6 CandidateValue(int actorRow, ReadOnlySpan<int> alts, Fx6 budget, long month) => Value(alts.ToArray());
    }

    private sealed class NoInputs : IPolicyInputs
    {
        public int NewestSeriesDesign(int actorRow, long month) => -1;
        public BottleneckLimit? Bottleneck(int actorRow, int design, long month) => null;
        public bool SeriesBelowQuality(int actorRow, Fx6 threshold, long month) => false;
    }

    private sealed class Rig
    {
        public readonly Snapshot S;
        public readonly PolicySystem Policy;
        private readonly TickRunner _runner;
        private readonly TickCalendar _calendar = new(new CivilDate(1945, 9, 1));

        public Rig(PolicyProfile profile, Action<Snapshot>? setup = null, bool derive = false, PolicyParams? parameters = null)
        {
            S = new Snapshot();
            setup?.Invoke(S);
            var table = new Table(1);
            PolicyParams p = (parameters ?? new PolicyParams()) with { FvBudget = Fx6.FromInt(10) };
            Policy = new PolicySystem(table, p, [0], [Fx6.One], [Fx6.Zero], new CounterRng(1), new NoInputs(), new IndustryMailbox(1), 99,
                [(int)profile], products: S, designs: Designs, generations: Probe.Generations, derive: derive);
            Policy.Initialize();
            _runner = new TickRunner(table, 1, [Policy], 1, 1, _calendar);
        }

        public void Month(long month)
        {
            while (_runner.Tick <= _calendar.TickOfMonth(month))
                _runner.RunTick();
        }

        /// <summary>The AI's design commands for month <paramref name="consumedAt"/> (decided in the month before).</summary>
        public IReadOnlyList<DesignCommand> Commands(long consumedAt) => Policy.AiDesignCommands(0, consumedAt);
    }

    private static PolicyDesign Prototype(int stageO, int gp, Fx6 hidden, bool gate, int parent = -1) =>
        new(DesignStage.Prototype, gp, false, false, 0, stageO, parent, gate, hidden, Fx6.FromInt(1000));

    private static PolicyDesign Series(int gp, int idle, bool built) =>
        new(DesignStage.Series, gp, false, built, idle, 5, -1, true, Fx6.Zero, Fx6.FromInt(1000));

    private static PolicyDesign Live(DesignStage stage, int gp) =>
        new(stage, gp, false, stage != DesignStage.Project && stage != DesignStage.Prototype, 0, 3, -1, true, Fx6.Zero, Fx6.FromInt(1000));

    private static PolicyDesign Cancelled(int gp) =>
        new(DesignStage.Withdrawn, gp, true, false, 61, 3, -1, true, Fx6.Zero, Fx6.FromInt(1000));

    // ---- A40, branch О

    [Fact]
    public void A40_ProjectInO_IsCancelledExactlyAtTAb_OnceNotTwice_AndRequirementRepeatsOnceAtTheNextAnnualStep()
    {
        var rig = new Rig(PolicyProfile.Adaptive, s => { s.KnownGen = 2; s.Design[0] = Live(DesignStage.InService, 1); });
        rig.S.Design[1] = Prototype(stageO: 10, gp: 2, hidden: Fx6.FromInt(700), gate: true);   // above every k_P·D_ref: stuck
        for (long t = 11; t < 70; t++)
        {
            rig.Month(t);
            Assert.DoesNotContain(rig.Commands(t + 1), c => c.Kind == DesignCommandKind.Cancel);
        }
        rig.Month(70);                                                  // t − stage_o_month = 60 = T_ab by the snapshot
        DesignCommand cancel = Assert.Single(rig.Commands(71), c => c.Kind == DesignCommandKind.Cancel);
        Assert.Equal(1, cancel.Target);
        rig.Month(71);                                                  // the command is still pending in the snapshot: no second one
        Assert.DoesNotContain(rig.Commands(72), c => c.Kind == DesignCommandKind.Cancel);

        // The layer applied it: the project is «снят». The next annual step (t = 72) repeats the Requirement for G2, once.
        rig.S.Design[1] = Cancelled(2);
        rig.Month(72);
        DesignCommand again = Assert.Single(rig.Commands(73), c => c.Kind == DesignCommandKind.Compose);
        Assert.Equal(2, again.Target);
        Assert.Equal(1, rig.Policy.Repeats(0, 2));

        // The repeated project gets stuck as well: it is not cancelled (the counter is spent), the dead end stays visible.
        rig.S.Design[2] = Prototype(stageO: 73, gp: 2, hidden: Fx6.FromInt(700), gate: true);
        for (long t = 73; t <= 73 + 130; t++)
        {
            rig.Month(t);
            Assert.DoesNotContain(rig.Commands(t + 1), c => c.Kind == DesignCommandKind.Cancel);
        }
    }

    [Fact]
    public void Requirement_IsNotIssued_WhileAProjectOfTheGenerationLives_OrWithoutAFreeBureau_OrTwiceAfterACancel()
    {
        foreach (DesignStage live in new[] { DesignStage.Project, DesignStage.Prototype, DesignStage.Series, DesignStage.InService })
        {
            var rig = new Rig(PolicyProfile.Quality, s => s.Design[0] = Live(live, 2));
            Assert.Empty(rig.Commands(0));                              // A8: decided before tick 0, in force in it
            rig.Month(12);
            Assert.Empty(rig.Commands(13));
        }
        var free = new Rig(PolicyProfile.Quality, s => { s.Design[0] = Live(DesignStage.InService, 1); s.Free = 0; });
        Assert.Empty(free.Commands(0));
        free.S.Free = 1;
        free.Month(12);
        Assert.Single(free.Commands(13));                               // the bureau is free: the project for G2 is ordered
        var spent = new Rig(PolicyProfile.Quality, s => { s.Design[0] = Live(DesignStage.InService, 1); s.Design[1] = Cancelled(2); });
        Assert.Single(spent.Commands(0));                               // the first repeat
        Assert.Equal(1, spent.Policy.Repeats(0, 2));
        spent.S.Design[2] = Cancelled(2);
        spent.Month(12);
        Assert.Empty(spent.Commands(13));                               // a second repeat is not made
    }

    [Fact]
    public void Requirement_AtInitialisationActsInTickZero_AndIsNotRepeatedInThatComputation()
    {
        var rig = new Rig(PolicyProfile.Quality, s => s.Design[0] = Live(DesignStage.InService, 1));
        DesignCommand c = Assert.Single(rig.Commands(0));
        Assert.Equal(DesignCommandKind.Compose, c.Kind);
        Assert.Equal(2, c.Target);
        rig.Month(0);
        Assert.Empty(rig.Commands(1));
    }

    // ---- A40, branch С; the bundle

    [Fact]
    public void A40_SeriesWithoutOutput_IsClosedWhenIdleReachesTAb_BuiltOrNot_AndAfterThatNotAgainForTheGeneration()
    {
        foreach (bool built in new[] { false, true })
        {
            var rig = new Rig(PolicyProfile.Mass, s => s.Design[0] = Live(DesignStage.InService, 1));
            rig.S.Design[1] = Series(2, idle: 59, built);
            rig.Month(20);
            Assert.False(rig.Policy.AiClose(0, 1, 21));
            rig.S.Design[1] = Series(2, idle: 60, built);               // the snapshot t has idle_c = T_ab: close_line, in force from t + 1
            rig.Month(21);
            Assert.True(rig.Policy.AiClose(0, 1, 22));
            rig.Month(22);                                              // still in С in the snapshot: the command is pending, not repeated
            Assert.False(rig.Policy.AiClose(0, 1, 23));
            // The repeat of the Requirement is spent: a second stuck series of the generation is not closed.
            var spent = new Rig(PolicyProfile.Mass, s => { s.Design[0] = Cancelled(2); s.Design[1] = Live(DesignStage.InService, 1); });
            Assert.Equal(1, spent.Policy.Repeats(0, 2));
            spent.S.Design[2] = Series(2, idle: 100, built);
            spent.Month(1);
            Assert.False(spent.Policy.AiClose(0, 2, 2));
        }
    }

    [Fact]
    public void LaunchAndTheBundle_ForOneLineAndOneCloseCommand_WhenAGiveUpAndALaunchMeetInAMonth()
    {
        var rig = new Rig(PolicyProfile.Quality, s => s.Design[0] = Live(DesignStage.InService, 1));
        rig.S.Design[1] = Series(2, idle: 60, built: false);            // A40 closes it ...
        rig.S.Design[2] = Prototype(3, 3, Fx6.FromInt(290), gate: true);// ... and the launch bundle closes it as well: one command
        rig.S.Design[3] = Series(1, idle: 0, built: true);
        rig.Month(0);
        Assert.NotNull(rig.Policy.AiStart(0, 2, 1));
        Assert.True(rig.Policy.AiLine(0, 2));
        Assert.True(rig.Policy.AiClose(0, 1, 1));
        Assert.True(rig.Policy.AiClose(0, 3, 1));                       // one line per actor: every other line in С is closed
        Assert.False(rig.Policy.AiClose(0, 2, 1));
        Assert.Null(rig.Policy.AiStart(0, 1, 1));
    }

    [Fact]
    public void Launch_FollowsTheDoctrine_GateOpenAndHiddenDefectsWithinKP_ParentOfADerivedDesignKeepsItsLine()
    {
        // k_P·D_ref with D_ref = 740: P1 0,40 → 296, P3 0,55 → 407, P2 0,70 → 518.
        (PolicyProfile Profile, long Hidden, bool Launch)[] cases =
        [
            (PolicyProfile.Quality, 296, true), (PolicyProfile.Quality, 297, false),
            (PolicyProfile.Adaptive, 407, true), (PolicyProfile.Adaptive, 408, false),
            (PolicyProfile.Mass, 518, true), (PolicyProfile.Mass, 519, false),
        ];
        foreach ((PolicyProfile profile, long hidden, bool launch) in cases)
        {
            var rig = new Rig(profile, s => s.Design[0] = Live(DesignStage.InService, 1));
            rig.S.Design[1] = Prototype(5, 2, Fx6.FromInt(hidden), gate: true);
            rig.Month(0);
            Assert.Equal(launch, rig.Policy.AiStart(0, 1, 1) is not null);
        }
        var closed = new Rig(PolicyProfile.Mass, s => s.Design[0] = Live(DesignStage.InService, 1));
        closed.S.Design[1] = Prototype(5, 2, Fx6.FromInt(400), gate: false);        // the gate is closed: even a tiny D_hidden does not launch
        closed.Month(0);
        Assert.Null(closed.Policy.AiStart(0, 1, 1));
        var derived = new Rig(PolicyProfile.Mass, s => s.Design[0] = Live(DesignStage.InService, 1));
        derived.S.Design[1] = Series(2, 0, true);
        derived.S.Design[2] = Prototype(5, 2, Fx6.FromInt(100), gate: true, parent: 1);
        derived.S.Design[3] = Series(1, 0, true);
        derived.Month(0);
        Assert.False(derived.Policy.AiClose(0, 1, 1));                  // the layer moves the parent's line
        Assert.True(derived.Policy.AiClose(0, 3, 1));
    }

    [Fact]
    public void LaunchRate_IsTheProcurementBudgetOverTheUnitCostFloor_AndTheCommandNeverCarriesForce()
    {
        var rig = new Rig(PolicyProfile.Quality, s => s.Design[0] = Live(DesignStage.InService, 1));
        rig.S.Design[1] = Prototype(5, 2, Fx6.FromInt(100), gate: true);
        Fx6 b = rig.Policy.Shares(0).Procurement * rig.Policy.Budget(0);     // growth 0: the budget of every month is the start one
        rig.Month(0);
        Assert.Equal(PolicyRules.LineRate(b, Fx6.FromInt(1000)), rig.Policy.AiStart(0, 1, 1));
        Assert.Equal(Fx6.FromRaw(400_000) / Fx6.FromInt(1000), PolicyRules.LineRate(Fx6.FromRaw(400_000), Fx6.FromInt(1000)));
    }

    // ---- presets

    [Fact]
    public void Presets_P1Superior_P2Producible_P3TheLargerFV_TieToSuperior()
    {
        ProductsLayout l = Probe;
        var supA = new int[l.SlotCount];
        var prodA = new int[l.SlotCount];
        Span<int> sup = supA, prod = prodA;
        Func<int, bool> knows = l.KnownUpTo(3);
        // A weak process makes «producible» differ from «superior» (a yield below y_prod on a high-tier hot section).
        Func<int, Fx6> yield = p => p == 41 || p == 43 || p == 52 ? Fx6.FromRaw(100_000) : Fx6.One;
        Assert.True(DesignRules.Superior(l, 3, knows, sup));
        Assert.True(DesignRules.Producible(l, 3, knows, yield, new PolicyParams().YProd, null, prod));
        Assert.False(sup.SequenceEqual(prod));
        void Setup(Snapshot s)
        {
            s.KnownGen = 3;
            s.Yield = yield;
            s.Design[0] = Live(DesignStage.InService, 1);
            s.Design[1] = Live(DesignStage.InService, 2);
        }
        int[] Composed(PolicyProfile profile, Func<int[], Fx6>? value = null)
        {
            var rig = new Rig(profile, s => { Setup(s); if (value is not null) s.Value = value; });
            return rig.Commands(0).Single().Alts.ToArray();
        }
        Assert.Equal(supA, Composed(PolicyProfile.Quality));
        Assert.Equal(prodA, Composed(PolicyProfile.Mass));
        Assert.Equal(prodA, Composed(PolicyProfile.Adaptive, a => a.SequenceEqual(prodA) ? Fx6.FromInt(2) : Fx6.One));
        Assert.Equal(supA, Composed(PolicyProfile.Adaptive, a => a.SequenceEqual(supA) ? Fx6.FromInt(2) : Fx6.One));
        Assert.Equal(supA, Composed(PolicyProfile.Adaptive));                  // tie
    }

    [Fact]
    public void Presets_AlloyTestRejectsAHotSectionTheSupplyCannotFeed_ForProducibleAndAdaptive()
    {
        ProductsLayout l = Probe;
        // Supply 400 kg a month: n_service_min/T_field = 50/24 ≈ 2,08 units a month need 2,08·alloy_kg — only the light hot sections pass.
        var rig = new Rig(PolicyProfile.Mass, s => { s.KnownGen = 3; s.Supply = Fx6.FromInt(400); s.Design[0] = Live(DesignStage.InService, 1); s.Design[1] = Live(DesignStage.InService, 2); });
        int[] alts = rig.Commands(0).Single().Alts.ToArray();
        // No hot section fits 400 kg (the lightest burns 290 kg → 1,38 units a month < 2,08): the preset takes the lightest one. The G3 engine
        // rules the steel substitute out (max_eng_gen 1), and a substitute is never the lightest-one fallback when a plain alternative exists.
        Fx6 lightest = Enumerable.Range(l.AltStart[l.AlloySlot], l.AltStart[l.AlloySlot + 1] - l.AltStart[l.AlloySlot])
            .Where(a => !l.AltSubstitute[a]).Select(a => l.AltAlloyKg[a]).Min();
        Assert.Equal(lightest, l.AltAlloyKg[alts[l.AlloySlot]]);
        var rich = new Rig(PolicyProfile.Mass, s => { s.KnownGen = 3; s.Supply = Fx6.FromInt(1_000_000); s.Design[0] = Live(DesignStage.InService, 1); s.Design[1] = Live(DesignStage.InService, 2); });
        Assert.True(l.AltAlloyKg[rich.Commands(0).Single().Alts[l.AlloySlot]] >= lightest);
    }

    // ---- DERIV (spec §6.8)

    [Fact]
    public void Derive_OnlyWhenTheEngineRankGrew_OnePerPlatform_NeedsTwoFreeBureausWithARequirement()
    {
        // The root project is G2 (eng_g2), the actor knows G3 nodes: the best engine rank is 3 ≥ 2 + 1.
        int g2 = Alt(Probe, "eng_g2");
        void Parent(Snapshot s)
        {
            s.KnownGen = 3;
            s.Design[0] = Live(DesignStage.InService, 1);
            s.Design[1] = Live(DesignStage.Series, 2);
            s.Alts[1] = [g2, Alt(Probe, "hot_sa2_forged"), Alt(Probe, "comp_axial"), Alt(Probe, "af_swept"), Alt(Probe, "ab_on")];
        }
        var rig = new Rig(PolicyProfile.Quality, Parent, derive: true);
        Assert.Contains(rig.Commands(0), c => c.Kind == DesignCommandKind.Derive && c.Target == 1 && c.Alts[Probe.AirframeSlot] == -1);
        var onlyOne = new Rig(PolicyProfile.Quality, s => { Parent(s); s.Free = 1; }, derive: true);
        Assert.DoesNotContain(onlyOne.Commands(0), c => c.Kind == DesignCommandKind.Derive);   // one bureau for the Requirement, none left
        var has = new Rig(PolicyProfile.Quality, s => { Parent(s); s.Design[0] = Cancelled(1); s.Design[2] = new PolicyDesign(DesignStage.Project, 2, false, false, 0, -1, 1, false, Fx6.Zero, Fx6.One); }, derive: true);
        Assert.DoesNotContain(has.Commands(0), c => c.Kind == DesignCommandKind.Derive);       // the platform has a derived design already
        var off = new Rig(PolicyProfile.Quality, Parent, derive: false);
        Assert.DoesNotContain(off.Commands(0), c => c.Kind == DesignCommandKind.Derive);       // main: no derive
    }

    // ---- manifest liveness (spec A40 «Проверка живости»)

    [Fact]
    public void Launch_ReferencesFromTheV24Reference_AndTLaunchMaxIs33()
    {
        var p = new ProductsParams();
        Fx6 dRef = ProductsMath.DRef(p.D0Factor, Probe.CRef);
        Assert.Equal(740, dRef.Raw / Fx6.Scale);
        // D0 740 (reference G2): P2 / P3 / P1 = 6 / 14 / 24; D0 960 (G3): 15 / 22 / 32 months from entering О.
        foreach ((long d0, int[] months) in new[] { (740L, new[] { 6, 14, 24 }), (960L, new[] { 15, 22, 32 }) })
        {
            Assert.Equal(months[0], PolicyLiveness.LaunchMonth(p, Fx6.FromInt(d0), dRef, new PolicyParams().LaunchShare[(int)PolicyProfile.Mass]));
            Assert.Equal(months[1], PolicyLiveness.LaunchMonth(p, Fx6.FromInt(d0), dRef, new PolicyParams().LaunchShare[(int)PolicyProfile.Adaptive]));
            Assert.Equal(months[2], PolicyLiveness.LaunchMonth(p, Fx6.FromInt(d0), dRef, new PolicyParams().LaunchShare[(int)PolicyProfile.Quality]));
        }
        Assert.Equal(33, PolicyLiveness.LaunchMonthMax(p, new PolicyParams(), Probe));
    }

    [Fact]
    public void Liveness_PassesTheNumbersOfSection81_AndRejectsEveryViolation()
    {
        var p = new ProductsParams();
        var pp = new PolicyParams();
        ReadOnlySpan<Fx6> budget = Scenario.BudgetIndex.AsSpan(), growth = Scenario.GrowthPa.AsSpan();
        PolicyLiveness.Validate(p, pp, Probe, budget, growth);

        LivenessException Fails(ProductsParams products, PolicyParams policy, Fx6[] b, Fx6[] g) =>
            Assert.Throws<LivenessException>(() => PolicyLiveness.Validate(products, policy, Probe, b, g));
        Fx6[] budgets = [.. Scenario.BudgetIndex], growths = [.. Scenario.GrowthPa];

        // T_ab ≤ t_launch_max: the stage-О branch would become part of main.
        Assert.Equal("LAUNCH", Fails(p, pp with { TAb = 33 }, budgets, growths).Code);
        Assert.Equal("LAUNCH", Fails(p with { LambdaD = Fx6.FromRaw(3_000) }, pp, budgets, growths).Code);
        // L-C1: a disruption that leaves nothing or lasts T_ab months.
        Assert.Equal("L-C1", Fails(p, pp with { DisruptionDeficit = Fx6.One }, budgets, growths).Code);
        Assert.Equal("L-C1", Fails(p, pp with { DisruptionMaxMonths = 60 }, budgets, growths).Code);
        // L-C2: a zero procurement share (also the one of the programme years), a zero start budget.
        Assert.Equal("L-C2", Fails(p, pp with { SharesByProfile = [pp.SharesByProfile[0], new BudgetShares(Fx6.FromRaw(300_000), Fx6.FromRaw(300_000), Fx6.FromRaw(400_000), Fx6.Zero, Fx6.Zero), pp.SharesByProfile[2]] }, budgets, growths).Code);
        Assert.Equal("L-C2", Fails(p, pp with { MassProgrammeShares = new BudgetShares(Fx6.FromRaw(500_000), Fx6.FromRaw(250_000), Fx6.FromRaw(250_000), Fx6.Zero, Fx6.Zero) }, budgets, growths).Code);
        Assert.Equal("L-C2", Fails(p, pp, [Fx6.Zero, .. budgets.Skip(1)], growths).Code);
        // L-C3: the largest fall of the item (P2 at a programme: 0,50 → 0,45) is not covered by the growth within T_ab, or the budget falls.
        Assert.Equal("L-C3", Fails(p, pp, budgets, [Fx6.FromRaw(20_000), .. growths.Skip(1)]).Code);
        Assert.Equal("L-C3", Fails(p, pp, budgets, [Fx6.FromRaw(-1), .. growths.Skip(1)]).Code);
        // The check is tight at the threshold the spec names: 1,116 ≥ 1,111 passes for the smallest growth of the scenario (GBR 2,20%).
        Fx6[] smallest = [.. growths.Select(_ => Fx6.FromRaw(22_000))];
        PolicyLiveness.Validate(p, pp, Probe, budget, smallest);
    }

    // ---- out_lim (A41-T)

    [Fact]
    public void OutLim_IsTheOutputLimit_EffortCountsOnlyWhenTheProcessHasNoOutput()
    {
        bool Out(LimiterKind k, bool output) => new BottleneckLimit(BottleneckSource.H, k, output).OutLim;
        Assert.True(Out(LimiterKind.Volume, true));
        Assert.True(Out(LimiterKind.LabCeiling, true));
        Assert.True(Out(LimiterKind.Volume, false));
        Assert.False(Out(LimiterKind.Effort, true));           // a line with output: tooling money lifts the limit
        Assert.True(Out(LimiterKind.Effort, false));           // a series without output: r4 made it EFFORT, the limit is still output
        Assert.False(Out(LimiterKind.Tooling, true));
        Assert.False(Out(LimiterKind.Tooling, false));
    }

    [Fact]
    public void A40_OneWithdrawalPerGeneration_AcrossBothBranches_AlsoForAnOlderGeneration()
    {
        // Branch О gave up a project of G2 (cancelled); a stuck series of the same G2 in the same year is not closed, and neither a G1 one after a G1 cancel.
        foreach (int gen in new[] { 2, 1 })
        {
            var rig = new Rig(PolicyProfile.Mass, s => { s.KnownGen = 3; s.Design[0] = Live(DesignStage.InService, 3); s.Design[1] = Cancelled(gen); });
            rig.S.Design[2] = Series(gen, idle: 100, built: false);
            rig.Month(0);
            Assert.False(rig.Policy.AiClose(0, 2, 1));
            rig.S.Design[3] = Prototype(0, gen, Fx6.FromInt(700), gate: true);
            rig.Month(70);
            Assert.DoesNotContain(rig.Commands(71), c => c.Kind == DesignCommandKind.Cancel);
        }
    }

    [Fact]
    public void CancelAndStart_OfOneProject_ExcludeEachOther()
    {
        // Stuck for T_ab months but the gate is open and the hidden defects are within k_P (a stale snapshot): the cancel wins, no launch of the same project.
        var rig = new Rig(PolicyProfile.Mass, s => s.Design[0] = Live(DesignStage.InService, 1));
        rig.S.Design[1] = Prototype(0, 2, Fx6.FromInt(100), gate: true);
        rig.Month(60);                                                // t − s = 60 and a launch condition both hold
        Assert.Null(rig.Policy.AiStart(0, 1, 61));
        Assert.Contains(rig.Commands(61), c => c.Kind == DesignCommandKind.Cancel && c.Target == 1);
        // A launch pending from last month: the project is not cancelled.
        var other = new Rig(PolicyProfile.Mass, s => s.Design[0] = Live(DesignStage.InService, 1));
        other.S.Design[1] = Prototype(0, 2, Fx6.FromInt(100), gate: true);
        other.Month(0);
        Assert.NotNull(other.Policy.AiStart(0, 1, 1));
    }
}
