using System.Numerics;
using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

/// <summary>K0 primitives (ADR-0027 p. 5): DaySlice, Apportion, Rate, DayRate.</summary>
public class DaySlicingTests
{
    private const long S = Fx6.Scale;
    private static readonly int[] Lengths = [12, 365, 366];

    [Theory]
    [InlineData(12)]
    [InlineData(365)]
    [InlineData(366)]
    public void DaySlice_SlicesSumToTheAnnualAmountExactly(int y)
    {
        var rng = new Random(11);
        long[] samples = [0, 1, -1, 7, y, y - 1, long.MaxValue, long.MinValue, 10_000_000_000_000_000];
        for (int i = 0; i < 300; i++)
            samples = [.. samples, rng.NextInt64(long.MinValue, long.MaxValue), rng.NextInt64(-1_000_000, 1_000_000)];
        foreach (long f in samples)
        {
            BigInteger sum = 0;
            for (int k = 1; k <= y; k++)
            {
                long slice = DetMath.DaySlice(Fx6.FromRaw(f), k, y).Raw;
                // Each slice is within one raw of the exact share.
                Assert.True(BigInteger.Abs(slice * (BigInteger)y - f) <= y, $"F={f} k={k}");
                sum += slice;
            }

            Assert.Equal((BigInteger)f, sum);
        }
    }

    [Fact]
    public void DaySlice_KnownValues()
    {
        // 10 over 3 days: floor(10/3)=3, floor(20/3)-3=3, 10-6=4.
        Assert.Equal([3L, 3L, 4L], [.. Enumerable.Range(1, 3).Select(k => DetMath.DaySlice(Fx6.FromRaw(10), k, 3).Raw)]);
        // Negative amounts floor too: -10 over 3 days: -4, -3, -3.
        Assert.Equal([-4L, -3L, -3L], [.. Enumerable.Range(1, 3).Select(k => DetMath.DaySlice(Fx6.FromRaw(-10), k, 3).Raw)]);
    }

    [Fact]
    public void DaySlice_RejectsBadArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.DaySlice(Fx6.One, 0, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.DaySlice(Fx6.One, 13, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.DaySlice(Fx6.One, 1, 0));
    }

    [Fact]
    public void Apportion_PartsSumToTheWholeForAnySign()
    {
        var rng = new Random(5);
        for (int i = 0; i < 2_000; i++)
        {
            int n = rng.Next(1, 40);
            long[] w = new long[n];
            for (int j = 0; j < n; j++)
                w[j] = rng.Next(4) == 0 ? 0 : rng.NextInt64(0, 1_000_000_000_000);
            long f = rng.Next(3) == 0 ? long.MinValue : rng.NextInt64(long.MinValue, long.MaxValue);
            var parts = new Fx6[n];
            DetMath.Apportion(Fx6.FromRaw(f), w, parts);

            BigInteger sum = 0, total = 0;
            foreach (long x in w) total += x;
            for (int j = 0; j < n; j++)
            {
                sum += parts[j].Raw;
                if (w[j] == 0)
                    Assert.Equal(0, parts[j].Raw);
                // Each part is within one raw of the exact share.
                if (total != 0)
                    Assert.True(BigInteger.Abs(parts[j].Raw * total - f * (BigInteger)w[j]) < total, $"i={i} j={j}");
            }

            if (total != 0)
                Assert.Equal((BigInteger)f, sum);
        }
    }

    [Fact]
    public void Apportion_KnownValuesAndRemainderOrder()
    {
        var parts = new Fx6[3];
        DetMath.Apportion(Fx6.FromRaw(10), [1, 1, 1], parts);
        Assert.Equal([3L, 3L, 4L], parts.Select(p => p.Raw));
        DetMath.Apportion(Fx6.FromRaw(-10), [1, 1, 1], parts);
        Assert.Equal([-4L, -3L, -3L], parts.Select(p => p.Raw));
        DetMath.Apportion(Fx6.FromRaw(100), [0, 5, 0], parts);
        Assert.Equal([0L, 100L, 0L], parts.Select(p => p.Raw));
    }

    [Fact]
    public void Apportion_ZeroWeightsLeaveEverythingWithTheSource()
    {
        var parts = new Fx6[] { Fx6.FromRaw(9), Fx6.FromRaw(9) };
        DetMath.Apportion(Fx6.FromRaw(77), [0, 0], parts);
        Assert.All(parts, p => Assert.Equal(0, p.Raw));
        DetMath.Apportion(Fx6.FromRaw(77), [], []);
    }

    [Fact]
    public void Apportion_RejectsBadInput()
    {
        Assert.Throws<ArgumentException>(() => DetMath.Apportion(Fx6.One, [1, 2], new Fx6[1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.Apportion(Fx6.One, [1, -1], new Fx6[2]));
        Assert.Throws<OverflowException>(() => DetMath.Apportion(Fx6.One, [long.MaxValue, 1], new Fx6[2]));
    }

    [Fact]
    public void Rate_FromFx6AndMulFloor()
    {
        Assert.Equal(Rate.One, Rate.FromFx6(Fx6.One).Raw);
        Assert.Equal(Rate.One / 2, Rate.FromFx6(Fx6.FromRaw(S / 2)).Raw);
        Assert.Equal(-Rate.One / 2, Rate.FromFx6(Fx6.FromRaw(-S / 2)).Raw);
        Rate half = Rate.FromFx6(Fx6.FromRaw(S / 2));
        Assert.Equal(5, half.MulFloor(Fx6.FromRaw(11)).Raw);   // floor(5.5)
        Assert.Equal(-6, half.MulFloor(Fx6.FromRaw(-11)).Raw); // floor(-5.5)
        Assert.Equal(0, Rate.Zero.MulFloor(Fx6.FromRaw(123)).Raw);
    }

    [Fact]
    public void Rate_MulFloorIsTheExactFloor()
    {
        var rng = new Random(3);
        for (int i = 0; i < 5_000; i++)
        {
            long r = rng.NextInt64(-Rate.One, Rate.One), x = rng.NextInt64(long.MinValue / 2, long.MaxValue / 2);
            BigInteger n = (BigInteger)x * r, d = BigInteger.One << 53;
            BigInteger q = BigInteger.DivRem(n, d, out BigInteger rem);
            if (rem < 0) q -= 1;
            Assert.Equal((long)q, Rate.FromRaw(r).MulFloor(Fx6.FromRaw(x)).Raw);
        }
    }

    [Fact]
    public void DayRate_Monthly_MatchesTheLeakFormulaOfA2()
    {
        // 1 - 0.9^(1/12) in double is a test oracle only; Kernel code does not use it.
        double expected = 1.0 - Math.Pow(0.9, 1.0 / 12.0);
        double actual = (double)DetMath.DayRate(Fx6.FromRaw(S / 10), 12).Raw / Rate.One;
        Assert.InRange(actual - expected, -1e-14, 1e-14);
    }

    [Theory]
    [InlineData(0L, 365, 0L)]
    [InlineData(1_000_000L, 365, 9007199254740992L)]
    [InlineData(500_000L, 1, 4503599627370496L)]
    // Yearly rates of the ADR-0003 inventory (δ_H 3.54 %, δ_X 4.70 %, δ_M 5.84 %, δ_E 26.2 %) and growth 2 %, 5 %.
    [InlineData(35400L, 365, 889368187814L)]
    [InlineData(35400L, 366, 886938339731L)]
    [InlineData(47000L, 365, 1187894135105L)]
    [InlineData(47000L, 366, 1184648736159L)]
    [InlineData(58400L, 365, 1484824780266L)]
    [InlineData(58400L, 366, 1480768215451L)]
    [InlineData(262000L, 365, 7494114379910L)]
    [InlineData(262000L, 366, 7473647154693L)]
    [InlineData(-20000L, 365, -488687803922L)]
    [InlineData(-20000L, 366, -487352555221L)]
    [InlineData(-50000L, 365, -1204087954837L)]
    [InlineData(-50000L, 366, -1200797877752L)]
    public void DayRate_GoldenVectors(long d, int y, long expectedRaw) =>
        Assert.Equal(expectedRaw, DetMath.DayRate(Fx6.FromRaw(d), y).Raw);

    [Theory]
    [InlineData(1_000, 12)]
    [InlineData(100_000, 365)]
    [InlineData(500_000, 366)]
    [InlineData(-20_000, 365)]
    [InlineData(-50_000, 366)]
    [InlineData(-50_000, 12)]
    public void DayRate_IsTheBoundaryOfTheDefiningInequality(long d, int y)
    {
        long r = DetMath.DayRate(Fx6.FromRaw(d), y).Raw;
        BigInteger Left(long rr) => BigInteger.Pow(Rate.One - rr, y) * S;
        BigInteger right = (S - d) * (BigInteger.One << (53 * y));
        if (d > 0)
        {
            Assert.True(Left(r) >= right);      // never decays faster than asked
            Assert.True(Left(r + 1) < right);   // and r is the largest such
        }
        else
        {
            Assert.True(Left(r) <= right);      // never grows faster than asked
            Assert.True(Left(r - 1) > right);
            Assert.True(r < 0);
        }
    }

    public static IEnumerable<object[]> StockCases()
    {
        long[] deltas = [1_000, 100_000, 500_000, -20_000, -50_000];
        // 7_480 and 18_426 were the worst cases of the old floor-per-day step (1.02·Y, found by scan).
        long[] stocks = [1_000, 7_480, 18_426, 123_456, 10_000_000_000, 999_999_999_999_999];
        foreach (long d in deltas)
            foreach (int y in Lengths)
                foreach (long x in stocks)
                    yield return [d, y, x];
    }

    [Theory]
    [MemberData(nameof(StockCases))]
    public void DayRate_AfterYSteps_IsCloseToTheAnnualChange_AndNeverFasterForDecay(long d, int y, long x0)
    {
        Rate r = DetMath.DayRate(Fx6.FromRaw(d), y);
        Fx6 x = Fx6.FromRaw(x0);
        long rem = 0;
        for (int i = 0; i < y; i++)
        {
            x = DetMath.ApplyDayRate(x, r, ref rem);
            Assert.InRange(rem, -Rate.One + 1, Rate.One - 1);
        }

        // Compare with the exact rational target x0·(1−δ), not its floor.
        BigInteger scaled = (BigInteger)x.Raw * S - (BigInteger)x0 * (S - d); // error · 10^6
        BigInteger error = BigInteger.Divide(scaled, S);
        // Strict ADR-0027 bound: Y raw plus the rate error (1 Q53 unit a day on at most 2·x0).
        BigInteger tolerance = y + 4 * (BigInteger)y * x0 / Rate.One + 1;
        Assert.True(BigInteger.Abs(error) <= tolerance, $"d={d} y={y} x0={x0} error={error}");
        if (d > 0)
            Assert.True(scaled >= 0, $"decay ran faster than asked: {scaled}/10^6");
        else
            Assert.True(scaled <= 0, $"growth ran faster than asked: {scaled}/10^6");
    }

    [Fact]
    public void ApplyDayRate_CarriesTheRemainder_SoATinyStockStillMoves()
    {
        // 5 % growth on 1000 raw: 0.13 raw a day, so a floor-per-day step never moves it; the carried one does.
        Rate r = DetMath.DayRate(Fx6.FromRaw(-50_000), 365);
        Fx6 x = Fx6.FromRaw(1_000);
        long rem = 0;
        for (int i = 0; i < 365; i++)
            x = DetMath.ApplyDayRate(x, r, ref rem);
        Assert.InRange(x.Raw, 1_049, 1_050);
    }

    [Fact]
    public void DayRate_TinyStockDoesNotChange()
    {
        Rate r = DetMath.DayRate(Fx6.FromRaw(100_000), 365);
        long limit = Rate.One / r.Raw; // x < 1/r gives a zero change
        Assert.Equal(0, r.MulFloor(Fx6.FromRaw(limit - 1)).Raw);
        Assert.Equal(1, r.MulFloor(Fx6.FromRaw(limit + 1)).Raw);
    }

    [Theory]
    [InlineData(-1_000_001L, 366)]
    [InlineData(-128_746_338L, 12)] // growth ×130: the Y-raw bound of ApplyDayRate no longer holds
    [InlineData(-9_000_000_000_000_000_000L, 366)]
    [InlineData(long.MinValue, 366)]
    public void DayRate_RefusesAnnualGrowthAbove100Percent(long d, int y) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.DayRate(Fx6.FromRaw(d), y));

    [Theory]
    [InlineData(-1_000_000L, 12)]
    [InlineData(-1_000_000L, 366)]
    [InlineData(-1_000_000L, 1)]
    public void DayRate_AcceptsExactly100PercentGrowth(long d, int y)
    {
        long r = DetMath.DayRate(Fx6.FromRaw(d), y).Raw;
        Assert.InRange(r, -Rate.One, -1);
        Assert.True(BigInteger.Pow(Rate.One - r, y) * S <= (S - (BigInteger)d) * (BigInteger.One << (53 * y)));
    }

    [Fact]
    public void ApplyDayRate_RandomRatesAndStocks_StaySlowAndWithinTheBound()
    {
        var rng = new Random(2026);
        for (int n = 0; n < 150; n++)
        {
            long d = rng.NextInt64(-1_000_000, 1_000_000);
            int y = Lengths[rng.Next(3)];
            long x0 = rng.Next(3) == 0 ? rng.NextInt64(1, 100_000) : rng.NextInt64(1, 1_000_000_000_000_000);
            Rate r = DetMath.DayRate(Fx6.FromRaw(d), y);
            Fx6 x = Fx6.FromRaw(x0);
            long rem = 0;
            for (int i = 0; i < y; i++)
                x = DetMath.ApplyDayRate(x, r, ref rem);
            BigInteger scaled = (BigInteger)x.Raw * S - (BigInteger)x0 * (S - d);
            Assert.True(d >= 0 ? scaled >= 0 : scaled <= 0, $"d={d} y={y} x0={x0}");
            Assert.True(BigInteger.Abs(scaled) <= (BigInteger)S * (y + 4 * (BigInteger)y * x0 / Rate.One + 1), $"d={d} y={y} x0={x0}");
        }
    }

    [Fact]
    public void ApplyDayRate_DecayRoundsUpAtTheExactTarget()
    {
        // Codex round 3 #1: 1011 raw, 26.2 % a year, Y = 365; exact target 746.118 raw, so 746 would be faster.
        Rate r = DetMath.DayRate(Fx6.FromRaw(262_000), 365);
        Fx6 x = Fx6.FromRaw(1_011);
        long rem = 0;
        for (int i = 0; i < 365; i++)
            x = DetMath.ApplyDayRate(x, r, ref rem);
        Assert.Equal(747, x.Raw);
        Assert.True(rem <= 0);
    }

    [Fact]
    public void ApplyDayRate_OverflowLeavesTheRemainderUntouched()
    {
        // Codex round 3 #3: a failed step must not leave a remainder paired with the old stock.
        long rem = 12345;
        Assert.Throws<OverflowException>(() =>
            DetMath.ApplyDayRate(Fx6.FromRaw(long.MaxValue), Rate.FromRaw(-Rate.One / 2), ref rem));
        Assert.Equal(12345, rem);
    }

    [Fact]
    public void DayRate_RejectsBadArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.DayRate(Fx6.One, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.DayRate(Fx6.One, 367));
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.DayRate(Fx6.FromRaw(S + 1), 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.DayRate(Fx6.FromRaw(-10 * S), 1));
    }
}
