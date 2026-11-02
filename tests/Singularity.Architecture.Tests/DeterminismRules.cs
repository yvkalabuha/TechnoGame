using System.Collections.Concurrent;
using System.Diagnostics;
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

    // Single-precision IL opcodes: float values can appear without a float-typed member.
    private static readonly OpCode[] FloatOpCodes =
    [
        OpCodes.Ldc_R4, OpCodes.Conv_R4, OpCodes.Ldind_R4, OpCodes.Stind_R4,
        OpCodes.Ldelem_R4, OpCodes.Stelem_R4,
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

    // Hash-ordered collections: enumerating them makes the order depend on hashing.
    private static readonly Type[] UnorderedCollections =
    [
        typeof(Dictionary<,>), typeof(HashSet<>), typeof(ConcurrentDictionary<,>),
    ];

    public static List<string> Check(IEnumerable<Type> types)
    {
        var violations = new List<string>();
        foreach (Type type in types)
        {
            foreach (FieldInfo field in type.GetFields(IlScanner.AllDeclared))
                CheckType(field.FieldType, $"{type}.{field.Name} (field)", violations);

            var methods = type.GetMethods(IlScanner.AllDeclared).Cast<MethodBase>()
                .Concat(type.GetConstructors(IlScanner.AllDeclared));
            foreach (MethodBase method in methods)
                CheckMethod(method, $"{type}.{method.Name}", violations);
        }
        return violations;
    }

    private static void CheckMethod(MethodBase method, string site, List<string> violations)
    {
        if (method is MethodInfo info)
            CheckType(info.ReturnType, $"{site} (return)", violations);
        foreach (ParameterInfo parameter in method.GetParameters())
            CheckType(parameter.ParameterType, $"{site} (parameter {parameter.Name})", violations);
        foreach (LocalVariableInfo local in method.GetMethodBody()?.LocalVariables ?? [])
            CheckType(local.LocalType, $"{site} (local #{local.LocalIndex})", violations);

        foreach (IlScanner.Instruction instruction in IlScanner.Read(method))
        {
            if (Array.IndexOf(FloatOpCodes, instruction.OpCode) >= 0)
                violations.Add($"{site}: single-precision opcode {instruction.OpCode.Name}");

            switch (instruction.Operand)
            {
                case MethodBase callee:
                    CheckCallee(callee, site, violations);
                    break;
                case FieldInfo field:
                    CheckType(field.FieldType, $"{site} uses field {field.Name}", violations);
                    break;
                case Type type:
                    CheckType(type, $"{site} uses type", violations);
                    break;
            }
        }
    }

    private static void CheckCallee(MethodBase callee, string site, List<string> violations)
    {
        Type? owner = callee.DeclaringType;
        string name = $"{owner?.Name}.{callee.Name}";

        if (owner == typeof(Math) && Array.IndexOf(AllowedMath, callee.Name) < 0)
            violations.Add($"{site}: System.Math.{callee.Name} is not in the allowed list");
        if (owner == typeof(MathF))
            violations.Add($"{site}: MathF.{callee.Name}");
        if (owner == typeof(Random) || owner == typeof(Stopwatch))
            violations.Add($"{site}: {name}");
        foreach ((Type type, string member) in BannedMembers)
        {
            if (owner == type && callee.Name == member)
                violations.Add($"{site}: {name}");
        }
        if (owner is { IsGenericType: true } generic && callee.Name == "GetEnumerator" &&
            Array.IndexOf(UnorderedCollections, generic.GetGenericTypeDefinition()) >= 0)
            violations.Add($"{site}: enumerates {generic.Name}");

        if (owner is not null)
            CheckType(owner, $"{site} calls {name}", violations);
        if (callee is MethodInfo info)
            CheckType(info.ReturnType, $"{site} calls {name} (return)", violations);
        foreach (ParameterInfo parameter in callee.GetParameters())
            CheckType(parameter.ParameterType, $"{site} calls {name} (parameter)", violations);
    }

    private static void CheckType(Type type, string site, List<string> violations)
    {
        if (ContainsForbiddenType(type))
            violations.Add($"{site}: forbidden type {type}");
    }

    private static bool ContainsForbiddenType(Type type)
    {
        if (type.HasElementType)
            return ContainsForbiddenType(type.GetElementType()!);
        if (type == typeof(float) || type == typeof(Random) || type == typeof(MathF))
            return true;
        return type.IsGenericType && !type.IsGenericTypeDefinition &&
               type.GetGenericArguments().Any(ContainsForbiddenType);
    }
}
