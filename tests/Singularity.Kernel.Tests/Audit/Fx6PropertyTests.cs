using System.Numerics;
using Singularity.Kernel;

namespace Singularity.Kernel.Tests.Audit;

/// <summary>Seeded property tests for Fx6 against a BigInteger reference (test-engineer audit, TASK-0001).</summary>
public class Fx6PropertyTests
{
    private const int Iterations = 20_000;
    private static readonly BigInteger LongMin = long.MinValue;
    private static readonly BigInteger LongMax = long.MaxValue;

    private static BigInteger FloorDiv(BigInteger n, BigInteger d)
    {
        BigInteger q = BigInteger.DivRem(n, d, out BigInteger r);
        if (!r.IsZero && (r.Sign < 0) != (d.Sign < 0))
            q -= 1;
        return q;
    }

    private static long RandomRaw(Random rnd)
    {
        // Mix of magnitudes: small, medium, full-range, and boundary values.
        return rnd.Next(6) switch
        {
            0 => rnd.NextInt64(-3_000_000, 3_000_001),
            1 => rnd.NextInt64(-1_000_000_000_000, 1_000_000_000_001),
            2 => rnd.NextInt64(long.MinValue, long.MaxValue),
            3 => new[] { long.MinValue, long.MinValue + 1, long.MaxValue, long.MaxValue - 1, 0, 1, -1, 1_000_000, -1_000_000 }[rnd.Next(9)],
            4 => rnd.NextInt64(-3_037_000_499, 3_037_000_500), // near sqrt(long.MaxValue)
            _ => rnd.NextInt64(-1_000, 1_001),
        };
    }

    [Fact]
    public void Mul_MatchesBigIntegerFloorOrThrows()
    {
        var rnd = new Random(101);
        for (int i = 0; i < Iterations; i++)
        {
            long a = RandomRaw(rnd), b = RandomRaw(rnd);
            BigInteger expected = FloorDiv((BigInteger)a * b, Fx6.Scale);
            if (expected < LongMin || expected > LongMax)
                Assert.Throws<OverflowException>(() => Fx6.FromRaw(a) * Fx6.FromRaw(b));
            else
                Assert.True((long)expected == (Fx6.FromRaw(a) * Fx6.FromRaw(b)).Raw, $"mul a={a} b={b}");
        }
    }

    [Fact]
    public void Div_MatchesBigIntegerFloorOrThrows()
    {
        var rnd = new Random(202);
        for (int i = 0; i < Iterations; i++)
        {
            long a = RandomRaw(rnd), b = RandomRaw(rnd);
            if (b == 0)
            {
                Assert.Throws<DivideByZeroException>(() => Fx6.FromRaw(a) / Fx6.FromRaw(b));
                continue;
            }
            BigInteger expected = FloorDiv((BigInteger)a * Fx6.Scale, b);
            if (expected < LongMin || expected > LongMax)
                Assert.Throws<OverflowException>(() => Fx6.FromRaw(a) / Fx6.FromRaw(b));
            else
                Assert.True((long)expected == (Fx6.FromRaw(a) / Fx6.FromRaw(b)).Raw, $"div a={a} b={b}");
        }
    }

    [Fact]
    public void AddSub_MatchBigIntegerOrThrow()
    {
        var rnd = new Random(303);
        for (int i = 0; i < Iterations; i++)
        {
            long a = RandomRaw(rnd), b = RandomRaw(rnd);
            BigInteger sum = (BigInteger)a + b, diff = (BigInteger)a - b;
            if (sum < LongMin || sum > LongMax)
                Assert.Throws<OverflowException>(() => Fx6.FromRaw(a) + Fx6.FromRaw(b));
            else
            {
                Assert.Equal((long)sum, (Fx6.FromRaw(a) + Fx6.FromRaw(b)).Raw);
                Assert.Equal(a, (Fx6.FromRaw(a) + Fx6.FromRaw(b) - Fx6.FromRaw(b)).Raw); // a + b - b == a
            }
            if (diff < LongMin || diff > LongMax)
                Assert.Throws<OverflowException>(() => Fx6.FromRaw(a) - Fx6.FromRaw(b));
            else
                Assert.Equal((long)diff, (Fx6.FromRaw(a) - Fx6.FromRaw(b)).Raw);
        }
    }

    [Fact]
    public void Comparison_IsConsistentWithRaw()
    {
        var rnd = new Random(404);
        for (int i = 0; i < Iterations; i++)
        {
            long a = RandomRaw(rnd), b = RandomRaw(rnd);
            Fx6 x = Fx6.FromRaw(a), y = Fx6.FromRaw(b);
            Assert.Equal(a.CompareTo(b), x.CompareTo(y));
            Assert.Equal(a < b, x < y);
            Assert.Equal(a <= b, x <= y);
            Assert.Equal(a == b, x == y);
            Assert.Equal(a == b, x.Equals((object)y));
            if (a == b)
                Assert.Equal(x.GetHashCode(), y.GetHashCode());
        }
    }

    [Theory]
    [InlineData(-7, 2, -3_500_000)]       // exact
    [InlineData(-1, 3, -333_334)]         // negative floor
    [InlineData(-2, 3, -666_667)]
    [InlineData(7, -2, -3_500_000)]
    [InlineData(-7, -2, 3_500_000)]
    public void Div_NegativeOperands_FloorTowardsMinusInfinity(long a, long b, long expectedRaw)
    {
        Assert.Equal(expectedRaw, (Fx6.FromInt(a) / Fx6.FromInt(b)).Raw);
    }

    [Fact]
    public void MinValue_EdgeCases()
    {
        var min = Fx6.FromRaw(long.MinValue);
        var minusOne = Fx6.FromInt(-1);
        Assert.Throws<OverflowException>(() => min * minusOne);           // -min overflows
        Assert.Throws<OverflowException>(() => min / minusOne);
        Assert.Equal(long.MinValue, (min * Fx6.One).Raw);                  // identity
        Assert.Equal(long.MinValue, (min / Fx6.One).Raw);
        Assert.Equal(long.MinValue, (min + Fx6.Zero).Raw);
        Assert.Equal(-long.MaxValue, (-Fx6.FromRaw(long.MaxValue)).Raw);
        Assert.Throws<OverflowException>(() => min - Fx6.FromRaw(1));
        Assert.Throws<OverflowException>(() => Fx6.FromInt(long.MinValue / Fx6.Scale - 1));
        Assert.Equal(long.MinValue / Fx6.Scale * Fx6.Scale, Fx6.FromInt(long.MinValue / Fx6.Scale).Raw);
        Assert.Equal(long.MinValue, Fx6.FromDouble(-9_223_372_036_854.775808).Raw); // -2^63 is representable
    }

    [Fact]
    public void FromDouble_FloorProperty()
    {
        // raw <= value * 1e6 < raw + 1, checked exactly with decimal on values decimal can hold.
        var rnd = new Random(505);
        for (int i = 0; i < Iterations; i++)
        {
            double v = (rnd.NextDouble() - 0.5) * Math.Pow(10, rnd.Next(-6, 9));
            long raw = Fx6.FromDouble(v).Raw;
            decimal exactScaled = (decimal)v * Fx6.Scale; // decimal conversion rounds to ~15 sig. digits
            Assert.True(raw <= exactScaled + 1 && exactScaled - 1 < raw + 1, $"v={v:R} raw={raw}");
        }
    }

    [Fact]
    public void FromDouble_IsMonotonic()
    {
        var rnd = new Random(606);
        for (int i = 0; i < Iterations; i++)
        {
            double a = (rnd.NextDouble() - 0.5) * 1e9, b = (rnd.NextDouble() - 0.5) * 1e9;
            if (a > b) (a, b) = (b, a);
            Assert.True(Fx6.FromDouble(a) <= Fx6.FromDouble(b), $"a={a:R} b={b:R}");
        }
    }

    [Fact]
    public void FromDouble_NegativeZeroAndTinyValues()
    {
        Assert.Equal(0, Fx6.FromDouble(-0.0).Raw);
        Assert.Equal(0, Fx6.FromDouble(double.Epsilon).Raw);
        Assert.Equal(-1, Fx6.FromDouble(-double.Epsilon).Raw);
        Assert.Throws<OverflowException>(() => Fx6.FromDouble(double.MaxValue)); // product -> +inf
        Assert.Throws<OverflowException>(() => Fx6.FromDouble(-double.MaxValue));
    }

    [Fact]
    public void ToDoubleFromDouble_RoundTrip_LosesAtMostOneRawUnit_Downwards()
    {
        // Documents finding F2: floor(r/1e6*1e6) can be r-1 (~1% of values with |r| < 1e12).
        // Model code doing Fx6 -> double -> Fx6 leaks value downwards; deterministic, but not conservative.
        var rnd = new Random(707);
        int drifted = 0;
        for (int i = 0; i < Iterations; i++)
        {
            long r = rnd.NextInt64(-1_000_000_000_000, 1_000_000_000_000);
            long back = Fx6.FromDouble(Fx6.FromRaw(r).ToDouble()).Raw;
            Assert.InRange(r - back, 0, 1);
            if (back != r) drifted++;
        }
        Assert.True(drifted > 0, "expected drift no longer happens: update finding F2 and this test");
        Assert.Equal(262_478_426_718, Fx6.FromDouble(Fx6.FromRaw(262_478_426_719).ToDouble()).Raw);
    }
}
