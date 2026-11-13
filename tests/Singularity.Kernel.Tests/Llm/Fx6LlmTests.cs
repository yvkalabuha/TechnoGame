using System;
using Singularity.Kernel;

namespace Singularity.Kernel.Tests.Llm;

public class Fx6LlmTests
{
    [Theory]
    [InlineData(0.0, 0L)]
    [InlineData(1.0, 1_000_000L)]
    [InlineData(1.5, 1_500_000L)]
    [InlineData(0.000001, 1L)]
    [InlineData(0.0000019, 1L)]
    [InlineData(-1.5, -1_500_000L)]
    [InlineData(-0.000001, -1L)]
    [InlineData(-0.0000019, -2L)]
    [InlineData(-2.25, -2_250_000L)]
    public void FromDouble_Floors(double value, long expectedRaw)
    {
        Fx6 fx = Fx6.FromDouble(value);
        Assert.Equal(expectedRaw, fx.Raw);
    }

    [Fact]
    public void FromDouble_RejectsNaN()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Fx6.FromDouble(double.NaN));
    }

    [Fact]
    public void FromDouble_RejectsPositiveInfinity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Fx6.FromDouble(double.PositiveInfinity));
    }

    [Fact]
    public void FromDouble_RejectsNegativeInfinity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Fx6.FromDouble(double.NegativeInfinity));
    }

    [Fact]
    public void Addition_AddsRawValues()
    {
        Fx6 a = Fx6.FromRaw(1_500_000L);
        Fx6 b = Fx6.FromRaw(2_250_000L);
        Assert.Equal(3_750_000L, (a + b).Raw);
    }

    [Fact]
    public void Addition_OverflowThrows()
    {
        Fx6 a = Fx6.FromRaw(long.MaxValue);
        Fx6 b = Fx6.FromRaw(1L);
        Assert.Throws<OverflowException>(() => { _ = a + b; });
    }

    [Fact]
    public void Subtraction_SubtractsRawValues()
    {
        Fx6 a = Fx6.FromRaw(2_250_000L);
        Fx6 b = Fx6.FromRaw(1_500_000L);
        Assert.Equal(750_000L, (a - b).Raw);
    }

    [Fact]
    public void Subtraction_OverflowThrows()
    {
        Fx6 a = Fx6.FromRaw(long.MinValue);
        Fx6 b = Fx6.FromRaw(1L);
        Assert.Throws<OverflowException>(() => { _ = a - b; });
    }

    [Fact]
    public void Multiplication_MultipliesAndFloors()
    {
        // 1.5 * 2.0 = 3.0
        Fx6 a = Fx6.FromRaw(1_500_000L);
        Fx6 b = Fx6.FromRaw(2_000_000L);
        Assert.Equal(3_000_000L, (a * b).Raw);
    }

    [Fact]
    public void Multiplication_FloorsNegativeResult()
    {
        // -1.5 * 1.0 = -1.5 exactly
        Fx6 a = Fx6.FromRaw(-1_500_000L);
        Fx6 b = Fx6.FromRaw(1_000_000L);
        Assert.Equal(-1_500_000L, (a * b).Raw);
    }

    [Fact]
    public void Multiplication_FloorsTowardsNegativeInfinity()
    {
        // raw 1 * raw 1 = 1; 1 / 1_000_000 floors to 0
        Fx6 a = Fx6.FromRaw(1L);
        Fx6 b = Fx6.FromRaw(1L);
        Assert.Equal(0L, (a * b).Raw);

        // raw -1 * raw 1 = -1; -1 / 1_000_000 floors to -1
        Fx6 c = Fx6.FromRaw(-1L);
        Assert.Equal(-1L, (c * b).Raw);
    }

    [Fact]
    public void Division_DividesAndFloors()
    {
        // 3.0 / 2.0 = 1.5
        Fx6 a = Fx6.FromRaw(3_000_000L);
        Fx6 b = Fx6.FromRaw(2_000_000L);
        Assert.Equal(1_500_000L, (a / b).Raw);
    }

    [Fact]
    public void Division_FloorsNegativeResult()
    {
        // -3.0 / 2.0 = -1.5 exactly
        Fx6 a = Fx6.FromRaw(-3_000_000L);
        Fx6 b = Fx6.FromRaw(2_000_000L);
        Assert.Equal(-1_500_000L, (a / b).Raw);
    }

    [Fact]
    public void Division_FloorsTowardsNegativeInfinity()
    {
        // 1 raw / 3 raw: (1 * 1_000_000) / 3 = 333333.33.. -> 333333
        Fx6 a = Fx6.FromRaw(1L);
        Fx6 b = Fx6.FromRaw(3L);
        Assert.Equal(333_333L, (a / b).Raw);

        // -1 raw / 3 raw: (-1 * 1_000_000) / 3 = -333333.33.. -> -333334
        Fx6 c = Fx6.FromRaw(-1L);
        Assert.Equal(-333_334L, (c / b).Raw);
    }

    [Fact]
    public void Division_ByZeroThrows()
    {
        Fx6 a = Fx6.FromRaw(1_000_000L);
        Fx6 zero = Fx6.Zero;
        Assert.Throws<DivideByZeroException>(() => { _ = a / zero; });
    }

    [Fact]
    public void Comparison_EqualRawValues()
    {
        Fx6 a = Fx6.FromRaw(1_000_000L);
        Fx6 b = Fx6.FromRaw(1_000_000L);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a <= b);
        Assert.True(a >= b);
        Assert.False(a < b);
        Assert.False(a > b);
    }

    [Fact]
    public void Comparison_AdjacentRawValues()
    {
        Fx6 lower = Fx6.FromRaw(1_000_000L);
        Fx6 higher = Fx6.FromRaw(1_000_001L);
        Assert.False(lower == higher);
        Assert.True(lower != higher);
        Assert.True(lower < higher);
        Assert.True(lower <= higher);
        Assert.False(lower > higher);
        Assert.False(lower >= higher);
        Assert.True(higher > lower);
        Assert.True(higher >= lower);
        Assert.False(higher < lower);
        Assert.False(higher <= lower);
    }

    [Theory]
    [InlineData(0L, "0.000000")]
    [InlineData(1_500_000L, "1.500000")]
    [InlineData(-1_500_000L, "-1.500000")]
    [InlineData(1L, "0.000001")]
    [InlineData(-1L, "-0.000001")]
    public void ToString_FormatsWithSixFractionalDigits(long raw, string expected)
    {
        Fx6 fx = Fx6.FromRaw(raw);
        Assert.Equal(expected, fx.ToString());
    }
}
