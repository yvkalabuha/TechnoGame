using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

public class DetMathTests
{
    [Theory]
    [InlineData(4.0, 2.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(2.0, 1.4142135623730951)]
    public void Sqrt_IsIeeeSqrt(double x, double expected)
    {
        Assert.Equal(expected, DetMath.Sqrt(x));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Sqrt_RejectsInvalidInput(double x)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.Sqrt(x));
    }

    [Theory]
    [InlineData(256.0, 0, 1.0)]
    [InlineData(256.0, 8, 256.0)]
    [InlineData(256.0, 4, 16.0)]
    [InlineData(256.0, 2, 4.0)]
    [InlineData(256.0, 1, 2.0)]
    [InlineData(256.0, 12, 4096.0)]   // 256^1.5
    [InlineData(256.0, 7, 128.0)]     // 256^(7/8) = 2^7
    [InlineData(256.0, -4, 0.0625)]
    [InlineData(2.0, 24, 8.0)]
    [InlineData(0.0, 3, 0.0)]
    public void PowDyadic_ExactCases(double x, int eighths, double expected)
    {
        Assert.Equal(expected, DetMath.PowDyadic(x, eighths));
    }

    [Theory]
    [InlineData(1.7, 3)]
    [InlineData(0.35, 13)]
    [InlineData(12345.0, -5)]
    public void PowDyadic_IsCloseToMathPow(double x, int eighths)
    {
        double expected = Math.Pow(x, eighths / 8.0);
        Assert.Equal(expected, DetMath.PowDyadic(x, eighths), expected * 1e-14);
    }

    [Fact]
    public void PowDyadic_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.PowDyadic(-2.0, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.PowDyadic(double.NaN, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.PowDyadic(0.0, -1));
    }
}
