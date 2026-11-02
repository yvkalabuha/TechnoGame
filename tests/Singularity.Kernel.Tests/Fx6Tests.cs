using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

public class Fx6Tests
{
    [Theory]
    [InlineData(1.5, 1_500_000)]
    [InlineData(-1.5, -1_500_000)]
    [InlineData(0.0000015, 1)]        // floor, not round
    [InlineData(-0.0000005, -1)]      // floor goes towards negative infinity
    [InlineData(0.0, 0)]
    [InlineData(123456.789, 123_456_789_000)]
    public void FromDouble_Floors(double value, long expectedRaw)
    {
        Assert.Equal(expectedRaw, Fx6.FromDouble(value).Raw);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FromDouble_RejectsNonFinite(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Fx6.FromDouble(value));
    }

    [Theory]
    [InlineData(1e13)]   // 1e19 raw > long.MaxValue
    [InlineData(-1e13)]
    [InlineData(9_223_372_036_854.775808)] // exactly 2^63 raw after rounding
    public void FromDouble_RejectsOutOfRange(double value)
    {
        Assert.Throws<OverflowException>(() => Fx6.FromDouble(value));
    }

    [Fact]
    public void AddSub_AreCheckedOnOverflow()
    {
        var max = Fx6.FromRaw(long.MaxValue);
        var min = Fx6.FromRaw(long.MinValue);
        Assert.Throws<OverflowException>(() => max + Fx6.FromRaw(1));
        Assert.Throws<OverflowException>(() => min - Fx6.FromRaw(1));
        Assert.Throws<OverflowException>(() => -min);
        Assert.Throws<OverflowException>(() => Fx6.FromInt(long.MaxValue / 10));
    }

    [Fact]
    public void Mul_UsesWideIntermediateAndFloors()
    {
        // 3e9 * 2e3: the raw product (3e15 * 2e9 = 6e24) does not fit into long, the result does.
        Assert.Equal(Fx6.FromInt(6_000_000_000_000), Fx6.FromInt(3_000_000_000) * Fx6.FromInt(2_000));
        Assert.Equal(Fx6.FromRaw(2_250_000), Fx6.FromRaw(1_500_000) * Fx6.FromRaw(1_500_000));
        // 0.000001 * 0.5 = 0.0000005 -> floor 0; negative -> floor -0.000001.
        Assert.Equal(0, (Fx6.FromRaw(1) * Fx6.FromRaw(500_000)).Raw);
        Assert.Equal(-1, (Fx6.FromRaw(-1) * Fx6.FromRaw(500_000)).Raw);
        Assert.Throws<OverflowException>(() => Fx6.FromRaw(long.MaxValue) * Fx6.FromInt(2));
    }

    [Fact]
    public void Div_FloorsAndThrowsOnZero()
    {
        Assert.Equal(333_333, (Fx6.One / Fx6.FromInt(3)).Raw);
        Assert.Equal(-333_334, (-Fx6.One / Fx6.FromInt(3)).Raw);
        Assert.Equal(-333_334, (Fx6.One / Fx6.FromInt(-3)).Raw);
        Assert.Equal(333_333, (-Fx6.One / Fx6.FromInt(-3)).Raw);
        Assert.Throws<DivideByZeroException>(() => Fx6.One / Fx6.Zero);
        Assert.Throws<OverflowException>(() => Fx6.FromRaw(long.MaxValue) / Fx6.FromRaw(1));
    }

    [Fact]
    public void ToString_IsCultureIndependent()
    {
        Assert.Equal("-1.500000", Fx6.FromDouble(-1.5).ToString());
        Assert.Equal("0.000001", Fx6.FromRaw(1).ToString());
        Assert.Equal("-9223372036854.775808", Fx6.FromRaw(long.MinValue).ToString());
    }

    [Fact]
    public void ToDouble_RoundTripsExactValues()
    {
        Assert.Equal(-1.5, Fx6.FromDouble(-1.5).ToDouble());
        Assert.Equal(1e-6, Fx6.FromRaw(1).ToDouble());
    }
}
