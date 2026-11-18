using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Channels;

namespace Singularity.Architecture.Tests;

/// <summary>
/// Determinism rules for world-state assemblies (ADR-0003, ADR-0004, ADR-0007, EXP-0001 review #9).
/// Returns human-readable violations; an empty list means the code is clean.
/// </summary>
internal static class DeterminismRules
{
    public static readonly string[] AllowedMath = ["Sqrt", "Floor", "Abs", "Min", "Max", "BigMul"];

    // System.Double static methods: the System.Math allow-list plus exact classification checks.
    // Operators (op_*) are exact IEEE operations and are allowed separately.
    private static readonly string[] AllowedDoubleStatics =
    [
        .. AllowedMath,
        "IsFinite", "IsNaN", "IsInfinity", "IsPositiveInfinity", "IsNegativeInfinity",
        "IsNegative", "IsPositive", "IsNormal", "IsSubnormal", "IsInteger",
    ];

    // Single-precision IL opcodes: float values can appear without a float-typed member.
    private static readonly OpCode[] FloatOpCodes =
    [
        OpCodes.Ldc_R4, OpCodes.Conv_R4, OpCodes.Ldind_R4, OpCodes.Stind_R4,
        OpCodes.Ldelem_R4, OpCodes.Stelem_R4,
    ];

    // Double-precision IL opcodes, forbidden in forbidDouble mode (ADR-0003 R1).
    private static readonly OpCode[] DoubleOpCodes =
    [
        OpCodes.Ldc_R8, OpCodes.Conv_R8, OpCodes.Conv_R_Un, OpCodes.Ldind_R8, OpCodes.Stind_R8,
        OpCodes.Ldelem_R8, OpCodes.Stelem_R8,
    ];

    private static readonly (Type Type, string Member)[] BannedMembers =
    [
        (typeof(DateTime), "get_Now"), (typeof(DateTime), "get_UtcNow"), (typeof(DateTime), "get_Today"),
        (typeof(DateTimeOffset), "get_Now"), (typeof(DateTimeOffset), "get_UtcNow"),
        (typeof(Environment), "get_TickCount"), (typeof(Environment), "get_TickCount64"),
        (typeof(Guid), "NewGuid"),
        (typeof(Parallel), "ForEach"), (typeof(Parallel), "ForEachAsync"), (typeof(Parallel), "Invoke"),
        (typeof(ParallelEnumerable), "AsParallel"),
    ];

    // Hash- or thread-ordered collections: enumerating them makes the order depend on hashing or
    // scheduling. Dictionary/set interfaces are listed conservatively: the implementation behind
    // them may be hash-based. Ordered types (SortedDictionary, ImmutableSortedSet, List…) are fine.
    private static readonly Type[] UnorderedCollections =
    [
        typeof(Dictionary<,>), typeof(HashSet<>), typeof(ConcurrentDictionary<,>), typeof(ConcurrentBag<>),
        typeof(Dictionary<,>.KeyCollection), typeof(Dictionary<,>.ValueCollection),
        typeof(IDictionary<,>), typeof(IReadOnlyDictionary<,>), typeof(ISet<>), typeof(IReadOnlySet<>),
        typeof(FrozenDictionary<,>), typeof(FrozenSet<>),
        typeof(ImmutableDictionary<,>), typeof(ImmutableHashSet<>),
        typeof(ImmutableDictionary<,>.Builder), typeof(ImmutableHashSet<>.Builder),
        typeof(IImmutableDictionary<,>), typeof(IImmutableSet<>),
    ];

    // ADR-0007 rev 2: thread-scheduled containers; any use (not only enumeration) is banned.
    private static readonly Type[] BannedConcurrent =
    [
        typeof(ConcurrentQueue<>), typeof(ConcurrentStack<>), typeof(ConcurrentBag<>),
        typeof(Channel), typeof(Channel<>), typeof(ChannelReader<>), typeof(ChannelWriter<>),
        // ADR-0007 rev 3 p. 3 and 8: blocking queue and free threading (Task.Run, new Thread, ThreadPool).
        typeof(BlockingCollection<>), typeof(Task), typeof(Task<>), typeof(TaskFactory), typeof(TaskFactory<>),
        typeof(Thread), typeof(ThreadPool),
    ];

    /// <summary>
    /// ADR-0007 rev 3 p. 8: the only methods ("Namespace.Type.Method") that may call Parallel.For.
    /// A new site means an allowlist edit with review.
    /// </summary>
    public static readonly string[] ParallelForAllowlist =
    [
        "Singularity.Kernel.TickRunner.RunTick",
        "Singularity.Session.ViewBuilder.Inputs",
    ];

    // Culture-dependent string members whose overloads need an explicit ordinal StringComparison.
    private static readonly string[] CultureStringMethods = ["Compare", "StartsWith", "EndsWith", "IndexOf", "LastIndexOf", "Equals"];
    private static readonly string[] CultureComparerGetters =
        ["get_CurrentCulture", "get_CurrentCultureIgnoreCase", "get_InvariantCulture", "get_InvariantCultureIgnoreCase", "Create"];
    private static readonly string[] SortingMethods = ["OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "Order", "OrderDescending"];

    private static readonly Type[] UnorderedNonGeneric =
    [
        typeof(Hashtable), typeof(IDictionary), typeof(HybridDictionary), typeof(StringDictionary),
    ];

    // Frozen collections hand out their keys/values as arrays in hash order.
    private static readonly Type[] FrozenCollections = [typeof(FrozenDictionary<,>), typeof(FrozenSet<>)];
    private static readonly string[] FrozenOrderMembers = ["get_Keys", "get_Values", "get_Items"];

    // Generic math interfaces whose members are not correctly rounded (pow, exp, log, trig, cbrt,
    // FMA…). double.Pow etc. are caught by the System.Double rule; these catch T.Pow in generic code.
    private static readonly Type[] BannedGenericMath =
    [
        typeof(IPowerFunctions<>), typeof(IExponentialFunctions<>), typeof(ILogarithmicFunctions<>),
        typeof(ITrigonometricFunctions<>), typeof(IHyperbolicFunctions<>), typeof(IRootFunctions<>),
        typeof(IFloatingPointIeee754<>),
    ];

    /// <param name="forbidDouble">
    /// ADR-0003 R1: also reject <see cref="double"/> anywhere (fields, signatures, locals, callees,
    /// r8 opcodes). Used for Core outside the R4 exceptions.
    /// </param>
    /// <param name="parallelForAllowlist">Methods allowed to call Parallel.For; defaults to <see cref="ParallelForAllowlist"/>.</param>
    public static List<string> Check(IEnumerable<Type> types, bool forbidDouble = false,
        IReadOnlyCollection<string>? parallelForAllowlist = null)
    {
        parallelForAllowlist ??= ParallelForAllowlist;
        var violations = new List<string>();
        foreach (Type type in types)
        {
            foreach (FieldInfo field in type.GetFields(IlScanner.AllDeclared))
                CheckType(field.FieldType, $"{type}.{field.Name} (field)", violations, forbidDouble);

            foreach (MethodBase method in DeclaredMethods(type))
                CheckMethod(method, $"{type}.{method.Name}", violations, forbidDouble, parallelForAllowlist);
        }
        return violations;
    }

    /// <summary>
    /// ADR-0003 R3: the members ("Type.Member", sorted, distinct) that use <see cref="double"/> in a
    /// field type, signature, local, callee or opcode. Compared against an exact allow-list.
    /// </summary>
    public static List<string> DoubleUsers(IEnumerable<Type> types)
    {
        var users = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Type type in types)
        {
            foreach (FieldInfo field in type.GetFields(IlScanner.AllDeclared))
            {
                if (ContainsForbiddenType(field.FieldType, forbidDouble: true) &&
                    !ContainsForbiddenType(field.FieldType, forbidDouble: false))
                    users.Add($"{type.Name}.{field.Name}");
            }
            foreach (MethodBase method in DeclaredMethods(type))
            {
                // The double check only adds violations, so a longer list means the method uses double.
                var withDouble = new List<string>();
                var without = new List<string>();
                CheckMethod(method, "", withDouble, forbidDouble: true, ParallelForAllowlist);
                CheckMethod(method, "", without, forbidDouble: false, ParallelForAllowlist);
                if (withDouble.Count > without.Count)
                    users.Add($"{type.Name}.{method.Name}");
            }
        }
        return [.. users];
    }

    private static IEnumerable<MethodBase> DeclaredMethods(Type type) =>
        type.GetMethods(IlScanner.AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(IlScanner.AllDeclared));

    private static void CheckMethod(MethodBase method, string site, List<string> violations, bool forbidDouble,
        IReadOnlyCollection<string> parallelForAllowlist)
    {
        if (method is MethodInfo info)
            CheckType(info.ReturnType, $"{site} (return)", violations, forbidDouble);
        foreach (ParameterInfo parameter in method.GetParameters())
            CheckType(parameter.ParameterType, $"{site} (parameter {parameter.Name})", violations, forbidDouble);
        foreach (LocalVariableInfo local in method.GetMethodBody()?.LocalVariables ?? [])
            CheckType(local.LocalType, $"{site} (local #{local.LocalIndex})", violations, forbidDouble);

        // Indirect enumeration (via an interface, LINQ or a constructor taking IEnumerable) cannot be
        // tied to a concrete collection without data-flow analysis. Conservative rule: a method that
        // touches an unordered collection must not enumerate anything indirectly.
        bool touchesUnordered = TouchesUnordered(method);

        // Record/struct GetHashCode synthesised by the compiler hashes string members through
        // EqualityComparer<string>; the hash only serves lookups and never leaves the process.
        bool generatedHash = method.Name == "GetHashCode" &&
            method.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null;

        Type? previous = null; // static type of the value the previous instruction pushed
        IlScanner.Instruction? prevInstruction = null;
        foreach (IlScanner.Instruction instruction in IlScanner.Read(method))
        {
            IlScanner.Instruction? before = prevInstruction;
            prevInstruction = instruction;
            // C# emits Object.GetHashCode for a string receiver, so look at what was just loaded.
            if (instruction.Operand is MethodBase { Name: "GetHashCode", DeclaringType: { } hashOwner } &&
                (hashOwner == typeof(string) || (hashOwner == typeof(object) && previous == typeof(string))))
                violations.Add($"{site}: string.GetHashCode is process-dependent");
            previous = PushedType(method, instruction);

            if (Array.IndexOf(FloatOpCodes, instruction.OpCode) >= 0)
                violations.Add($"{site}: single-precision opcode {instruction.OpCode.Name}");
            if (forbidDouble && Array.IndexOf(DoubleOpCodes, instruction.OpCode) >= 0)
                violations.Add($"{site}: double-precision opcode {instruction.OpCode.Name}");

            switch (instruction.Operand)
            {
                case MethodBase callee:
                    CheckCallee(callee, site, violations, forbidDouble);
                    CheckStrings(callee, before, site, violations, generatedHash);
                    if (callee.DeclaringType == typeof(Parallel) && callee.Name == "For" &&
                        Array.IndexOf(parallelForAllowlist.ToArray(), site) < 0)
                        violations.Add($"{site}: Parallel.For outside the allowlist");
                    if (touchesUnordered && EnumeratesIndirectly(callee))
                        violations.Add($"{site}: enumerates an unordered collection indirectly via {callee.DeclaringType?.Name}.{callee.Name}");
                    break;
                case FieldInfo field:
                    CheckType(field.FieldType, $"{site} uses field {field.Name}", violations, forbidDouble);
                    break;
                case Type type:
                    CheckType(type, $"{site} uses type", violations, forbidDouble);
                    break;
            }
        }
    }

    // Best-effort static type of the value an instruction pushes (enough for `s.GetHashCode()`).
    private static Type? PushedType(MethodBase method, IlScanner.Instruction i)
    {
        string? name = i.OpCode.Name;
        if (name is null)
            return null;
        if (name == "ldstr")
            return typeof(string);
        if (i.Operand is MethodInfo called && name is "call" or "callvirt")
            return called.ReturnType;
        if (i.Operand is FieldInfo field && name.StartsWith("ldfld", StringComparison.Ordinal))
            return field.FieldType;
        ParameterInfo[] parameters = method.GetParameters();
        IList<LocalVariableInfo> locals = method.GetMethodBody()?.LocalVariables ?? [];
        int? index = name switch
        {
            "ldarg.0" or "ldloc.0" => 0,
            "ldarg.1" or "ldloc.1" => 1,
            "ldarg.2" or "ldloc.2" => 2,
            "ldarg.3" or "ldloc.3" => 3,
            _ => i.Operand is byte b ? b : i.Operand is short sh ? sh : i.Operand is int n ? n : null,
        };
        if (index is null)
            return null;
        if (name.StartsWith("ldloc", StringComparison.Ordinal) && !name.StartsWith("ldloca", StringComparison.Ordinal))
            return index < locals.Count ? locals[index.Value].LocalType : null;
        if (name.StartsWith("ldarg", StringComparison.Ordinal) && !name.StartsWith("ldarga", StringComparison.Ordinal))
        {
            int shift = method.IsStatic ? 0 : 1;
            return index - shift is >= 0 and var k && k < parameters.Length ? parameters[k].ParameterType : null;
        }
        return null;
    }

    private static void CheckCallee(MethodBase callee, string site, List<string> violations, bool forbidDouble)
    {
        Type? owner = callee.DeclaringType;
        string name = $"{owner?.Name}.{callee.Name}";

        if (owner == typeof(Math) && Array.IndexOf(AllowedMath, callee.Name) < 0)
            violations.Add($"{site}: System.Math.{callee.Name} is not in the allowed list");
        if (owner == typeof(double) && callee.IsStatic && !callee.Name.StartsWith("op_", StringComparison.Ordinal) &&
            Array.IndexOf(AllowedDoubleStatics, callee.Name) < 0 && !IsCultureFixedParse(callee))
            violations.Add($"{site}: System.Double.{callee.Name} is not in the allowed list");
        if (owner is { IsInterface: true, IsGenericType: true } math &&
            Array.IndexOf(BannedGenericMath, math.GetGenericTypeDefinition()) >= 0 &&
            !(math.GetGenericTypeDefinition() == typeof(IRootFunctions<>) && callee.Name == "Sqrt"))
            violations.Add($"{site}: generic math {math.Name}.{callee.Name}");
        if (IsUnorderedType(owner) && callee.Name is "GetEnumerator" or "CopyTo")
            violations.Add($"{site}: enumerates {owner!.Name}");
        if (owner is { IsGenericType: true } frozen &&
            Array.IndexOf(FrozenCollections, frozen.GetGenericTypeDefinition()) >= 0 &&
            Array.IndexOf(FrozenOrderMembers, callee.Name) >= 0)
            violations.Add($"{site}: {frozen.Name}.{callee.Name} exposes hash order");
        if (owner == typeof(MathF))
            violations.Add($"{site}: MathF.{callee.Name}");
        if (owner == typeof(Random) || owner == typeof(Stopwatch))
            violations.Add($"{site}: {name}");
        foreach ((Type type, string member) in BannedMembers)
        {
            if (owner == type && callee.Name == member)
                violations.Add($"{site}: {name}");
        }

        if (owner is not null)
            CheckType(owner, $"{site} calls {name}", violations, forbidDouble);
        if (callee is MethodInfo info)
            CheckType(info.ReturnType, $"{site} calls {name} (return)", violations, forbidDouble);
        foreach (ParameterInfo parameter in callee.GetParameters())
            CheckType(parameter.ParameterType, $"{site} calls {name} (parameter)", violations, forbidDouble);
        // Run<double>(long) -> long hides the type in the method instantiation only.
        if (callee.IsGenericMethod)
        {
            foreach (Type argument in callee.GetGenericArguments())
                CheckType(argument, $"{site} calls {name} (type argument)", violations, forbidDouble);
        }
    }

    // ADR-0007 rev 3 p. 4-5: string hashing and culture-dependent comparison/sorting.
    private static void CheckStrings(MethodBase callee, IlScanner.Instruction? before, string site, List<string> violations, bool generatedHash)
    {
        Type? owner = callee.DeclaringType;
        if (owner is null)
            return;
        string name = $"{owner.Name}.{callee.Name}";
        ParameterInfo[] ps = callee.GetParameters();
        Type[] generic = callee.IsGenericMethod ? callee.GetGenericArguments() : [];
        bool hasComparer = ps.Any(p => p.ParameterType.IsGenericType &&
            p.ParameterType.GetGenericTypeDefinition() is var d && (d == typeof(IComparer<>) || d == typeof(Comparison<>)));
        bool ownerGeneric = owner.IsGenericType;
        Type[] ownerArgs = ownerGeneric ? owner.GetGenericArguments() : [];
        Type? ownerDef = ownerGeneric ? owner.GetGenericTypeDefinition() : null;

        // Hash of a string: HashCode.Combine/Add<string>, StringComparer/EqualityComparer<string>.GetHashCode(string).
        if (owner == typeof(HashCode) && callee.Name is "Combine" or "Add" && generic.Contains(typeof(string)))
            violations.Add($"{site}: {name} hashes a string");
        if (!generatedHash && callee.Name == "GetHashCode" && owner != typeof(object) && ps.Length == 1 && ps[0].ParameterType == typeof(string))
            violations.Add($"{site}: {name}(string) hashes a string");

        // string members: need StringComparison.Ordinal*, passed as a constant right before the call.
        if (owner == typeof(string) && Array.IndexOf(CultureStringMethods, callee.Name) >= 0)
        {
            bool takesComparison = ps.Length > 0 && ps[^1].ParameterType == typeof(StringComparison);
            bool stringArg = callee.Name is "Compare" or "Equals" || ps.Length > 0 && ps[0].ParameterType == typeof(string);
            if (takesComparison ? !IsOrdinalConstant(before) : callee.Name != "Equals" && stringArg)
                violations.Add($"{site}: culture-dependent {name}");
        }
        if ((owner == typeof(string) || owner == typeof(IComparable<string>)) && callee.Name == "CompareTo")
            violations.Add($"{site}: culture-dependent string.CompareTo");
        if (owner == typeof(Comparer<string>) && callee.Name == "get_Default")
            violations.Add($"{site}: Comparer<string>.Default is culture-dependent");
        if (owner == typeof(string) && callee.Name is "ToLower" or "ToUpper")
            violations.Add($"{site}: {name} is culture-dependent (use the Invariant variant)");
        if (owner == typeof(StringComparer) && Array.IndexOf(CultureComparerGetters, callee.Name) >= 0)
            violations.Add($"{site}: culture-dependent StringComparer.{callee.Name}");
        if (owner == typeof(CompareInfo))
            violations.Add($"{site}: culture-dependent {name}");

        // Sorted containers and sorts keyed by string need an explicit comparer.
        bool sortedOwner = ownerDef == typeof(SortedDictionary<,>) || ownerDef == typeof(SortedSet<>) ||
            ownerDef == typeof(SortedList<,>) || ownerDef == typeof(ImmutableSortedDictionary<,>.Builder) ||
            ownerDef == typeof(ImmutableSortedSet<>.Builder);
        if (sortedOwner && callee is ConstructorInfo && ownerArgs.Length > 0 && ownerArgs[0] == typeof(string) && !hasComparer)
            violations.Add($"{site}: {owner.Name} with string keys needs an explicit comparer");
        if (owner.Name.StartsWith("ImmutableSorted", StringComparison.Ordinal) && !ownerGeneric &&
            generic.Length > 0 && generic[0] == typeof(string) && !hasComparer)
            violations.Add($"{site}: {name} with string keys needs an explicit comparer");
        if (owner == typeof(Enumerable) && Array.IndexOf(SortingMethods, callee.Name) >= 0 &&
            generic.Length > 0 && generic[^1] == typeof(string) && !hasComparer)
            violations.Add($"{site}: {name} on strings needs an explicit comparer");
        if (owner == typeof(Array) && callee.Name == "Sort" && generic.Length > 0 && generic[0] == typeof(string) && !hasComparer)
            violations.Add($"{site}: Array.Sort on strings needs an explicit comparer");
        if (ownerDef == typeof(List<>) && ownerArgs[0] == typeof(string) && callee.Name == "Sort" && !hasComparer)
            violations.Add($"{site}: List<string>.Sort needs an explicit comparer");
    }

    private static bool IsOrdinalConstant(IlScanner.Instruction? i)
    {
        if (i is null)
            return false;
        int? value = i.Value.OpCode.Name switch
        {
            "ldc.i4.4" => 4,
            "ldc.i4.5" => 5,
            "ldc.i4.s" or "ldc.i4" => Convert.ToInt32(i.Value.Operand, CultureInfo.InvariantCulture),
            _ => null,
        };
        return value is 4 or 5; // StringComparison.Ordinal, OrdinalIgnoreCase
    }

    private static bool TouchesUnordered(MethodBase method)
    {
        if (method.GetParameters().Any(p => ContainsUnordered(p.ParameterType)) ||
            (method.GetMethodBody()?.LocalVariables ?? []).Any(l => ContainsUnordered(l.LocalType)))
            return true;
        foreach (IlScanner.Instruction instruction in IlScanner.Read(method))
        {
            bool hit = instruction.Operand switch
            {
                MethodInfo callee => ContainsUnordered(callee.DeclaringType) || ContainsUnordered(callee.ReturnType),
                MethodBase callee => ContainsUnordered(callee.DeclaringType),
                FieldInfo field => ContainsUnordered(field.FieldType),
                Type type => ContainsUnordered(type),
                _ => false,
            };
            if (hit)
                return true;
        }
        return false;
    }

    // Any enumerator counts: e.g. foreach over FrozenDictionary.Keys goes through ImmutableArray.
    private static bool EnumeratesIndirectly(MethodBase callee)
    {
        Type? owner = callee.DeclaringType;
        if (owner == typeof(Enumerable))
            return true;
        if (callee.Name == "GetEnumerator")
            return true;
        return callee.GetParameters().Any(p => IsEnumerableInterface(p.ParameterType));
    }

    private static bool IsEnumerableInterface(Type type) =>
        type == typeof(IEnumerable) ||
        (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>));

    /// <summary>
    /// double.Parse/TryParse with an explicit format provider: IEEE-correctly rounded since .NET Core 3.0,
    /// so the result is the same everywhere. Overloads without a provider use the current culture.
    /// </summary>
    private static bool IsCultureFixedParse(MethodBase callee) =>
        callee.Name is "Parse" or "TryParse" &&
        callee.GetParameters().Any(p => p.ParameterType == typeof(IFormatProvider));

    private static bool IsUnorderedType(Type? type) =>
        type is not null &&
        (Array.IndexOf(UnorderedNonGeneric, type) >= 0 ||
         (type.IsGenericType && Array.IndexOf(UnorderedCollections, type.GetGenericTypeDefinition()) >= 0));

    private static bool ContainsUnordered(Type? type)
    {
        if (type is null)
            return false;
        if (type.HasElementType)
            return ContainsUnordered(type.GetElementType());
        if (IsUnorderedType(type))
            return true;
        return type.IsGenericType && type.GetGenericArguments().Any(ContainsUnordered);
    }

    private static void CheckType(Type type, string site, List<string> violations, bool forbidDouble)
    {
        if (ContainsForbiddenType(type, forbidDouble))
            violations.Add($"{site}: forbidden type {type}");
    }

    private static bool ContainsForbiddenType(Type type, bool forbidDouble)
    {
        Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        if (Array.IndexOf(BannedConcurrent, definition) >= 0)
            return true;
        if (type.HasElementType)
            return ContainsForbiddenType(type.GetElementType()!, forbidDouble);
        if (type == typeof(float) || type == typeof(Half) || type == typeof(Random) || type == typeof(MathF))
            return true;
        if (forbidDouble && type == typeof(double))
            return true;
        return type.IsGenericType && !type.IsGenericTypeDefinition &&
               type.GetGenericArguments().Any(t => ContainsForbiddenType(t, forbidDouble));
    }
}
