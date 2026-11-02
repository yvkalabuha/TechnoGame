using System.Diagnostics.CodeAnalysis;

namespace Singularity.Architecture.Tests;

/// <summary>Checks that the rule checker itself catches violations, so an empty result means something.</summary>
public class DeterminismRulesTests
{
    [Fact]
    public void CleanCode_HasNoViolations()
    {
        Assert.Empty(DeterminismRules.Check([typeof(Clean)]));
    }

    [Theory]
    [InlineData(typeof(UsesPow), "System.Math.Pow")]
    [InlineData(typeof(UsesExpInHelper), "System.Math.Exp")]
    [InlineData(typeof(UsesFloatLocal), "forbidden type System.Single")]
    [InlineData(typeof(UsesFloatField), "forbidden type System.Single")]
    [InlineData(typeof(UsesFloatInLambda), "opcode conv.r4")]
    [InlineData(typeof(UsesRandom), "Random")]
    [InlineData(typeof(UsesNow), "DateTime.get_Now")]
    [InlineData(typeof(UsesMathF), "MathF")]
    [InlineData(typeof(EnumeratesDictionary), "enumerates Dictionary")]
    [InlineData(typeof(UsesParallelForEach), "Parallel.ForEach")]
    public void Violation_IsDetected(Type sample, string expected)
    {
        // Lambdas compile into nested types, so scan those too.
        Type[] types = [sample, .. sample.GetNestedTypes(IlScanner.AllDeclared)];
        List<string> violations = DeterminismRules.Check(types);
        Assert.Contains(violations, v => v.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }
}

[SuppressMessage("Performance", "CA1822", Justification = "IL samples for the rule checker.")]
internal sealed class Clean
{
    public double Run(double x, long a, long b) =>
        Math.Sqrt(x) + Math.Floor(x) + Math.Abs(x) + Math.Min(a, b) + Math.Max(a, b) + (double)Math.BigMul(a, b);
}

internal sealed class UsesPow
{
    public double Run(double x) => Math.Pow(x, 1.5);
}

internal sealed class UsesExpInHelper
{
    public double Run(double x) => Exp(x);

    private static double Exp(double x) => System.Math.Exp(x);
}

internal sealed class UsesFloatLocal
{
    public double Run(double x)
    {
        float f = (float)x;
        f += 1;
        return f;
    }
}

internal sealed class UsesFloatField
{
    private float _value;

    public void Set(float value) => _value = value;

    public float Get() => _value;
}

internal sealed class UsesFloatInLambda
{
    public Func<double, double> Make() => x => (float)x;
}

internal sealed class UsesRandom
{
    public int Run() => new Random(1).Next();
}

internal sealed class UsesNow
{
    public long Run() => DateTime.Now.Ticks;
}

internal sealed class UsesMathF
{
    public double Run(double x) => MathF.Sqrt((float)x);
}

internal sealed class EnumeratesDictionary
{
    public int Run(Dictionary<int, int> map)
    {
        int sum = 0;
        foreach (var pair in map)
            sum += pair.Value;
        return sum;
    }
}

internal sealed class UsesParallelForEach
{
    public void Run(List<int> items) => Parallel.ForEach(items, _ => { });
}
