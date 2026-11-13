using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Kernel;
using Xunit.Abstractions;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// P0 «бюджет падения цены на удвоение» (contract §8.2, spec §6.4 п.2). Reference line: a G2 design of the core mod (the «superior» composition,
/// real <c>prc_set</c>, <c>n_p</c>, <c>base_cost_p</c>, cost card and R-PS2m scales from the data), a GBR-like site (culture and tooling at
/// the requirement of every process: <c>g = 1</c>, so the output-driven yield channel is the ramp), constant pace. No RNG.
/// Four runs: full / <c>yield</c> frozen at t0 (<c>X</c> = X(t0) = 0) / <c>b = 0</c> / both. The unit cost goes through exactly the functions
/// of the run: <see cref="IndustryMath.Yield"/> → <see cref="IndustryMath.Profile"/> (<c>unit_cost_mult</c> per block, П-8) →
/// <see cref="ProductsMath.ProcessPart"/> → <see cref="ProductsMath.UnitCost"/> (cf. <c>ProductsSystem.CardFor</c>).
/// Scope [Предп.]: culture does not grow here (in C-B it grows from money, not from volume, contract §8.2 v2.4), so the yield channel is the
/// <c>proc_X</c>/ramp part; the full-system stand and the corridors (≤ 40% / 50–80% / 15–30%) belong to the calibration of T12:
/// the shares are printed, the mechanism (signs, additivity, both-off ⇒ no fall, monotonicity) is asserted.
/// </summary>
public class ProductsChannelBudgetTests
{
    private readonly ITestOutputHelper _output;
    private readonly ProductsLayout _l = Probe;
    private readonly IndustryLayout _il = IndustryLayout.FromData(CoreData.Nodes, Scenario, Categories, 64);
    private readonly IndustryParams _p = new();
    private readonly int[] _alts;
    private readonly int[] _pu, _af;

    public ProductsChannelBudgetTests(ITestOutputHelper output)
    {
        _output = output;
        _alts = new int[_l.SlotCount];
        Assert.True(DesignRules.Superior(_l, 2, _ => true, _alts), "no G2 composition");
        int[] prc = DesignRules.PrcSet(_l, _alts);
        _pu = DesignRules.Block(_l, prc, airframe: false);
        _af = DesignRules.Block(_l, prc, airframe: true);
    }

    /// <summary>Unit cost of the reference line at cumulative output <paramref name="cum"/> (pace constant ⇒ <c>E = X = cum</c>).</summary>
    private Fx6 UnitCost(long cum, bool yieldChannel, bool assemblyChannel)
    {
        Fx6 x = yieldChannel ? Fx6.FromInt(cum) : Fx6.Zero;   // frozen at t0: X(t0) = 0
        Fx6 U(int[] block)
        {
            if (block.Length == 0)
                return Fx6.One;
            var items = new ProfileItem[block.Length];
            for (int i = 0; i < block.Length; i++)
            {
                int r = _il.RowOf(block[i]), k = _l.ProcIndex(block[i]);
                Dims3 req = _il.Req[r];
                ProcessYield y = IndustryMath.Yield(req, req, req, x, _p.XHalf(_il.Tier[r]), Fx6.Zero, _p.Lambda0);   // H = M = req
                items[i] = new ProfileItem(block[i], y, true, _l.ProcN[k], _l.ProcBaseCost[k]);
            }
            return IndustryMath.Profile(items, _p.Eps).UnitCostMult;
        }
        int af = DesignRules.AfClass(_l, _alts), pr = DesignRules.PuRank(_l, _alts);
        Fx6 sumPu = DesignRules.BaseCostSum(_l, _pu), sumAf = DesignRules.BaseCostSum(_l, _af);
        var card = new CostCard(_l.HFirst[af - 1], _l.HFloor[af - 1], _l.WageKh, _l.MaterialsFloor[af - 1],
            ProductsMath.ProcessPart(sumPu, sumPu == Fx6.Zero ? Fx6.One : U(_pu), _l.ProcScalePu[pr - 1], sumAf, sumAf == Fx6.Zero ? Fx6.One : U(_af), _l.ProcScaleAf[af - 1]),
            assemblyChannel ? new ProductsParams().LearningQ10 : 0);
        return ProductsMath.UnitCost(card, Fx6.FromInt(cum));
    }

    private double Drop(long n, bool y, bool b) => Math.Log(UnitCost(n, y, b).ToDouble() / UnitCost(2 * n, y, b).ToDouble());

    private (double Full, double Yield, double Assembly) Channels(long n)
    {
        double full = Drop(n, true, true), none = Drop(n, false, false);
        double yc = 0.5 * ((full - Drop(n, false, true)) + (Drop(n, true, false) - none));
        double ac = 0.5 * ((full - Drop(n, true, false)) + (Drop(n, false, true) - none));
        return (full, yc, ac);
    }

    [Fact]
    public void BothChannelsOff_GiveNoPriceFallAtAnyDoubling()
    {
        foreach (long n in new long[] { 1, 4, 40, 400, 4000, 40_000 })
            Assert.Equal(0.0, Drop(n, false, false));
    }

    [Fact]
    public void NoAssemblyChannel_AssemblyPartDoesNotMove_AndWithItItFalls()
    {
        Fx6 h0 = ProductsMath.AssemblyHours(Fx6.Zero, _l.HFirst[0], _l.HFloor[0], 0);
        foreach (long e in new long[] { 1, 100, 100_000 })
            Assert.Equal(h0, ProductsMath.AssemblyHours(Fx6.FromInt(e), _l.HFirst[0], _l.HFloor[0], 0));
        Assert.True(ProductsMath.AssemblyHours(Fx6.FromInt(100), _l.HFirst[0], _l.HFloor[0], new ProductsParams().LearningQ10) < h0);
    }

    [Fact]
    public void EveryArm_IsNonIncreasingInCumulativeOutput_AndFullFallsAtLeastAsMuchAsAnyAblatedArm()
    {
        foreach ((bool y, bool b) in new[] { (true, true), (false, true), (true, false), (false, false) })
        {
            Fx6 prev = Fx6.FromInt(long.MaxValue / 4_000_000_000_000L);
            for (long n = 1; n <= 65_536; n *= 2)
            {
                Fx6 c = UnitCost(n, y, b);
                Assert.True(c <= prev, $"arm y={y} b={b}: cost rises at n={n}");
                prev = c;
            }
        }
        for (long n = 1; n <= 16_384; n *= 4)
        {
            double full = Drop(n, true, true);
            Assert.True(full >= Drop(n, false, true) && full >= Drop(n, true, false), $"doubling from {n}");
        }
    }

    [Fact]
    public void SymmetricDecomposition_YieldChannelIsStrictlyPositive_AssemblyIsNonNegative_AndTheSumIsTheFullEffectWithin15Percent()
    {
        int positiveYield = 0;
        for (long n = 1; n <= 16_384; n *= 2)
        {
            (double full, double yc, double ac) = Channels(n);
            Assert.True(full > 0, $"n={n}: no price fall at all");
            Assert.True(yc > 0, $"n={n}: yield channel is not strictly positive ({yc})");
            Assert.True(ac >= 0, $"n={n}: negative assembly channel ({ac})");
            Assert.True(Math.Abs(yc + ac - full) <= 0.15 * full, $"n={n}: Σ channels {yc + ac:0.0000} vs full {full:0.0000}");
            positiveYield++;
        }
        Assert.True(positiveYield > 10);
    }

    [Fact]
    public void Print_PriceFallPerDoublingByChannel_LearningPhaseAndMatureLine()
    {
        // Mature line: the first cumulative output at which the lowest process yield of the design reaches 0.9.
        long mature = 1;
        while (mature < 1_000_000 && Matured(mature) < 0.9)
            mature *= 2;
        _output.WriteLine("G2 reference line, GBR-like site (g = 1), constant pace; fall = 1 − unit_cost(2N)/unit_cost(N)");
        foreach (long n in new long[] { 1, 2, 4 }.Concat([mature, mature * 2]))
        {
            (double full, double yc, double ac) = Channels(n);
            _output.WriteLine($"N={n,6}: total fall {1 - Math.Exp(-full):P1}; yield channel {yc / (yc + ac):P0}, assembly {ac / (yc + ac):P0}; Σ channels / full = {(yc + ac) / full:0.000}");
        }
        _output.WriteLine($"mature (min process yield ≥ 0.9) from N={mature}");
        Assert.True(mature < 1_000_000);
    }

    private double Matured(long cum)
    {
        double min = 1;
        foreach (int number in _pu.Concat(_af))
        {
            int r = _il.RowOf(number);
            Dims3 req = _il.Req[r];
            min = Math.Min(min, IndustryMath.Yield(req, req, req, Fx6.FromInt(cum), _p.XHalf(_il.Tier[r]), Fx6.Zero, _p.Lambda0).Yield.ToDouble());
        }
        return min;
    }
}
