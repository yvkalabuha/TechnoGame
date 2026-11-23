using System.Collections.Immutable;
using System.Reflection;
using static Singularity.Architecture.Tests.IlScanner;
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
    private enum Kind { None, ThreadStatic, InstanceThreadLocal, ImmutableObject }

    private static readonly Dictionary<string, (Kind Kind, string Reason)> Exceptions = new(StringComparer.Ordinal)
    {
        // Thread-bound pure temporaries: sizes and contents are rewritten before every read, never hashed or saved, hold no world data.
        // Moving them into per-chunk buffers is not mechanical (DEBT proposal, see the TASK-0125 report); TwoWorldsOnOnePoolTests (Session.Tests) guards the behaviour.
        ["Singularity.Core.WhyNot.ClosureArena.t_sets"] = (Kind.ThreadStatic, "DEBT-0070: ThreadStatic temporary of the closure walk"),
        ["Singularity.Core.WhyNot.ClosureArena.t_memos"] = (Kind.ThreadStatic, "DEBT-0070: ThreadStatic temporary of the closure walk"),
        ["Singularity.Core.WhyNot.ClosureArena.t_setsUsed"] = (Kind.ThreadStatic, "DEBT-0070: ThreadStatic fill count of the arena, Reset before each walk"),
        ["Singularity.Core.WhyNot.ClosureArena.t_memosUsed"] = (Kind.ThreadStatic, "DEBT-0070: ThreadStatic fill count of the arena, Reset before each walk"),
        ["Singularity.Core.Policy.PolicySystem.t_decisions"] = (Kind.ThreadStatic, "DEBT-0070: ThreadStatic [Scratch] temporary of the policy decision"),
        ["Singularity.Core.Policy.ToolingPlan._temp"] = (Kind.ThreadStatic, "DEBT-0070: ThreadStatic temporary of the tooling plan"),
        ["Singularity.Core.Industry.EffortScratch._forThread"] = (Kind.ThreadStatic, "DEBT-0070: ThreadStatic arrays of the effort allocator, overwritten per call"),
        ["Singularity.Core.Industry.IndustryMath.t_items"] = (Kind.ThreadStatic, "DEBT-0070: ThreadStatic [Scratch] items of one Profile call"),
        ["Singularity.Core.Industry.IndustryMath.t_sorted"] = (Kind.ThreadStatic, "DEBT-0070: ThreadStatic [Scratch] sorted copy of one Profile call"),
        // An instance field, one per world adapter; the book is refilled with the parameters of its own world (TASK-0103, CrossWorldScratchTests).
        ["Singularity.Session.PolicyProductsAdapter._book"] = (Kind.InstanceThreadLocal, "DEBT-0070: per-world ThreadLocal book, refilled per call"),
        // A class whose instance fields are all readonly and of safe types (a lever bit mask), built once.
        ["Singularity.Core.WhyNot.LeverRegistry.<Slice>k__BackingField"] = (Kind.ImmutableObject, "immutable lever mask of the slice"),
    };

    private static bool IsThreadLocal(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ThreadLocal<>);

    // A sealed class whose instance fields are all readonly and pass the static allow-list (no interface shortcut): a value in all but name.
    private static bool IsImmutableClass(Type t) =>
        t.IsClass && t.IsSealed && !t.IsGenericType
        && BaseChain(t).SelectMany(c => c.GetFields(IlScanner.AllDeclared)).Where(x => !x.IsStatic).All(x => x.IsInitOnly && IsAllowedStaticType(x.FieldType));

    private static IEnumerable<Type> BaseChain(Type t)
    {
        for (Type? c = t; c is not null && c != typeof(object); c = c.BaseType)
            yield return c;
    }

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
    /// decimal; a readonly struct whose instance fields are all allowed; Nullable of an allowed type; an ImmutableArray / List / Queue / Stack of allowed types (exact list).
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
            // Exact closed list of generic definitions. Immutable/Frozen dictionaries and sets are not on it: their comparer can be stateful
            // and reflection cannot see it. A Builder or any nested type is mutable and is not listed either.
            bool immutable = d == typeof(Nullable<>) || d == typeof(ImmutableArray<>) || d == typeof(ImmutableList<>)
                || d == typeof(ImmutableQueue<>) || d == typeof(ImmutableStack<>)
                || (d.Namespace == "System" && d.Name.StartsWith("ValueTuple`", StringComparison.Ordinal));
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
        if (!f.IsStatic && IsThreadLocal(f.FieldType))
            return Kind.InstanceThreadLocal;
        if (f.IsStatic && f.IsInitOnly && !f.FieldType.IsInterface && f.FieldType.IsClass && IsImmutableClass(f.FieldType))
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

    // The "write through an unsafe API" category, closed at once: in Kernel, Core and Session any call into these classes, and any raw pointer
    // access (ldind/stind/cpblk/initblk, or a pinned local made by `fixed`), is a violation unless the exact (method -> callee) pair is listed
    // with the number of its occurrences and a reason. A bare ref/span over memory a caller owns is the way a static ImmutableArray or List is written.
    private static readonly HashSet<string> UnsafeClasses = new(StringComparer.Ordinal)
    {
        "System.Runtime.InteropServices.MemoryMarshal",
        "System.Runtime.InteropServices.CollectionsMarshal",
        "System.Runtime.InteropServices.Marshal",
        "System.Runtime.InteropServices.GCHandle",
        "System.Runtime.InteropServices.ImmutableCollectionsMarshal",
        "System.Runtime.CompilerServices.Unsafe",
    };

    private static readonly Dictionary<string, (int Count, string Reason)> UnsafeUses = new(StringComparer.Ordinal)
    {
        ["Singularity.Kernel.Column`1.AppendCurrentTo -> MemoryMarshal.AsBytes"] = (1, "read-only byte view of the column's own current array, passed to IncrementalHash.AppendData (a ReadOnlySpan sink); no write"),
        ["Singularity.Core.Policy.ToolingPlan.Compute -> CollectionsMarshal.AsSpan"] = (2, "sorts in place the thread-bound scratch list t.Cells of the plan (DEBT-0070), cleared and refilled each call; no static or world storage"),
        // Only the single non-fresh AsImmutableArray of this method: another unsafe callee or a second call in it still fails.
        ["Singularity.Session.ViewBuilder.Inputs -> ImmutableCollectionsMarshal.AsImmutableArray"] = (1, "captured only by the Parallel.For lambda, which does not outlive the call; the local array rows is created here and nobody else holds it, no copy"),
    };

    private static string? UnsafeCallee(Instruction ins, List<Instruction> body, int index)
    {
        if (ins.Operand is MethodBase callee && callee.DeclaringType?.FullName is { } dt && UnsafeClasses.Contains(dt))
        {
            // The compiler lowers a collection expression for ImmutableArray to AsImmutableArray over an array it has just built (newarr/stelem,
            // a static-data InitializeArray, or a ToArray copy): no alias exists, so this exact shape is not a use.
            if (callee.Name == "AsImmutableArray" && IsFreshArray(body, index))
                return null;
            return $"{callee.DeclaringType!.Name}.{callee.Name}";
        }
        string? op = ins.OpCode.Name;
        // ldind/stind are not flagged: every ref/out/in parameter uses them. A raw pointer is caught by its type (see HasPointer).
        return op is "cpblk" or "initblk" or "calli" ? "opcode " + op : null;
    }

    // The array wrapped by the call at body[callIndex] is fresh only if the instruction that PRODUCED the top stack slot is a newarr or a copying
    // ToArray. The producer is found by walking back with the stack effect of each instruction (a dup of a written array, an InitializeArray or a
    // stelem do not make an array fresh: they only act on one that the producer made). Any branch or unknown stack effect means "not fresh".
    private static bool IsFreshArray(List<Instruction> body, int callIndex)
    {
        int depth = 1; // 1 = top of the stack
        for (int i = callIndex - 1; i >= 0; i--)
        {
            Instruction ins = body[i];
            if (ins.OpCode.FlowControl is not (System.Reflection.Emit.FlowControl.Next or System.Reflection.Emit.FlowControl.Call)
                || !StackEffect(ins, out int pop, out int push))
                return false;
            if (depth <= push)
            {
                if (ins.OpCode == System.Reflection.Emit.OpCodes.Dup)
                {
                    depth = 1; // both slots are the value below
                    continue;
                }
                return ins.OpCode == System.Reflection.Emit.OpCodes.Newarr
                    || (ins.Operand is MethodBase m && m.Name == "ToArray" && m.DeclaringType?.Name is "List`1" or "Enumerable" or "ReadOnlySpan`1");
            }
            depth = depth - push + pop;
        }
        return false;
    }

    private static bool StackEffect(Instruction ins, out int pop, out int push)
    {
        pop = push = 0;
        var op = ins.OpCode;
        if (op.Name is "call" or "callvirt" or "newobj" && ins.Operand is MethodBase)
        {
            var m = (MethodBase)ins.Operand!;
            bool ctor = op.Name == "newobj";
            pop = m.GetParameters().Length + (!m.IsStatic && !ctor ? 1 : 0);
            push = ctor || (m is MethodInfo mi && mi.ReturnType != typeof(void)) ? 1 : 0;
            return true;
        }
        string a = op.StackBehaviourPop.ToString(), b = op.StackBehaviourPush.ToString();
        if (a.StartsWith("Var", StringComparison.Ordinal) || b.StartsWith("Var", StringComparison.Ordinal))
            return false;
        pop = a.EndsWith('0') ? 0 : a.Split('_').Length;
        push = b.EndsWith('0') ? 0 : b.Split('_').Length;
        return true;
    }

    // A pointer or a pinned local anywhere in the signature or the locals: the way unmanaged code reaches storage without the type system.
    private static bool HasPointer(MethodBase m) =>
        m.GetParameters().Any(p => p.ParameterType.IsPointer || p.ParameterType.IsByRef && p.ParameterType.GetElementType()!.IsPointer)
        || (m is MethodInfo mi && mi.ReturnType.IsPointer)
        || m.GetMethodBody()?.LocalVariables.Any(l => l.IsPinned || l.LocalType.IsPointer) == true;

    private static Dictionary<string, int> UnsafeCalls(IEnumerable<Type> types)
    {
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Type type in types)
        {
            foreach (MethodBase method in type.GetMethods(IlScanner.AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(IlScanner.AllDeclared)))
            {
                string name = $"{type.FullName}.{method.Name}";
                if (HasPointer(method))
                    found[name + " -> raw pointer (fixed or unsafe)"] = 1;
                List<Instruction> body = [.. IlScanner.Read(method)];
                for (int i = 0; i < body.Count; i++)
                {
                    Instruction ins = body[i];
                    string? what = UnsafeCallee(ins, body, i);
                    if (what is null)
                        continue;
                    string key = $"{name} -> {what}";
                    found[key] = found.GetValueOrDefault(key) + 1;
                }
            }
        }
        return found;
    }

    private static List<string> UnsafeViolations(Dictionary<string, int> found, IReadOnlyDictionary<string, (int Count, string Reason)> allowed) =>
    [
        .. found.Where(kv => !allowed.TryGetValue(kv.Key, out var a) || a.Count != kv.Value).Select(kv => $"{kv.Key} x{kv.Value}: not allowed (or the listed count differs)"),
        .. allowed.Where(kv => !found.ContainsKey(kv.Key)).Select(kv => $"{kv.Key}: stale allow-list entry"),
    ];

    [Fact]
    public void KernelCoreSession_UseNoUnsafeApi_ExceptListedMethodCalleePairs()
    {
        List<string> v = UnsafeViolations(UnsafeCalls(Assemblies.SelectMany(a => a.GetTypes())), UnsafeUses);
        Assert.True(v.Count == 0, string.Join("\n", v));
    }

    [Fact]
    public void TheUnsafeScan_SeesEachMutant_AndOnlyTheListedPairIsExempt()
    {
        Dictionary<string, int> found = UnsafeCalls([typeof(UnsafeMutant)]);
        List<string> v = UnsafeViolations(found, new Dictionary<string, (int, string)>());
        Assert.Contains(v, f => f.Contains("Wrap") && f.Contains("AsImmutableArray"));
        Assert.Contains(v, f => f.Contains("Unwrap") && f.Contains("AsArray"));
        Assert.Contains(v, f => f.Contains("Cast") && f.Contains("Unsafe.As"));
        Assert.Contains(v, f => f.Contains("Ref") && f.Contains("Unsafe.AsRef"));
        Assert.Contains(v, f => f.Contains("PokeStatic") && f.Contains("MemoryMarshal.GetReference"));  // writes into a static ImmutableArray
        Assert.Contains(v, f => f.Contains("SpanOfStatic") && f.Contains("CollectionsMarshal.AsSpan")); // a span over a static list
        Assert.Contains(v, f => f.Contains("Fixed") && f.Contains("raw pointer"));
        Assert.Contains(v, f => f.Contains("Raw") && f.Contains("raw pointer"));
        Assert.Contains(v, f => f.Contains("WrapWritten") && f.Contains("AsImmutableArray"));      // stelem into a caller-owned array
        Assert.Contains(v, f => f.Contains("WrapStaticWritten") && f.Contains("AsImmutableArray")); // stelem into a static array
        Assert.Contains(v, f => f.Contains("WrapInitStatic") && f.Contains("AsImmutableArray"));    // InitializeArray over a caller-owned array
        Assert.Contains(v, f => f.Contains("WrapLocalNew") && f.Contains("AsImmutableArray"));      // newarr, then a local: the producer is the local, listing needed
        Assert.Contains(v, f => f.Contains("Pin") && f.Contains("GCHandle.Alloc"));
        Assert.Contains(v, f => f.Contains("Copy") && f.Contains("Marshal.Copy"));
        Assert.DoesNotContain(v, f => f.Contains("Clean") || f.Contains("Fresh")); // collection expressions wrap a fresh array
        // An allow entry covers exactly its pair and its count: another callee in the same method, or a second call, still fails.
        var one = new Dictionary<string, (int, string)> { ["Singularity.Architecture.Tests.UnsafeMutant.Two -> ImmutableCollectionsMarshal.AsImmutableArray"] = (1, "x") };
        List<string> two = UnsafeViolations(UnsafeCalls([typeof(UnsafeMutant)]), one);
        Assert.Contains(two, f => f.Contains("Two -> Unsafe.As") && f.Contains("not allowed"));
        Assert.DoesNotContain(two, f => f.Contains("Two -> ImmutableCollectionsMarshal"));
        var twice = new Dictionary<string, (int, string)> { ["Singularity.Architecture.Tests.UnsafeMutant.Twice -> ImmutableCollectionsMarshal.AsImmutableArray"] = (1, "x") };
        Assert.Contains(UnsafeViolations(UnsafeCalls([typeof(UnsafeMutant)]), twice), f => f.Contains("Twice") && f.Contains("count differs"));
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
        Assert.DoesNotContain(found, f => f.Contains("Fine") || f.Contains("StaticMutant.Frozen:") || f.Contains("Wide") || f.Contains("Pairs") || f.Contains("Max") || f.Contains("<>9") || f.Contains("<>O"));
        // A closure class with a static lambda cache really exists in the mutant (the scan skips it on purpose).
        Assert.Contains(typeof(StaticMutant).GetNestedTypes(IlScanner.AllDeclared), t => t.GetFields(IlScanner.AllDeclared).Any(IsLambdaCache));
        Assert.Contains(found, f => f.Contains("AsyncOnly") && f.Contains("ThreadLocal"));          // AsyncLocal is not a ThreadLocal exception kind
        Assert.Contains(found, f => f.Contains("OpenObject") && f.Contains("listed as ImmutableObject but is None")); // unsealed, interface field
        Assert.Contains(found, f => f.Contains("MutObject") && f.Contains("listed as ImmutableObject but is None"));  // sealed, writable field
        Assert.Contains(found, f => f.Contains("InheritedObject") && f.Contains("listed as ImmutableObject but is None")); // mutable field in the base class
        Assert.DoesNotContain(found, f => f.Contains("GoodObject"));
        // An exception entry of the wrong kind or of a missing kind is reported.
        Assert.Contains(found, f => f.Contains("WrongKind") && f.Contains("listed as ThreadStatic but is None"));
        Assert.Contains(found, f => f.Contains("Table") && f.Contains("allow-list"));       // a static readonly array is writable: never allowed
        Assert.Contains(found, f => f.Contains("ImmBuilder") && f.Contains("allow-list"));  // ImmutableArray<int>.Builder is mutable
        Assert.Contains(found, f => f.Contains("DictBuilder") && f.Contains("allow-list"));
        Assert.Contains(found, f => f.Contains("ImmDict") && f.Contains("allow-list"));         // comparer may be stateful
        Assert.Contains(found, f => f.Contains("SetOfInt") && f.Contains("allow-list"));
    }

    private static readonly Dictionary<string, (Kind Kind, string Reason)> MutantExceptions = new()
    {
        ["Singularity.Architecture.Tests.StaticMutant.AsyncOnly"] = (Kind.InstanceThreadLocal, "an AsyncLocal listed as a ThreadLocal"),
        ["Singularity.Architecture.Tests.StaticMutant.OpenObject"] = (Kind.ImmutableObject, "unsealed class with an interface field"),
        ["Singularity.Architecture.Tests.StaticMutant.MutObject"] = (Kind.ImmutableObject, "sealed class with a writable field"),
        ["Singularity.Architecture.Tests.StaticMutant.InheritedObject"] = (Kind.ImmutableObject, "sealed class whose base class has a writable field"),
        ["Singularity.Architecture.Tests.StaticMutant.GoodObject"] = (Kind.ImmutableObject, "sealed class of readonly allowed fields"),
        ["Singularity.Architecture.Tests.StaticMutant.WrongKind"] = (Kind.ThreadStatic, "DEBT-0070: a plain static named like a ThreadStatic entry"),
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

internal class OpenClass { public readonly IList<int> Items = []; }
internal sealed class MutClass { public int Count = 1; }
internal class MutBase { public int Count = 1; }
internal sealed class InheritedClass : MutBase { public readonly long Mask = 1; }
internal sealed class GoodClass { public readonly long Mask = 1; public readonly ImmutableArray<int> Ids = [1]; }

internal static class FakeHelpers { public static int[] InitializeArray(int[] a) => a; } // same method name as RuntimeHelpers.InitializeArray, returns the caller's array

internal static class UnsafeMutant
{
    public static ImmutableArray<int> Wrap(int[] a) => System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(a);
    public static int[]? Unwrap(ImmutableArray<int> a) => System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(a);
    public static string Cast(object o) => System.Runtime.CompilerServices.Unsafe.As<string>(o);
    public static ref int Ref(in int x) => ref System.Runtime.CompilerServices.Unsafe.AsRef(in x);
    private static readonly ImmutableArray<int> Shared = [1, 2];
    private static readonly List<int> SharedList = [1];
    public static void PokeStatic() { ref int r = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(Shared.AsSpan()); r = 5; }
    public static Span<int> SpanOfStatic() => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(SharedList);
    public static unsafe void Fixed(int[] a) { fixed (int* p = a) { *p = 1; } }
    public static unsafe void Raw(int* p) { *p = 2; }
    // Two callees in one method (an entry for the first must not exempt the second); one callee twice (an entry with count 1 must not exempt both).
    public static ImmutableArray<int> Two(int[] a, object o) { _ = System.Runtime.CompilerServices.Unsafe.As<string>(o); return System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(a); }
    public static (ImmutableArray<int>, ImmutableArray<int>) Twice(int[] a, int[] b) => (System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(a), System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(b));
    public static int Clean(int x) => x + 1;
    private static readonly int[] StaticArr = [1, 2];
    // A write into an array the caller owns and a wrap of it: the array is NOT fresh although the pattern ends in stelem / InitializeArray.
    public static ImmutableArray<int> WrapWritten(int[] a) { a[0] = 1; return System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(a); }
    public static ImmutableArray<int> WrapStaticWritten() { StaticArr[0] = 3; return System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(StaticArr); }
    public static ImmutableArray<int> WrapLocalNew(int n) { var a = new int[n]; return System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(a); }
    public static ImmutableArray<int> WrapInitStatic() => System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(FakeHelpers.InitializeArray(StaticArr));
    public static IntPtr Pin(object o) => System.Runtime.InteropServices.GCHandle.Alloc(o, System.Runtime.InteropServices.GCHandleType.Pinned).AddrOfPinnedObject();
    public static void Copy(int[] a, IntPtr p) => System.Runtime.InteropServices.Marshal.Copy(a, 0, p, a.Length);
    public static ImmutableArray<int> FreshCollection(int n) => [n, n + 1];
    public static ImmutableArray<int> FreshSpread(List<int> l) => [.. l];
}

internal static class StaticMutant
{
    public static int Counter = 1;
    [ThreadStatic] public static long[]? Buffer = null;
    public static readonly List<int> Shared = [];
    public static readonly ThreadLocal<int> Local = new();
    public static readonly AsyncLocal<int> Async = new();
    public static readonly AsyncLocal<int> AsyncOnly = new();
    public const int Max = 3;
    public static readonly string Fine = "x";
    public static int WrongKind = 2;
    public static readonly string[] Table = ["a"];
    public static readonly ImmutableArray<int>.Builder ImmBuilder = ImmutableArray.CreateBuilder<int>();
    public static readonly ImmutableDictionary<int, int>.Builder DictBuilder = ImmutableDictionary.CreateBuilder<int, int>();

    public static readonly IList<int> AsInterface = new List<int>();
    public static readonly ThreadLocal<int>[] LocalArray = [];
    public static readonly object Boxed = new();
    public static readonly ImmutableArray<string> Frozen = ["a"];
    public static readonly Int128 Wide = 1;
    public static readonly OpenClass OpenObject = new();
    public static readonly MutClass MutObject = new();
    public static readonly GoodClass GoodObject = new();
    public static readonly InheritedClass InheritedObject = new();
    public static readonly ImmutableDictionary<int, int> ImmDict = ImmutableDictionary<int, int>.Empty;
    public static readonly System.Collections.Frozen.FrozenSet<int> SetOfInt = System.Collections.Frozen.FrozenSet<int>.Empty;
    public static readonly ImmutableArray<(string Name, int Value)> Pairs = [("a", 1)];
    // A static lambda in a method: the compiler caches it in a static field of a closure class, which the scan must not report.
    public static int Twice(int x)
    {
        Func<int, int> f = static y => y * 2;
        return f(x);
    }
}
