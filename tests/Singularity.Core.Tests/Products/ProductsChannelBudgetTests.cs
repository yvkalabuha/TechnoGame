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
/// Four runs: full / <c>yield</c> frozen at its start point / <c>line_E</c> frozen at its start point (not <c>b = 0</c>, contract §8.2 r9) / both. The unit cost goes through exactly the functions
/// of the run: <see cref="IndustryMath.Yield"/> → <see cref="IndustryMath.Profile"/> (<c>unit_cost_mult</c> per block, П-8) →
/// <see cref="ProductsMath.ProcessPart"/> → <see cref="ProductsMath.UnitCost"/> (cf. <c>ProductsSystem.CardFor</c>).
/// Scope [Предп.]: culture does not grow here (in C-B it grows from money, not from volume, contract §8.2 v2.4), so the yield channel is the
/// <c>proc_X</c>/ramp part. Two setups (r9, #185): «mastery» (<c>g = 1</c> only; culture/tooling of GBR-1945 stay for T10/T12) and «mature»
/// (<c>X0 = 6·X½</c>, new line). Asserted: C1c (over-floor assembly fall = the P0 formula, #187), additivity ±15%, signs, both-off ⇒ no fall,
/// monotonicity. Other corridors (C1a, C1b, C1d, C1e) are printed only (calibration of T12); the old stand (mature point N = 16384) is a diagnostic.
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
    private Fx6 UnitCost(long cum, bool yieldChannel, bool assemblyChannel) =>
        Cost(0, yieldChannel ? cum : 0, assemblyChannel ? cum : 0);   // an ablated channel is frozen at its start point (X = 0, line_E = 0)

    /// <summary>
    /// Unit cost at <c>X(p) = x0Mult·X½(p) + xAdd</c> (per process) and <c>line_E = e</c>; the assembly channel is always on
    /// (the ablation freezes <c>e</c> at the start point, it does not set <c>b = 0</c>, contract §8.2 r9).
    /// </summary>
    private Fx6 Cost(int x0Mult, long xAdd, long e)
    {
        Fx6 U(int[] block)
        {
            if (block.Length == 0)
                return Fx6.One;
            var items = new ProfileItem[block.Length];
            for (int i = 0; i < block.Length; i++)
            {
                int r = _il.RowOf(block[i]), k = _l.ProcIndex(block[i]);
                Dims3 req = _il.Req[r];
                Fx6 xh = _p.XHalf(_il.Tier[r]);
                Fx6 x = Fx6.FromInt(x0Mult) * xh + Fx6.FromInt(xAdd);
                ProcessYield y = IndustryMath.Yield(req, req, req, x, xh, Fx6.Zero, _p.Lambda0);   // H = M = req
                items[i] = new ProfileItem(block[i], y, true, _l.ProcN[k], _l.ProcBaseCost[k]);
            }
            return IndustryMath.Profile(items, _p.Eps).UnitCostMult;
        }
        int af = DesignRules.AfClass(_l, _alts), pr = DesignRules.PuRank(_l, _alts);
        Fx6 sumPu = DesignRules.BaseCostSum(_l, _pu), sumAf = DesignRules.BaseCostSum(_l, _af);
        var card = new CostCard(_l.HFirst[af - 1], _l.HFloor[af - 1], _l.WageKh, _l.MaterialsFloor[af - 1],
            ProductsMath.ProcessPart(sumPu, sumPu == Fx6.Zero ? Fx6.One : U(_pu), _l.ProcScalePu[pr - 1], sumAf, sumAf == Fx6.Zero ? Fx6.One : U(_af), _l.ProcScaleAf[af - 1]),
            new ProductsParams().LearningQ10);
        return ProductsMath.UnitCost(card, Fx6.FromInt(e));
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
        // By construction (UnitCost ignores n when both channels are frozen): guards the stand's ablation wiring, not the model.
        foreach (long n in new long[] { 1, 4, 40, 400, 4000, 40_000 })
            Assert.Equal(0.0, Drop(n, false, false));
    }

    [Fact]
    public void NoAssemblyChannel_AssemblyPartDoesNotMove_AndWithItItFalls()
    {
        // Frozen line_E (not b = 0): the cost with the channel off at any cumulative output equals the cost at line_E = 0.
        int q = new ProductsParams().LearningQ10;
        int af = DesignRules.AfClass(_l, _alts);
        Fx6 h0 = ProductsMath.AssemblyHours(Fx6.Zero, _l.HFirst[af - 1], _l.HFloor[af - 1], q);
        Assert.Equal(_l.HFirst[af - 1], h0);   // non-trivial: line_E = 0 is exactly h_first
        Assert.True(ProductsMath.AssemblyHours(Fx6.FromInt(100), _l.HFirst[af - 1], _l.HFloor[af - 1], q) < h0);
        Assert.True(UnitCost(100, false, true) < UnitCost(100, false, false));   // the channel on lowers the cost, off does not
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

    // ---- r9 (#185, #187, #188): two setups of C1 -------------------------------------------------------------------------------------

    private readonly record struct Dbl(long Start, double Full, double Yield, double Assembly, double MinYield);

    /// <summary>Doubling start→2·start of the cumulative output <c>n</c>; the line starts at X = x0Mult·X½ (+ n), line_E = n (+0).</summary>
    private Dbl Doubling(int x0Mult, long n, long baseX, long baseE)
    {
        double L(bool y, bool b, long m) => Math.Log(Cost(x0Mult, y ? baseX + m : baseX, b ? baseE + m : baseE).ToDouble());
        double D(bool y, bool b) => L(y, b, n) - L(y, b, 2 * n);
        double full = D(true, true), none = D(false, false);
        double yc = 0.5 * ((full - D(false, true)) + (D(true, false) - none));
        double ac = 0.5 * ((full - D(true, false)) + (D(false, true) - none));
        return new Dbl(n, full, yc, ac, MinYieldAt(x0Mult, baseX + n));
    }

    private double MinYieldAt(int x0Mult, long xAdd)
    {
        double min = 1;
        foreach (int number in _pu.Concat(_af))
        {
            int r = _il.RowOf(number);
            Dims3 req = _il.Req[r];
            Fx6 xh = _p.XHalf(_il.Tier[r]);
            Fx6 x = Fx6.FromInt(x0Mult) * xh + Fx6.FromInt(xAdd);
            min = Math.Min(min, IndustryMath.Yield(req, req, req, x, xh, Fx6.Zero, _p.Lambda0).Yield.ToDouble());
        }
        return min;
    }

    private static (double Fall, double YieldShare, double Additivity) Aggregate(IReadOnlyList<Dbl> d)
    {
        double full = d.Sum(v => v.Full), y = d.Sum(v => v.Yield), a = d.Sum(v => v.Assembly);
        return (1 - Math.Exp(-full / d.Count), y / (y + a), (y + a) / full);
    }

    private IReadOnlyList<Dbl> MatureWindow(int x0Mult, long maxStart)
    {
        var list = new List<Dbl>();
        for (long n = 1; n <= maxStart; n *= 2)
            list.Add(Doubling(x0Mult, n, 0, 0));
        return list;
    }

    [Fact]
    public void MatureSetup_C1c_AssemblyOverFloorFallMatchesTheFormulaOfP0_AndTheMechanicsHold()
    {
        int q = new ProductsParams().LearningQ10;
        double b = q / 1024.0;
        int af = DesignRules.AfClass(_l, _alts);
        Fx6 hFirst = _l.HFirst[af - 1], hFloor = _l.HFloor[af - 1];
        double sumCore = 0, sumFormula = 0, sumFull = 0;
        int count = 0;
        for (long e = 1; e <= 256; e *= 2)
        {
            double over1 = (ProductsMath.AssemblyHours(Fx6.FromInt(e), hFirst, hFloor, q) - hFloor).ToDouble();
            double over2 = (ProductsMath.AssemblyHours(Fx6.FromInt(2 * e), hFirst, hFloor, q) - hFloor).ToDouble();
            double core = Math.Log(over1 / over2);
            double formula = -Math.Log(Math.Pow((1 + 2.0 * e) / (1 + e), -b));
            Assert.Equal(formula, core, 1e-3);
            double fullH = Math.Log(ProductsMath.AssemblyHours(Fx6.FromInt(e), hFirst, hFloor, q).ToDouble() / ProductsMath.AssemblyHours(Fx6.FromInt(2 * e), hFirst, hFloor, q).ToDouble());
            _output.WriteLine($"C1c line_E {e,3}→{2 * e,3}: over-floor fall {1 - Math.Exp(-core):P1} (formula {1 - Math.Exp(-formula):P1}); full assembly_hours fall {1 - Math.Exp(-fullH):P1} [diagnostic]");
            sumCore += core; sumFormula += formula; sumFull += fullH; count++;
        }
        double c1c = 1 - Math.Exp(-sumCore / count), c1cFormula = 1 - Math.Exp(-sumFormula / count);
        _output.WriteLine($"C1c window mean (9 doublings): {c1c:P2}; formula {c1cFormula:P2}; full assembly_hours {1 - Math.Exp(-sumFull / count):P2} [diagnostic]");
        Assert.Equal(c1cFormula, c1c, 1e-3);
        Assert.InRange(c1c, 0.15, 0.30);   // P0 construction check (#187), not a calibration anchor

        // Mechanics of the mature setup: both channels frozen ⇒ no fall; additivity within ±15% on the window sums.
        Assert.True(MinYieldAt(6, 0) >= 0.9, "mature precondition: yield ≥ 0.9 at line_E = 0");
        var w = MatureWindow(6, 256);
        (double fall, double share, double add) = Aggregate(w);
        Assert.InRange(add, 0.85, 1.15);
        Assert.True(w.All(v => v.Yield > 0 && v.Assembly >= 0 && v.Full > 0));
        Assert.True(share > 0 && fall > 0);
    }

    [Fact]
    public void Print_MatureSetup_C1d_C1e_WindowToBaselineAndArmX0Equals3Xhalf()
    {
        _output.WriteLine("mature setup: X = X0 + line_E, X0 = 6·X½ (ramp 0.9), g = 1, new line_E = 0; arms: yield frozen at X0, assembly frozen at line_E = 0");
        foreach (int x0 in new[] { 6, 3 })
        {
            _output.WriteLine($"-- X0 = {x0}·X½: min process yield at line_E = 0: {MinYieldAt(x0, 0):0.000}");
            var all = MatureWindow(x0, 4096);
            foreach (Dbl v in all)
                _output.WriteLine($"   line_E {v.Start,5}→{2 * v.Start,5}: unit_cost fall {1 - Math.Exp(-v.Full):P1}; yield share {v.Yield / (v.Yield + v.Assembly):P0}; Σ/full {(v.Yield + v.Assembly) / v.Full:0.000}");
            var win = all.Where(v => v.Start <= 400).ToList();
            (double fall, double share, double add) = Aggregate(win);
            _output.WriteLine($"   window ≤400 ({win.Count} doublings): C1e unit_cost fall {fall:P2} (≤30%); C1d yield share {share:P1} (≤20%); additivity {add:0.000}");
            var horizon = all;
            (double f2, double s2, double a2) = Aggregate(horizon);
            _output.WriteLine($"   to ~9600 pcs (starts ≤4096) [diagnostic, #188]: unit_cost fall {f2:P2}; yield share {s2:P1}; additivity {a2:0.000}");
        }
    }

    [Fact]
    public void Print_MasterySetup_C1a_C1b_WithGEqualsOne_AndWhatRemainsForT10T12()
    {
        // X0 = 0, line_E = 0; doublings from the first month's output r_ref = 25: 25→50→100→200; only doublings with yield ≥ 0.25 count.
        var all = new List<Dbl>();
        for (long n = 25; n <= 100; n *= 2)
            all.Add(Doubling(0, n, 0, 0));
        foreach (Dbl v in all)
            _output.WriteLine($"mastery N {v.Start,3}→{2 * v.Start,3}: min process yield at start {v.MinYield:0.000}; unit_cost fall {1 - Math.Exp(-v.Full):P1}; yield share {v.Yield / (v.Yield + v.Assembly):P0}; Σ/full {(v.Yield + v.Assembly) / v.Full:0.000}");
        var used = all.Where(v => v.MinYield >= 0.25).ToList();
        if (used.Count == 0)
        {
            _output.WriteLine("mastery: no doubling passes yield ≥ 0.25 ⇒ C1a, C1b undefined, deviation d = 1 (contract §8.2)");
            return;
        }
        (double fall, double share, double add) = Aggregate(used);
        _output.WriteLine($"mastery ({used.Count} doublings, g ≡ 1): C1a unit_cost fall {fall:P2} (≤40%); C1b yield share {share:P1} (50–80%); additivity {add:0.000}");
        _output.WriteLine("NOT at Core level (T10/T12): culture_H (§4.1) and tooling_M (§4.1b, V5) from the GBR-1945 scenario, the default AI set-up rule, t = N/r_ref; variants E ≡ 0 / g ≡ 1");
        Assert.InRange(add, 0.85, 1.15);
    }
}
