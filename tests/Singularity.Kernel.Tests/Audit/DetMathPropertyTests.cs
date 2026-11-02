using Singularity.Kernel;

namespace Singularity.Kernel.Tests.Audit;

public class DetMathPropertyTests
{
    [Fact]
    public void PowDyadic_NegativeExponent_IsExactReciprocal()
    {
        var rnd = new Random(11);
        for (int i = 0; i < 5000; i++)
        {
            double x = rnd.NextDouble() * 1000 + 1e-3;
            int k = rnd.Next(1, 64);
            Assert.Equal(1.0 / DetMath.PowDyadic(x, k), DetMath.PowDyadic(x, -k));
        }
    }

    [Fact]
    public void PowDyadic_IsMonotonicInBase()
    {
        // sqrt and multiplication are correctly rounded and monotone, so the composition is too.
        var rnd = new Random(12);
        for (int i = 0; i < 5000; i++)
        {
            double a = rnd.NextDouble() * 100, b = rnd.NextDouble() * 100;
            if (a > b) (a, b) = (b, a);
            int k = rnd.Next(0, 40);
            Assert.True(DetMath.PowDyadic(a, k) <= DetMath.PowDyadic(b, k), $"a={a:R} b={b:R} k={k}");
        }
    }

    [Fact]
    public void PowDyadic_CloseToMathPow_RandomInputs()
    {
        var rnd = new Random(13);
        for (int i = 0; i < 5000; i++)
        {
            double x = rnd.NextDouble() * 50 + 1e-6;
            int k = rnd.Next(-40, 41);
            double expected = Math.Pow(x, k / 8.0);
            double actual = DetMath.PowDyadic(x, k);
            Assert.True(Math.Abs(actual - expected) <= Math.Abs(expected) * 1e-13, $"x={x:R} k={k} got={actual:R} exp={expected:R}");
        }
    }

    [Theory]
    [InlineData(1.0, 1000)]
    [InlineData(1.0, -1000)]
    [InlineData(0.0, 0)]     // 0^0 = 1 by convention
    [InlineData(5.0, 0)]
    public void PowDyadic_TrivialCases_AreOne(double x, int k)
    {
        Assert.Equal(1.0, DetMath.PowDyadic(x, k));
    }

    [Fact]
    public void PowDyadic_RejectsEdgeInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.PowDyadic(2.0, int.MinValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.PowDyadic(double.PositiveInfinity, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.PowDyadic(-0.0 - 1e-300, 1));
        Assert.Equal(0.0, DetMath.PowDyadic(0.0, 1));
        Assert.Equal(0.0, DetMath.PowDyadic(-0.0, 8)); // -0.0 is accepted (x < 0 is false)
        Assert.Equal(double.MaxValue, DetMath.PowDyadic(double.MaxValue, 8));
        Assert.Equal(DetMath.Sqrt(double.MaxValue), DetMath.PowDyadic(double.MaxValue, 4));
    }

    [Fact]
    public void PowDyadic_LargeExponent_Overflow_Throws()
    {
        // F4, TASK-0007: a non-finite x^|e| is rejected instead of leaking infinity (or 0 after 1/x).
        Assert.Throws<OverflowException>(() => DetMath.PowDyadic(10.0, 8 * 400));
        Assert.Throws<OverflowException>(() => DetMath.PowDyadic(10.0, -8 * 400));
        Assert.Throws<OverflowException>(() => DetMath.PowDyadic(1e-310, -8)); // 1/subnormal overflows
    }

    [Fact]
    public void Sqrt_RejectsNegativeInfinityAndAcceptsNegativeZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DetMath.Sqrt(double.NegativeInfinity));
        Assert.Equal(-0.0, DetMath.Sqrt(-0.0));
    }
}
