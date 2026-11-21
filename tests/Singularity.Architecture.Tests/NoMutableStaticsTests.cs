using System.Reflection;
using System.Runtime.CompilerServices;
using Singularity.Core;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Architecture.Tests;

/// <summary>
/// TASK-0125 (review 2026-11-19, A6): two worlds share one process and one thread pool, so no type of Kernel, Core or Session may keep
/// state in a static field. Allowed: <c>const</c>, a <c>static readonly</c> field of an immutable type (the rules of
/// <see cref="ScratchTests.IsSafeType"/>), and the explicit list below. Forbidden: any other static field, [ThreadStatic],
/// and any field of type ThreadLocal / AsyncLocal. Nested and compiler-generated types are scanned too.
/// </summary>
public class NoMutableStaticsTests
{
    private static readonly Assembly[] Assemblies =
    [
        typeof(Table).Assembly,
        typeof(Singularity.Core.Industry.IndustrySystem).Assembly,
        typeof(SliceSession).Assembly,
    ];

    // Type.Field -> reason. Empty unless a field is proven safe and cannot be written in the safe form.
    private static readonly Dictionary<string, string> Exceptions = new(StringComparer.Ordinal)
    {
        // Thread-bound pure temporaries: sizes and contents are rewritten before every read, never hashed or saved, hold no world data.
        // Moving them into per-chunk buffers is not mechanical (DEBT proposal, see the TASK-0125 report); TwoWorldsOnOnePoolTests (Session.Tests) guards the behaviour.
        ["Singularity.Core.WhyNot.ClosureArena.t_sets"] = "ThreadStatic temporary of the closure walk",
        ["Singularity.Core.WhyNot.ClosureArena.t_memos"] = "ThreadStatic temporary of the closure walk",
        ["Singularity.Core.WhyNot.ClosureArena.t_setsUsed"] = "ThreadStatic fill count of the arena, Reset before each walk",
        ["Singularity.Core.WhyNot.ClosureArena.t_memosUsed"] = "ThreadStatic fill count of the arena, Reset before each walk",
        ["Singularity.Core.Policy.PolicySystem.t_decisions"] = "ThreadStatic [Scratch] temporary of the policy decision",
        ["Singularity.Core.Policy.ToolingPlan._temp"] = "ThreadStatic temporary of the tooling plan",
        ["Singularity.Core.Industry.EffortScratch._forThread"] = "ThreadStatic arrays of the effort allocator, overwritten per call",
        ["Singularity.Core.Industry.IndustryMath.t_items"] = "ThreadStatic [Scratch] items of one Profile call",
        ["Singularity.Core.Industry.IndustryMath.t_sorted"] = "ThreadStatic [Scratch] sorted copy of one Profile call",
        // An instance field, one per world adapter; the book is refilled with the parameters of its own world (TASK-0103, CrossWorldScratchTests).
        ["Singularity.Session.PolicyProductsAdapter._book"] = "per-world ThreadLocal book, refilled per call",
        // Constant tables: filled once in the static initialiser, never written afterwards (reviewed; an ImmutableArray would change many call sites).
        ["Singularity.Core.Industry.IndustrySystem.DimNames"] = "constant name table",
        ["Singularity.Core.Data.DataNames.NodeTypes"] = "constant name table",
        ["Singularity.Core.Data.DataNames.Clusters"] = "constant name table",
        ["Singularity.Core.Data.DataNames.Disciplines"] = "constant name table",
        ["Singularity.Core.Data.DataNames.Dimensions"] = "constant name table",
        ["Singularity.Core.Data.DataNames.Hazards"] = "constant name table",
        ["Singularity.Core.Data.DataNames.ActorClasses"] = "constant name table",
        ["Singularity.Core.Data.DataNames.Variants"] = "constant name table",
        ["Singularity.Core.Data.ScenarioData.ActorFields"] = "constant name list",
        ["Singularity.Core.Data.ScenarioData.SymmetricActors"] = "constant name list",
        ["Singularity.Core.Data.ScenarioData.NoMultiplier"] = "constant name list",
        ["Singularity.Core.Data.Yaml.StrictYaml.ImplicitBoolOrNull"] = "constant literal list",
        ["Singularity.Session.ViewBuilder.Reasons"] = "constant table of licence reasons",
    };

    private static bool IsThreadBound(Type t) =>
        t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(ThreadLocal<>) || t.GetGenericTypeDefinition() == typeof(AsyncLocal<>)
            || t.GetGenericArguments().Any(IsThreadBound));

    // The compiler caches a lambda in a static field of its closure class (<>9__x_y): written once, idempotent, holds no data.
    private static bool IsLambdaCache(FieldInfo f) =>
        (f.Name.StartsWith("<>9", StringComparison.Ordinal) || f.DeclaringType!.Name == "<>O") // <>O: cache of a method-group delegate
        && f.DeclaringType!.GetCustomAttribute<CompilerGeneratedAttribute>() is not null;

    // Readonly structs of the runtime that carry no IsReadOnly attribute in the reference view.
    private static bool IsValueLike(Type t) => t == typeof(Int128) || t == typeof(UInt128) || t == typeof(decimal);

    internal static List<string> Violations(IEnumerable<Type> types)
    {
        var found = new List<string>();
        foreach (Type type in types)
        {
            if (type.Name.StartsWith("<PrivateImplementationDetails>", StringComparison.Ordinal))
                continue; // compiler data blobs: static readonly structs of constant bytes
            foreach (FieldInfo f in type.GetFields(IlScanner.AllDeclared))
            {
                string name = $"{type.FullName}.{f.Name}";
                if (Exceptions.ContainsKey(name))
                    continue;
                if (IsThreadBound(f.FieldType))
                    found.Add($"{name}: ThreadLocal/AsyncLocal keeps state per thread");
                if (!f.IsStatic || f.IsLiteral || IsLambdaCache(f) || Exceptions.ContainsKey(name))
                    continue;
                if (f.GetCustomAttribute<ThreadStaticAttribute>() is not null)
                    found.Add($"{name}: [ThreadStatic]");
                else if (!f.IsInitOnly)
                    found.Add($"{name}: static field is not readonly");
                else if (!IsValueLike(f.FieldType) && !ScratchTests.IsSafeType(f.FieldType))
                    found.Add($"{name}: static readonly of a mutable type {f.FieldType.Name}");
            }
        }
        return found;
    }

    [Fact]
    public void KernelCoreSession_HaveNoMutableStaticFields()
    {
        List<string> found = Violations(Assemblies.SelectMany(a => a.GetTypes()));
        Assert.True(found.Count == 0, string.Join("\n", found));
    }

    [Fact]
    public void TheScan_SeesAStaticInt_AThreadStatic_AMutableReadonlyList_AThreadLocal_AndAClosureOfAStaticLambda()
    {
        List<string> found = Violations([typeof(StaticMutant)]);
        Assert.Contains(found, f => f.Contains("Counter") && f.Contains("not readonly"));
        Assert.Contains(found, f => f.Contains("Buffer") && f.Contains("ThreadStatic"));
        Assert.Contains(found, f => f.Contains("Shared") && f.Contains("mutable type"));
        Assert.Contains(found, f => f.Contains("Local") && f.Contains("ThreadLocal"));
        Assert.Contains(found, f => f.Contains("Async") && f.Contains("ThreadLocal"));
        Assert.DoesNotContain(found, f => f.Contains("Fine") || f.Contains("Max"));
    }

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
}
