using System.Reflection;
using Singularity.Core.Industry;
using Singularity.Core.Knowledge;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Architecture.Tests;

/// <summary>
/// #28, reference README I7: the capability (<c>Singularity.Core.Industry</c>) and product (<c>Singularity.Core.Products</c>)
/// layers have no randomness. No type of
/// the namespace (nested and compiler-generated types included) holds, takes, returns or stores in a local a
/// <see cref="CounterRng"/>, touches any of its members, or reads <see cref="TickContext.Rng"/>.
/// Not transitive: a call from the namespace into another Core type that draws (or an <c>IIndustryInputs</c>
/// implementation that does) is not seen. The layer calls only <c>Fx6</c>, Kernel tables and its own types
/// today; reviewers check new outgoing calls.
/// </summary>
public class CapabilityNoRngTests
{
    private const string CapabilityNamespace = "Singularity.Core.Industry";

    [Fact]
    public void CapabilityLayer_UsesNoRng()
    {
        Type[] types = typeof(IndustrySystem).Assembly.GetTypes().Where(t => t.Namespace == CapabilityNamespace).ToArray();
        Assert.Contains(typeof(IndustrySystem), types);
        Assert.Contains(typeof(IndustryMath), types);
        Assert.Empty(RngUses(types));
    }

    [Fact]
    public void ProductLayer_UsesNoRng()
    {
        Type[] types = typeof(ProductsSystem).Assembly.GetTypes().Where(t => t.Namespace == "Singularity.Core.Products").ToArray();
        Assert.Contains(typeof(ProductsSystem), types);
        Assert.Contains(typeof(ProductsMath), types);
        Assert.Contains(typeof(DesignRules), types);
        Assert.Empty(RngUses(types));
    }

    [Fact]
    public void Check_FlagsTheKnowledgeLayer()
    {
        // Canary: the knowledge layer draws hazards, so the same check must see it.
        Assert.NotEmpty(RngUses([typeof(KnowledgeSystem)]));
        Assert.NotEmpty(RngUses([typeof(KnowledgeRng)]));
    }

    private static List<string> RngUses(IEnumerable<Type> types)
    {
        var uses = new List<string>();
        foreach (Type type in types)
        {
            foreach (FieldInfo f in type.GetFields(IlScanner.AllDeclared))
            {
                if (IsRng(f.FieldType))
                    uses.Add($"{type}.{f.Name} (field)");
            }
            foreach (MethodBase m in type.GetMethods(IlScanner.AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(IlScanner.AllDeclared)))
            {
                string site = $"{type}.{m.Name}";
                if (m is MethodInfo mi && IsRng(mi.ReturnType))
                    uses.Add($"{site} (return)");
                foreach (ParameterInfo p in m.GetParameters())
                {
                    if (IsRng(p.ParameterType))
                        uses.Add($"{site} (parameter {p.Name})");
                }
                foreach (LocalVariableInfo l in m.GetMethodBody()?.LocalVariables ?? [])
                {
                    if (IsRng(l.LocalType))
                        uses.Add($"{site} (local)");
                }
                foreach (IlScanner.Instruction i in IlScanner.Read(m))
                {
                    if (i.Operand is MemberInfo member && (IsRng(member as Type) || IsRng(member.DeclaringType) || IsRngProperty(member)))
                        uses.Add($"{site} → {member.DeclaringType?.Name}.{member.Name}");
                }
            }
        }
        return uses;
    }

    private static bool IsRng(Type? t)
    {
        if (t is null)
            return false;
        if (t.IsByRef || t.IsArray || t.IsPointer)
            return IsRng(t.GetElementType());
        return t == typeof(CounterRng) || (t.IsGenericType && t.GetGenericArguments().Any(IsRng));
    }

    private static bool IsRngProperty(MemberInfo m) =>
        m.DeclaringType == typeof(TickContext) && m.Name == "get_Rng";
}
