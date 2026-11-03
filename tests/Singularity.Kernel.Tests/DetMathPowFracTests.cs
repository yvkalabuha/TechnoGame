using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

/// <summary>DetMath.PowFrac / PowFracFx (ADR-0012, ADR-0003 R2).</summary>
public class DetMathPowFracTests
{
    // Golden vectors: IEEE-754 bits, cross-checked by tools/reference/kernel_vectors.py (pow_frac).
    // A change here changes every result that goes through a power, so it must be deliberate.
    public static TheoryData<double, int, long> Golden => new()
    {
        { 2.0, -330, 0x3FE9981121235682L },   // LR 0.8 quantised (ADR-0012 example)
        { 2.0, -329, 0x3FE99C80E685F2B6L },
        { 2.0, 512, 0x3FF6A09E667F3BCDL },
        { 10000.0, 307, 0x402FA411A0E620C6L },  // beta = 0.3
        { 1000000.0, 717, 0x40CF09E0B49F35ACL }, // 1 - beta
        { 1234.5, 1331, 0x40C45F9AA7DA0A93L },
        { 0.35, 1664, 0x3FC73E98B84EEE94L },
        { 3.0, 1023, 0x4007F9696FB8BDFAL },
        { 7.0, -1, 0x3FEFF0728F6831F2L },
        { 0.001, 4097, 0x3D7179523CC8B3AAL },
        { 1.5, 2049, 0x400201D33033F5C2L },
        { 123456.789, -717, 0x3F31D75B6FC16CBCL },
        { 1.0000001, 1, 0x3FF000000006B5FCL },
        { 98765.4321, 3333, 0x43500BA7E480EB9DL },
    };

    [Theory]
    [MemberData(nameof(Golden))]
    public void PowFrac_MatchesGoldenBits(double x, int q10, long bits)
    {
        Assert.Equal(bits, BitConverter.DoubleToInt64Bits(DetMath.PowFrac(x, q10)));
    }

    [Fact]
    public void PowFrac_EarlyExit_EqualsFullLoop()
    {
        double[] bases = [0.001, 0.35, 0.8, 1.0, 1.0000001, 2.0, 3.0, 1234.5, 98765.4321];
        foreach (double x in bases)
        {
            for (int q = -4096; q <= 4096; q++)
            {
                if (x == 0 && q < 0)
                    continue;
                Assert.Equal(BitConverter.DoubleToInt64Bits(FullLoop(x, q)),
                    BitConverter.DoubleToInt64Bits(DetMath.PowFrac(x, q)));
            }
        }
    }

    [Fact]
    public void PowFrac_OnEighths_EqualsPowDyadic()
    {
        double[] bases = [0.0, 0.001, 0.35, 1.0, 1.7, 2.0, 256.0, 12345.0, 98765.4321];
        foreach (double x in bases)
        {
            for (int e = -64; e <= 64; e++)
            {
                if (x == 0 && e < 0)
                    continue;
                Assert.Equal(BitConverter.DoubleToInt64Bits(DetMath.PowDyadic(x, e)),
                    BitConverter.DoubleToInt64Bits(DetMath.PowFrac(x, 128 * e)));
            }
        }
    }

    [Fact]
    public void PowFrac_IsCloseToMathPow()
    {
        // Math.Pow is fine in tests: it is the yardstick, not world state.
        double[] bases = [0.001, 0.1, 0.5, 0.8, 1.5, 2.0, 10.0, 1e4, 1e6];
        foreach (double x in bases)
        {
            for (int q = -4096; q <= 4096; q += 7)
            {
                double expected = Math.Pow(x, q / 1024.0);
                double actual = DetMath.PowFrac(x, q);
                Assert.True(Math.Abs(actual - expected) <= expected * 1e-13, $"x={x:R} q10={q}: {actual:R} vs {expected:R}");
            }
        }
    }

    [Fact]
    public void PowFrac_IsMonotonicInBase()
    {
        int[] exponents = [1, 330, 717, 1023, 1024, 1331, 3333];
        foreach (int q in exponents)
        {
            double previous = DetMath.PowFrac(0.0, q);
            for (double x = 0.01; x < 1e5; x *= 1.0123)
            {
                double current = DetMath.PowFrac(x, q);
                Assert.True(previous <= current, $"q10={q} x={x:R}");
                previous = current;
            }
        }
    }

    [Fact]
    public void PowFrac_LrSearchExample_FromAdr0012()
    {
        // q10 = 330 is the closest quantisation of LR = 0.8: 2^(-330/1024) beats its neighbours.
        double at = Math.Abs(DetMath.PowFrac(2.0, -330) - 0.8);
        Assert.True(at < Math.Abs(DetMath.PowFrac(2.0, -329) - 0.8));
        Assert.True(at < Math.Abs(DetMath.PowFrac(2.0, -331) - 0.8));
    }

    [Theory]
    [InlineData(5.0, 0, 1.0)]
    [InlineData(0.0, 0, 1.0)]
    [InlineData(0.0, 5, 0.0)]
    [InlineData(1.0, 4096, 1.0)]
    [InlineData(1.0, -4096, 1.0)]
    [InlineData(4.0, 512, 2.0)]
    [InlineData(4.0, 3072, 64.0)]
    [InlineData(65536.0, 64, 2.0)]   // 2^16 to the 1/16
    public void PowFrac_SimpleCases(double x, int q10, double expected)
    {
        Assert.Equal(expected, DetMath.PowFrac(x, q10), expected * 1e-15);
    }

    [Fact]
    public void PowFrac_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.PowFrac(-1.0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.PowFrac(double.NaN, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.PowFrac(double.PositiveInfinity, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.PowFrac(0.0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.PowFrac(2.0, int.MinValue));
        Assert.Throws<OverflowException>(() => DetMath.PowFrac(10.0, 1024 * 400));
        Assert.Throws<OverflowException>(() => DetMath.PowFrac(10.0, -1024 * 400));
        Assert.Throws<OverflowException>(() => DetMath.PowFrac(1e-310, -1024));
    }

    [Theory]
    [InlineData(4_000_000L, 512, 2_000_000L)]       // 4^0.5 = 2
    [InlineData(10_000_000_000L, 307, 15_820_446L)] // 10^4 ^ 0.2998..., floor to 1e-6
    [InlineData(2_000_000L, -330, 799_812L)]        // effective LR 0.79981...
    [InlineData(0L, 717, 0L)]
    public void PowFracFx_IsFlooredPowFrac(long xRaw, int q10, long expectedRaw)
    {
        Fx6 result = DetMath.PowFracFx(Fx6.FromRaw(xRaw), q10);
        Assert.Equal(expectedRaw, result.Raw);
        Assert.Equal(Fx6.FromDouble(DetMath.PowFrac(xRaw / 1e6, q10)), result);
    }

    [Fact]
    public void PowFracFx_RejectsNegativeBase()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.PowFracFx(Fx6.FromRaw(-1), 512));
    }

    // Literal ADR-0012 algorithm without the early exit: all 10 roots, then x^int.
    private static double FullLoop(double x, int q10)
    {
        int k = Math.Abs(q10);
        double acc = 1.0;
        double root = x;
        for (int i = 1; i <= 10; i++)
        {
            root = Math.Sqrt(root);
            if ((k & (1 << (10 - i))) != 0)
                acc *= root;
        }
        double p = 1.0;
        double b = x;
        for (int n = k >> 10; n > 0; n >>= 1)
        {
            if ((n & 1) != 0)
                p *= b;
            b *= b;
        }
        acc *= p;
        return q10 >= 0 ? acc : 1.0 / acc;
    }
}
