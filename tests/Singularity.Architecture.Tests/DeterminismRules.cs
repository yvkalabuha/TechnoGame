using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;

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
        (typeof(Parallel), "ForEach"), (typeof(Parallel), "ForEachAsync"),
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
    public static List<string> Check(IEnumerable<Type> types, bool forbidDouble = false)
    {
        var violations = new List<string>();
        foreach (Type type in types)
        {
            foreach (FieldInfo field in type.GetFields(IlScanner.AllDeclared))
                CheckType(field.FieldType, $"{type}.{field.Name} (field)", violations, forbidDouble);

            foreach (MethodBase method in DeclaredMethods(type))
                CheckMethod(method, $"{type}.{method.Name}", violations, forbidDouble);
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
                CheckMethod(method, "", withDouble, forbidDouble: true);
                CheckMethod(method, "", without, forbidDouble: false);
                if (withDouble.Count > without.Count)
                    users.Add($"{type.Name}.{method.Name}");
            }
        }
        return [.. users];
    }

    private static IEnumerable<MethodBase> DeclaredMethods(Type type) =>
        type.GetMethods(IlScanner.AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(IlScanner.AllDeclared));

    private static void CheckMethod(MethodBase method, string site, List<string> violations, bool forbidDouble)
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

        foreach (IlScanner.Instruction instruction in IlScanner.Read(method))
        {
            if (Array.IndexOf(FloatOpCodes, instruction.OpCode) >= 0)
                violations.Add($"{site}: single-precision opcode {instruction.OpCode.Name}");
            if (forbidDouble && Array.IndexOf(DoubleOpCodes, instruction.OpCode) >= 0)
                violations.Add($"{site}: double-precision opcode {instruction.OpCode.Name}");

            switch (instruction.Operand)
            {
                case MethodBase callee:
                    CheckCallee(callee, site, violations, forbidDouble);
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
