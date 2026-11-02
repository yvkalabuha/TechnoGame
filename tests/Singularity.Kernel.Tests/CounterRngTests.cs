using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

public class CounterRngTests
{
    // Reference vectors, cross-checked by tools/reference/kernel_vectors.py.
    // If any of these change, every recorded seed and golden hash changes too.
    public static TheoryData<ulong, long, int, int, int, ulong> Vectors => new()
    {
        { 0UL, 0, 0, 0, 0, 0x281D3919BF10EE55UL },
        { 1UL, 0, 0, 0, 0, 0xAFCDC9416BC8BB58UL },
        { 0UL, 1, 0, 0, 0, 0x6C9AEB1C21F221FCUL },
        { 0UL, 0, 1, 0, 0, 0x94CBD1B25048DBFDUL },
        { 0UL, 0, 0, 1, 0, 0x00D4E7DEF34AB173UL },
        { 0UL, 0, 0, 0, 1, 0x42A5E4A1A7E7D5D0UL },
        { 42UL, 1910, 3, 17, 2, 0x7601680BA6401039UL },
        { 42UL, 17, 3, 1910, 2, 0x6FD45CE0EA22FA5DUL },
        { ulong.MaxValue, -1, -1, -1, -1, 0x3F3F562C58F94C3EUL },
        { 20261102UL, 69400, 7, 199, 3, 0x4ED1F3C62DEA5815UL },
        { 123456789UL, 365, 1, 0, 0, 0xE07DC8379342DB10UL },
        { 123456789UL, 0, 1, 365, 0, 0x7AB1223628E862BEUL },
    };

    [Theory]
    [MemberData(nameof(Vectors))]
    public void NextU64_MatchesReferenceVectors(ulong seed, long tick, int system, int entity, int n, ulong expected)
    {
        Assert.Equal(expected, new CounterRng(seed).NextU64(tick, system, entity, n));
    }

    [Fact]
    public void Mix_IsStandardSplitMix64()
    {
        // First output of the reference SplitMix64 generator seeded with 0.
        Assert.Equal(0xE220A8397B1DCDAFUL, CounterRng.Mix(0));
    }

    [Fact]
    public void NextUnit_UsesTop53Bits()
    {
        var rng = new CounterRng(42);
        Assert.Equal(0.46095896038445994, rng.NextUnit(1910, 3, 17, 2));
        Assert.Equal(0.15669590834210434, new CounterRng(0).NextUnit(0, 0, 0, 0));
    }

    [Fact]
    public void NextUnit_StaysInHalfOpenUnitInterval()
    {
        var rng = new CounterRng(7);
        for (int i = 0; i < 100_000; i++)
        {
            double u = rng.NextUnit(i, 1, i * 31, 0);
            Assert.InRange(u, 0.0, 0.9999999999999999);
        }
    }

    [Fact]
    public void SwappingKeyFields_GivesDifferentValues()
    {
        var rng = new CounterRng(2026);
        for (int a = 0; a < 64; a++)
        {
            for (int b = 0; b < 64; b++)
            {
                if (a == b)
                    continue;
                ulong v = rng.NextU64(a, a, b, b);
                Assert.NotEqual(v, rng.NextU64(b, a, a, b)); // tick <-> entity
                Assert.NotEqual(v, rng.NextU64(a, b, a, b)); // system <-> entity
                Assert.NotEqual(rng.NextU64(a, 0, b, 0), rng.NextU64(b, 0, a, 0)); // (tick, entity) <-> (entity, tick)
                Assert.NotEqual(rng.NextU64(0, 0, a, b), rng.NextU64(0, 0, b, a)); // entity <-> n
            }
        }
    }

    [Fact]
    public void SeedIsPartOfTheKey()
    {
        Assert.NotEqual(new CounterRng(1).NextU64(5, 1, 2, 0), new CounterRng(2).NextU64(5, 1, 2, 0));
        Assert.NotEqual(new CounterRng(5).NextU64(1, 1, 2, 0), new CounterRng(1).NextU64(5, 1, 2, 0));
    }

    [Fact]
    public void NoCollisionsOnDenseGrid()
    {
        // 64^3 neighbouring keys: a weak chain would produce duplicates here.
        var rng = new CounterRng(1);
        var values = new List<ulong>(64 * 64 * 64);
        for (int t = 0; t < 64; t++)
            for (int e = 0; e < 64; e++)
                for (int n = 0; n < 64; n++)
                    values.Add(rng.NextU64(t, 1, e, n));
        values.Sort();
        for (int i = 1; i < values.Count; i++)
            Assert.NotEqual(values[i - 1], values[i]);
    }
}
