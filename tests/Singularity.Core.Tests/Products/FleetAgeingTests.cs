using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// TASK-0129 (#431, #435, #436): a fleet past its service life is not written off by age. It ages by the half-life curve, counts in the
/// «fielded» test by its effective units, and leaves only against the replacement credit that new units earn.
/// </summary>
public class FleetAgeingTests
{
    private const int Usa = 0;
    private static readonly ProductsParams P = new();

    // Cohort index 0 is 1945 with midyear −2 for the start 1945-09 (spec A32); the layout is the first column of m0.
    private static int[] M0(int years) => [.. Enumerable.Range(0, years).Select(y => y * 12 - 2)];

    [Fact]
    public void AgeValue_IsExactAtWholeHalfLives_MatchesTheDoubleTable_AndNeverRises()
    {
        Assert.Equal(1_000_000, ProductsParams.AgeValue(0).Raw);
        Assert.Equal(1_000_000, ProductsParams.AgeValue(-5).Raw);
        Assert.Equal(500_000, ProductsParams.AgeValue(60).Raw);
        Assert.Equal(250_000, ProductsParams.AgeValue(120).Raw);
        for (int k = 0; k < 60; k++)
            Assert.Equal((long)Math.Round(Math.Pow(2, -k / 60.0) * 1e6), ProductsParams.AgeValue(k).Raw);
        long last = long.MaxValue;
        for (int m = 0; m <= 1020; m++)
        {
            long v = ProductsParams.AgeValue(m).Raw;
            Assert.True(v <= last, $"f({m}) rises");
            Assert.True(v > 0, $"f({m}) is zero");
            last = v;
        }
    }

    // One cohort of n units of 1945 that nobody replaces: the first month (t = 178 + m) it is no longer fielded.
    [Theory]
    [InlineData(50, 1)]
    [InlineData(100, 61)]
    [InlineData(300, 156)]
    [InlineData(2000, 320)]
    public void ALoneFleet_StopsCounting_AtTheMonthOfTheHalfLifeCurve(int units, int monthsOver)
    {
        var h = new ProductsHarness(startLine: false, years: 50, tweakStart: s =>
        {
            for (int y = 0; y < s.Layout.Years; y++)
                s.Cohort[Usa * s.Layout.Designs * s.Layout.Years + y] = y == 0 ? Fx6.FromInt(units) : Fx6.Zero;
        }) { IsMastered = (_, _) => true };
        long first = 178 + monthsOver;
        h.RunThroughMonth(first + 1);
        ProductsSystem p = h.Products;
        Assert.Equal(Fx6.FromInt(units), p.InService(Usa, 0));            // nothing leaves: no replacement arrived
        Assert.True(p.FieldedMastered(Usa, 0, first - 1), $"{units} units must still count at t = {first - 1}");
        Assert.False(p.FieldedMastered(Usa, 0, first), $"{units} units must stop counting at t = {first}");
        Assert.Equal(p.FieldedL(Usa, 0, first - 1), p.FieldedMastered(Usa, 0, first - 1));
    }

    [Fact]
    public void FieldedUntil_IsTheMonthWhereTheCoreStopsCounting()
    {
        var h = new ProductsHarness(startLine: false, tweakStart: s =>
        {
            for (int y = 0; y < s.Layout.Years; y++)
                s.Cohort[Usa * s.Layout.Designs * s.Layout.Years + y] = y == 0 ? Fx6.FromInt(300) : Fx6.Zero;
        }) { IsMastered = (_, _) => true };
        h.RunThroughMonth(10);
        long end = h.Products.FieldedUntil(Usa, 0, 11);
        Assert.Equal(178 + 156, end);
        Assert.True(h.Products.FieldedL(Usa, 0, end - 1));
        Assert.False(h.Products.FieldedL(Usa, 0, end));
    }

    [Fact]
    public void Effective_WeighsEveryCohortByItsOwnAge()
    {
        int[] m0 = M0(3);
        Fx6[] counts = [Fx6.FromInt(100), Fx6.FromInt(100), Fx6.FromInt(100)];
        // At t = 178 + 120: cohort 0 is 120 months over (1/4), cohort 1 is 108 over, cohort 2 is 96 over.
        Fx6 eff = FleetRules.Effective(counts, 178 + 120, m0, P);
        Fx6 expected = Fx6.FromInt(100) * ProductsParams.AgeValue(120) + Fx6.FromInt(100) * ProductsParams.AgeValue(108) + Fx6.FromInt(100) * ProductsParams.AgeValue(96);
        Assert.Equal(expected, eff);
        Assert.Equal(Fx6.FromInt(300), FleetRules.Effective(counts, 10, m0, P));
    }

    // ---- the replacement credit

    [Fact]
    public void ExpendCredit_TakesTheOldestOverageFirst_ByTheCreditOnly_AndClipsTheCredit()
    {
        int[] m0 = M0(4);   // at t = 200 the cohorts 0 and 1 (ages 202, 190) are past 180, cohort 2 (178) is not
        Fx6[] counts = [Fx6.FromInt(100), Fx6.FromInt(50), Fx6.FromInt(80), Fx6.FromInt(10)];
        Fx6 credit = Fx6.FromInt(120);
        FleetRules.ExpendCredit(counts, ref credit, 200, m0, P);
        Assert.Equal([0, 30, 80, 10], counts.Select(c => (int)(c.Raw / Fx6.Scale)));
        Assert.Equal(Fx6.Zero, credit);

        // More credit than overage: only the overage leaves, the rest of the credit stays (up to the units left).
        counts = [Fx6.FromInt(100), Fx6.FromInt(50), Fx6.FromInt(80), Fx6.FromInt(10)];
        credit = Fx6.FromInt(500);
        FleetRules.ExpendCredit(counts, ref credit, 200, m0, P);
        Assert.Equal([0, 0, 80, 10], counts.Select(c => (int)(c.Raw / Fx6.Scale)));
        Assert.Equal(Fx6.FromInt(90), credit);   // 500 − 150 leaves, clipped to the 90 units left

        // No overage yet: nothing leaves.
        counts = [Fx6.FromInt(100), Fx6.FromInt(50), Fx6.FromInt(80), Fx6.FromInt(10)];
        credit = Fx6.FromInt(70);
        FleetRules.ExpendCredit(counts, ref credit, 50, m0, P);
        Assert.Equal(Fx6.FromInt(240), FleetRules.Sum(counts));
        Assert.Equal(Fx6.FromInt(70), credit);
    }

    [Fact]
    public void Accrue_CreditsTheNearestLowerProjectFirst_OnceEach_AndLosesWhatIsLeft()
    {
        // Projects: 0 = gen 1, 1 = gen 2, 2 = gen 3 (the new one), 3 = gen 2 with a lower perf.
        int[] gen = [1, 2, 3, 2];
        Fx6[] perf = [Fx6.One, Fx6.One, Fx6.One, Fx6.FromRaw(500_000)];
        long[] whole = [300, 100, 0, 150];
        Fx6[] credit = new Fx6[4];
        FleetRules.Accrue(Fx6.FromInt(400), 2, gen, perf, whole, credit, new bool[4], P);
        // Highest key first: gen 2 perf 1 (project 1) takes 100, then gen 2 perf 0.5 (project 3) takes 150, then gen 1 takes the last 150.
        Assert.Equal([150, 100, 0, 150], credit.Select(c => (int)(c.Raw / Fx6.Scale)));

        // 1000 arrive: the credit never exceeds the units, the surplus is lost.
        credit = new Fx6[4];
        FleetRules.Accrue(Fx6.FromInt(1000), 2, gen, perf, whole, credit, new bool[4], P);
        Assert.Equal([300, 100, 0, 150], credit.Select(c => (int)(c.Raw / Fx6.Scale)));

        // Already credited units are not credited twice.
        FleetRules.Accrue(Fx6.FromInt(10), 2, gen, perf, whole, credit, new bool[4], P);
        Assert.Equal([300, 100, 0, 150], credit.Select(c => (int)(c.Raw / Fx6.Scale)));
    }

    [Fact]
    public void Replaces_NeedsAHigherGeneration_OrTheSameOneWithFivePercentMorePerf()
    {
        Fx6 one = Fx6.One;
        Assert.True(FleetRules.Replaces(3, one, 2, Fx6.FromInt(9), P));          // a higher generation always replaces
        Assert.False(FleetRules.Replaces(2, Fx6.FromInt(9), 3, one, P));         // a lower one never does
        Assert.False(FleetRules.Replaces(2, one, 2, one, P));                    // equal projects do not replace each other
        Assert.False(FleetRules.Replaces(2, Fx6.FromRaw(1_049_999), 2, one, P));
        Assert.True(FleetRules.Replaces(2, Fx6.FromRaw(1_050_000), 2, one, P));
    }

    [Fact]
    public void ArrivingUnits_CreditTheLowerProject_AndTheOverageLeavesAgainstItOnce()
    {
        // GBR: slot 0 = 100 units of G1 (1945), slot 1 = a G2 line. The line's output credits slot 0; the 1945 cohort leaves at month 178 by the credit.
        var h = new ProductsHarness(startLine: false, tweakStart: s =>
        {
            ProductsLayout l = s.Layout;
            var alts = new int[l.SlotCount];
            Assert.True(DesignRules.Superior(l, 2, l.KnownUpTo(2), alts));
            int row = Gbr * l.Designs + 1;
            s.Stage[row] = DesignStage.Series;
            s.PlatformGen[row] = 2;
            for (int k = 0; k < alts.Length; k++)
                s.Alts[row * l.SlotCount + k] = alts[k];
        })
        {
            IsMastered = (_, _) => true,
            Knows = (_, _) => true,
            Rate = (a, d, _) => a == Gbr && d == 1 ? Fx6.FromInt(5) : null,
        };
        h.RunThroughMonth(20);
        ProductsSystem p = h.Products;
        Fx6 produced = p.InService(Gbr, 1);
        Assert.True(produced > Fx6.Zero, "the G2 line must produce");
        Fx6 whole0 = Fx6.FromInt((long)(p.InService(Gbr, 0).Raw / Fx6.Scale));
        Assert.Equal(produced < whole0 ? produced : whole0, p.ReplacementCredit(Gbr, 0));
        Assert.Equal(Fx6.Zero, p.ReplacementCredit(Gbr, 1));                       // the new project replaces nobody above it
        Fx6 credit = p.ReplacementCredit(Gbr, 0);
        Fx6 before = p.InService(Gbr, 0);
        h.RunThroughMonth(177);
        Assert.Equal(before, p.InService(Gbr, 0));                                  // before the service life nothing leaves
        h.RunThroughMonth(178);
        Assert.True(p.InService(Gbr, 0) < before, "the overage must leave against the credit");
    }

    private const int Gbr = 1;

    [Fact]
    public void ASlotThatEmptiesIsWithdrawn_ItsCreditIsZero_AndAReusedSlotStartsAtZero()
    {
        var h = new ProductsHarness(startLine: false, tweakStart: s =>
        {
            for (int y = 0; y < s.Layout.Years; y++)
                s.Cohort[Usa * s.Layout.Designs * s.Layout.Years + y] = y == 0 ? Fx6.FromInt(60) : Fx6.Zero;
        }) { IsMastered = (_, _) => true };
        h.FleetScript = (a, m) => a == Usa && m == 5 ? [new FleetCommand(FleetCommandKind.Retire, ProductsHarness.Now(0), 60)] : [];
        h.RunThroughMonth(6);
        Assert.Equal(DesignStage.Withdrawn, h.Products.Stage(Usa, 0));
        Assert.Equal(Fx6.Zero, h.Products.ReplacementCredit(Usa, 0));
    }

    // ---- the AI's book

    [Fact]
    public void MaxRetire_KeepsTheEffectiveUnitsOfAProjectOfDefK_AtOrAboveTheMinimum()
    {
        var book = new FleetBook(P, M0(3), 1);
        book.Counts[0][0] = Fx6.FromInt(60);
        // One month over: f = 0,988514, so 60 units are worth 59,3; after n units 60·f − n·f ≥ 50 holds for n ≤ 9.
        Assert.Equal(9, book.MaxRetire(0, 100, 179, 50));
        Assert.Equal(60, book.MaxRetire(0, 100, 179, 0));
        // Not yet old: the plain count, whole − keep.
        Assert.Equal(10, book.MaxRetire(0, 100, 100, 50));
    }

    [Fact]
    public void TheBookProjection_OfTheCreditStep_EqualsTheCoreStep()
    {
        var h = new ProductsHarness(startLine: false, tweakStart: s =>
        {
            ProductsLayout l = s.Layout;
            var alts = new int[l.SlotCount];
            Assert.True(DesignRules.Superior(l, 2, l.KnownUpTo(2), alts));
            int row = Gbr * l.Designs + 1;
            s.Stage[row] = DesignStage.Series;
            s.PlatformGen[row] = 2;
            for (int k = 0; k < alts.Length; k++)
                s.Alts[row * l.SlotCount + k] = alts[k];
        })
        {
            IsMastered = (_, _) => true,
            Knows = (_, _) => true,
            Rate = (a, d, _) => a == Gbr && d == 1 ? Fx6.FromInt(5) : null,
        };
        h.RunThroughMonth(177);
        FleetBook book = h.Products.SnapshotBook(Gbr, 178);
        Assert.True(book.ReplCredit[0] > Fx6.Zero);
        book.ExpendCredit(178);
        h.RunThroughMonth(178);
        Assert.Equal(h.Products.Cohorts(Gbr, 0), book.Counts[0]);
        Assert.True(h.Products.Cohort(Gbr, 0, 1945) < Fx6.FromInt(100));
    }
}
