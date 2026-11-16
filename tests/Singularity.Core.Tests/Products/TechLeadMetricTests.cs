using Singularity.Core.Data;
using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>Contract §9.1 r13a, T-17 (#246, #247): <c>fielded_L</c>, the column <c>q(d, y)</c> and the key built on it.</summary>
public class TechLeadMetricTests
{
    private const int Usa = 0, Gbr = 1;

    // GBR owns a G2 fleet of the given size in design 1, cohort year index 5 (as FleetCommandTests).
    private static Action<ProductsStart> G2Fleet(long units) => s =>
    {
        ProductsLayout l = s.Layout;
        var alts = new int[l.SlotCount];
        Assert.True(DesignRules.Superior(l, 2, l.KnownUpTo(2), alts));
        int row = Gbr * l.Designs + 1;
        s.Stage[row] = DesignStage.InService;
        s.PlatformGen[row] = 2;
        for (int k = 0; k < alts.Length; k++)
            s.Alts[row * l.SlotCount + k] = alts[k];
        s.Cohort[row * l.Years + 5] = Fx6.FromInt(units);
    };

    // GBR's G2 fleet (so that G_front is 2 and a G1 sale is admitted) and USA's G1 start fleet cut to 52 units: one retire of 3 or one sale of 5 (quota 10%) takes it under 50.
    private static void SaleWorld(ProductsStart s)
    {
        G2Fleet(60)(s);
        for (int y = 0; y < s.Layout.Years; y++)
            s.Cohort[(Usa * s.Layout.Designs + 0) * s.Layout.Years + y] = y == 0 ? Fx6.FromInt(52) : Fx6.Zero;
    }

    private static FleetCommand Retire(int d, long n) => new(FleetCommandKind.Retire, d, n);

    private static FleetCommand Sell(int d, long n) => new(FleetCommandKind.Sell, d, n);

    [Fact]
    public void FieldedL_InitialScenarioFleet()
    {
        // Mastered at t0: the scenario cohort carries q; it stays in the fleet when mastery is lost afterwards.
        bool mastered = true;
        var h = new ProductsHarness(startLine: false, tweakStart: G2Fleet(60), isMastered: (_, _) => mastered);
        ProductsSystem p = h.Products;
        Assert.True(p.CohortMastered(Gbr, 1, 5));
        Assert.False(p.CohortMastered(Gbr, 1, 4));                       // no cohort, no q
        mastered = false;
        Assert.False(p.FieldedMastered(Gbr, 1, 0));
        Assert.True(p.FieldedL(Gbr, 1, 0));
        // Not mastered at t0: no q, so the same fleet is not in fielded_L.
        var cold = new ProductsHarness(startLine: false, tweakStart: G2Fleet(60), isMastered: (_, _) => false);
        Assert.False(cold.Products.CohortMastered(Gbr, 1, 5));
        Assert.False(cold.Products.FieldedL(Gbr, 1, 0));
        // 49 units are not enough even when mastered.
        var small = new ProductsHarness(startLine: false, tweakStart: G2Fleet(49), isMastered: (_, _) => true);
        Assert.False(small.Products.FieldedL(Gbr, 1, 0));
    }

    [Fact]
    public void ClosingTheLastLineOfAProcess_KeepsTheFleetInTheKey()
    {
        bool mastered = true;
        var h = new ProductsHarness(startLine: false, tweakStart: G2Fleet(60), isMastered: (_, _) => mastered);
        h.RunThroughMonth(0);
        TechKey before = h.Products.KeyOf(Gbr, 0);
        Assert.True(before.G >= 2 && before.T > 0 && before.N > 0);
        mastered = false;                                                  // the line of every process of the fleet is closed: nothing is mastered
        h.RunThroughMonth(1);
        TechKey after = h.Products.KeyOf(Gbr, 1);
        Assert.Equal(before.G, after.G);                                   // G and P stay: the fleet is in service
        Assert.Equal(before.P, after.P);
        Assert.True(after.T < before.T || after.T == before.T && after.N < before.N);   // T and N follow mastered(a, p) and fall
        Assert.Equal(0, after.T);
        Assert.Equal(0, after.N);
    }

    [Fact]
    public void TAndN_DoNotUseFieldedL()
    {
        // A fleet with q but nothing mastered gives G and P and no T, N; the other way round, mastery without a fleet gives T, N and no G.
        var fleet = new ProductsHarness(startLine: false, tweakStart: G2Fleet(60), isMastered: (_, _) => true);
        fleet.IsMastered = (_, _) => false;
        TechKey k = fleet.Products.KeyOf(Gbr, 0);
        Assert.True(k.G >= 2);
        Assert.Equal((0, 0), (k.T, k.N));
        var none = new ProductsHarness(startLine: false, isMastered: (_, _) => true);
        TechKey n = none.Products.KeyOf(Gbr, 0);
        Assert.True(n.T > 0 && n.N > 0);
    }


    // The start line without its scenario fleet: only what the test produces counts.
    private static void NoStartFleet(ProductsStart s)
    {
        for (int a = 0; a < s.Actors; a++)
        {
            for (int y = 0; y < s.Layout.Years; y++)
                s.Cohort[(a * s.Layout.Designs + 0) * s.Layout.Years + y] = Fx6.Zero;
        }
    }

    // One producing start line (design 0 of every actor, rate 20 a month); `Now` is the month whose compute asks for mastered.
    private sealed class Line
    {
        public long Now;
        public Func<long, bool> MasteredAt = _ => true;
        public readonly ProductsHarness H;

        public Line(long closeAt = long.MaxValue)
        {
            H = new ProductsHarness(isMastered: (_, _) => MasteredAt(Now), tweakStart: NoStartFleet)
            {
                Rate = (_, _, _) => Fx6.FromInt(20),
                Close = (_, d, t) => d == 0 && t == closeAt,
            };
        }

        public void Run(long month)
        {
            Now = month;
            H.RunThroughMonth(month);
        }
    }

    [Fact]
    public void CohortWhoseLastOutputMonthWasNotMastered_DoesNotCount()
    {
        // 1946 is year index 1 (month 4 = January 1946). The line produces in months 4 to 6 and closes at month 7; q(1946) is rewritten by every compute that sees
        // rate > 0 (months 5 to 7). In the control the last of them sees mastery, in the case under test it does not.
        bool Counts(bool masteredInLastWrite)
        {
            var line = new Line(closeAt: 7) { MasteredAt = m => m >= 5 && (m != 7 || masteredInLastWrite) };   // 1945 is never counted
            for (long m = 0; m < 12; m++)
                line.Run(m);
            ProductsSystem p = line.H.Products;
            Assert.Equal(DesignStage.InService, p.Stage(Usa, 0));
            Assert.True(p.Cohort(Usa, 0, 1946) >= Fx6.FromInt(50));            // plenty of units; only q decides
            Assert.Equal(masteredInLastWrite, p.CohortMastered(Usa, 0, 1));
            line.MasteredAt = _ => false;                                      // mastery is lost afterwards (the line is closed)
            return p.FieldedL(Usa, 0, 12);
        }
        Assert.True(Counts(masteredInLastWrite: true));
        Assert.False(Counts(masteredInLastWrite: false));
    }

    [Fact]
    public void DecemberOutput_WritesTheCohortOfItsOwnYear()
    {
        // Month 3 is December 1945 (start in September). Its output is read by the compute of month 4 (January 1946) and belongs to the 1945 cohort.
        var line = new Line { MasteredAt = m => m == 4 };
        for (long m = 0; m <= 4; m++)
            line.Run(m);
        ProductsSystem p = line.H.Products;
        Assert.True(p.Rate(Usa, 0) > Fx6.Zero);
        Assert.True(p.CohortMastered(Usa, 0, 0), "December output not written to the cohort of 1945");
        Assert.False(p.CohortMastered(Usa, 0, 1), "January's compute wrote the year of the month it runs in");
    }

    [Fact]
    public void RetiredSoldOrWithdrawnFleet_LeavesFieldedL()
    {
        foreach (string how in new[] { "retire", "sell", "all" })
        {
            // USA's start fleet (design 0, 100 units, G1) is in the key; GBR's G2 fleet makes G_front 2 so that the sale is admitted.
            var h = new ProductsHarness(startLine: false, tweakStart: SaleWorld, isMastered: (_, _) => true);
            FleetCommand cmd = how switch { "retire" => Retire(0, 3), "sell" => Sell(0, 5), _ => Retire(0, 52) };
            h.FleetScript = (a, m) => a == Usa && m == 1 ? [cmd] : [];
            h.RunThroughMonth(0);
            Assert.True(h.Products.FieldedL(Usa, 0, 0), how);
            h.RunThroughMonth(1);
            Assert.False(h.Products.FieldedL(Usa, 0, 1), how);                // 49 or 0 units: n_Q < n_service_min
        }
        var withdrawn = new ProductsHarness(startLine: false, tweakStart: SaleWorld, isMastered: (_, _) => true);
        withdrawn.FleetScript = (a, m) => a == Usa && m == 0 ? [Retire(0, 52)] : [];
        withdrawn.RunThroughMonth(0);
        withdrawn.RunThroughMonth(1);
        Assert.Equal(DesignStage.Withdrawn, withdrawn.Products.Stage(Usa, 0));
        Assert.False(withdrawn.Products.FieldedL(Usa, 0, 1));
    }

    [Fact]
    public void SoldUnits_LeaveSellerAndDoNotCountForBuyer()
    {
        // The buyers are outside the slice (§4.4f): the sold units leave the seller's count and no actor gets them.
        var h = new ProductsHarness(startLine: false, tweakStart: SaleWorld, isMastered: (_, _) => true);
        h.FleetScript = (a, m) => a == Usa && m == 1 ? [Sell(0, 5)] : [];
        h.RunThroughMonth(0);
        Assert.True(h.Products.FieldedL(Usa, 0, 0));
        h.RunThroughMonth(1);
        Assert.Equal(Fx6.FromInt(47), h.Products.InService(Usa, 0));
        Assert.False(h.Products.FieldedL(Usa, 0, 1));
        for (int buyer = 0; buyer < Scenario.ActorCount; buyer++)
        {
            if (buyer != Usa && buyer != Gbr)
                Assert.False(h.Products.FieldedL(buyer, 0, 1));                     // the units went outside the slice
        }
    }
}
