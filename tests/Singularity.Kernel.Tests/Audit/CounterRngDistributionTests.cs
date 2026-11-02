using System.Numerics;
using Singularity.Kernel;

namespace Singularity.Kernel.Tests.Audit;

/// <summary>Sanity (not a full statistical battery) for CounterRng outputs on structured keys.</summary>
public class CounterRngDistributionTests
{
    private const int Samples = 200_000;

    [Fact]
    public void NextUnit_MeanAndVariance_AreUniform()
    {
        var rng = new CounterRng(1910);
        double sum = 0, sumSq = 0;
        for (int i = 0; i < Samples; i++)
        {
            double u = rng.NextUnit(i / 1000, 2, i % 1000, 0);
            sum += u;
            sumSq += u * u;
        }
        double mean = sum / Samples, variance = sumSq / Samples - mean * mean;
        // sigma(mean) = sqrt(1/12/N) ~ 6.5e-4; 6 sigma bound.
        Assert.InRange(mean, 0.5 - 0.004, 0.5 + 0.004);
        Assert.InRange(variance, 1.0 / 12 - 0.002, 1.0 / 12 + 0.002);
    }

    [Fact]
    public void NextUnit_ChiSquare64Buckets_OnSequentialEntities()
    {
        var rng = new CounterRng(42);
        var buckets = new int[64];
        for (int e = 0; e < Samples; e++)
            buckets[(int)(rng.NextUnit(0, 1, e, 0) * 64)]++;
        double expected = Samples / 64.0, chi2 = 0;
        foreach (int c in buckets)
            chi2 += (c - expected) * (c - expected) / expected;
        // df = 63; p = 1e-6 critical value is ~ 130.
        Assert.True(chi2 < 130, $"chi2={chi2}");
    }

    [Fact]
    public void NextU64_EveryBitIsBalanced()
    {
        var rng = new CounterRng(7);
        var ones = new int[64];
        const int n = 100_000;
        for (int i = 0; i < n; i++)
        {
            ulong v = rng.NextU64(i, 0, 0, 0); // only tick varies
            for (int b = 0; b < 64; b++)
                ones[b] += (int)((v >> b) & 1);
        }
        // sigma = sqrt(n)/2 ~ 158; 6 sigma ~ 950.
        for (int b = 0; b < 64; b++)
            Assert.True(Math.Abs(ones[b] - n / 2) < 950, $"bit {b}: {ones[b]}");
    }

    [Theory]
    [InlineData(0)] // tick
    [InlineData(1)] // system
    [InlineData(2)] // entity
    [InlineData(3)] // n
    [InlineData(4)] // seed
    public void SingleBitFlip_InAnyField_Avalanches(int field)
    {
        var rnd = new Random(31 + field);
        long totalFlipped = 0;
        int trials = 0;
        for (int i = 0; i < 2000; i++)
        {
            ulong seed = (ulong)rnd.NextInt64();
            long tick = rnd.NextInt64(0, 100_000);
            int system = rnd.Next(64), entity = rnd.Next(), n = rnd.Next(16);
            int bit = rnd.Next(field == 0 || field == 4 ? 64 : 32);
            ulong a = new CounterRng(seed).NextU64(tick, system, entity, n);
            ulong b = field switch
            {
                0 => new CounterRng(seed).NextU64(tick ^ (1L << bit), system, entity, n),
                1 => new CounterRng(seed).NextU64(tick, system ^ (1 << bit), entity, n),
                2 => new CounterRng(seed).NextU64(tick, system, entity ^ (1 << bit), n),
                3 => new CounterRng(seed).NextU64(tick, system, entity, n ^ (1 << bit)),
                _ => new CounterRng(seed ^ (1UL << bit)).NextU64(tick, system, entity, n),
            };
            int flipped = BitOperations.PopCount(a ^ b);
            Assert.True(flipped > 0, "single-bit key change produced identical output");
            totalFlipped += flipped;
            trials++;
        }
        double avg = (double)totalFlipped / trials;
        Assert.InRange(avg, 31.0, 33.0); // ideal 32, sigma(avg) ~ 0.09
    }

    [Fact]
    public void NegativeAndIntMinFields_AreDistinctFromPositive()
    {
        var rng = new CounterRng(3);
        // int fields are zero-extended; -1 must not alias any small positive id.
        Assert.NotEqual(rng.NextU64(0, -1, 0, 0), rng.NextU64(0, int.MaxValue, 0, 0));
        Assert.NotEqual(rng.NextU64(0, 0, int.MinValue, 0), rng.NextU64(0, 0, 0, 0));
        // tick is 64-bit: -1 differs from uint.MaxValue (would alias if tick were truncated to 32 bits).
        Assert.NotEqual(rng.NextU64(-1, 0, 0, 0), rng.NextU64(uint.MaxValue, 0, 0, 0));
        Assert.NotEqual(rng.NextU64(1L << 32, 0, 0, 0), rng.NextU64(0, 0, 0, 0));
    }

    [Fact]
    public void IsPure_SameKeyAcrossThreads()
    {
        var rng = new CounterRng(99);
        var expected = Enumerable.Range(0, 10_000).Select(i => rng.NextU64(i, 1, i * 7, 0)).ToArray();
        var actual = new ulong[expected.Length];
        Parallel.For(0, expected.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 },
            i => actual[i] = rng.NextU64(i, 1, i * 7, 0));
        Assert.Equal(expected, actual);
    }
}
