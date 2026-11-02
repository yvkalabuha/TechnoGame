using System.Reflection;
using System.Reflection.Emit;

namespace Singularity.Architecture.Tests;

/// <summary>
/// Minimal IL reader: yields the members and types referenced by method bodies.
/// Chosen over source analysis because compiled IL sees through aliases, <c>using static</c>,
/// <c>var</c>, lambdas and generated code, and needs no C# parser (no extra NuGet).
/// </summary>
internal static class IlScanner
{
    private static readonly OpCode?[] OneByte = new OpCode?[256];
    private static readonly OpCode?[] TwoByte = new OpCode?[256];

    static IlScanner()
    {
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var op = (OpCode)field.GetValue(null)!;
            ushort value = (ushort)op.Value;
            if (op.Size == 1)
                OneByte[value] = op;
            else
                TwoByte[value & 0xFF] = op;
        }
    }

    public const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
        BindingFlags.Static | BindingFlags.DeclaredOnly;

    public readonly record struct Instruction(OpCode OpCode, object? Operand);

    /// <summary>Instructions of a method body; operands of member/type tokens are resolved.</summary>
    public static IEnumerable<Instruction> Read(MethodBase method)
    {
        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
            yield break;

        Module module = method.Module;
        Type[]? typeArgs = method.DeclaringType is { IsGenericType: true } t ? t.GetGenericArguments() : null;
        Type[]? methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;

        int pos = 0;
        while (pos < il.Length)
        {
            OpCode op = il[pos] == 0xFE
                ? TwoByte[il[++pos]] ?? throw Bad(method, pos)
                : OneByte[il[pos]] ?? throw Bad(method, pos);
            pos++;

            object? operand = null;
            switch (op.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    pos += 1;
                    break;
                case OperandType.InlineVar:
                    pos += 2;
                    break;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.ShortInlineR:
                    pos += 4;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    pos += 8;
                    break;
                case OperandType.InlineSwitch:
                    int count = BitConverter.ToInt32(il, pos);
                    pos += 4 + 4 * count;
                    break;
                case OperandType.InlineMethod:
                case OperandType.InlineField:
                case OperandType.InlineType:
                case OperandType.InlineTok:
                    operand = module.ResolveMember(BitConverter.ToInt32(il, pos), typeArgs, methodArgs);
                    pos += 4;
                    break;
                default:
                    throw Bad(method, pos);
            }
            yield return new Instruction(op, operand);
        }
    }

    private static InvalidOperationException Bad(MethodBase method, int pos) =>
        new($"Cannot decode IL of {method.DeclaringType}.{method.Name} at offset {pos}.");
}
