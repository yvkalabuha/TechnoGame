using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

/// <summary>Integer draws from NextU64 (ADR-0003 R2, slice spec §9.2).</summary>
public class CounterRngDrawTests
{
    private const ulong Top = ulong.MaxValue; // r = 2^53 - 1, U just below 1

    [Fact]
    public void Chance_ThresholdIsFloorOfP_Times2Pow53()
    {
        // p = 0.5: threshold = 2^52 exactly, so r = 2^52 - 1 passes and r = 2^52 does not.
        Fx6 half = Fx6.FromRaw(500_000);
        Assert.True(CounterRng.Chance(((1UL << 52) - 1) << 11, half));
        Assert.False(CounterRng.Chance((1UL << 52) << 11, half));
        // The low 11 bits are ignored.
        Assert.True(CounterRng.Chance((((1UL << 52) - 1) << 11) | 0x7FF, half));

        // p = 1e-6: threshold = floor(2^53 / 10^6) = 9007199254.
        Fx6 tiny = Fx6.FromRaw(1);
        Assert.True(CounterRng.Chance(9_007_199_253UL << 11, tiny));
        Assert.False(CounterRng.Chance(9_007_199_254UL << 11, tiny));
    }

    [Fact]
    public void Chance_EdgeProbabilities()
    {
        Assert.False(CounterRng.Chance(0, Fx6.Zero));
        Assert.False(CounterRng.Chance(0, Fx6.FromRaw(-5)));
        Assert.True(CounterRng.Chance(Top, Fx6.One));
        Assert.True(CounterRng.Chance(Top, Fx6.FromInt(3)));
        Assert.True(CounterRng.Chance(Top, Fx6.FromRaw(long.MaxValue)));
        Assert.False(CounterRng.Chance(Top, Fx6.FromRaw(999_999)));
    }

    [Fact]
    public void Chance_MatchesNextUnitComparison()
    {
        // r < floor(p*2^53) agrees with U < p (p/1e6 rounded in double) on sampled draws.
        var rng = new CounterRng(20261103);
        long[] ps = [1, 123_457, 250_000, 500_000, 999_999];
        for (int i = 0; i < 20_000; i++)
        {
            ulong u = rng.NextU64(i, 2, i, 0);
            double unit = rng.NextUnit(i, 2, i, 0);
            foreach (long p in ps)
                Assert.Equal(unit < p / 1e6, CounterRng.Chance(u, Fx6.FromRaw(p)));
        }
    }

    [Theory]
    [InlineData(6L, 18L)]
    [InlineData(500_000L, 1_500_000L)]
    [InlineData(-3L, 3L)]
    [InlineData(7L, 7L)]
    [InlineData(long.MinValue, long.MaxValue)]
    public void UniformInt_StaysInRange_AndHitsBothEnds(long lo, long hi)
    {
        Assert.Equal(lo, CounterRng.UniformInt(0, lo, hi));
        // U has 53 bits: wider spans cannot reach every value, including hi.
        if ((Int128)hi - lo < (Int128)1 << 53)
            Assert.Equal(hi, CounterRng.UniformInt(Top, lo, hi));
        var rng = new CounterRng(5);
        for (int i = 0; i < 5_000; i++)
        {
            long v = CounterRng.UniformInt(rng.NextU64(0, 1, i, 0), lo, hi);
            Assert.InRange(v, lo, hi);
        }
    }

    [Fact]
    public void UniformInt_IsFloorOfSpanTimesU()
    {
        // Slice spec §9.2 formula for j: 500000 + ((r * 1000001) >> 53).
        var rng = new CounterRng(77);
        for (int i = 0; i < 1_000; i++)
        {
            ulong u = rng.NextU64(1, 3, i, 0);
            ulong r = u >> 11;
            long expected = 500_000 + (long)(((UInt128)r * 1_000_001) >> 53);
            Assert.Equal(expected, CounterRng.UniformInt(u, 500_000, 1_500_000));
            Assert.Equal((int)((r * 3) >> 53), CounterRng.Index(u, 3));
        }
    }

    [Fact]
    public void Index_IsRoughlyUniform()
    {
        var rng = new CounterRng(9);
        int[] counts = new int[3];
        for (int i = 0; i < 30_000; i++)
            counts[CounterRng.Index(rng.NextU64(0, 4, i, 0), 3)]++;
        foreach (int c in counts)
            Assert.InRange(c, 9_500, 10_500);
    }

    [Fact]
    public void Draws_RejectEmptyRanges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CounterRng.UniformInt(0, 5, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => CounterRng.Index(0, 0));
    }
}
