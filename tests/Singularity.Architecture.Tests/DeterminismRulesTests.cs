using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Singularity.Kernel;

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
    // DEBT-0003: interfaces, frozen/immutable/legacy/concurrent collections, generic math.
    [InlineData(typeof(EnumeratesReadOnlyDictionary), "enumerates an unordered collection")]
    [InlineData(typeof(EnumeratesISet), "enumerates an unordered collection")]
    [InlineData(typeof(EnumeratesReadOnlySetViaLinq), "enumerates an unordered collection")]
    [InlineData(typeof(EnumeratesFrozenDictionary), "enumerates FrozenDictionary")]
    [InlineData(typeof(EnumeratesFrozenKeys), "exposes hash order")]
    [InlineData(typeof(EnumeratesFrozenSet), "enumerates FrozenSet")]
    [InlineData(typeof(EnumeratesImmutableDictionary), "enumerates ImmutableDictionary")]
    [InlineData(typeof(EnumeratesImmutableHashSet), "enumerates ImmutableHashSet")]
    [InlineData(typeof(EnumeratesHashtable), "enumerates Hashtable")]
    [InlineData(typeof(EnumeratesConcurrentBag), "enumerates ConcurrentBag")]
    [InlineData(typeof(CopiesDictionaryKeys), "enumerates KeyCollection")]
    [InlineData(typeof(UsesGenericPow), "generic math IPowerFunctions")]
    [InlineData(typeof(UsesGenericExp), "generic math IExponentialFunctions")]
    [InlineData(typeof(UsesGenericLog), "generic math ILogarithmicFunctions")]
    [InlineData(typeof(UsesGenericSin), "generic math ITrigonometricFunctions")]
    [InlineData(typeof(UsesGenericCbrt), "generic math IRootFunctions")]
    [InlineData(typeof(UsesGenericFma), "generic math IFloatingPointIeee754")]
    [InlineData(typeof(UsesCultureParse), "System.Double.Parse")]
    // TASK-0085 (ADR-0007 rev 2).
    [InlineData(typeof(UsesStringHashCode), "string.GetHashCode")]
    [InlineData(typeof(UsesConcurrentQueue), "forbidden type")]
    [InlineData(typeof(UsesConcurrentStack), "forbidden type")]
    [InlineData(typeof(UsesConcurrentBag), "forbidden type")]
    [InlineData(typeof(UsesChannel), "forbidden type")]
    // TASK-0092 (ADR-0007 rev 3 p. 3-5, 8).
    [InlineData(typeof(HashesStringViaCombine), "hashes a string")]
    [InlineData(typeof(HashesStringViaAdd), "hashes a string")]
    [InlineData(typeof(HashesStringViaComparer), "hashes a string")]
    [InlineData(typeof(HashesStringViaEqualityComparer), "hashes a string")]
    [InlineData(typeof(UsesCompareCulture), "culture-dependent")]
    [InlineData(typeof(UsesStartsWithCulture), "culture-dependent")]
    [InlineData(typeof(UsesIndexOfCulture), "culture-dependent")]
    [InlineData(typeof(UsesEndsWithCulture), "culture-dependent")]
    [InlineData(typeof(UsesCompareWithCulturalComparison), "culture-dependent")]
    [InlineData(typeof(UsesStringCompareTo), "string.CompareTo")]
    [InlineData(typeof(UsesDefaultStringComparer), "Comparer<string>.Default")]
    [InlineData(typeof(UsesToLower), "ToLower")]
    [InlineData(typeof(UsesToUpper), "ToUpper")]
    [InlineData(typeof(UsesCultureComparer), "StringComparer.get_CurrentCulture")]
    [InlineData(typeof(UsesInvariantCultureComparer), "StringComparer.get_InvariantCultureIgnoreCase")]
    [InlineData(typeof(UsesCompareInfo), "CompareInfo")]
    [InlineData(typeof(UsesSortedDictionaryStringKey), "explicit comparer")]
    [InlineData(typeof(UsesSortedSetString), "explicit comparer")]
    [InlineData(typeof(UsesImmutableSortedSetString), "explicit comparer")]
    [InlineData(typeof(UsesOrderByString), "explicit comparer")]
    [InlineData(typeof(UsesArraySortString), "explicit comparer")]
    [InlineData(typeof(UsesListSortString), "explicit comparer")]
    [InlineData(typeof(UsesTaskRun), "forbidden type")]
    [InlineData(typeof(UsesNewThread), "forbidden type")]
    [InlineData(typeof(UsesThreadPool), "forbidden type")]
    [InlineData(typeof(UsesParallelInvoke), "Parallel.Invoke")]
    [InlineData(typeof(UsesBlockingCollection), "forbidden type")]
    [InlineData(typeof(UsesParallelFor), "outside the allowlist")]
    public void Violation_IsDetected(Type sample, string expected)
    {
        // Lambdas compile into nested types, so scan those too.
        Type[] types = [sample, .. sample.GetNestedTypes(IlScanner.AllDeclared)];
        List<string> violations = DeterminismRules.Check(types);
        Assert.Contains(violations, v => v.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    // Explicit ordinal comparers and allowlisted Parallel.For stay clean.
    [Fact]
    public void OrdinalStringCode_HasNoViolations()
    {
        Assert.Empty(DeterminismRules.Check([typeof(OrdinalStrings)]));
    }

    [Fact]
    public void ParallelFor_AllowedOnlyAtAllowlistedSite()
    {
        Assert.Empty(DeterminismRules.Check([typeof(UsesParallelFor)], parallelForAllowlist: [$"{typeof(UsesParallelFor)}.Run"]));
        Assert.Contains(DeterminismRules.Check([typeof(UsesParallelFor)], parallelForAllowlist: ["Other.Run"]),
            v => v.Contains("outside the allowlist", StringComparison.Ordinal));
    }

    // ADR-0003 R1: forbidDouble mode.
    [Fact]
    public void FixedPointCode_PassesForbidDouble()
    {
        Assert.Empty(DeterminismRules.Check([typeof(FixedPointOnly)], forbidDouble: true));
    }

    [Theory]
    [InlineData(typeof(UsesDoubleLocal), "forbidden type System.Double")]
    [InlineData(typeof(UsesDoubleLocal), "opcode ldc.r8")]
    [InlineData(typeof(UsesDoubleField), "forbidden type System.Double")]
    [InlineData(typeof(UsesDoubleParameter), "forbidden type System.Double")]
    [InlineData(typeof(ConvertsToDouble), "opcode conv.r8")]
    [InlineData(typeof(ConvertsUnsignedToDouble), "opcode conv.r.un")]
    [InlineData(typeof(UsesDoubleArray), "System.Double")]
    [InlineData(typeof(UsesNullableDouble), "System.Nullable`1[System.Double]")]
    [InlineData(typeof(CallsNextUnit), "CounterRng.NextUnit (return)")]
    [InlineData(typeof(CallsGetDouble), "DoubleSource.Get (return)")]
    [InlineData(typeof(CallsToDouble), "Fx6.ToDouble (return)")]
    [InlineData(typeof(CallsPowFrac), "DetMath.PowFrac (return)")]
    [InlineData(typeof(CallsGenericWithDouble), "GenericHelper.Run (type argument)")]
    public void DoubleViolation_IsDetected(Type sample, string expected)
    {
        Type[] types = [sample, .. sample.GetNestedTypes(IlScanner.AllDeclared)];
        Assert.Contains(DeterminismRules.Check(types, forbidDouble: true),
            v => v.Contains(expected, StringComparison.Ordinal));
        // Default mode keeps double allowed (Kernel allowlist, Lab).
        Assert.Empty(DeterminismRules.Check(types));
    }

    [Fact]
    public void DoubleUsers_ListsEveryMemberTouchingDouble()
    {
        Type[] types = [typeof(FixedPointOnly), typeof(UsesDoubleLocal), typeof(UsesDoubleField), typeof(CallsToDouble)];
        Assert.Equal(["CallsToDouble.Run", "UsesDoubleField..ctor", "UsesDoubleField.Value", "UsesDoubleLocal.Run"], DeterminismRules.DoubleUsers(types));
    }
}

[SuppressMessage("Performance", "CA1822", Justification = "IL samples for the rule checker.")]
internal sealed class FixedPointOnly
{
    // The Kernel API Core uses under R1: Fx6 powers and integer draws, no double in sight.
    public Fx6 Run(Fx6 x, ulong u) =>
        CounterRng.Chance(u, Fx6.FromRaw(250_000))
            ? DetMath.PowFracFx(x, 330) + Fx6.FromRaw(CounterRng.UniformInt(u, 500_000, 1_500_000))
            : Fx6.FromInt(CounterRng.Index(u, 3));
}

internal sealed class UsesDoubleLocal
{
    public long Run(long a)
    {
        double d = a * 0.5;
        return (long)d;
    }
}

internal sealed class UsesDoubleField
{
    public double Value = 2;
}

internal sealed class UsesDoubleParameter
{
    public static long Run(double x) => x > 0 ? 1 : 0;
}

internal sealed class ConvertsToDouble
{
    // No double local: the value lives only on the evaluation stack.
    public static bool Run(long a, long b) => (double)a < b;
}

internal sealed class ConvertsUnsignedToDouble
{
    public static bool Run(ulong a, long b) => a < (double)b;
}

internal sealed class UsesDoubleArray
{
    public static int Run(int n) => new double[n].Length;
}

internal sealed class UsesNullableDouble
{
    public static bool Run(double? x) => x.HasValue;
}

internal sealed class CallsNextUnit
{
    public static bool Run(CounterRng rng) => rng.NextUnit(0, 0, 0, 0) < 0.5;
}

internal static class DoubleSource
{
    public static double Get(string key) => key.Length;
}

internal sealed class CallsGetDouble
{
    public static bool Run() => DoubleSource.Get("x") > 0;
}

internal sealed class CallsToDouble
{
    public static bool Run(Fx6 x) => x.ToDouble() > 1;
}

internal sealed class CallsPowFrac
{
    public static long Run(long x) => (long)DetMath.PowFrac(x, 330);
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

    // Ordered dictionaries are fine. A method that touches an unordered type must not enumerate
    // anything (conservative rule), so the lookup through the interface lives in its own method.
    public int Sorted(SortedDictionary<string, int> map)
    {
        int sum = 0;
        foreach (var pair in map)
            sum += pair.Value;
        return sum;
    }

    public int LookupView(IReadOnlyDictionary<string, int> view) => view.TryGetValue("a", out int v) ? v : 0;

    // Generic sqrt is correctly rounded; parsing with a fixed culture is exact.
    public static T Root<T>(T x) where T : IRootFunctions<T> => T.Sqrt(x);

    public double ParseInvariant(string s) =>
        double.Parse(s, System.Globalization.CultureInfo.InvariantCulture) +
        (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : 0);
}

internal sealed class EnumeratesReadOnlyDictionary
{
    public int Run(IReadOnlyDictionary<int, int> map)
    {
        int sum = 0;
        foreach (var pair in map)
            sum += pair.Value;
        return sum;
    }
}

internal sealed class EnumeratesISet
{
    public int Run(ISet<int> set)
    {
        int sum = 0;
        foreach (int item in set)
            sum += item;
        return sum;
    }
}

internal sealed class EnumeratesReadOnlySetViaLinq
{
    public int Run(IReadOnlySet<int> set) => set.First();
}

internal sealed class EnumeratesFrozenDictionary
{
    public int Run(FrozenDictionary<int, int> map)
    {
        int sum = 0;
        foreach (var pair in map)
            sum += pair.Value;
        return sum;
    }
}

internal sealed class EnumeratesFrozenKeys
{
    public int Run(FrozenDictionary<int, int> map) => map.Keys[0];
}

internal sealed class EnumeratesFrozenSet
{
    public int Run(FrozenSet<int> set)
    {
        int sum = 0;
        foreach (int item in set)
            sum += item;
        return sum;
    }
}

internal sealed class EnumeratesImmutableDictionary
{
    public int Run(ImmutableDictionary<int, int> map)
    {
        int sum = 0;
        foreach (var pair in map)
            sum += pair.Value;
        return sum;
    }
}

internal sealed class EnumeratesImmutableHashSet
{
    public int Run(ImmutableHashSet<int> set)
    {
        int sum = 0;
        foreach (int item in set)
            sum += item;
        return sum;
    }
}

internal sealed class EnumeratesHashtable
{
    public int Run(Hashtable table)
    {
        int count = 0;
        foreach (object? _ in table)
            count++;
        return count;
    }
}

internal sealed class EnumeratesConcurrentBag
{
    public int Run(ConcurrentBag<int> bag)
    {
        int sum = 0;
        foreach (int item in bag)
            sum += item;
        return sum;
    }
}

internal sealed class CopiesDictionaryKeys
{
    public int[] Run(Dictionary<int, int> map)
    {
        var keys = new int[map.Count];
        map.Keys.CopyTo(keys, 0);
        return keys;
    }
}

internal sealed class UsesGenericPow
{
    public static T Run<T>(T x, T y) where T : IPowerFunctions<T> => T.Pow(x, y);
}

internal sealed class UsesGenericExp
{
    public static T Run<T>(T x) where T : IExponentialFunctions<T> => T.Exp(x);
}

internal sealed class UsesGenericLog
{
    public static T Run<T>(T x) where T : ILogarithmicFunctions<T> => T.Log(x);
}

internal sealed class UsesGenericSin
{
    public static T Run<T>(T x) where T : ITrigonometricFunctions<T> => T.Sin(x);
}

internal sealed class UsesGenericCbrt
{
    public static T Run<T>(T x) where T : IRootFunctions<T> => T.Cbrt(x);
}

internal sealed class UsesGenericFma
{
    public static T Run<T>(T x) where T : IFloatingPointIeee754<T> => T.FusedMultiplyAdd(x, x, x);
}

internal sealed class UsesCultureParse
{
    public double Run(string s) => double.Parse(s);
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

internal static class GenericHelper
{
    public static long Run<T>(long x) => x + (typeof(T).IsValueType ? 1 : 0);
}

internal sealed class CallsGenericWithDouble
{
    public static long Run(long x) => GenericHelper.Run<double>(x);
}

internal sealed class UsesStringHashCode
{
    public int Run(string s) => s.GetHashCode();
}

internal sealed class UsesConcurrentQueue
{
    public int Run() => new System.Collections.Concurrent.ConcurrentQueue<int>().Count;
}

internal sealed class UsesConcurrentStack
{
    public int Run() => new System.Collections.Concurrent.ConcurrentStack<int>().Count;
}

internal sealed class UsesConcurrentBag
{
    public int Run() => new System.Collections.Concurrent.ConcurrentBag<int>().Count;
}

internal sealed class UsesChannel
{
    public object Run() => System.Threading.Channels.Channel.CreateUnbounded<int>();
}

internal sealed class HashesStringViaCombine
{
    public int Run(string s) => HashCode.Combine(s, 1);
}

internal sealed class HashesStringViaAdd
{
    public int Run(string s)
    {
        var h = new HashCode();
        h.Add(s);
        return h.ToHashCode();
    }
}

internal sealed class HashesStringViaComparer
{
    public int Run(string s) => StringComparer.Ordinal.GetHashCode(s);
}

internal sealed class HashesStringViaEqualityComparer
{
    public int Run(string s) => EqualityComparer<string>.Default.GetHashCode(s);
}

internal sealed class UsesCompareCulture
{
    public int Run(string a, string b) => string.Compare(a, b);
}

internal sealed class UsesStartsWithCulture
{
    public bool Run(string a, string b) => a.StartsWith(b);
}

internal sealed class UsesIndexOfCulture
{
    public int Run(string a, string b) => a.IndexOf(b);
}

internal sealed class UsesEndsWithCulture
{
    public bool Run(string a, string b) => a.EndsWith(b);
}

internal sealed class UsesCompareWithCulturalComparison
{
    public int Run(string a, string b) => string.Compare(a, b, StringComparison.CurrentCulture);
}

internal sealed class UsesStringCompareTo
{
    public int Run(string a, string b) => a.CompareTo(b);
}

internal sealed class UsesDefaultStringComparer
{
    public int Run(string a, string b) => Comparer<string>.Default.Compare(a, b);
}

internal sealed class UsesToLower
{
    public string Run(string a) => a.ToLower();
}

internal sealed class UsesToUpper
{
    public string Run(string a) => a.ToUpper();
}

internal sealed class UsesCultureComparer
{
    public bool Run(string a, string b) => StringComparer.CurrentCulture.Equals(a, b);
}

internal sealed class UsesInvariantCultureComparer
{
    public bool Run(string a, string b) => StringComparer.InvariantCultureIgnoreCase.Equals(a, b);
}

internal sealed class UsesCompareInfo
{
    public int Run(string a, string b) => System.Globalization.CultureInfo.InvariantCulture.CompareInfo.Compare(a, b);
}

internal sealed class UsesSortedDictionaryStringKey
{
    public int Run() => new SortedDictionary<string, int>().Count;
}

internal sealed class UsesSortedSetString
{
    public int Run() => new SortedSet<string>().Count;
}

internal sealed class UsesImmutableSortedSetString
{
    public int Run() => ImmutableSortedSet.Create<string>().Count;
}

internal sealed class UsesOrderByString
{
    public int Run(string[] a) => a.OrderBy(x => x).Count();
}

internal sealed class UsesArraySortString
{
    public void Run(string[] a) => Array.Sort(a);
}

internal sealed class UsesListSortString
{
    public void Run(List<string> a) => a.Sort();
}

internal sealed class UsesTaskRun
{
    public void Run() => System.Threading.Tasks.Task.Run(() => { });
}

internal sealed class UsesNewThread
{
    public void Run() => new System.Threading.Thread(() => { }).Start();
}

internal sealed class UsesThreadPool
{
    public bool Run() => System.Threading.ThreadPool.QueueUserWorkItem(_ => { });
}

internal sealed class UsesParallelInvoke
{
    public void Run() => System.Threading.Tasks.Parallel.Invoke(() => { });
}

internal sealed class UsesBlockingCollection
{
    public int Run() => new BlockingCollection<int>().Count;
}

internal sealed class UsesParallelFor
{
    public void Run(int[] a) => System.Threading.Tasks.Parallel.For(0, a.Length, i => a[i] = i);
}

internal sealed class OrdinalStrings
{
    public int Run(string a, string b, string[] arr, List<string> list)
    {
        Array.Sort(arr, StringComparer.Ordinal);
        list.Sort(StringComparer.Ordinal);
        var set = new SortedSet<string>(StringComparer.Ordinal);
        var dict = new SortedDictionary<string, int>(StringComparer.Ordinal);
        _ = arr.OrderBy(x => x, StringComparer.Ordinal).Count();
        _ = a.ToLowerInvariant();
        _ = a.StartsWith(b, StringComparison.Ordinal) | a.EndsWith(b, StringComparison.OrdinalIgnoreCase);
        _ = a.StartsWith('x') | a.Equals(b) | a.Contains(b, StringComparison.Ordinal);
        return string.Compare(a, b, StringComparison.Ordinal) + a.IndexOf(b, StringComparison.Ordinal) + set.Count + dict.Count;
    }
}
