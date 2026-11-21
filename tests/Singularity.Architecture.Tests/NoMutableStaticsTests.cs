using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Singularity.Core;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Architecture.Tests;

/// <summary>
/// TASK-0125 (review 2026-11-19, A6): two worlds share one process and one thread pool, so no type of Kernel, Core or Session may keep
/// state in a static field. Allowed: <c>const</c>, a <c>static readonly</c> field of an immutable type (the closed
/// allow-list <see cref="IsAllowedStaticType"/>), and the explicit list below. Forbidden: any other static field, [ThreadStatic],
/// and any field of type ThreadLocal / AsyncLocal. Nested and compiler-generated types are scanned too; skipped are the lambda caches
/// of the compiler (<c>&lt;&gt;9*</c>, <c>&lt;&gt;O</c>) and <c>&lt;PrivateImplementationDetails&gt;</c> (static readonly structs of constant bytes).
/// </summary>
public class NoMutableStaticsTests
{
    private static readonly Assembly[] Assemblies =
    [
        typeof(Table).Assembly,
        typeof(Singularity.Core.Industry.IndustrySystem).Assembly,
        typeof(SliceSession).Assembly,
    ];

    // Type.Field -> (expected kind, reason). The kind is checked against the real field, and every entry must resolve to a real field,
    // so a stale or mistyped entry fails instead of hiding a violation.
    private enum Kind { None, ThreadStatic, InstanceThreadLocal, ConstantArray, ImmutableObject }

    private static readonly Dictionary<string, (Kind Kind, string Reason)> Exceptions = new(StringComparer.Ordinal)
    {
        // Thread-bound pure temporaries: sizes and contents are rewritten before every read, never hashed or saved, hold no world data.
        // Moving them into per-chunk buffers is not mechanical (DEBT proposal, see the TASK-0125 report); TwoWorldsOnOnePoolTests (Session.Tests) guards the behaviour.
        ["Singularity.Core.WhyNot.ClosureArena.t_sets"] = (Kind.ThreadStatic, "ThreadStatic temporary of the closure walk"),
        ["Singularity.Core.WhyNot.ClosureArena.t_memos"] = (Kind.ThreadStatic, "ThreadStatic temporary of the closure walk"),
        ["Singularity.Core.WhyNot.ClosureArena.t_setsUsed"] = (Kind.ThreadStatic, "ThreadStatic fill count of the arena, Reset before each walk"),
        ["Singularity.Core.WhyNot.ClosureArena.t_memosUsed"] = (Kind.ThreadStatic, "ThreadStatic fill count of the arena, Reset before each walk"),
        ["Singularity.Core.Policy.PolicySystem.t_decisions"] = (Kind.ThreadStatic, "ThreadStatic [Scratch] temporary of the policy decision"),
        ["Singularity.Core.Policy.ToolingPlan._temp"] = (Kind.ThreadStatic, "ThreadStatic temporary of the tooling plan"),
        ["Singularity.Core.Industry.EffortScratch._forThread"] = (Kind.ThreadStatic, "ThreadStatic arrays of the effort allocator, overwritten per call"),
        ["Singularity.Core.Industry.IndustryMath.t_items"] = (Kind.ThreadStatic, "ThreadStatic [Scratch] items of one Profile call"),
        ["Singularity.Core.Industry.IndustryMath.t_sorted"] = (Kind.ThreadStatic, "ThreadStatic [Scratch] sorted copy of one Profile call"),
        // An instance field, one per world adapter; the book is refilled with the parameters of its own world (TASK-0103, CrossWorldScratchTests).
        ["Singularity.Session.PolicyProductsAdapter._book"] = (Kind.InstanceThreadLocal, "per-world ThreadLocal book, refilled per call"),
        // Constant tables: filled once in the static initialiser, never written afterwards (reviewed; an ImmutableArray would change many call sites).
        ["Singularity.Core.Industry.IndustrySystem.DimNames"] = (Kind.ConstantArray, "constant name table"),
        ["Singularity.Core.Data.DataNames.NodeTypes"] = (Kind.ConstantArray, "constant name table"),
        ["Singularity.Core.Data.DataNames.Clusters"] = (Kind.ConstantArray, "constant name table"),
        ["Singularity.Core.Data.DataNames.Disciplines"] = (Kind.ConstantArray, "constant name table"),
        ["Singularity.Core.Data.DataNames.Dimensions"] = (Kind.ConstantArray, "constant name table"),
        ["Singularity.Core.Data.DataNames.Hazards"] = (Kind.ConstantArray, "constant name table"),
        ["Singularity.Core.Data.DataNames.ActorClasses"] = (Kind.ConstantArray, "constant name table"),
        ["Singularity.Core.Data.DataNames.Variants"] = (Kind.ConstantArray, "constant name table"),
        ["Singularity.Core.Data.ScenarioData.ActorFields"] = (Kind.ConstantArray, "constant name list"),
        ["Singularity.Core.Data.ScenarioData.SymmetricActors"] = (Kind.ConstantArray, "constant name list"),
        ["Singularity.Core.Data.ScenarioData.NoMultiplier"] = (Kind.ConstantArray, "constant name list"),
        ["Singularity.Core.Data.Yaml.StrictYaml.ImplicitBoolOrNull"] = (Kind.ConstantArray, "constant literal list"),
        // A class whose instance fields are all readonly and of safe types (a lever bit mask), built once.
        ["Singularity.Core.WhyNot.LeverRegistry.<Slice>k__BackingField"] = (Kind.ImmutableObject, "immutable lever mask of the slice"),
        ["Singularity.Session.ViewBuilder.Reasons"] = (Kind.ConstantArray, "constant table of licence reasons"),
    };

    private static bool IsThreadBound(Type t) =>
        t.HasElementType ? IsThreadBound(t.GetElementType()!) : t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(ThreadLocal<>) || t.GetGenericTypeDefinition() == typeof(AsyncLocal<>)
            || t.GetGenericArguments().Any(IsThreadBound));

    // The compiler caches a lambda in a static field of its closure class (<>9__x_y): written once, idempotent, holds no data.
    private static bool IsLambdaCache(FieldInfo f) =>
        (f.Name.StartsWith("<>9", StringComparison.Ordinal) || f.DeclaringType!.Name == "<>O") // <>O: cache of a method-group delegate
        && f.DeclaringType!.GetCustomAttribute<CompilerGeneratedAttribute>() is not null;

    /// <summary>
    /// The CLOSED allow-list for a static readonly field (an interface, object, array, class or generic wrapper is not on it; the instance-field
    /// rule of <see cref="ScratchTests"/> that an injected interface is safe does not apply to statics): primitive, enum, string, Int128, UInt128,
    /// decimal; a readonly struct whose instance fields are all allowed; Nullable of an allowed type; an Immutable* / Frozen* collection of allowed types.
    /// </summary>
    private static bool IsAllowedStaticType(Type t, HashSet<Type>? seen = null)
    {
        if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(Int128) || t == typeof(UInt128) || t == typeof(decimal))
            return true;
        if (t.IsArray || t.IsInterface || t.IsPointer || t.IsByRef || t == typeof(object))
            return false;
        if (t.IsGenericType)
        {
            Type d = t.GetGenericTypeDefinition();
            string ns = d.Namespace ?? "";
            bool immutable = d == typeof(Nullable<>) || ns == "System.Collections.Immutable" || ns == "System.Collections.Frozen";
            return immutable && t.GetGenericArguments().All(a => IsAllowedStaticType(a, seen));
        }
        if (!t.IsValueType || !ScratchTests.IsReadOnlyStruct(t) || t.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            return false;
        seen ??= [];
        if (!seen.Add(t))
            return true;
        return t.GetFields(IlScanner.AllDeclared).Where(x => !x.IsStatic).All(x => x.IsInitOnly && IsAllowedStaticType(x.FieldType, seen));
    }

    private static Kind KindOf(FieldInfo f)
    {
        if (f.IsStatic && f.GetCustomAttribute<ThreadStaticAttribute>() is not null)
            return Kind.ThreadStatic;
        if (!f.IsStatic && IsThreadBound(f.FieldType))
            return Kind.InstanceThreadLocal;
        if (f.IsStatic && f.IsInitOnly && f.FieldType.IsArray)
            return Kind.ConstantArray;
        if (f.IsStatic && f.IsInitOnly && !f.FieldType.IsInterface && f.FieldType.IsClass && ScratchTests.IsSafeType(f.FieldType))
            return Kind.ImmutableObject;
        return Kind.None;
    }

    private static List<string> Violations(IEnumerable<Type> types, IReadOnlyDictionary<string, (Kind Kind, string Reason)> exceptions)
    {
        var found = new List<string>();
        foreach (Type type in types)
        {
            if (type.Name.StartsWith("<PrivateImplementationDetails>", StringComparison.Ordinal))
                continue;
            foreach (FieldInfo f in type.GetFields(IlScanner.AllDeclared))
            {
                string name = $"{type.FullName}.{f.Name}";
                if (exceptions.TryGetValue(name, out var allowed))
                {
                    Kind actual = KindOf(f);
                    if (actual != allowed.Kind)
                        found.Add($"{name}: listed as {allowed.Kind} but is {actual}");
                    continue;
                }
                if (IsThreadBound(f.FieldType))
                    found.Add($"{name}: ThreadLocal/AsyncLocal keeps state per thread");
                if (!f.IsStatic || f.IsLiteral || IsLambdaCache(f))
                    continue;
                if (f.GetCustomAttribute<ThreadStaticAttribute>() is not null)
                    found.Add($"{name}: [ThreadStatic]");
                else if (!f.IsInitOnly)
                    found.Add($"{name}: static field is not readonly");
                else if (!IsAllowedStaticType(f.FieldType))
                    found.Add($"{name}: static readonly of a type that is not on the allow-list ({f.FieldType.Name})");
            }
        }
        return found;
    }

    [Fact]
    public void KernelCoreSession_HaveNoMutableStaticFields()
    {
        List<string> found = Violations(Assemblies.SelectMany(a => a.GetTypes()), Exceptions);
        Assert.True(found.Count == 0, string.Join("\n", found));
    }

    [Fact]
    public void EveryException_ResolvesToARealField()
    {
        Type[] all = [.. Assemblies.SelectMany(a => a.GetTypes())];
        List<string> stale = [.. Exceptions.Keys.Where(k => !all.Any(t => t.GetFields(IlScanner.AllDeclared).Any(f => $"{t.FullName}.{f.Name}" == k)))];
        Assert.True(stale.Count == 0, "stale exception entries: " + string.Join(", ", stale));
    }

    [Fact]
    public void TheScan_SeesAStaticInt_AThreadStatic_AMutableReadonlyList_AThreadLocal_AndIgnoresAStaticLambdaCache()
    {
        List<string> found = Violations([typeof(StaticMutant), .. typeof(StaticMutant).GetNestedTypes(IlScanner.AllDeclared)], MutantExceptions);
        Assert.Contains(found, f => f.Contains("Counter") && f.Contains("not readonly"));
        Assert.Contains(found, f => f.Contains("Buffer") && f.Contains("ThreadStatic"));
        Assert.Contains(found, f => f.Contains("Shared") && f.Contains("allow-list"));
        Assert.Contains(found, f => f.Contains("Local") && f.Contains("ThreadLocal"));
        Assert.Contains(found, f => f.Contains("Async") && f.Contains("ThreadLocal"));
        Assert.Contains(found, f => f.Contains("AsInterface") && f.Contains("allow-list"));   // an interface is not safe for a static
        Assert.Contains(found, f => f.Contains("LocalArray") && f.Contains("ThreadLocal"));  // an array of ThreadLocal
        Assert.Contains(found, f => f.Contains("Boxed") && f.Contains("allow-list"));
        Assert.DoesNotContain(found, f => f.Contains("Fine") || f.Contains("Frozen") || f.Contains("Wide") || f.Contains("Max") || f.Contains("<>9") || f.Contains("<>O") || f.Contains("Listed"));
        // A closure class with a static lambda cache really exists in the mutant (the scan skips it on purpose).
        Assert.Contains(typeof(StaticMutant).GetNestedTypes(IlScanner.AllDeclared), t => t.GetFields(IlScanner.AllDeclared).Any(IsLambdaCache));
        // An exception entry of the wrong kind or of a missing kind is reported.
        Assert.Contains(found, f => f.Contains("WrongKind") && f.Contains("listed as ThreadStatic but is None"));
        Assert.Contains(found, f => f.Contains("WrongArray") && f.Contains("listed as ConstantArray but is None"));
    }

    private static readonly Dictionary<string, (Kind Kind, string Reason)> MutantExceptions = new()
    {
        ["Singularity.Architecture.Tests.StaticMutant.WrongKind"] = (Kind.ThreadStatic, "a plain static named like a ThreadStatic entry"),
        ["Singularity.Architecture.Tests.StaticMutant.WrongArray"] = (Kind.ConstantArray, "a list named like a constant array entry"),
        ["Singularity.Architecture.Tests.StaticMutant.Listed"] = (Kind.ConstantArray, "a real constant array: allowed"),
    };

    [Fact]
    public void TheAssembliesAreScanned_NestedAndGeneratedTypesIncluded()
    {
        Type[] all = [.. Assemblies.SelectMany(a => a.GetTypes())];
        Assert.Contains(all, t => t.IsNested);
        Assert.Contains(all, t => t.GetCustomAttribute<CompilerGeneratedAttribute>() is not null);
        Assert.Contains(all, t => t.GetFields(IlScanner.AllDeclared).Any(f => f.IsStatic && f.IsInitOnly));
    }
}

internal static class StaticMutant
{
    public static int Counter = 1;
    [ThreadStatic] public static long[]? Buffer = null;
    public static readonly List<int> Shared = [];
    public static readonly ThreadLocal<int> Local = new();
    public static readonly AsyncLocal<int> Async = new();
    public const int Max = 3;
    public static readonly string Fine = "x";
    public static int WrongKind = 2;
    public static readonly List<int> WrongArray = [];
    public static readonly string[] Listed = ["a"];

    // A static lambda in a method: the compiler caches it in a static field of a closure class, which the scan must not report.
    public static readonly IList<int> AsInterface = new List<int>();
    public static readonly ThreadLocal<int>[] LocalArray = [];
    public static readonly object Boxed = new();
    public static readonly ImmutableArray<string> Frozen = ["a"];
    public static readonly Int128 Wide = 1;
    public static int Twice(int x)
    {
        Func<int, int> f = static y => y * 2;
        return f(x);
    }
}
