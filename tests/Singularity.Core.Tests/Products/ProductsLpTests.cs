using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// The L_P stub (spec §6.1): canonical project with output <c>min(ρ[g]·budget, alloy capacity)</c>; no bureaus,
/// <c>line_E</c>, defects, variants, commands or procurement spending. Plus cohort aging across calendar years
/// (cohort of the calendar year of output; whole-cohort write-off at <c>t − m0(Y) ≥ service_life</c>).
/// </summary>
public class ProductsLpTests
{
    private const int Usa = 0, G1Sup = 0;

    internal static ProductsParams LpParams => new()
    {
        LpTDesign = [2, 4, 6, 8],
        LpRho = [Fx6.FromInt(100), Fx6.FromInt(50), Fx6.FromInt(20), Fx6.FromInt(10)],
    };

    /// <summary>An L_P run whose AI also sends every full-model command; L_P must ignore them.</summary>
    internal static ProductsHarness Lp(int threads, int chunkSize) => new(threads, chunkSize, ProductsMode.CanonicalLp, LpParams)
    {
        Knows = (a, n) => Probe.KnownUpTo(a % 2 == 0 ? 3 : 2)(n),
        AppKnownSince = (a, g) => g <= (a % 2 == 0 ? 3 : 2) ? 5 * g + a : -1,
        Budget = (a, t) => Fx6.FromRaw(100_000 + 20_000 * a + 1_000 * (t % 7)),
        Alloy = (a, t) => Fx6.FromInt(20_000 + 3_000 * a),
        Yield = (a, p) => (a + p) % 9 == 0 ? Fx6.Zero : Fx6.One,
        Order = (a, t) => new DesignOrder(1 + (int)(t % 3), t % 2 == 0 ? DesignVariant.Superior : DesignVariant.Producible),
        Launch = (_, _, _) => true,
        Close = (_, _, t) => t % 3 == 0,
        Rate = (_, _, _) => Fx6.FromInt(1000),
    };

    [Fact]
    public void Lp_HasNoBureausDefectsLineExperienceOrSpending_AndIgnoresCommands()
    {
        ProductsHarness h = Lp(1, 1);
        ProductsSystem p = h.Products;
        int seriesMonths = 0;
        for (long t = 0; t < 120; t++)
        {
            h.RunThroughMonth(t);
            Assert.DoesNotContain(h.Requests, q => q.Category == ProductsLayout.EngineerCategory);
            for (int a = 0; a < h.Table.RowCount; a++)
            {
                Assert.Equal(Fx6.Zero, p.Spent(a));
                Assert.Equal(Fx6.Zero, p.UpkeepNeeded(a));
                Assert.Equal(Fx6.One, p.PaidRatio(a));
                for (int b = 0; b < h.Layout.Bureaus; b++)
                {
                    Assert.Equal(h.Start.BureauExp[a * h.Layout.Bureaus + b], p.BureauExp(a, b));
                    Assert.Equal(-1, p.BureauDesign(a, b));
                }
                int series = 0;
                for (int d = 0; d < h.Layout.Designs; d++)
                {
                    DesignStage s = p.Stage(a, d);
                    Assert.True(s is not (DesignStage.Project or DesignStage.Prototype), $"month {t}: stage {s} under L_P");
                    Assert.Equal(Fx6.Zero, p.InitialDefects(a, d));
                    Assert.Equal(Fx6.Zero, p.LineE(a, d));
                    Assert.Equal(Fx6.Zero, p.WorkLeft(a, d));
                    if (s != DesignStage.None)
                        Assert.Equal(Fx6.One, p.Readiness(a, d, t + 1));
                    if (s == DesignStage.Series)
                    {
                        series++;
                        seriesMonths++;
                        // Only the canonical slot of its generation holds the project (§6.1, T-13).
                        Assert.Equal(d, p.CanonicalSlot(a, p.PlatformGen(a, d)));
                    }
                }
                Assert.True(series <= 1, $"month {t}: {series} canonical lines at once");
            }
        }
        Assert.True(seriesMonths > 100, $"only {seriesMonths} series months");
    }

    [Fact]
    public void Lp_Rate_IsMinOfRhoBudgetAndAlloyCapacity()
    {
        var h = new ProductsHarness(mode: ProductsMode.CanonicalLp, parameters: LpParams)
        {
            Knows = (_, n) => Probe.KnownUpTo(1)(n),
            AppKnownSince = (a, g) => g == 1 ? 0 : -1,
            Budget = (a, _) => Fx6.FromRaw(100_000 * (a + 1)),
            Alloy = (a, _) => a == 1 ? Fx6.FromInt(2_900) : Fx6.FromInt(1_000_000),
        };
        h.RunThroughMonth(2);
        ProductsSystem p = h.Products;
        Assert.Equal(Fx6.FromInt(10), p.Rate(0, p.CanonicalSlot(0, 1)));        // ρ·budget = 100·0.1 = 10 < alloy cap
        Assert.Equal(Fx6.FromInt(2_900) / Fx6.FromInt(290), p.Rate(1, p.CanonicalSlot(1, 1))); // alloy-limited: 2900 kg / alloy_kg[hot_nicr_forged]
        Assert.True(p.Rate(1, p.CanonicalSlot(1, 1)) < Fx6.FromInt(20));
        Assert.Equal(Fx6.FromInt(30), p.Rate(2, p.CanonicalSlot(2, 1)));
        // §6.1: the price print is the R-PS2m process part + materials_floor + h_floor hours (yield 1: = unit_cost_floor); nothing is spent.
        Assert.Equal(p.UnitCostFloor(0, p.CanonicalSlot(0, 1)), p.UnitCost(0, p.CanonicalSlot(0, 1)));
        Assert.Equal(Fx6.Zero, p.Spent(0));
    }

    [Fact]
    public void Lp_NotAdmitted_NoOutputNoReports_AndUnknownAppNoProject()
    {
        var h = new ProductsHarness(mode: ProductsMode.CanonicalLp, parameters: LpParams)
        {
            Knows = (_, n) => Probe.KnownUpTo(1)(n),
            AppKnownSince = (a, g) => g == 1 && a != 2 ? 0 : -1,
            // Actor 1 has a canonical project whose bottleneck yield then drops to 0 (not admitted).
            Yield = (a, p) => Fx6.One,
        };
        h.RunThroughMonth(2);
        Assert.Equal(DesignStage.Series, h.Products.Stage(1, h.Products.CanonicalSlot(1, 1)));
        Assert.Equal(-1, h.Products.CanonicalSlot(2, 1));
        int blocked = h.Products.PrcSet(1, h.Products.CanonicalSlot(1, 1))[0];
        h.Yield = (a, p) => a == 1 && p == blocked ? Fx6.Zero : Fx6.One;
        h.RunThroughMonth(3);
        Assert.Equal(DesignStage.Series, h.Products.Stage(1, h.Products.CanonicalSlot(1, 1)));
        Assert.Equal(Fx6.Zero, h.Products.Rate(1, h.Products.CanonicalSlot(1, 1)));
        Assert.DoesNotContain(h.Reports, r => r.Site == 1);
        Assert.Contains(h.Reports, r => r.Site == 0);
        Assert.DoesNotContain(h.Reports, r => r.Site == 2);
    }

    [Fact]
    public void Lp_StartFleetsStillRetireAtServiceLife()
    {
        var h = new ProductsHarness(mode: ProductsMode.CanonicalLp, parameters: LpParams);
        h.RunThroughMonth(177);
        Assert.Equal(Fx6.FromInt(100), h.Products.InService(Usa, G1Sup));
        h.RunThroughMonth(178);
        Assert.Equal(Fx6.Zero, h.Products.InService(Usa, G1Sup));
        Assert.Equal(DesignStage.Withdrawn, h.Products.Stage(Usa, G1Sup));
    }

    // ---- cohort aging across calendar years

    [Fact]
    public void Output_GoesToTheCohortOfItsCalendarYear()
    {
        var h = new ProductsHarness
        {
            Launch = (a, d, t) => a == Usa && d == G1Sup && t == 0,
            Rate = (_, _, _) => Fx6.FromInt(10),
        };
        long jan46 = MonthOf(h, 1946, 1);
        Assert.Equal(4, jan46); // start 1945-09
        h.RunThroughMonth(jan46 - 1);
        Assert.Equal(Fx6.FromInt(100 + 10 * 4), h.Products.Cohort(Usa, G1Sup, 1945));
        Assert.Equal(Fx6.Zero, h.Products.Cohort(Usa, G1Sup, 1946));
        h.RunThroughMonth(jan46);
        Assert.Equal(Fx6.FromInt(140), h.Products.Cohort(Usa, G1Sup, 1945));
        Assert.Equal(Fx6.FromInt(10), h.Products.Cohort(Usa, G1Sup, 1946));
        long jan47 = MonthOf(h, 1947, 1);
        h.RunThroughMonth(jan47);
        Assert.Equal(Fx6.FromInt(120), h.Products.Cohort(Usa, G1Sup, 1946));
        Assert.Equal(Fx6.FromInt(10), h.Products.Cohort(Usa, G1Sup, 1947));
        Assert.Equal(Fx6.FromInt(140 + 120 + 10), h.Products.InService(Usa, G1Sup));
    }

    [Fact]
    public void Cohorts_RetireWhole_AtServiceLifeFromJulyOfTheirYear()
    {
        var h = new ProductsHarness
        {
            Launch = (a, d, t) => a == Usa && d == G1Sup && t == 0,
            Close = (a, d, t) => a == Usa && d == G1Sup && t == MonthOf(1947, 1),
            Rate = (_, _, _) => Fx6.FromInt(10),
        };
        int life = h.Params.ServiceLife;
        long m45 = MonthOf(1945, 7), m46 = MonthOf(1946, 7);
        h.RunThroughMonth(m45 + life - 1);
        Assert.Equal(Fx6.FromInt(140), h.Products.Cohort(Usa, G1Sup, 1945));
        h.RunThroughMonth(m45 + life);
        // A32: whole-cohort aging from July (1945 → month 178, 1946 → 190); December units serve 175 months.
        Assert.Equal(Fx6.Zero, h.Products.Cohort(Usa, G1Sup, 1945));
        Assert.Equal(Fx6.FromInt(120), h.Products.Cohort(Usa, G1Sup, 1946));
        Assert.Equal(DesignStage.InService, h.Products.Stage(Usa, G1Sup));
        h.RunThroughMonth(m46 + life - 1);
        Assert.Equal(Fx6.FromInt(120), h.Products.Cohort(Usa, G1Sup, 1946));
        h.RunThroughMonth(m46 + life);
        Assert.Equal(Fx6.Zero, h.Products.InService(Usa, G1Sup));
        Assert.Equal(DesignStage.Withdrawn, h.Products.Stage(Usa, G1Sup));
    }

    [Fact]
    public void MonthOutsideTheCohortYears_Throws()
    {
        var h = new ProductsHarness(years: 1);
        h.RunThroughMonth(3); // December 1945 is still inside
        Exception e = Assert.ThrowsAny<Exception>(() => h.RunThroughMonth(4));
        Assert.Contains(Chain(e), m => m.Contains("outside the cohort years"));
    }

    private static IEnumerable<string> Chain(Exception? e)
    {
        for (; e is not null; e = e.InnerException)
        {
            yield return e.Message;
            if (e is AggregateException ag)
            {
                foreach (Exception inner in ag.InnerExceptions)
                    yield return inner.Message;
            }
        }
    }

    private static long MonthOf(ProductsHarness h, int year, int month) => MonthOf(year, month);

    private static long MonthOf(int year, int month) =>
        (long)(year - Scenario.StartYear) * 12 + (month - Scenario.StartMonth);
}
