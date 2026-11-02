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
    [InlineData(typeof(UsesDoublePow), "System.Double.Pow")]
    [InlineData(typeof(UsesDoubleExp), "System.Double.Exp")]
    [InlineData(typeof(UsesDoubleLog), "System.Double.Log")]
    [InlineData(typeof(UsesDoubleSin), "System.Double.Sin")]
    [InlineData(typeof(UsesDoubleCbrt), "System.Double.Cbrt")]
    [InlineData(typeof(UsesDoubleRound), "System.Double.Round")]
    [InlineData(typeof(UsesHalf), "forbidden type System.Half")]
    [InlineData(typeof(EnumeratesDictionaryKeys), "enumerates KeyCollection")]
    [InlineData(typeof(EnumeratesDictionaryValues), "enumerates ValueCollection")]
    [InlineData(typeof(EnumeratesHashSetViaInterface), "enumerates an unordered collection")]
    [InlineData(typeof(EnumeratesDictionaryViaLinq), "enumerates an unordered collection")]
    [InlineData(typeof(CopiesHashSetIntoList), "enumerates an unordered collection")]
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

    // Exact double helpers and classification are allowed, like the System.Math allow-list.
    public double RunDouble(double x) =>
        double.IsFinite(x) && !double.IsNaN(x) ? double.Sqrt(x) + double.Floor(x) + double.Abs(x) + double.Max(x, 1) : 0;

    // Lookups in a hash collection are fine; only enumeration order is a problem.
    public int Lookup(Dictionary<int, int> map, HashSet<int> set, int key) =>
        map.TryGetValue(key, out int value) && set.Contains(key) ? value + map.Count : 0;

    // LINQ and interface enumeration over ordered collections are fine.
    public int Ordered(List<int> items)
    {
        IEnumerable<int> view = items;
        int sum = 0;
        foreach (int item in view)
            sum += item;
        return sum + items.Where(i => i > 0).Sum();
    }
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

internal sealed class UsesDoublePow
{
    public double Run(double x) => double.Pow(x, 1.5);
}

internal sealed class UsesDoubleExp
{
    public double Run(double x) => double.Exp(x);
}

internal sealed class UsesDoubleLog
{
    public double Run(double x) => double.Log(x);
}

internal sealed class UsesDoubleSin
{
    public double Run(double x) => double.Sin(x);
}

internal sealed class UsesDoubleCbrt
{
    public double Run(double x) => double.Cbrt(x);
}

internal sealed class UsesDoubleRound
{
    public double Run(double x) => double.Round(x);
}

internal sealed class UsesHalf
{
    public double Run(double x) => (double)(Half)x;
}

internal sealed class EnumeratesDictionaryKeys
{
    public int Run(Dictionary<int, int> map)
    {
        int sum = 0;
        foreach (int key in map.Keys)
            sum += key;
        return sum;
    }
}

internal sealed class EnumeratesDictionaryValues
{
    public int Run(Dictionary<int, int> map)
    {
        int sum = 0;
        foreach (int value in map.Values)
            sum += value;
        return sum;
    }
}

internal sealed class EnumeratesHashSetViaInterface
{
    public int Run(HashSet<int> set)
    {
        IEnumerable<int> view = set;
        int sum = 0;
        foreach (int item in view)
            sum += item;
        return sum;
    }
}

internal sealed class EnumeratesDictionaryViaLinq
{
    public int Run(Dictionary<int, int> map) => map.Select(pair => pair.Value).First();
}

internal sealed class CopiesHashSetIntoList
{
    public List<int> Run(HashSet<int> set) => new(set);
}
