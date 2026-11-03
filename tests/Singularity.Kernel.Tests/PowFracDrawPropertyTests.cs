using System.Numerics;
using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

/// <summary>
/// Property tests for DetMath.PowFrac/PowFracFx and the integer draws of CounterRng
/// (TASK-0010, ADR-0003 R2, ADR-0012). References are computed independently with BigInteger.
/// </summary>
public class PowFracDrawPropertyTests
{
    private static readonly double[] BasesAboveOne = [1.0001, 1.01, 1.5, 2.0, 3.0, 10.0, 1234.5, 98765.4321];
    private static readonly double[] BasesBelowOne = [0.9999, 0.99, 0.8, 0.5, 0.35, 0.1, 0.001];

    // ---------- PowFrac ----------

    [Fact]
    public void PowFrac_IsMonotonicInExponent()
    {
        foreach (double x in BasesAboveOne)
        {
            double previous = DetMath.PowFrac(x, -4096);
            for (int q = -4095; q <= 4096; q++)
            {
                double current = DetMath.PowFrac(x, q);
                Assert.True(previous < current, $"x={x:R} q10={q}: {previous:R} !< {current:R}");
                previous = current;
            }
        }
        foreach (double x in BasesBelowOne)
        {
            double previous = DetMath.PowFrac(x, -4096);
            for (int q = -4095; q <= 4096; q++)
            {
                double current = DetMath.PowFrac(x, q);
                Assert.True(previous > current, $"x={x:R} q10={q}: {previous:R} !> {current:R}");
                previous = current;
            }
        }
    }

    [Fact]
    public void PowFrac_OneAndZeroIdentities()
    {
        for (int q = -1 << 20; q <= 1 << 20; q += 97)
            Assert.Equal(1.0, DetMath.PowFrac(1.0, q));
        Assert.Equal(1.0, DetMath.PowFrac(1.0, int.MaxValue));
        Assert.Equal(1.0, DetMath.PowFrac(1.0, -int.MaxValue));
        for (int q = 1; q <= 1 << 16; q++)
            Assert.Equal(0.0, DetMath.PowFrac(0.0, q));
        Assert.Equal(1.0, DetMath.PowFrac(0.0, 0));
        // x^(1/1) and x^0 for arbitrary bases.
        foreach (double x in BasesAboveOne.Concat(BasesBelowOne))
        {
            Assert.Equal(x, DetMath.PowFrac(x, 1024));
            Assert.Equal(1.0, DetMath.PowFrac(x, 0));
            Assert.Equal(1.0 / x, DetMath.PowFrac(x, -1024));
        }
    }

    [Theory]
    [InlineData(3.0, 33)]     // 3^33 < 2^53: every intermediate is exact
    [InlineData(1.5, 33)]
    [InlineData(10.0, 22)]
    [InlineData(2.0, 1000)]
    [InlineData(0.5, 1000)]
    [InlineData(0.25, 500)]
    public void PowFrac_MultiplesOf1024_AreExactIntegerPowers(double x, int maxN)
    {
        for (int n = 0; n <= maxN; n++)
        {
            double expected = ExactPow(x, n);
            Assert.Equal(expected, DetMath.PowFrac(x, n * 1024));
            Assert.Equal(expected, DetMath.PowDyadic(x, n * 8));
            // Negative exponent is exactly one division of the positive power.
            if (expected != 0)
                Assert.Equal(1.0 / expected, DetMath.PowFrac(x, -n * 1024));
        }
    }

    [Fact]
    public void PowFrac_IsBitStable_SequentialVsParallel()
    {
        // L1/L2 for a pure function: repeated and multi-threaded evaluation give identical bits.
        double[] bases = [0.001, 0.35, 0.8, 1.7, 2.0, 1234.5, 98765.4321];
        const int span = 8193;
        long[] first = new long[bases.Length * span];
        long[] second = new long[first.Length];
        for (int i = 0; i < first.Length; i++)
            first[i] = BitConverter.DoubleToInt64Bits(DetMath.PowFrac(bases[i / span], i % span - 4096));
        Parallel.For(0, second.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 },
            i => second[i] = BitConverter.DoubleToInt64Bits(DetMath.PowFrac(bases[i / span], i % span - 4096)));
        Assert.Equal(first, second);
    }

    // ---------- PowFracFx ----------

    [Fact]
    public void PowFracFx_RoundsDown_ToTheMicroGrid()
    {
        var rng = new CounterRng(10);
        for (int i = 0; i < 20_000; i++)
        {
            long raw = (long)(rng.NextU64(0, 1, i, 0) % 1_000_000_000_000UL); // x in [0; 1e6)
            int q = (int)(rng.NextU64(0, 1, i, 1) % 4097) - 2048;
            if (raw == 0 && q < 0)
                continue;
            double v = DetMath.PowFrac(raw / 1e6, q);
            if (v * 1e6 >= 4.5e15) // keep got + 1 exact in double (< 2^52)
                continue;
            long got = DetMath.PowFracFx(Fx6.FromRaw(raw), q).Raw;
            double scaled = v * 1e6;
            Assert.True(got <= scaled && scaled < got + 1.0, $"raw={raw} q={q}: {got} vs {scaled:R}");
        }
    }

    [Fact]
    public void PowFracFx_IdentityExponent_LosesAtMostOneRawUnit()
    {
        // ToDouble/FromDouble round-trip (Fx6 doc): x^1 may come back 1e-6 lower, never higher.
        var rng = new CounterRng(11);
        for (int i = 0; i < 50_000; i++)
        {
            long raw = (long)(rng.NextU64(0, 2, i, 0) >> 12); // up to 2^52
            long got = DetMath.PowFracFx(Fx6.FromRaw(raw), 1024).Raw;
            Assert.InRange(got, raw - 1, raw);
        }
    }

    [Fact]
    public void PowFracFx_Overflow_ThrowsInsteadOfWrapping()
    {
        // 1e7^2 = 1e14 > Fx6 max (~9.22e12).
        Assert.Throws<OverflowException>(() => DetMath.PowFracFx(Fx6.FromInt(10_000_000), 2048));
        // 1e-6 ^ -3 = 1e18.
        Assert.Throws<OverflowException>(() => DetMath.PowFracFx(Fx6.FromRaw(1), -3072));
        // Largest finite power that fits: 1e6^2 = 1e12.
        Assert.Equal(Fx6.FromInt(1_000_000_000_000), DetMath.PowFracFx(Fx6.FromInt(1_000_000), 2048));
        // Underflow floors to zero, not to a negative or a throw.
        Assert.Equal(Fx6.Zero, DetMath.PowFracFx(Fx6.FromRaw(1), 4096));
    }

    [Fact]
    public void PowFracFx_MaxRaw_Overflows_EvenForIdentity()
    {
        // Documents a limit: long.MaxValue / 1e6 rounds to 2^63 / 1e6 in double, so x^1 overflows.
        Assert.Throws<OverflowException>(() => DetMath.PowFracFx(Fx6.FromRaw(long.MaxValue), 1024));
    }

    // ---------- Chance ----------

    [Fact]
    public void Chance_ExactBoundary_MatchesBigIntegerThreshold()
    {
        var rng = new CounterRng(12);
        long[] fixedPs = [1, 2, 3, 7, 333_333, 500_000, 666_667, 999_998, 999_999, 1_000_000];
        IEnumerable<long> ps = fixedPs.Concat(Enumerable.Range(0, 2_000)
            .Select(i => 1 + (long)(rng.NextU64(0, 3, i, 0) % 1_000_000)));
        foreach (long p in ps)
        {
            BigInteger t = (new BigInteger(p) << 53) / 1_000_000;
            ulong threshold = (ulong)t;
            Fx6 fp = Fx6.FromRaw(p);
            Assert.True(CounterRng.Chance((threshold - 1) << 11 | 0x7FF, fp), $"p={p} r=T-1");
            if (threshold < 1UL << 53)
                Assert.False(CounterRng.Chance(threshold << 11, fp), $"p={p} r=T");
            Assert.True(CounterRng.Chance(0, fp), $"p={p} r=0");
        }
    }

    [Fact]
    public void Chance_IsMonotonicInP()
    {
        var rng = new CounterRng(13);
        for (int i = 0; i < 2_000; i++)
        {
            ulong u = rng.NextU64(0, 4, i, 0);
            bool previous = false;
            for (long p = 0; p <= 1_000_000; p += 997)
            {
                bool current = CounterRng.Chance(u, Fx6.FromRaw(p));
                Assert.True(!previous || current, $"u={u} p={p}");
                previous = current;
            }
        }
    }

    [Theory]
    [InlineData(10_000L)]
    [InlineData(100_000L)]
    [InlineData(250_000L)]
    [InlineData(500_000L)]
    [InlineData(900_000L)]
    public void Chance_FrequencyMatchesP(long pRaw)
    {
        const int n = 200_000;
        var rng = new CounterRng(14);
        Fx6 p = Fx6.FromRaw(pRaw);
        int hits = 0;
        for (int i = 0; i < n; i++)
            if (CounterRng.Chance(rng.NextU64(i, 5, i & 1023, 0), p))
                hits++;
        double pp = pRaw / 1e6;
        double sigma = Math.Sqrt(n * pp * (1 - pp));
        Assert.InRange(hits, n * pp - 5 * sigma, n * pp + 5 * sigma);
    }

    // ---------- UniformInt / Index ----------

    public static TheoryData<long, long> Ranges => new()
    {
        { long.MinValue, long.MaxValue },
        { long.MinValue, long.MinValue },
        { long.MaxValue, long.MaxValue },
        { long.MaxValue - 10, long.MaxValue },
        { long.MinValue, long.MinValue + 10 },
        { long.MinValue, 0 },
        { 0, long.MaxValue },
        { -(1L << 53), (1L << 53) - 1 },
        { 0, (1L << 53) - 1 },
        { 0, 1L << 53 },
        { -1, 0 },
    };

    [Theory]
    [MemberData(nameof(Ranges))]
    public void UniformInt_MatchesBigIntegerReference_OnExtremeSpans(long lo, long hi)
    {
        var rng = new CounterRng(15);
        ulong[] fixedU = [0, 0x7FF, 1UL << 11, 1UL << 63, ulong.MaxValue, ulong.MaxValue - 0x7FF - 1];
        IEnumerable<ulong> us = fixedU.Concat(Enumerable.Range(0, 5_000).Select(i => rng.NextU64(0, 6, i, 0)));
        BigInteger span = (BigInteger)hi - lo + 1;
        foreach (ulong u in us)
        {
            BigInteger expected = lo + (((BigInteger)(u >> 11) * span) >> 53);
            long got = CounterRng.UniformInt(u, lo, hi);
            Assert.Equal(expected, (BigInteger)got);
            Assert.InRange(got, lo, hi);
        }
    }

    [Fact]
    public void UniformInt_FullSpan_TopValue()
    {
        // r = 2^53 - 1, span = 2^64: lo + 2^64 - 2^11 = long.MaxValue - 2047.
        Assert.Equal(long.MaxValue - 2047, CounterRng.UniformInt(ulong.MaxValue, long.MinValue, long.MaxValue));
        Assert.Equal(long.MinValue, CounterRng.UniformInt(0, long.MinValue, long.MaxValue));
    }

    [Fact]
    public void UniformInt_IsMonotonicInU()
    {
        var rng = new CounterRng(16);
        ulong[] us = Enumerable.Range(0, 5_000).Select(i => rng.NextU64(0, 7, i, 0)).Order().ToArray();
        (long lo, long hi)[] ranges = [(0, 2), (-5, 5), (long.MinValue, long.MaxValue), (0, 1_000_001)];
        foreach ((long lo, long hi) in ranges)
            for (int i = 1; i < us.Length; i++)
                Assert.True(CounterRng.UniformInt(us[i - 1], lo, hi) <= CounterRng.UniformInt(us[i], lo, hi));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void Index_StaysInRange_AndHitsEnds(int count)
    {
        Assert.Equal(0, CounterRng.Index(0, count));
        Assert.Equal(count - 1, CounterRng.Index(ulong.MaxValue, count));
        var rng = new CounterRng(17);
        for (int i = 0; i < 5_000; i++)
        {
            ulong u = rng.NextU64(0, 8, i, 0);
            int got = CounterRng.Index(u, count);
            Assert.InRange(got, 0, count - 1);
            Assert.Equal((int)(((BigInteger)(u >> 11) * count) >> 53), got);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => CounterRng.Index(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CounterRng.Index(0, int.MinValue));
    }

    private static double ExactPow(double x, int n)
    {
        double r = 1.0;
        for (int i = 0; i < n; i++)
            r *= x;
        return r;
    }
}
