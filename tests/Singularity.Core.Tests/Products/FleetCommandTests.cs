using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// TASK-0028 (T-fleet, spec §11 п. 1–3; contract r6 §4.4f): <c>retire_cohort</c> and <c>sell_cohort</c> on the product layer with scripted
/// inputs. Start state without a line (all start fleets in stage В) unless a test says otherwise; USA and GBR hold G1 superior, 100 units of 1945.
/// </summary>
public class FleetCommandTests
{
    private const int Usa = 0, Gbr = 1;
    private const long S = Fx6.Scale;

    private static FleetCommand Sell(int d, long n) => new(FleetCommandKind.Sell, d, n);
    private static FleetCommand Retire(int d, long n) => new(FleetCommandKind.Retire, d, n);

    // GBR also holds a G2 «superior» project (slot 1) of the given size in 1950: with everything mastered it makes G_front = 2 once it has 50 units.
    private static Action<ProductsStart> WithGbrG2(long units) => s =>
    {
        ProductsLayout l = s.Layout;
        int d = l.Designs;
        var alts = new int[l.SlotCount];
        Assert.True(DesignRules.Superior(l, 2, l.KnownUpTo(2), alts));
        int row = (Gbr * d + 1);
        s.Stage[row] = DesignStage.InService;
        s.PlatformGen[row] = 2;
        for (int k = 0; k < alts.Length; k++)
            s.Alts[row * l.SlotCount + k] = alts[k];
        s.Cohort[row * l.Years + 5] = Fx6.FromInt(units);
    };

    private static ProductsHarness World(Action<ProductsStart>? tweak = null, Func<int, long, Fx6>? budget = null, bool mastered = true, bool startLine = false)
    {
        var h = new ProductsHarness(startLine: startLine, tweakStart: tweak);
        if (mastered)
            h.IsMastered = (_, _) => true;
        if (budget is not null)
            h.Budget = budget;
        return h;
    }

    private static void Cohorts(ProductsStart s, int actor, int design, params double[] units)
    {
        int d = s.Layout.Designs;
        for (int y = 0; y < s.Layout.Years; y++)
            s.Cohort[(actor * d + design) * s.Layout.Years + y] = y < units.Length ? Fx6.FromRaw((long)Math.Round(units[y] * S)) : Fx6.Zero;
    }

    // ---- retire

    [Fact]
    public void Retire_TakesTheOldestFirst_TheFractionOfTheOldestCohortGoesFirst_AndUpkeepFallsByExactlyNTimesUpkeep()
    {
        Action<ProductsStart> tweak = s => Cohorts(s, Usa, 0, 10.5, 20, 5);
        var with = World(tweak);
        with.FleetScript = (a, m) => a == Usa && m == 0 ? [Retire(0, 12)] : [];
        var without = World(tweak);
        with.RunThroughMonth(0);
        without.RunThroughMonth(0);
        ProductsSystem p = with.Products;
        Assert.Equal(Fx6.Zero, p.Cohort(Usa, 0, 1945));
        Assert.Equal(Fx6.FromRaw(18_500_000), p.Cohort(Usa, 0, 1946));
        Assert.Equal(Fx6.FromInt(5), p.Cohort(Usa, 0, 1947));
        Assert.Equal(Fx6.FromInt(12), p.Retired(Usa, 0));
        Assert.Equal(Fx6.Zero, p.Sold(Usa, 0));
        // FL3: need falls by n·upkeep exactly.
        Assert.Equal(12 * p.Upkeep(Usa, 0).Raw, without.Products.UpkeepNeeded(Usa).Raw - p.UpkeepNeeded(Usa).Raw);
    }

    [Fact]
    public void Retire_ALeftoverBelowOneUnitIsSweptByTheSameCommand_AndAWithdrawnProjectIsNotCancelled()
    {
        var h = World(s => Cohorts(s, Usa, 0, 3.5));
        Set(h, "prd.d0.lossAcc", Usa, 500_000);
        h.FleetScript = (a, m) => a == Usa && m == 0 ? [Retire(0, 3)] : [];
        h.RunThroughMonth(0);
        ProductsSystem p = h.Products;
        Assert.Equal(Fx6.Zero, p.InService(Usa, 0));
        Assert.Equal(Fx6.FromRaw(3_500_000), p.Retired(Usa, 0));
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Usa, 0));
        Assert.False(p.Cancelled(Usa, 0));
        Assert.True(p.BuiltAny(Usa, 0));
        Assert.Equal(Fx6.Zero, p.LossAcc(Usa, 0));   // no fleet, no phantom accidents
    }

    [Fact]
    public void Retire_OfTheWholeFleetOfALineLeavesTheLine_StageC()
    {
        var h = World(startLine: true);
        h.FleetScript = (a, m) => a == Usa && m == 0 ? [Retire(0, 100)] : [];
        h.RunThroughMonth(0);
        ProductsSystem p = h.Products;
        Assert.Equal(DesignStage.Series, p.Stage(Usa, 0));
        Assert.Equal(Fx6.Zero, p.InService(Usa, 0));
        Assert.False(p.Cancelled(Usa, 0));
        Assert.True(p.BuiltAny(Usa, 0));
        Assert.Equal(Fx6.FromInt(100), p.Retired(Usa, 0));
    }

    [Fact]
    public void Retire_RefusalsCarryTheFirstViolatedCode_AndChangeNothing()
    {
        var h = World(startLine: false);
        h.FleetScript = (a, m) => a != Usa || m != 0 ? [] :
        [
            Retire(0, 0),            // COUNT: n < 1
            Retire(0, 101),          // COUNT: n above ⌊Σcount⌋ (slot still free: refusals do not take it)
            Retire(7, 1),            // STAGE: an empty slot
            Retire(-1, 1),           // STAGE: not a project at all
            Retire(0, 10),           // accepted
            Retire(0, 1),            // SLOT
        ];
        h.RunThroughMonth(0);
        ProductsSystem p = h.Products;
        Assert.Equal(2, p.RejectionCount(Usa, CommandRejection.BadRetireCount));
        Assert.Equal(2, p.RejectionCount(Usa, CommandRejection.BadRetireStage));
        Assert.Equal(1, p.RejectionCount(Usa, CommandRejection.BadRetireSlot));
        Assert.Equal(Fx6.FromInt(90), p.InService(Usa, 0));
    }

    // ---- sell

    [Fact]
    public void Sell_PricesTheOldestUnitsByTheSpecFormula_BitForBit_AndPaysTheSeller()
    {
        var h = World(WithGbrG2(60));
        h.FleetScript = (a, m) => a == Usa && m == 0 ? [Sell(0, 10)] : [];
        h.RunThroughMonth(0);
        ProductsSystem p = h.Products;
        // Cohort 1945 at month 0: age 0 − (12·0 − 2) = 2, remaining 178 of 180; gap = 2 − 1 = 1 (φ = 1).
        long floor = p.UnitCostFloor(Usa, 0).Raw;
        long price = ((floor * 500_000 / S) * 1_000_000 / S) * 178 / 180;
        Assert.Equal(10 * price, p.SaleIncome(Usa).Raw);
        Assert.Equal(10 * price, p.ExternalTotal(Usa).Raw);
        Assert.Equal(Fx6.FromInt(10), p.Sold(Usa, 0));
        Assert.Equal(10, p.SoldInYear(Usa, 0));
        Assert.Equal(10 * price, p.SaleInYear(Usa).Raw);
        Assert.Equal(Fx6.FromInt(90), p.InService(Usa, 0));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 60)]
    [InlineData(3, 179)]
    [InlineData(5, 179)]
    public void Price_IsTheCanonicalFloorChain_ForGapAndAge(int gap, int age)
    {
        var prm = new ProductsParams();
        Fx6 floor = Fx6.FromRaw(2_256_789);
        long phi = gap == 1 ? 1_000_000 : gap == 2 ? 600_000 : 300_000;
        long expected = (((floor.Raw * 500_000 / S) * phi / S) * (180 - age)) / 180;
        Assert.Equal(expected, FleetRules.Price(floor, prm, gap, age).Raw);
        Assert.Equal(0, FleetRules.Price(floor, prm, gap, 180).Raw);   // service life used up: nothing left to pay for
        Assert.Equal(0, FleetRules.Price(floor, prm, gap, 400).Raw);
    }

    [Fact]
    public void Sell_NeverPaysForDust_AndCountsItInSold()
    {
        var h = new ProductsHarness(startLine: false, parameters: new ProductsParams { SaleQuota = Fx6.One },
            tweakStart: s => { WithGbrG2(60)(s); Cohorts(s, Usa, 0, 3.5); }) { IsMastered = (_, _) => true };
        h.FleetScript = (a, m) => a == Usa && m == 0 ? [Sell(0, 3)] : [];
        h.RunThroughMonth(0);
        ProductsSystem p = h.Products;
        long floor = p.UnitCostFloor(Usa, 0).Raw;
        long price = ((floor * 500_000 / S) * 1_000_000 / S) * 178 / 180;
        Assert.Equal(3 * price, p.SaleIncome(Usa).Raw);
        Assert.Equal(Fx6.FromRaw(3_500_000), p.Sold(Usa, 0));
        Assert.Equal(3, p.SoldInYear(Usa, 0));
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Usa, 0));
    }

    [Fact]
    public void Sell_Refusals_FollowTheValidatorOrder_AndStageIsOnlyInService()
    {
        // Mastered nothing: G_front = 0, gap < 1 → NOT_SURPLUS comes before QUOTA (the sale of 11 would also exceed the quota of 10).
        var none = World(mastered: false);
        none.FleetScript = (a, m) => a == Usa && m == 0 ? [Sell(0, 11), Sell(0, 0), Sell(3, 1), Sell(0, 5)] : [];
        none.RunThroughMonth(0);
        Assert.Equal(1, none.Products.RejectionCount(Usa, CommandRejection.BadSellCount));
        Assert.Equal(1, none.Products.RejectionCount(Usa, CommandRejection.BadSellStage));
        Assert.Equal(2, none.Products.RejectionCount(Usa, CommandRejection.BadSellNotSurplus));
        Assert.Equal(Fx6.Zero, none.Products.SaleIncome(Usa));

        // A line (stage С) is never sold.
        var line = World(WithGbrG2(60), startLine: true);
        line.FleetScript = (a, m) => a == Usa && m == 0 ? [Sell(0, 1)] : [];
        line.RunThroughMonth(0);
        Assert.Equal(1, line.Products.RejectionCount(Usa, CommandRejection.BadSellStage));

        // Two sales of one project in a month: the second is SLOT; sell and retire do not share a slot.
        var h = World(WithGbrG2(60));
        h.FleetScript = (a, m) => a == Usa && m == 0 ? [Sell(0, 2), Retire(0, 3), Sell(0, 1)] : [];
        h.RunThroughMonth(0);
        Assert.Equal(1, h.Products.RejectionCount(Usa, CommandRejection.BadSellSlot));
        Assert.Equal(0, h.Products.RejectionCount(Usa, CommandRejection.BadRetireSlot));
        Assert.Equal(Fx6.FromInt(95), h.Products.InService(Usa, 0));
    }

    [Fact]
    public void Sell_OutcomeDoesNotDependOnTheOrderOfTheJournal_SellsRunBeforeRetires()
    {
        var a = World(WithGbrG2(60));
        a.FleetScript = (x, m) => x == Usa && m == 0 ? [Sell(0, 4), Retire(0, 6)] : [];
        var b = World(WithGbrG2(60));
        b.FleetScript = (x, m) => x == Usa && m == 0 ? [Retire(0, 6), Sell(0, 4)] : [];
        a.RunThroughMonth(0);
        b.RunThroughMonth(0);
        Assert.Equal(a.Hash(), b.Hash());
    }

    [Fact]
    public void Quota_AndCeiling_ResetAtTheStartOfThePolicyYear_BeforeTheCommands()
    {
        var h = World(WithGbrG2(60));
        // ς = 0,10: the quota of the year is ⌈0,1·100⌉ = 10 units, taken in two parts; the next unit is refused until month 12.
        h.FleetScript = (a, m) => a != Usa ? [] : m switch
        {
            0 => [Sell(0, 6)],
            3 => [Sell(0, 4)],
            4 => [Sell(0, 1)],
            12 => [Sell(0, 9)],   // the new year: quota base ⌊Σcount⌋ = 90 → 9 units
            _ => [],
        };
        h.RunThroughMonth(11);
        ProductsSystem p = h.Products;
        Assert.Equal(10, p.SoldInYear(Usa, 0));
        Assert.Equal(1, p.RejectionCount(Usa, CommandRejection.BadSellQuota));
        Assert.Equal(100, p.QuotaBase(Usa, 0));
        h.RunThroughMonth(12);
        Assert.Equal(0, p.RejectionCount(Usa, CommandRejection.BadSellRevenueCap));
        Assert.Equal(90, p.QuotaBase(Usa, 0));
        Assert.Equal(9, p.SoldInYear(Usa, 0));
        Assert.Equal(Fx6.FromInt(19), p.Sold(Usa, 0));   // the cumulative counter does not reset
    }

    [Fact]
    public void Ceiling_IsChiTimesTwelveBaseBudgets_AndRefusesASaleThatWouldExceedIt()
    {
        // B_base = 1000 raw → ceiling ⌊0,1·(12·1000)⌋ = 1200 raw; one unit sells for ≈ 1115 raw, two do not fit.
        var h = World(WithGbrG2(60), budget: (_, _) => Fx6.FromRaw(1000));
        h.FleetScript = (a, m) => a == Usa && m == 0 ? [Sell(0, 2)] : a == Usa && m == 1 ? [Sell(0, 1)] : [];
        h.RunThroughMonth(0);
        Assert.Equal(Fx6.FromRaw(1200), h.Products.SaleCap(Usa));
        Assert.Equal(1, h.Products.RejectionCount(Usa, CommandRejection.BadSellRevenueCap));
        Assert.Equal(Fx6.Zero, h.Products.SaleInYear(Usa));
        h.RunThroughMonth(1);
        Assert.Equal(1, h.Products.SoldInYear(Usa, 0));
        Assert.True(h.Products.SaleInYear(Usa) <= h.Products.SaleCap(Usa));
    }

    [Fact]
    public void Quota_OfAProjectThatHadNoFleetAtTheStartOfTheYear_IsZero()
    {
        // The line has no machines at t = 0, builds units in months 0…4 and closes (stage В): quota_base = 0, so nothing of it is sold this year.
        var h = World(s => { WithGbrG2(60)(s); Cohorts(s, Usa, 0); }, startLine: true, budget: (_, _) => Fx6.FromInt(1000));
        h.FleetScript = (a, m) => a == Usa && m == 6 ? [Sell(0, 1)] : [];
        h.Rate = (a, d, m) => a == Usa && d == 0 ? Fx6.FromInt(5) : null;
        h.Close = (a, d, m) => a == Usa && d == 0 && m == 5;
        h.RunThroughMonth(6);
        ProductsSystem p = h.Products;
        Assert.Equal(DesignStage.InService, p.Stage(Usa, 0));
        Assert.True(p.InService(Usa, 0) >= Fx6.FromInt(20));
        Assert.Equal(0, p.QuotaBase(Usa, 0));
        Assert.Equal(1, p.RejectionCount(Usa, CommandRejection.BadSellQuota));
        Assert.Equal(0, p.RejectionCount(Usa, CommandRejection.BadSellNotSurplus));
    }

    [Fact]
    public void SaleIncome_IsPartOfTheProcurementInputBIn_AndIsNotCarriedOver()
    {
        // The budget cannot pay the upkeep of 100 units (2900 raw): with the revenue of the sale it can.
        var sold = World(WithGbrG2(60), budget: (_, _) => Fx6.FromRaw(1000));
        sold.FleetScript = (a, m) => a == Usa && m == 0 ? [Sell(0, 1)] : [];
        var plain = World(WithGbrG2(60), budget: (_, _) => Fx6.FromRaw(1000));
        sold.RunThroughMonth(0);
        plain.RunThroughMonth(0);
        Assert.True(plain.Products.PaidRatio(Usa) < Fx6.One);
        Assert.True(sold.Products.PaidRatio(Usa) > plain.Products.PaidRatio(Usa));
        Assert.True(sold.Products.Spent(Usa) <= Fx6.FromRaw(1000) + sold.Products.SaleIncome(Usa));
        sold.RunThroughMonth(1);
        Assert.Equal(Fx6.Zero, sold.Products.SaleIncome(Usa));   // the next month starts from the base alone
        Assert.True(sold.Products.Spent(Usa) <= Fx6.FromRaw(1000));
        Assert.Equal(sold.Products.SaleIncome(Usa) + Fx6.FromRaw(sold.Products.ExternalTotal(Usa).Raw - sold.Products.SaleIncome(Usa).Raw),
            sold.Products.ExternalTotal(Usa));
    }

    [Fact]
    public void ExternalTotal_IsTheSumOfTheSaleIncomeOfEveryMonth()
    {
        var h = World(WithGbrG2(60));
        h.FleetScript = (a, m) => a == Usa && m is 0 or 2 or 13 ? [Sell(0, 2)] : [];
        Fx6 sum = Fx6.Zero;
        for (long m = 0; m < 16; m++)
        {
            h.RunThroughMonth(m);
            sum += h.Products.SaleIncome(Usa);
            Assert.Equal(sum, h.Products.ExternalTotal(Usa));
        }
        Assert.True(sum > Fx6.Zero);
    }

    // ---- leader key: fielded, Def(K), G_front

    [Fact]
    public void Fielded_NeedsFiftyWholeUnitsAndEveryProcessMastered()
    {
        var h = World(WithGbrG2(49));
        h.RunThroughMonth(0);
        ProductsSystem p = h.Products;
        Assert.True(p.Fielded(Gbr, 0, 0));
        Assert.False(p.Fielded(Gbr, 1, 0));            // 49 units: not enough
        var more = World(WithGbrG2(50));
        more.RunThroughMonth(0);
        Assert.True(more.Products.Fielded(Gbr, 1, 0));
        var unmastered = new ProductsHarness(startLine: false, tweakStart: WithGbrG2(60)) { IsMastered = (_, p2) => p2 != 49 };
        unmastered.RunThroughMonth(0);
        Assert.False(unmastered.Products.Fielded(Gbr, 0, 0));   // one process of the set missing
        Assert.Equal(0, unmastered.Products.FieldedGen(Gbr, 0));
        Assert.Equal(0, unmastered.Products.GFront(0));
    }

    [Fact]
    public void DefK_ProtectsTheFleetThatHoldsGUntilTheNewerLineIsFielded()
    {
        var before = World(WithGbrG2(30));
        before.RunThroughMonth(0);
        ProductsSystem p = before.Products;
        Assert.Equal(1, p.FieldedGen(Gbr, 0));
        Assert.True(p.DefK(Gbr, 0)[0]);                 // G1 fleet holds G while the G2 project is not fielded
        Assert.Equal(p.Parameters.NServiceMin, p.Keep(p.DefK(Gbr, 0)[0]));

        var after = World(WithGbrG2(60));
        after.RunThroughMonth(0);
        p = after.Products;
        Assert.Equal(2, p.FieldedGen(Gbr, 0));
        Assert.Equal(2, p.GFront(0));
        bool[] def = p.DefK(Gbr, 0);
        Assert.False(def[0]);                           // gen 1 < G and perf below P: free to go
        Assert.True(def[1]);
        Assert.Equal(0, p.Keep(def[0]));
        TechKey key = p.KeyOf(Gbr, 0);
        Assert.Equal(2, key.G);
        Assert.Equal(p.Perf(Gbr, 1), key.P);
        Assert.True(key.CompareTo(p.KeyOf(Usa, 0)) > 0);
    }

    [Fact]
    public void Gen_IsMinOfPlatformGenAndEnginePlusOne()
    {
        var h = World(s =>
        {
            ProductsLayout l = s.Layout;
            var alts = new int[l.SlotCount];
            Assert.True(DesignRules.Superior(l, 2, l.KnownUpTo(2), alts));
            int eng = Alt(l, "eng_g1");
            alts[l.SlotOf(eng)] = eng;
            int row = Gbr * l.Designs + 1;
            s.Stage[row] = DesignStage.InService;
            s.PlatformGen[row] = 3;
            for (int k = 0; k < alts.Length; k++)
                s.Alts[row * l.SlotCount + k] = alts[k];
        });
        Assert.Equal(3, h.Products.PlatformGen(Gbr, 1));
        // eng_gen(G1) = 1 (core data), so the engine lags a G3 platform by two generations: gen = min(3, 1 + 1) = 2. (Spec §11 п. 4 says g_p = 2 on a G1 engine
        // gives gen 1; with eng_gen(G1) = 1 the formula of contract §4.4a gives 2 there — recorded in the task.)
        Assert.Equal(2, h.Products.Gen(Gbr, 1));
    }

    // ---- the pure rules

    [Fact]
    public void Take_RemovesExactlyN_OldestFirst_AndSweepDustRemovesTheLeftoverBelowOne()
    {
        Fx6[] counts = [Fx6.FromRaw(2_500_000), Fx6.FromInt(4), Fx6.FromRaw(300_000)];
        var taken = new Fx6[3];
        FleetRules.Take(counts, Fx6.FromRaw(3_000_000), taken);
        Assert.Equal([Fx6.FromRaw(2_500_000), Fx6.FromRaw(500_000), Fx6.Zero], taken);
        Assert.Equal([Fx6.Zero, Fx6.FromRaw(3_500_000), Fx6.FromRaw(300_000)], counts);
        Fx6[] dust = [Fx6.FromRaw(400_000), Fx6.Zero, Fx6.FromRaw(300_000)];
        Assert.Equal(Fx6.FromRaw(700_000), FleetRules.SweepDust(dust));
        Assert.All(dust, c => Assert.Equal(Fx6.Zero, c));
        Fx6[] whole = [Fx6.FromRaw(1_000_000)];
        Assert.Equal(Fx6.Zero, FleetRules.SweepDust(whole));
        Assert.Equal(Fx6.One, whole[0]);
    }

    [Fact]
    public void SaleMax_IsTheLargestSaleTheValidatorAccepts()
    {
        var prm = new ProductsParams();
        var layout = Probe;
        int[] m0 = [.. Enumerable.Range(0, layout.Years).Select(y => y * 12 - 2)];
        Fx6[] counts = new Fx6[layout.Years];
        counts[0] = Fx6.FromInt(100);
        Fx6 floor = Fx6.FromRaw(2256);
        for (long s = 0; s <= 20; s++)
        {
            long max = FleetRules.SaleMax(counts, 50, 0, m0, floor, 1, prm, 100, 0, Fx6.Zero, Fx6.FromRaw(5000));
            bool ok = s == 0 || FleetRules.Quote(counts, s, 0, m0, floor, 1, prm, 100, 0, Fx6.Zero, Fx6.FromRaw(5000)).Reject == SaleReject.None;
            Assert.Equal(ok, s <= max);
        }
        Assert.Equal(0, FleetRules.SaleMax(counts, 50, 0, m0, floor, 0, prm, 100, 0, Fx6.Zero, Fx6.FromRaw(5000)));   // gap 0
    }

    private static void Set(ProductsHarness h, string column, int row, long value)
    {
        Kernel.LongColumn c = h.Table.GetLong(column);
        long[] all = c.Current.ToArray();
        all[row] = value;
        c.Load(all);
    }
}
