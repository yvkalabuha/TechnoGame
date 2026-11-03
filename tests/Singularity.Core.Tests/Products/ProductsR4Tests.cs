using Singularity.Core.Data;
using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// Contract v2.4 r3/r4 (#123, #128): <c>cancel_design</c> (§4.4e), the line columns <c>idle_c</c>/<c>built_any</c> (§3.1), closing a line
/// without output as «снят» by every path, and the invariants (1)–(5), (8)–(10), V1–V4 of §4.1a/§4.4e.
/// </summary>
public class ProductsR4Tests
{
    private const int Gbr = 1;

    // GBR knows G2: a superior G2 project in slot 1 (slot 0 is the start fleet).
    private static ProductsHarness Gb(Func<int, long, IReadOnlyList<DesignCommand>> commands) => new()
    {
        Knows = (a, n) => a == Gbr && Probe.KnownUpTo(2)(n),
        Commands = commands,
    };

    private static DesignCommand SuperiorG2() => Compose(Probe, 2, "eng_g2", "hot_sa2_forged", "comp_axial", "af_swept", "ab_on");

    private static DesignCommand Cancel(int design) => new(DesignCommandKind.Cancel, design, []);

    private static long ToPrototype(ProductsHarness h, int design = 1, long from = 0)
    {
        long t = from;
        while (h.Products.Stage(Gbr, design) != DesignStage.Prototype)
        {
            h.RunThroughMonth(t++);
            Assert.True(t < 120, "the project never reached stage О");
        }
        return t;
    }

    // ---- cancel_design (contract §4.4e)

    [Fact]
    public void Cancel_InStageO_WithdrawsTheProject_FreezesItsNumbers_AndKeepsTheBureau_Inv1to5()
    {
        long cancelAt = -1;
        var h = Gb((a, t) => a == Gbr && t == 0 ? [SuperiorG2()] : a == Gbr && t == cancelAt ? [Cancel(1)] : []);
        var control = Gb((a, t) => a == Gbr && t == 0 ? [SuperiorG2()] : []);        // the same run without the command
        ProductsSystem p = h.Products;
        long t = ToPrototype(h);
        ToPrototype(control);
        for (int i = 0; i < 2; i++)
        {
            h.RunThroughMonth(t);
            control.RunThroughMonth(t++);
        }
        int bureau = p.DesignBureau(Gbr, 1);
        Assert.True(p.BureauExp(Gbr, bureau) > Fx6.Zero);             // +1 was paid when W ended
        Fx6 d0 = p.InitialDefects(Gbr, 1), hidden = p.DefectsHidden(Gbr, 1), open = p.DefectsOpen(Gbr, 1);
        int[] alts = p.Alternatives(Gbr, 1);
        cancelAt = t;
        h.RunThroughMonth(t);
        control.RunThroughMonth(t++);
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 1));
        Assert.True(p.Cancelled(Gbr, 1));
        Assert.False(p.BuiltAny(Gbr, 1));
        Assert.Equal(control.Products.BureauExp(Gbr, bureau), p.BureauExp(Gbr, bureau));   // (3) the command changes no experience
        Assert.Equal(-1, p.BureauDesign(Gbr, bureau));
        Assert.Equal(Fx6.Zero, p.LineE(Gbr, 1));
        Assert.Equal(Fx6.Zero, p.InService(Gbr, 1));
        Assert.True(p.Spent(Gbr) <= control.Products.Spent(Gbr));      // (5) no new spending, no refund (the control keeps paying fixes)
        Assert.Equal(hidden, p.DefectsHidden(Gbr, 1));                 // (1) the tests stop in the month of application
        for (int i = 0; i < 6; i++)
        {
            h.RunThroughMonth(t++);
            Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 1));
            Assert.Equal(d0, p.InitialDefects(Gbr, 1));
            Assert.Equal(hidden, p.DefectsHidden(Gbr, 1));
            Assert.Equal(open, p.DefectsOpen(Gbr, 1));
            Assert.Equal(alts, p.Alternatives(Gbr, 1));
            // (2) not planned, v_exp 0, no request of its fix team
            Assert.False(p.Planned(Gbr, 53));
            Assert.Equal(Fx6.Zero, p.ExpectedVolume(Gbr, 53, Fx6.Zero, true));
            Assert.DoesNotContain(h.Requests, q => q.Site == Gbr && q.RequesterId == h.Layout.FixRequester(1));
        }
    }

    [Fact]
    public void Cancel_InStageP_FreesTheBureauWithoutTheExperience_AndWorkIsLost()
    {
        var h = Gb((a, t) => a == Gbr && t == 0 ? [SuperiorG2()] : a == Gbr && t == 3 ? [Cancel(1)] : []);
        ProductsSystem p = h.Products;
        for (long t = 0; t <= 2; t++)
            h.RunThroughMonth(t);
        Assert.Equal(DesignStage.Project, p.Stage(Gbr, 1));
        int bureau = p.DesignBureau(Gbr, 1);
        Assert.Equal(Fx6.Zero, p.BureauExp(Gbr, bureau));
        h.RunThroughMonth(3);
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 1));
        Assert.True(p.Cancelled(Gbr, 1));
        Assert.Equal(-1, p.BureauDesign(Gbr, bureau));               // the bureau is free again
        Assert.Equal(Fx6.Zero, p.BureauExp(Gbr, bureau));            // no +1 for an unfinished project
        Assert.False(p.Planned(Gbr, 53));
        Assert.Equal(0, p.RejectionCount(Gbr, CommandRejection.BadCancel));
    }

    [Fact]
    public void Cancel_Validator_RejectsEverythingButAnOwnProjectInPOrO_AndNothingStartsFromAWithdrawnProject_Inv4()
    {
        long at = -1;
        var h = Gb((a, t) => a != Gbr ? [] : t == 0 ? [SuperiorG2()]
            : t == at ? [Cancel(0) /* a fleet in В */, Cancel(7) /* empty slot */, Cancel(99) /* out of range */, Cancel(1), Cancel(1) /* twice */,
                         new DesignCommand(DesignCommandKind.Derive, 1, [.. Enumerable.Repeat(-1, Probe.SlotCount - 1), Alt(Probe, "ab_none")])] : []);
        ProductsSystem p = h.Products;
        long t = ToPrototype(h);
        at = t;
        h.Series = (a, d, tt) => a == Gbr && d == 1 && tt >= at ? new SeriesStart(Fx6.FromInt(5), true) : null;
        h.RunThroughMonth(t);
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 1));        // start_series of the same month after the cancel does nothing
        Assert.Equal(4, p.RejectionCount(Gbr, CommandRejection.BadCancel));
        Assert.Equal(1, p.RejectionCount(Gbr, CommandRejection.BadDerive));
        h.RunThroughMonth(t + 1);
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 1));        // nor in later months
        Assert.Equal(Fx6.Zero, p.Rate(Gbr, 1));
    }

    // ---- idle_c, built_any (contract §3.1, §4.4d step 7; invariants 8–10)

    [Fact]
    public void IdleAndBuiltAny_FollowTheLineOutputWithBreaks_Inv8to10()
    {
        bool dead = true;
        var h = Gb((a, t) => a == Gbr && t == 0 ? [SuperiorG2()] : []);
        h.Yield = (_, _) => dead ? Fx6.Zero : Fx6.One;
        ProductsSystem p = h.Products;
        // Until the project is ordered the yield does not matter (no line); let the script run in the live state.
        dead = false;
        long t = ToPrototype(h);
        dead = true;
        long launch = t;
        h.Series = (a, d, tt) => a == Gbr && d == 1 && tt == launch ? new SeriesStart(Fx6.FromInt(5), true) : null;
        var script = new (bool Dead, int Months)[] { (true, 3), (false, 2), (true, 2), (false, 1), (true, 4) };
        int idle = 0;
        bool built = false;
        bool first = true;
        foreach ((bool d, int months) in script)
        {
            dead = d;
            for (int i = 0; i < months; i++)
            {
                h.RunThroughMonth(t++);
                Assert.Equal(DesignStage.Series, p.Stage(Gbr, 1));
                bool produced = p.Rate(Gbr, 1) > Fx6.Zero;
                Assert.Equal(!d, produced);
                idle = produced ? 0 : idle + 1;                          // (10) includes the launch month
                built |= produced;
                Assert.Equal(idle, p.IdleMonths(Gbr, 1));
                Assert.Equal(built, p.BuiltAny(Gbr, 1));
                first = false;
            }
        }
        Assert.False(first);
        // A line that built something closes into В, built_any stays, idle_c freezes; (8) В ⇒ built_any.
        long closeAt = t;
        h.Close = (a, d, tt) => a == Gbr && d == 1 && tt == closeAt;
        h.RunThroughMonth(t++);
        Assert.Equal(DesignStage.InService, p.Stage(Gbr, 1));
        Assert.False(p.Cancelled(Gbr, 1));
        Assert.True(p.BuiltAny(Gbr, 1));
        int frozen = p.IdleMonths(Gbr, 1);
        h.RunThroughMonth(t++);
        Assert.Equal(frozen, p.IdleMonths(Gbr, 1));
    }

    [Fact]
    public void ClosingASeriesThatNeverProduced_IsWithdrawnAndCancelled_ByTheClosingCommand_Inv9()
    {
        ProductsHarness Run(bool close, out long closeMonth)
        {
            var h = Gb((a, t) => a == Gbr && t == 0 ? [SuperiorG2()] : []);
            long t = ToPrototype(h);
            h.Yield = (_, _) => Fx6.Zero;                              // the processes are not admitted: output 0
            long start = t;
            h.Series = (a, d, tt) => a == Gbr && d == 1 && tt == start ? new SeriesStart(Fx6.FromInt(5), true) : null;
            h.RunThroughMonth(t++);
            ProductsSystem p = h.Products;
            Assert.Equal(DesignStage.Series, p.Stage(Gbr, 1));
            Assert.True(p.SeriesForced(Gbr, 1));
            Assert.Equal(1, p.IdleMonths(Gbr, 1));                      // the launch month counts
            Assert.True(p.Planned(Gbr, 53));                            // r4: a series without output is planned like a project in О
            Assert.Equal(Fx6.FromInt(25) * Fx6.One * Fx6.FromInt(3700), p.ExpectedVolume(Gbr, 53, Fx6.Zero, true));
            h.RunThroughMonth(t++);
            closeMonth = t;
            long closeAt = t;
            if (close)
                h.Close = (a, d, tt) => a == Gbr && d == 1 && tt == closeAt;
            h.RunThroughMonth(t++);
            return h;
        }
        ProductsHarness h = Run(true, out long closeMonth), control = Run(false, out _);
        ProductsSystem p = h.Products;
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 1));
        Assert.True(p.Cancelled(Gbr, 1));
        Assert.False(p.BuiltAny(Gbr, 1));
        Assert.True(p.SeriesForced(Gbr, 1));                            // a journal record: kept
        Assert.Equal(Fx6.Zero, p.LineE(Gbr, 1));
        Assert.Equal(Fx6.Zero, p.InService(Gbr, 1));
        Assert.False(p.Planned(Gbr, 53));
        Assert.Equal(Fx6.Zero, p.ExpectedVolume(Gbr, 53, Fx6.Zero, true));   // (12): v_exp 0 from the month after the command
        Assert.Equal(DesignStage.Series, control.Products.Stage(Gbr, 1));
        for (int b = 0; b < h.Layout.Bureaus; b++)
            Assert.Equal(control.Products.BureauExp(Gbr, b), p.BureauExp(Gbr, b));   // bureau_exp is not touched by the closure
        Fx6 frozenHidden = p.DefectsHidden(Gbr, 1);
        int frozenIdle = p.IdleMonths(Gbr, 1);
        Assert.True(frozenHidden > control.Products.DefectsHidden(Gbr, 1));           // the control kept testing in the closing month
        h.RunThroughMonth(closeMonth + 1);
        Assert.Equal(frozenHidden, p.DefectsHidden(Gbr, 1));            // the tests of stage С stop
        Assert.Equal(frozenIdle, p.IdleMonths(Gbr, 1));                 // idle_c froze: the launch month and the next one, not the closing month
        Assert.Equal(2, frozenIdle);
    }

    [Fact]
    public void TransferOfALineToADerivedDesign_ClosesAParentWithoutOutputAsWithdrawn()
    {
        bool dead = false;
        var h = Gb((a, t) => a == Gbr && t == 0 ? [SuperiorG2()] : []);
        h.Yield = (_, _) => dead ? Fx6.Zero : Fx6.One;
        ProductsSystem p = h.Products;
        long t = ToPrototype(h);
        dead = true;
        long start = t;
        h.Series = (a, d, tt) => a == Gbr && d == 1 && tt == start ? new SeriesStart(Fx6.FromInt(5), true) : null;
        h.RunThroughMonth(t++);
        Assert.False(p.BuiltAny(Gbr, 1));
        long deriveAt = t;
        h.Commands = (a, tt) => a == Gbr && tt == deriveAt ? Derive(1, ("compressor", "comp_centrifugal")) : [];
        while (p.Stage(Gbr, 2) != DesignStage.Prototype)
        {
            h.RunThroughMonth(t++);
            Assert.True(t < 200, "the derived design never reached stage О");
        }
        long launch = t;
        h.Series = (a, d, tt) => a == Gbr && d == 2 && tt == launch ? new SeriesStart(Fx6.FromInt(5), true) : null;
        h.RunThroughMonth(t++);
        Assert.Equal(DesignStage.Series, p.Stage(Gbr, 2));
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 1));
        Assert.True(p.Cancelled(Gbr, 1));
        Assert.Equal(Fx6.Zero, p.LineE(Gbr, 2));                         // nothing to inherit: the parent never built
    }

    [Fact]
    public void ADerivedLineThatNeverProduced_IsWithdrawnWithItsInheritedLineE_TheParentStaysInService()
    {
        bool dead = false;
        var h = Gb((a, t) => a == Gbr && t == 0 ? [SuperiorG2()] : []);
        h.Yield = (_, _) => dead ? Fx6.Zero : Fx6.One;
        ProductsSystem p = h.Products;
        long t = ToPrototype(h);
        long start = t;
        h.Series = (a, d, tt) => a == Gbr && d == 1 && tt == start ? new SeriesStart(Fx6.FromInt(5), true) : null;
        for (int i = 0; i < 4; i++)
            h.RunThroughMonth(t++);
        Assert.True(p.BuiltAny(Gbr, 1));
        long deriveAt = t;
        h.Commands = (a, tt) => a == Gbr && tt == deriveAt ? Derive(1, ("compressor", "comp_centrifugal")) : [];
        while (p.Stage(Gbr, 2) != DesignStage.Prototype)
        {
            h.RunThroughMonth(t++);
            Assert.True(t < 200, "the derived design never reached stage О");
        }
        dead = true;                                                    // the derived line will produce nothing
        long launch = t;
        h.Series = (a, d, tt) => a == Gbr && d == 2 && tt == launch ? new SeriesStart(Fx6.FromInt(5), true) : null;
        h.RunThroughMonth(t++);
        Assert.Equal(DesignStage.InService, p.Stage(Gbr, 1));            // the parent's line moved
        Assert.Equal(DesignStage.Series, p.Stage(Gbr, 2));
        Assert.True(p.LineE(Gbr, 2) > Fx6.Zero);                         // inherited (τ_der = 1)
        Assert.False(p.BuiltAny(Gbr, 2));
        long closeDerived = t;
        h.Close = (a, d, tt) => a == Gbr && d == 2 && tt == closeDerived;
        h.RunThroughMonth(t++);
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 2));
        Assert.True(p.Cancelled(Gbr, 2));
        Assert.Equal(Fx6.Zero, p.LineE(Gbr, 2));                         // the inherited experience is lost
        Assert.Equal(DesignStage.InService, p.Stage(Gbr, 1));
        Assert.False(p.Cancelled(Gbr, 1));
    }

    private static IReadOnlyList<DesignCommand> Derive(int parent, params (string Slot, string Alt)[] changes)
    {
        var alts = new int[Probe.SlotCount];
        Array.Fill(alts, -1);
        foreach ((string slot, string alt) in changes)
            alts[Enumerable.Range(0, Probe.SlotCount).Single(s => CoreData.ProductClasses.SlotId[s] == slot)] = Alt(Probe, alt);
        return [new DesignCommand(DesignCommandKind.Derive, parent, [.. alts])];
    }

    // ---- v_exp (contract §4.1a r4; V3, V4)

    [Fact]
    public void ExpectedVolume_IsMonotoneInProcV_AndTheProjectPartIsCountedOnHomeOnly_V3V4()
    {
        var h = Gb((a, t) => a == Gbr && t == 0 ? [SuperiorG2()] : []);
        ProductsSystem p = h.Products;
        long t = ToPrototype(h);
        long launchMonth = t;
        h.Series = (a, d, tt) => a == Gbr && d == 1 && tt == launchMonth ? new SeriesStart(Fx6.FromInt(5), true) : null;
        h.RunThroughMonth(t);
        var rnd = new Random(41);
        foreach (int prc in new[] { 53, 40, 41, 9999 })
        {
            Fx6 projects = p.ExpectedVolume(Gbr, prc, Fx6.Zero, true);                 // V_C + V_PO with proc_v = 0
            Fx6 previous = Fx6.Zero;
            for (int i = 0; i < 200; i++)
            {
                Fx6 v = Fx6.FromRaw((long)rnd.Next(0, 90_000) * Fx6.Scale / 7);
                Fx6 other = Fx6.FromRaw((long)rnd.Next(0, 50_000) * Fx6.Scale / 3);
                Fx6 vexp = p.ExpectedVolume(Gbr, prc, v, true);
                Assert.True(vexp >= v);                                                // V3: proc_v ≤ v_exp
                Assert.True(vexp <= v + projects);                                     // V3: v_exp ≤ proc_v + V_C + V_PO
                Assert.Equal(other, p.ExpectedVolume(Gbr, prc, other, false));         // V3: other sites
                // the 2-site fixture: the project part is not counted twice
                Fx6 sum = vexp + p.ExpectedVolume(Gbr, prc, other, false);
                Assert.True(sum - v - other <= projects);
                Fx6 bigger = p.ExpectedVolume(Gbr, prc, v + Fx6.One, true);
                Assert.True(bigger >= vexp);                                           // V4
                previous = vexp;
            }
            Assert.True(previous >= Fx6.Zero);
        }
    }
}
