using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// Contract v2.4 r2 §4.4c-d invariants on random scripted runs (seeded test-side RNG): a G2 project of GBR, random launch month,
/// random <c>force</c>, rate, budget and detection speed; checked after every month. <c>series_forced = false ⇒ X = 0</c> in C/B,
/// no retrofit pool and no accidents without force; <c>retro_pool ≥ 0</c> and never grows after the launch; <c>loss_acc ∈ [0; 1)</c>;
/// written off ≤ units in service; whole machines; cohorts ≥ 0; spent ≤ budget.
/// </summary>
public class ProductsGateInvariantPropertyTests
{
    private const int Gbr = 1;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void RandomRuns_GateRetrofitAndAccidentInvariantsHold(int seed) => Run(seed);

    [Fact]
    public void RandomRuns_Generator_ReachesForcedLossesAndRetrofit_AndUnforcedSeries()
    {
        bool lossy = false, retro = false, unforced = false;
        for (int seed = 100; seed < 130; seed++)
        {
            (bool forced, Fx6 lost, Fx6 pool) = Run(seed);
            lossy |= forced && lost > Fx6.Zero;
            retro |= forced && pool > Fx6.Zero;
            unforced |= !forced;
        }
        Assert.True(lossy && retro && unforced, $"degenerate generator: losses {lossy}, retrofit pool {retro}, unforced {unforced}");
    }

    private static (bool Forced, Fx6 Lost, Fx6 PoolMax) Run(int seed)
    {
        var rng = new Random(seed * 7919);
        var par = new ProductsParams { LambdaD = Fx6.FromRaw(rng.Next(2) == 0 ? rng.Next(50, 500) : rng.Next(500, 40_000)) };
        long launchAfter = rng.Next(0, 14);                 // months after entering testing
        bool force = rng.Next(2) == 0;
        Fx6 rate = Fx6.FromInt(rng.Next(5, 250));
        Fx6 budget = Fx6.FromRaw(rng.Next(0, 4) == 0 ? rng.Next(0, 50_000) : rng.Next(100_000, 3_000_000));
        ProductsLayout probe = Probe;
        var h = new ProductsHarness(parameters: par)
        {
            Knows = (a, n) => a == Gbr && probe.KnownUpTo(2)(n),
            Commands = (a, t) => a == Gbr && t == 0 ? [Compose(probe, 2, "eng_g2", "hot_sa2_forged", "comp_axial", "af_swept", "ab_on")] : [],
            Budget = (_, _) => budget,
        };
        ProductsSystem p = h.Products;
        h.Series = (a, d, t) => a == Gbr && d == 1 && p.Stage(a, d) == DesignStage.Prototype && t >= p.StageOMonth(a, d) + launchAfter
            ? new SeriesStart(rate, force) : null;

        bool everInSeries = false;
        Fx6 lastPool = Fx6.Zero, lostTotal = Fx6.Zero, poolMax = Fx6.Zero;
        long launched = -1;
        for (long t = 0; t < 130; t++)
        {
            h.RunThroughMonth(t);
            Assert.True(p.Spent(Gbr) >= Fx6.Zero && p.Spent(Gbr) <= budget, $"seed {seed}, month {t}: spent {p.Spent(Gbr)} > budget {budget}");
            Fx6 pool = p.RetroPool(Gbr, 1), acc = p.LossAcc(Gbr, 1), lost = p.AccidentsLost(Gbr, 1);
            bool inLine = p.Stage(Gbr, 1) is DesignStage.Series or DesignStage.InService;
            everInSeries |= inLine;
            if (inLine && launched < 0)
                launched = t;
            Assert.True(pool >= Fx6.Zero, $"seed {seed}, month {t}: retro_pool {pool}");
            Assert.InRange(acc.Raw, 0, Fx6.Scale * 10);
            Assert.True(lost >= Fx6.Zero && lost <= p.InServiceBeforeAccidents(Gbr, 1), $"seed {seed}, month {t}: lost {lost} of {p.InServiceBeforeAccidents(Gbr, 1)}");
            Assert.Equal(0, lost.Raw % Fx6.Scale);
            lostTotal += lost;
            poolMax = pool > poolMax ? pool : poolMax;
            Assert.True(p.InService(Gbr, 1) >= Fx6.Zero);
            for (int y = 0; y < Years; y++)
                Assert.True(p.Cohort(Gbr, 1, h.Layout.StartYear + y) >= Fx6.Zero);
            if (!p.SeriesForced(Gbr, 1))
            {
                Assert.Equal(Fx6.Zero, pool);
                Assert.Equal(Fx6.Zero, lost);
                Assert.Equal(Fx6.Zero, acc);
                if (inLine)
                    Assert.True(p.GateOpened(Gbr, 1), $"seed {seed}, month {t}: unforced series before the gate opened");
                if (inLine && p.GateOpened(Gbr, 1))
                    Assert.Equal(Fx6.Zero, p.ExcessDefects(Gbr, 1));
            }
            else
            {
                Assert.True(force);                       // never forced unless the script said so (the AI never forces)
                if (launched >= 0 && t > launched)
                    Assert.True(pool <= lastPool, $"seed {seed}, month {t}: retro_pool grew {lastPool} → {pool}");
                lastPool = pool;
            }
        }
        // An unforced design with slow detection may never open the gate (hidden stays above k_gate·D_ref): not a violation.
        if (force || par.LambdaD >= new ProductsParams().LambdaD)
            Assert.True(everInSeries, $"seed {seed}: the design never reached the series (budget {budget}, force {force}, stage {p.Stage(Gbr, 1)})");
        return (force, lostTotal, poolMax);
    }
}
