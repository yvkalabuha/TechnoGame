using System.Numerics;
using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

/// <summary>SmoothStep01 and MulCeil (contract v2.4 §4.0 p. 2, G4-3, G4-4): one rounding in Int128.</summary>
public class OneRoundingHelpersTests
{
    private const long S = Fx6.Scale;

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(S, S)]
    [InlineData(S / 2, S / 2)]
    [InlineData(1L, 0L)]
    [InlineData(250_000L, 156_250L)]
    public void SmoothStep01_KnownValues(long raw, long expected) =>
        Assert.Equal(expected, DetMath.SmoothStep01(Fx6.FromRaw(raw)).Raw);

    [Fact]
    public void SmoothStep01_IsExactFloorOfTheRationalFormula()
    {
        var rng = new Random(7);
        for (int i = 0; i < 20_000; i++)
        {
            long x = rng.NextInt64(0, S + 1);
            BigInteger bx = x;
            BigInteger expected = bx * bx * (3 * S - 2 * bx) / ((BigInteger)S * S);
            Assert.Equal((long)expected, DetMath.SmoothStep01(Fx6.FromRaw(x)).Raw);
        }
    }

    [Fact]
    public void SmoothStep01_DiffersFromTwoChainedFloors_AtSomePoint()
    {
        // The reason for the helper: (x·x)·(3 − 2x) with two Fx6 floors is not the single-rounding value.
        bool differs = false;
        for (long x = 1; x < S && !differs; x += 997)
        {
            Fx6 fx = Fx6.FromRaw(x);
            Fx6 twice = fx * fx * (Fx6.FromInt(3) - Fx6.FromInt(2) * fx);
            differs = twice != DetMath.SmoothStep01(fx);
        }
        Assert.True(differs);
    }

    [Fact]
    public void SmoothStep01_IsMonotoneAndStaysInUnitInterval()
    {
        long prev = 0;
        for (long x = 0; x <= S; x += 1_013)
        {
            long y = DetMath.SmoothStep01(Fx6.FromRaw(x)).Raw;
            Assert.InRange(y, prev, S);
            prev = y;
        }
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(S + 1)]
    public void SmoothStep01_RejectsOutOfRange(long raw) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.SmoothStep01(Fx6.FromRaw(raw)));

    [Fact]
    public void MulCeil_RoundsUp_ExactWhenDivisible()
    {
        Assert.Equal(1L, DetMath.MulCeil(Fx6.FromRaw(1), Fx6.FromRaw(1)).Raw); // 1e-12 → one raw unit up
        Assert.Equal(0L, DetMath.MulCeil(Fx6.Zero, Fx6.FromRaw(123)).Raw);
        Assert.Equal(3_000_000L, DetMath.MulCeil(Fx6.FromInt(3), Fx6.One).Raw);
        Assert.Equal(184L, DetMath.MulCeil(Fx6.FromRaw(100_000), Fx6.FromRaw(1_838)).Raw); // 183.8 → 184
        Assert.Equal(183L, (Fx6.FromRaw(100_000) * Fx6.FromRaw(1_838)).Raw);               // floor for contrast
    }

    [Fact]
    public void MulCeil_MatchesBigIntegerCeiling()
    {
        var rng = new Random(11);
        for (int i = 0; i < 20_000; i++)
        {
            long a = rng.NextInt64(0, 50 * S), b = rng.NextInt64(0, 50 * S);
            BigInteger n = (BigInteger)a * b;
            BigInteger expected = (n + S - 1) / S;
            Assert.Equal((long)expected, DetMath.MulCeil(Fx6.FromRaw(a), Fx6.FromRaw(b)).Raw);
        }
    }

    [Fact]
    public void MulCeil_RejectsNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.MulCeil(Fx6.FromRaw(-1), Fx6.One));
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.MulCeil(Fx6.One, Fx6.FromRaw(-1)));
    }

    [Fact]
    public void MulCeil_OverflowThrows() =>
        Assert.Throws<OverflowException>(() => DetMath.MulCeil(Fx6.FromRaw(long.MaxValue), Fx6.FromInt(2)));
}
