using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Industry;

/// <summary>
/// Property tests of the capability formulas (reference README I1–I3, I6; decision #26 soft threshold;
/// contract §4.6 reserve_workforce) on seeded random inputs, plus the forgetting branch of <c>proc_X</c> over a
/// long idle stretch, which the reference trajectory does not reach.
/// </summary>
public class IndustryPropertyTests
{
    private static Fx6 F(long raw) => Fx6.FromRaw(raw);

    private static Fx6 D(int units) => Fx6.FromInt(units);

    private static Fx6 Rnd(Random r, long minRaw, long maxRaw) => F(r.NextInt64(minRaw, maxRaw + 1));

    // ---- g and yield (I3, #26)

    [Fact]
    public void G_IsMonotone_InUnitRange_ZeroFromOneTierBelow()
    {
        Fx6 prev = Fx6.Zero;
        for (long raw = -3_000_000; raw <= 2_000_000; raw += 997)
        {
            Fx6 g = IndustryMath.G(F(raw));
            Assert.InRange(g.Raw, 0, Fx6.One.Raw);
            Assert.True(g >= prev, $"g not monotone at gap {raw}");
            if (raw <= -1_000_000)
                Assert.Equal(Fx6.Zero, g); // #26: a gap of a whole tier or more gives nothing
            if (raw >= 0)
                Assert.Equal(Fx6.One, g);
            prev = g;
        }
        Assert.True(IndustryMath.G(F(-999_999)) >= Fx6.Zero);
        Assert.Equal(Fx6.Zero, IndustryMath.G(-Fx6.One));
    }

    [Fact]
    public void Yield_InUnitRange_MonotoneInCapabilityAndExperience()
    {
        var rnd = new Random(26);
        var p = new IndustryParams();
        for (int i = 0; i < 3000; i++)
        {
            Dims3 req = RandomReq(rnd);
            Dims3 h = RandomDims(rnd, 0, 6_000_000), m = RandomDims(rnd, 0, 6_000_000);
            Fx6 x = Rnd(rnd, 0, 50_000_000_000), xh = p.XHalf(3 + rnd.Next(3)), lic = Rnd(rnd, 0, 900_000);
            ProcessYield y = IndustryMath.Yield(req, h, m, x, xh, lic, p.Lambda0);
            Assert.InRange(y.Yield.Raw, 0, Fx6.One.Raw);
            Assert.InRange(y.QP.Raw, 0, Fx6.One.Raw);
            Assert.True(y.Yield <= y.Ramp);
            Assert.True(y.Ramp >= p.Lambda0 && y.Ramp <= Fx6.One);

            // Raising one stock (H or M) in one dimension never lowers yield; neither does more experience.
            int d = rnd.Next(3);
            Fx6 bump = Rnd(rnd, 1, 2_000_000);
            Dims3 h2 = With(h, d, h[d] + bump), m2 = With(m, d, m[d] + bump);
            Assert.True(IndustryMath.Yield(req, h2, m, x, xh, lic, p.Lambda0).Yield >= y.Yield);
            Assert.True(IndustryMath.Yield(req, h, m2, x, xh, lic, p.Lambda0).Yield >= y.Yield);
            Assert.True(IndustryMath.Yield(req, h2, m2, x, xh, lic, p.Lambda0).Yield >= y.Yield);
            Assert.True(IndustryMath.Yield(req, h, m, x + bump, xh, lic, p.Lambda0).Yield >= y.Yield);

            // #26: if any active dimension is a full tier (or more) short, the process yields exactly 0 at any X.
            bool farBelow = false;
            for (int k = 0; k < 3; k++)
                farBelow |= req[k] > Fx6.Zero && Min(h[k], m[k]) - req[k] <= -Fx6.One;
            if (farBelow)
            {
                Assert.Equal(Fx6.Zero, y.Yield);
                Assert.Equal(Fx6.Zero, IndustryMath.Yield(req, h, m, F(long.MaxValue / 4_000_000), xh, lic, p.Lambda0).Yield);
            }
            else
            {
                Assert.True(y.Yield >= Fx6.Zero);
            }
        }
    }

    [Fact]
    public void Yield_MonotoneInGap_AlongASweep()
    {
        // Same process, capability c sweeps from r − 2 to r + 1: yield never falls and is 0 up to c = r − 1.
        var p = new IndustryParams();
        var req = new Dims3(D(4), Fx6.Zero, Fx6.Zero);
        Fx6 prev = Fx6.Zero;
        for (long c = 2_000_000; c <= 5_000_000; c += 12_345)
        {
            var cap = new Dims3(F(c), Fx6.Zero, Fx6.Zero);
            Fx6 y = IndustryMath.Yield(req, cap, cap, D(800), D(800), Fx6.Zero, p.Lambda0).Yield;
            Assert.True(y >= prev);
            if (c <= 3_000_000)
                Assert.Equal(Fx6.Zero, y);
            else
                Assert.True(y > Fx6.Zero);
            prev = y;
        }
    }

    // ---- culture_H (I1, I2)

    [Fact]
    public void Culture_RandomCells_NonNegative_GrowthCapped_FloorRespected()
    {
        var rnd = new Random(30);
        for (int i = 0; i < 5000; i++)
        {
            var p = new IndustryParams
            {
                DeltaH = Rnd(rnd, 0, 500_000),
                Alpha = Rnd(rnd, 0, 200_000),
                RhoMax = Rnd(rnd, 0, 100_000),
                PhiFloor = Rnd(rnd, 0, 1_000_000),
                Sigma = Rnd(rnd, 0, 100_000),
                KCatch = Rnd(rnd, 0, 100_000),
            };
            var cell = new CultureCell(Rnd(rnd, 0, 7_000_000), Rnd(rnd, 100_000, 7_000_000), Rnd(rnd, 0, 2_000_000),
                Rnd(rnd, 0, 2_000_000), rnd.Next(2) == 0);
            int np = rnd.Next(5);
            var procs = new CultureProcess[np];
            int number = 0;
            for (int k = 0; k < np; k++)
            {
                number += 1 + rnd.Next(5);
                procs[k] = new CultureProcess(number, Rnd(rnd, 1, 10_000_000_000), Rnd(rnd, 0, 6_000_000), rnd.Next(3) == 0);
            }
            CultureSpill[] spill = [.. Enumerable.Range(0, rnd.Next(4)).Select(_ => new CultureSpill(Rnd(rnd, 0, 300_000), Rnd(rnd, 0, 7_000_000)))];
            CultureStep s = IndustryMath.Culture(cell, procs, spill, p);
            Fx6 h = cell.H;
            Assert.True(s.HNew >= Fx6.Zero, $"case {i}: I1 negative");
            Assert.True(s.HNew - h <= p.RhoMax, $"case {i}: I1 growth {s.HNew - h} > rho_max");
            Assert.True(s.HNew >= Min(h, s.HFloor), $"case {i}: I2 fell below min(H, H_floor)");
            Assert.True(s.DHMinus >= Fx6.Zero && s.DHPlusCapped >= Fx6.Zero);
            if (h >= cell.Hcap)
                Assert.Equal(Fx6.Zero, s.DHPlus); // saturation: no growth at or above the cap
        }
    }

    // ---- forgetting over a long idle stretch (contract §4.0 p. 6)

    [Fact]
    public void Forget_Iterated_DecreasesToAFixedPoint_NeverNegative()
    {
        Fx6 delta = new IndustryParams().DeltaX;
        Fx6 x = D(2400);
        long months = 0;
        while (true)
        {
            Fx6 next = IndustryMath.Forget(x, delta);
            Assert.True(next >= Fx6.Zero);
            Assert.True(next <= x);
            if (next == x)
                break;
            x = next;
            months++;
        }
        // Floor on δ·x stops forgetting once δ·x < 1 raw: x stalls just below 1/δ raw = 250 raw, not at 0.
        Assert.InRange(x.Raw, 1, 249);
        Assert.InRange(months, 3000, 6000);
    }

    [Fact]
    public void LongIdle_ProcXForgets_CultureDecaysToFloor_YieldNeverRises()
    {
        var rig = new IdleRig();
        int p = rig.Layout.RowOf(39);
        Fx6 x = rig.Start.X[p], h = rig.Start.H[rig.Start.Cell(0, 0, 1)];
        Fx6 floor = rig.P.PhiFloor * D(5);
        Fx6 prevYield = Fx6.One, prevH = h;
        for (long t = 0; t < 360; t++)
        {
            rig.RunThroughMonth(t);
            x = IndustryMath.Forget(x, rig.P.DeltaX);
            Assert.Equal(x, rig.System.ProcX(0, p));
            Assert.Equal(Fx6.Zero, rig.System.ProcV(0, p));
            Fx6 hNow = rig.System.CultureH(0, 0, Dimension.Purity);
            Assert.True(hNow <= prevH && hNow >= Min(h, floor), $"t={t}: H {hNow}");
            Fx6 y = rig.System.LastYield(0, p).Yield;
            Assert.True(y <= prevYield, $"t={t}: yield rose while idle");
            prevYield = y;
            prevH = hNow;
        }
        // 30 years idle: X fell by (1 − 0.004)^360 ≈ 0.236; culture is close to the floor and above it.
        Assert.InRange(rig.System.ProcX(0, p).Raw, D(2400).Raw * 22 / 100, D(2400).Raw * 25 / 100);
        Assert.True(rig.System.CultureH(0, 0, Dimension.Purity) - floor < F(450_000)); // 1.2·(1 − 0.003)^360 ≈ 0.41
    }

    private sealed class IdleRig
    {
        public IdleRig()
        {
            Layout = IndustryLayout.Create([Cluster.MET],
                [new IndustryProcessSpec(39, Cluster.MET, 0, 4, new Dims3(Fx6.Zero, D(4), F(3_500_000)))], [0], 0, 0);
            Start = new IndustryStart(Layout);
            Fx6[] h = [D(4), F(4_200_000), F(3_900_000)];
            for (int d = 0; d < 3; d++)
            {
                Start.H[Start.Cell(0, 0, d)] = h[d];
                Start.M[Start.Cell(0, 0, d)] = h[d] + F(200_000);
            }
            Start.X[0] = D(2400);
            Table = new Table(1);
            Calendar = new TickCalendar(new CivilDate(1945, 9, 1));
            System = new IndustrySystem(Table, Layout, Start, P, new IdleInputs(), 1);
            Runner = new TickRunner(Table, 1, [System], 1, 1, Calendar);
        }

        public IndustryLayout Layout { get; }
        public IndustryStart Start { get; }
        public IndustryParams P { get; } = new();
        public Table Table { get; }
        public TickCalendar Calendar { get; }
        public IndustrySystem System { get; }
        public TickRunner Runner { get; }

        public void RunThroughMonth(long month)
        {
            long last = Calendar.TickOfMonth(month);
            while (Runner.Tick <= last)
                Runner.RunTick();
        }
    }

    private sealed class IdleInputs : IIndustryInputs
    {
        public bool KnowsProcess(int siteRow, int processRow, long month) => true;

        public Fx6 Act(int siteRow, int cluster, long month) => Fx6.One;

        public Fx6 W(int siteRow, int cluster, long month) => Fx6.One;

        public Fx6 Hcap(int siteRow, Dimension dim, long month) => D(5);

        public bool QualityProgramme(int siteRow, long month) => false;

        public Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month) => Fx6.Zero;
    }

    // ---- reserve_workforce (I6, contract §4.6)

    [Fact]
    public void SplitWorkforce_Adversarial_MatchesExactProportionalRule()
    {
        var rnd = new Random(46);
        for (int i = 0; i < 4000; i++)
        {
            int count = rnd.Next(4) switch { 0 => 1, 1 => rnd.Next(2, 6), 2 => rnd.Next(6, 60), _ => rnd.Next(60, 400) };
            long scale = rnd.Next(4) switch { 0 => 3, 1 => 100, 2 => 1_000_000, _ => long.MaxValue / 1000 };
            long[] n = [.. Enumerable.Range(0, count).Select(_ => rnd.Next(5) == 0 ? 0 : rnd.NextInt64(0, scale))];
            if (rnd.Next(3) == 0)
                Array.Fill(n, n[0]); // ties: equal requests, the order decides
            long total = 0;
            foreach (long v in n)
                total += v;
            long avail = rnd.Next(4) switch
            {
                0 => 0,
                1 => rnd.NextInt64(0, Math.Max(1, total)),
                2 => Math.Max(0, total - rnd.Next(0, count + 1)),
                _ => rnd.NextInt64(0, long.MaxValue / 2),
            };
            var granted = new long[count];
            IndustryMath.SplitWorkforce(avail, n, granted);
            Assert.Equal(Expected(avail, n), granted);
            long sum = 0;
            for (int k = 0; k < count; k++)
            {
                Assert.InRange(granted[k], 0, n[k]);
                sum += granted[k];
            }
            Assert.Equal(Math.Min(avail, total), sum);
            if (total > avail)
            {
                // Within one person of the exact share; among equal requests earlier ids never get less.
                for (int k = 0; k < count; k++)
                {
                    long fl = (long)((Int128)n[k] * avail / total);
                    Assert.InRange(granted[k], fl, fl + 1);
                    for (int j = 0; j < k; j++)
                    {
                        if (n[j] == n[k])
                            Assert.True(granted[j] >= granted[k]);
                    }
                }
            }
        }
    }

    [Fact]
    public void SplitWorkforce_ManyEqualRequesters_RemainderGoesToLowestIds()
    {
        var granted = new long[100];
        long[] n = [.. Enumerable.Repeat(3L, 100)];
        IndustryMath.SplitWorkforce(250, n, granted); // 2 each = 200, 50 left → ids 0..49 get 3
        Assert.All(granted.Take(50), g => Assert.Equal(3, g));
        Assert.All(granted.Skip(50), g => Assert.Equal(2, g));
        IndustryMath.SplitWorkforce(7, [.. Enumerable.Repeat(1L, 10)], granted.AsSpan(0, 10));
        Assert.Equal([1L, 1, 1, 1, 1, 1, 1, 0, 0, 0], granted.Take(10));
    }

    [Fact]
    public void SplitWorkforce_NegativeInputs_Throw()
    {
        var g = new long[2];
        Assert.Throws<ArgumentOutOfRangeException>(() => IndustryMath.SplitWorkforce(-1, [1, 1], g));
        Assert.Throws<ArgumentOutOfRangeException>(() => IndustryMath.SplitWorkforce(1, [1, -1], g));
        Assert.Throws<OverflowException>(() => IndustryMath.SplitWorkforce(1, [long.MaxValue, 1], g));
    }

    /// <summary>Independent statement of contract §4.6 in BigInteger-free Int128 arithmetic.</summary>
    private static long[] Expected(long avail, long[] n)
    {
        Int128 total = 0;
        foreach (long v in n)
            total += v;
        if (total <= avail)
            return [.. n];
        var g = new long[n.Length];
        Int128 given = 0;
        for (int i = 0; i < n.Length; i++)
        {
            g[i] = (long)(n[i] * (Int128)avail / total);
            given += g[i];
        }
        Int128 rest = avail - given;
        for (int i = 0; i < n.Length && rest > 0; i++)
        {
            if (g[i] < n[i])
            {
                g[i]++;
                rest--;
            }
        }
        Assert.Equal(0, rest);
        return g;
    }

    // ---- helpers

    private static Dims3 RandomReq(Random r)
    {
        Fx6 Pick() => r.Next(3) == 0 ? Fx6.Zero : Rnd(r, 2_500_000, 5_500_000);
        return new Dims3(Pick(), Pick(), Pick());
    }

    private static Dims3 RandomDims(Random r, long min, long max) => new(Rnd(r, min, max), Rnd(r, min, max), Rnd(r, min, max));

    private static Dims3 With(Dims3 v, int d, Fx6 x) => d switch
    {
        0 => new Dims3(x, v[1], v[2]),
        1 => new Dims3(v[0], x, v[2]),
        _ => new Dims3(v[0], v[1], x),
    };

    private static Fx6 Min(Fx6 a, Fx6 b) => a < b ? a : b;
}
