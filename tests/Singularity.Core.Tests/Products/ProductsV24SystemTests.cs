using Singularity.Core.Data;
using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// Contract v2.4 behaviour of the product system (T-2…T-12, T-14): design capacity and the command validators, <c>g_p</c> and
/// <c>gen</c>, <c>derive</c> with the line move, the series gate with <c>force</c>, accidents, retrofit, the order of §4.4d,
/// R-PS2m per block, <c>planned</c>/<c>v_exp</c>, FV with the alloy cap.
/// </summary>
public class ProductsV24SystemTests
{
    private const int Usa = 0, Gbr = 1, Fra = 3;
    private static readonly ProductsParams P = new();

    private static ProductsHarness Gb(int gen, Func<int, long, IReadOnlyList<DesignCommand>> commands, int? maxDesigns = null) => new(maxDesigns: maxDesigns)
    {
        Knows = (a, n) => a == Gbr && Probe.KnownUpTo(gen)(n),
        Commands = commands,
    };

    private static DesignCommand SuperiorG2(ProductsLayout l) =>
        Compose(l, 2, "eng_g2", "hot_sa2_forged", "comp_axial", "af_swept", "ab_on");

    // ---- capacity (T-2)

    [Fact]
    public void DesignsTakeTheLowestFreeSlot_AndCapacityRejects_T2()
    {
        // max_designs = 1: GBR's start fleet holds the only slot, so every compose is DESIGN_CAPACITY; FRA has a free slot.
        var h = new ProductsHarness(maxDesigns: 1)
        {
            Knows = (a, n) => (a == Gbr || a == Fra) && Probe.KnownUpTo(2)(n),
            Commands = (a, t) => a is Gbr or Fra && t == 0 ? [SuperiorG2(Probe)] : [],
        };
        h.RunThroughMonth(0);
        ProductsSystem p = h.Products;
        Assert.Equal(DesignStage.Project, p.Stage(Fra, 0));
        Assert.Equal(DesignStage.InService, p.Stage(Gbr, 0));
        Assert.Equal(0, p.RejectionCount(Fra, CommandRejection.DesignCapacity));
        Assert.Equal(1, p.RejectionCount(Gbr, CommandRejection.DesignCapacity));
        Assert.Equal(1, h.Layout.Designs);
        Assert.Equal(h.Layout.Designs * h.Layout.ClusterCount + h.Layout.Bureaus + h.Layout.Designs, h.Layout.Requesters);
    }

    [Fact]
    public void SecondDesign_TakesTheNextSlot_NeverAReusedOne()
    {
        var h = Gb(2, (a, t) => a == Gbr && t is 0 or 1 ? [t == 0 ? SuperiorG2(Probe) : Compose(Probe, 2, "eng_g2", "hot_nicr_forged", "comp_axial", "af_swept", "ab_on")] : []);
        h.RunThroughMonth(1);
        Assert.Equal(DesignStage.Project, h.Products.Stage(Gbr, 1));
        Assert.Equal(DesignStage.Project, h.Products.Stage(Gbr, 2));
        Assert.Equal(0, h.Products.DesignBureau(Gbr, 1));
        Assert.Equal(1, h.Products.DesignBureau(Gbr, 2));
        Assert.Equal(DesignStage.None, h.Products.Stage(Gbr, 3));
    }

    // ---- compose_design validator (T-3)

    [Fact]
    public void Compose_Validator_RejectsWithTheirReasons_T3()
    {
        ProductsLayout l = Probe;
        var h = Gb(2, (a, t) => a != Gbr || t != 0 ? [] :
        [
            Compose(l, 1, "eng_g2", "hot_sa2_forged", "comp_axial", "af_swept", "ab_on"),          // g* is not the newest known APP
            Compose(l, 2, "eng_g3", "hot_sa2_forged", "comp_axial", "af_swept", "ab_on"),          // eng_g3 not available
            Compose(l, 2, "eng_g2", "hot_sa2_forged", "comp_axial", "af_straight", "ab_on"),       // rank 1 < platform_min_rank[2]
            new DesignCommand(DesignCommandKind.Compose, 2, [0, 0, 0]),                            // wrong slot count
            new DesignCommand(DesignCommandKind.Compose, 2, [l.AltStart[1], 0, 0, 0, 0]),          // alternative in the wrong slot
            SuperiorG2(l), SuperiorG2(l), SuperiorG2(l),                                           // 1st and 2nd take the bureaus, 3rd has none
        ]);
        h.RunThroughMonth(0);
        ProductsSystem p = h.Products;
        Assert.Equal(1, p.RejectionCount(Gbr, CommandRejection.BadRequirement));
        Assert.Equal(3, p.RejectionCount(Gbr, CommandRejection.SlotUnavailable));
        Assert.Equal(1, p.RejectionCount(Gbr, CommandRejection.PlatformThreshold));
        Assert.Equal(1, p.RejectionCount(Gbr, CommandRejection.NoBureau));
        Assert.Equal(DesignStage.Project, p.Stage(Gbr, 1));
        Assert.Equal(DesignStage.Project, p.Stage(Gbr, 2));
        Assert.Equal(DesignStage.None, p.Stage(Gbr, 3));
    }

    [Fact]
    public void Compose_AcceptsAnyAvailableCombination_NoGenerationCap_81()
    {
        // GBR knows G2: a G1 engine with the swept airframe is a legal project (no engine ≥ g rule, no cap on components).
        var h = Gb(2, (a, t) => a == Gbr && t == 0 ? [Compose(Probe, 2, "eng_g1", "hot_nicr_forged", "comp_centrifugal", "af_swept", "ab_none")] : []);
        h.RunThroughMonth(0);
        Assert.Equal(DesignStage.Project, h.Products.Stage(Gbr, 1));
        Assert.Equal(2, h.Products.PlatformGen(Gbr, 1));
        Assert.Equal(2, h.Products.Gen(Gbr, 1));   // min(g_p 2, eng_gen 1 + 1)
    }

    [Fact]
    public void Compose_FixesPlatformGen_AndGenFollowsTheEngine_T5()
    {
        // The actor knows the nodes of generations ≤ 3: G3 platform (af_titanium) with the G1 engine → gen = min(3, 2) = 2.
        var h = Gb(3, (a, t) => a == Gbr && t == 0
            ? [Compose(Probe, 3, "eng_g1", "hot_nicr_forged", "comp_axial", "af_titanium", "ab_on"), Compose(Probe, 3, "eng_g2", "hot_nicr_forged", "comp_axial", "af_titanium", "ab_on")]
            : []);
        h.RunThroughMonth(0);
        Assert.Equal(3, h.Products.PlatformGen(Gbr, 1));
        Assert.Equal(2, h.Products.Gen(Gbr, 1));
        Assert.Equal(3, h.Products.PlatformGen(Gbr, 2));
        Assert.Equal(3, h.Products.Gen(Gbr, 2));
        // g_p is fixed at creation: knowing the G4 nodes later does not change it.
        h.Knows = (a, n) => a == Gbr && Probe.KnownUpTo(4)(n);
        h.RunThroughMonth(3);
        Assert.Equal(3, h.Products.PlatformGen(Gbr, 1));
    }

    // ---- derive (T-4)

    private static IReadOnlyList<DesignCommand> Derive(int parent, params (string Slot, string Alt)[] changes)
    {
        var alts = new int[Probe.SlotCount];
        Array.Fill(alts, -1);
        foreach ((string slot, string alt) in changes)
            alts[Enumerable.Range(0, Probe.SlotCount).Single(s => CoreData.ProductClasses.SlotId[s] == slot)] = Alt(Probe, alt);
        return [new DesignCommand(DesignCommandKind.Derive, parent, [.. alts])];
    }

    [Fact]
    public void Derive_InheritsPlatform_WorksOnChangedSlotsOnly_T4()
    {
        // Parent: GBR's start fleet (G1 design, in service). Change one slot: compressor → comp_axial.
        var h = Gb(2, (a, t) => a == Gbr && t == 1 ? Derive(0, ("compressor", "comp_axial")) : []);
        ProductsSystem p = h.Products;
        h.RunThroughMonth(1);
        Assert.Equal(DesignStage.Project, p.Stage(Gbr, 1));
        Assert.Equal(0, p.Parent(Gbr, 1));
        Assert.Equal(p.PlatformGen(Gbr, 0), p.PlatformGen(Gbr, 1));      // g_p inherited (1), though G2 is known
        Assert.Equal(["eng_g1", "hot_nicr_forged", "comp_axial", "af_straight", "ab_none"], AltIds(Probe, p.Alternatives(Gbr, 1)));
        int cx = p.Complexity(Gbr, 1);
        Fx6 w = ProductsMath.DesignWork(P.W0, cx, Probe.DesignScale[0], Fx6.Zero, P.EB);
        Assert.Equal(w * Fx6.FromInt(1) / Fx6.FromInt(5), p.WorkLeft(Gbr, 1));
        // novelty counts the one changed slot (1/5): D0 = ((20·0.2)·complexity)/1.
        Assert.Equal(P.D0Factor * Fx6.FromRaw(200_000) * Fx6.FromInt(cx), p.InitialDefects(Gbr, 1));
    }

    [Fact]
    public void Derive_Validator_T4()
    {
        var h = Gb(2, (a, t) => a != Gbr ? [] : t switch
        {
            0 => [.. Derive(0, ("airframe", "af_swept")), .. Derive(0, ("compressor", "comp_centrifugal")), .. Derive(1, ("compressor", "comp_axial")),
                  .. Derive(0, ("engine", "eng_g3")), .. Derive(7, ("compressor", "comp_axial"))],
            _ => [],
        });
        h.RunThroughMonth(0);
        ProductsSystem p = h.Products;
        // airframe changed, nothing changed (same alternative), parent not an own design in С/В (slot 1 empty), parent out of range.
        Assert.Equal(4, p.RejectionCount(Gbr, CommandRejection.BadDerive));
        Assert.Equal(1, p.RejectionCount(Gbr, CommandRejection.SlotUnavailable));   // eng_g3 not available
        Assert.Equal(DesignStage.None, p.Stage(Gbr, 1));
    }

    [Fact]
    public void Derive_StartSeries_MovesTheParentsLine_P9()
    {
        var h = Gb(2, (a, t) => a == Gbr && t == 1 ? Derive(0, ("compressor", "comp_axial")) : []);
        ProductsSystem p = h.Products;
        h.Launch = (a, d, t) => a == Gbr && d == 0 && t == 0;
        h.Rate = (a, d, t) => d == 0 ? Fx6.FromInt(10) : Fx6.Zero;
        h.Series = (a, d, t) => a == Gbr && d == 1 && p.Stage(a, d) == DesignStage.Prototype && p.GateOpen(a, d, t) ? new SeriesStart(Fx6.Zero, false) : null;
        long launch = -1;
        Fx6 parentE = Fx6.Zero;
        for (long t = 1; t < 80 && launch < 0; t++)
        {
            parentE = p.LineE(Gbr, 0);          // the snapshot the move reads
            h.RunThroughMonth(t);
            if (p.Stage(Gbr, 1) == DesignStage.Series)
                launch = t;
        }
        Assert.True(launch > 0, "the derived design never reached the series");
        Assert.True(parentE > Fx6.Zero);
        Assert.Equal(0, p.Parent(Gbr, 1));
        Assert.Equal(DesignStage.InService, p.Stage(Gbr, 0));
        Assert.Equal(Fx6.Zero, p.LineE(Gbr, 0));
        // E(derived) = τ_der·E(parent of the snapshot), then δ_E forgetting; the derived line makes nothing (rate 0). Not a copy:
        // the parent's line is closed (double counting of the assembly channel, §8.2).
        Fx6 e0 = P.TauDer * parentE;
        Assert.Equal(e0 - P.DeltaE * e0, p.LineE(Gbr, 1));
    }

    // ---- series gate, force, accidents, retrofit (T-9, T-10)

    // GBR designs G2 superior at month 0 with full grants; the project reaches О after about 32 months.
    private static ProductsHarness Prototype(out long stageO, ProductsParams? parameters = null)
    {
        var h = new ProductsHarness(parameters: parameters)
        {
            Knows = (a, n) => a == Gbr && Probe.KnownUpTo(2)(n),
            Commands = (a, t) => a == Gbr && t == 0 ? [SuperiorG2(Probe)] : [],
        };
        long t = 0;
        while (h.Products.Stage(Gbr, 1) != DesignStage.Prototype)
        {
            h.RunThroughMonth(t++);
            Assert.True(t < 80, "the prototype never came");
        }
        stageO = h.Products.StageOMonth(Gbr, 1);
        return h;
    }

    [Fact]
    public void EnteringTesting_SetsThePreliminaryGate_AndStageOMonth_T9()
    {
        ProductsHarness h = Prototype(out long stageO);
        ProductsSystem p = h.Products;
        Assert.InRange(stageO, 20, 60);
        Fx6 d0 = p.InitialDefects(Gbr, 1);
        Assert.Equal(740_000_000, d0.Raw);
        Assert.Equal(516_981_760, p.DGate(Gbr, 1).Raw);     // reference v24 references G2.superior d_gate_preliminary
        Assert.False(p.GateOpened(Gbr, 1));
        Assert.False(p.SeriesForced(Gbr, 1));
    }

    [Fact]
    public void GateOpensSixMonthsAfterEnteringTesting_AndFixesDGate_T9()
    {
        ProductsHarness h = Prototype(out long stageO);
        ProductsSystem p = h.Products;
        Assert.Equal(6, P.TTestMin);
        Fx6 hiddenBefore = Fx6.Zero;
        for (long t = stageO + 1; t <= stageO + 6; t++)
        {
            hiddenBefore = p.DefectsHidden(Gbr, 1);
            Assert.Equal(t >= stageO + 6, p.GateOpen(Gbr, 1, t));
            h.RunThroughMonth(t);
            Assert.Equal(t >= stageO + 6, p.GateOpened(Gbr, 1));
        }
        // d_gate := D_hidden of the snapshot of the month the gate opened (final, ≤ the preliminary one).
        Assert.Equal(hiddenBefore, p.DGate(Gbr, 1));
        Assert.True(p.DGate(Gbr, 1) <= Fx6.FromRaw(516_981_760));
    }

    [Fact]
    public void ClosedGate_RefusesSeries_UnlessForced_ForceIsIrreversibleAndLogged_T9()
    {
        ProductsHarness h = Prototype(out long stageO);
        ProductsSystem p = h.Products;
        h.Series = (a, d, t) => a == Gbr && d == 1 ? new SeriesStart(Fx6.FromInt(10), false) : null;
        h.RunThroughMonth(stageO + 1);
        Assert.Equal(DesignStage.Prototype, p.Stage(Gbr, 1));
        Assert.Equal(1, p.RejectionCount(Gbr, CommandRejection.SeriesGate));
        Assert.False(p.SeriesForced(Gbr, 1));

        h.Series = (a, d, t) => a == Gbr && d == 1 ? new SeriesStart(Fx6.FromInt(10), true) : null;
        Fx6 hidden = p.DefectsHidden(Gbr, 1);
        Fx6 dGate = p.DGate(Gbr, 1);
        h.RunThroughMonth(stageO + 2);
        Assert.Equal(DesignStage.Series, p.Stage(Gbr, 1));
        Assert.True(p.SeriesForced(Gbr, 1));
        Assert.Equal(1, p.RejectionCount(Gbr, CommandRejection.SeriesGate));
        // retro_pool = X at launch: the excess of the snapshot's hidden defects over the preliminary d_gate (retrofit may already have started).
        Assert.True(hidden > dGate);
        Assert.True(p.RetroPool(Gbr, 1) <= hidden - dGate);
        Assert.True(p.RetroPool(Gbr, 1) > Fx6.Zero);
    }

    [Fact]
    public void ForcedSeries_CausesAccidents_AndWritesOffWholeMachines_T10()
    {
        // Slow detection (λ_D small): the excess X = D_hidden − d_gate stays ≈ 0.3·D_ref, so a forced fleet keeps losing machines.
        ProductsHarness h = Prototype(out long stageO, new ProductsParams { LambdaD = Fx6.FromRaw(100) });
        ProductsSystem p = h.Products;
        h.Series = (a, d, t) => a == Gbr && d == 1 ? new SeriesStart(Fx6.FromInt(200), true) : null;
        h.Budget = (a, t) => Fx6.FromInt(2);
        Fx6 lost = Fx6.Zero;
        for (long t = stageO + 1; t <= stageO + 12; t++)
        {
            Fx6 before = p.InService(Gbr, 1);
            h.RunThroughMonth(t);
            lost += p.AccidentsLost(Gbr, 1);
            Assert.True(p.AccidentsLost(Gbr, 1) <= p.InServiceBeforeAccidents(Gbr, 1));
            Assert.Equal(0, p.AccidentsLost(Gbr, 1).Raw % Fx6.Scale);
            Assert.InRange(p.LossAcc(Gbr, 1).Raw, 0, Fx6.Scale - 1);
        }
        Assert.True(lost >= Fx6.FromInt(3), $"only {lost} machines lost");
        Assert.True(p.SeriesForced(Gbr, 1));
        Assert.True(p.ExcessDefects(Gbr, 1) > Fx6.Zero);
    }

    [Fact]
    public void ForcedSeries_RetrofitSpendsThePool_WithinTheBudget_T10()
    {
        ProductsHarness h = Prototype(out long stageO);
        ProductsSystem p = h.Products;
        h.Series = (a, d, t) => a == Gbr && d == 1 ? new SeriesStart(Fx6.FromInt(20), true) : null;
        h.Budget = (a, t) => Fx6.FromRaw(200_000);
        Fx6 pool0 = Fx6.Zero;
        for (long t = stageO + 1; t <= stageO + 14; t++)
        {
            h.RunThroughMonth(t);
            if (t == stageO + 1)
                pool0 = p.RetroPool(Gbr, 1);
            // spent = upkeep paid + retrofit + purchases <= budget (contract 4.4d)
            Assert.True(p.Spent(Gbr) <= Fx6.FromRaw(200_000));
            Assert.True(p.RetroPool(Gbr, 1) >= Fx6.Zero);
        }
        Assert.True(pool0 > Fx6.Zero);
        Assert.True(p.RetroPool(Gbr, 1) < pool0, "the fix team retrofits built machines, spending the pool");
        Assert.True(p.InService(Gbr, 1) > Fx6.Zero);
    }

    [Fact]
    public void Retrofit_WithoutMoney_LeavesDefectsOpen_T10()
    {
        ProductsHarness h = Prototype(out long stageO);
        ProductsSystem p = h.Products;
        h.Series = (a, d, t) => a == Gbr && d == 1 && t == stageO + 1 ? new SeriesStart(Fx6.FromInt(20), true) : null;
        h.RunThroughMonth(stageO + 1);
        h.Budget = (_, _) => Fx6.Zero;
        Fx6 pool = p.RetroPool(Gbr, 1);
        Fx6 open = p.DefectsOpen(Gbr, 1);
        h.RunThroughMonth(stageO + 2);
        h.RunThroughMonth(stageO + 3);
        // No money: not one retrofit is paid, the pool stays, found defects stay open, readiness falls through the defects.
        Assert.Equal(pool, p.RetroPool(Gbr, 1));
        Assert.True(p.DefectsOpen(Gbr, 1) >= open);
    }

    [Fact]
    public void GateRespectingAi_NeverHasExcess_AndNeverLosesMachines_T14()
    {
        ProductsHarness h = Gb(2, (a, t) => a == Gbr && t == 0 ? [SuperiorG2(Probe)] : []);
        ProductsSystem p = h.Products;
        h.Series = (a, d, t) => a == Gbr && d == 1 && p.Stage(a, d) == DesignStage.Prototype ? new SeriesStart(Fx6.FromInt(15), false) : null;
        Fx6 lost = Fx6.Zero;
        for (long t = 0; t < 110; t++)
        {
            h.RunThroughMonth(t);                       // Post throws if series_forced = false and X > 0
            lost += p.AccidentsLost(Gbr, 1);
            if (p.Stage(Gbr, 1) is DesignStage.Series or DesignStage.InService)
            {
                Assert.False(p.SeriesForced(Gbr, 1));
                Assert.Equal(Fx6.Zero, p.ExcessDefects(Gbr, 1));
                Assert.Equal(Fx6.Zero, p.RetroPool(Gbr, 1));
            }
        }
        Assert.Equal(DesignStage.Series, p.Stage(Gbr, 1));
        Assert.True(p.GateOpened(Gbr, 1));
        Assert.Equal(Fx6.Zero, lost);
        Assert.True(p.RejectionCount(Gbr, CommandRejection.SeriesGate) > 0);   // the gate refused the launches until it opened
    }

    // ---- R-PS2m per block (T-6)

    [Fact]
    public void UnitCost_UsesProfileMultipliersPerBlock_T6()
    {
        // The airframe PRCs (38, 53, 55) yield 0.5 → U_AF = 2 (control profile); the power-unit PRCs yield 1.
        var h = new ProductsHarness
        {
            Launch = (a, d, _) => a == Usa && d == 0,
            Rate = (_, _, _) => Fx6.FromInt(5),
            Yield = (_, p) => p is 38 or 53 ? Fx6.FromRaw(500_000) : Fx6.One,
        };
        h.RunThroughMonth(0);
        ProductsSystem p = h.Products;
        int[] alts = p.Alternatives(Usa, 0);
        int[] prc = p.PrcSet(Usa, 0);
        int[] pu = DesignRules.Block(Probe, prc, airframe: false), af = DesignRules.Block(Probe, prc, airframe: true);
        Fx6 uPu = h.Inputs.Profile(Usa, DesignRules.Items(Probe, pu), 0).UnitCostMult, uAf = h.Inputs.Profile(Usa, DesignRules.Items(Probe, af), 0).UnitCostMult;
        Assert.Equal(Fx6.One, uPu);
        Assert.True(uAf > Fx6.One);
        int r = DesignRules.PuRank(Probe, alts), k = DesignRules.AfClass(Probe, alts);
        Fx6 part = ProductsMath.ProcessPart(DesignRules.BaseCostSum(Probe, pu), uPu, Probe.ProcScalePu[r - 1], DesignRules.BaseCostSum(Probe, af), uAf, Probe.ProcScaleAf[k - 1]);
        var card = new CostCard(Probe.HFirst[k - 1], Probe.HFloor[k - 1], Probe.WageKh, Probe.MaterialsFloor[k - 1], part, P.LearningQ10);
        Assert.Equal(ProductsMath.UnitCost(card, Fx6.Zero), p.UnitCost(Usa, 0));
        // Blocks without a process cost nothing and are skipped.
        Assert.Equal(Fx6.Zero, ProductsMath.ProcessPart(Fx6.Zero, Fx6.FromInt(7), Fx6.One, Fx6.Zero, Fx6.FromInt(9), Fx6.One));
    }

    [Fact]
    public void AlloyDraw_FollowsTheHotSectionAlternative_T7()
    {
        // The G2 hot section hot_sa2_forged burns 670 kg a unit, not the 290 of the G1 design: 2 900 kg buy 4.328358 units.
        var h = new ProductsHarness
        {
            Knows = (a, n) => a == Gbr && Probe.KnownUpTo(2)(n),
            Commands = (a, t) => a == Gbr && t == 0 ? [SuperiorG2(Probe)] : [],
            Alloy = (_, _) => Fx6.FromInt(2900),
        };
        ProductsSystem p = h.Products;
        long t = 0;
        while (p.Stage(Gbr, 1) != DesignStage.Prototype)
            h.RunThroughMonth(t++);
        h.Series = (a, d, tt) => a == Gbr && d == 1 ? new SeriesStart(Fx6.FromInt(50), true) : null;
        h.RunThroughMonth(t);
        Assert.Equal(Fx6.FromInt(2900) / Fx6.FromInt(670), p.Rate(Gbr, 1));
    }

    // ---- planned, v_exp (T-12)

    [Fact]
    public void PlannedAndVExp_FollowProjectsInStagesPAndO_T12()
    {
        var h = Gb(2, (a, t) => a == Gbr && t == 0 ? [SuperiorG2(Probe)] : []);
        ProductsSystem p = h.Products;
        Assert.False(p.Planned(Gbr, 53));
        Assert.Equal(Fx6.Zero, p.PlannedVolume(Gbr, 53));
        h.RunThroughMonth(0);
        // Stage П: r_plan = r_ref/4 = 6.25 units a month, v = (r_plan·n_p)·labour_p.
        Assert.True(p.Planned(Gbr, 53));
        Assert.True(p.Planned(Gbr, 40));
        Assert.False(p.Planned(Gbr, 41));                  // not in the G2 project
        Assert.False(p.Planned(Usa, 53));
        Assert.Equal(Fx6.FromRaw(6_250_000) * Fx6.One * Fx6.FromInt(3700), p.PlannedVolume(Gbr, 53));
        Assert.Equal(Fx6.Zero, p.PlannedVolume(Gbr, 41));
        Assert.Equal(Fx6.Zero, p.PlannedVolume(Gbr, 9999));
        long t = 1;
        while (p.Stage(Gbr, 1) != DesignStage.Prototype)
            h.RunThroughMonth(t++);
        Assert.Equal(Fx6.FromInt(25) * Fx6.One * Fx6.FromInt(3700), p.PlannedVolume(Gbr, 53));   // О: r_plan = r_ref
        h.Series = (a, d, tt) => a == Gbr && d == 1 ? new SeriesStart(Fx6.FromInt(5), true) : null;
        h.RunThroughMonth(t);
        Assert.False(p.Planned(Gbr, 53));                   // a series is not planned output
        Assert.Equal(Fx6.Zero, p.PlannedVolume(Gbr, 53));
    }

    // ---- FV (T-11)

    [Fact]
    public void FleetValue_AlloyCapsTheFleet_AndPerfRelEntersAsRoot_T11()
    {
        var h = new ProductsHarness { Alloy = (_, _) => Fx6.FromInt(290 * 5) };
        int[] alts = h.Products.Alternatives(Usa, 0);
        Fx6 budget = Fx6.FromInt(1_000);
        // supply/alloy_kg = 5 per month → ⌊5·24⌋ = 120 units cap; step 10 down.
        (int n, Fx6 v1) = h.Products.FleetValue(Usa, alts, Fx6.Zero, Fx6.One, Fx6.One, budget, 0);
        Assert.Equal(120, n);
        (_, Fx6 v4) = h.Products.FleetValue(Usa, alts, Fx6.Zero, Fx6.One, Fx6.FromInt(4), budget, 0);
        Assert.Equal(Fx6.FromInt(120), v1);
        Assert.Equal(Fx6.FromInt(240), v4);                 // PowFrac(4, 512) = 2
        // A tight budget binds before the alloy.
        Assert.True(h.Products.FleetValue(Usa, alts, Fx6.Zero, Fx6.One, Fx6.One, Fx6.FromRaw(40_000), 0).Count < 120);
    }

    [Fact]
    public void MaxFleetValue_IsTheBestAdmittedDesign_P11()
    {
        var h = new ProductsHarness { Alloy = (_, _) => Fx6.FromInt(1_000_000) };
        ProductsSystem p = h.Products;
        Fx6 usa = p.MaxFleetValue(Usa, Fx6.One, Fx6.FromRaw(440_000), 0);
        Assert.True(usa > Fx6.Zero);
        Assert.Equal(Fx6.Zero, p.MaxFleetValue(Fra, Fx6.One, Fx6.FromRaw(440_000), 0));   // no design
        h.Yield = (_, _) => Fx6.Zero;
        Assert.Equal(Fx6.Zero, p.MaxFleetValue(Usa, Fx6.One, Fx6.FromRaw(440_000), 0));  // not admitted
    }

    [Fact]
    public void LayoutVersion_IsTheSecondColumnLayout_T15() => Assert.Equal(2, ProductsSystem.LayoutVersion);
}
