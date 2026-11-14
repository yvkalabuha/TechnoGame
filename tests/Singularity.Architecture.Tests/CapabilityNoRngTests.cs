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
/// Transitive (DEBT-0012): the same rules hold for every method reachable from the layer through calls into Singularity assemblies,
/// including the implementations of an interface the layer calls (<c>IIndustryInputs</c>, <c>IProductsInputs</c>, ...). A virtual call
/// on a class is followed to the declared method only, not to its overrides.
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
    public void CapabilityAndProductLayers_ReachNoRng_ThroughCallsAndInputs()
    {
        Type[] types = typeof(IndustrySystem).Assembly.GetTypes().Where(t => t.Namespace is CapabilityNamespace or "Singularity.Core.Products").ToArray();
        List<MethodBase> reached = Reachable(types);
        // Guards against a vacuous walk: the Session implementation of the inputs and a Kernel method are reached.
        Assert.Contains(reached, m => m.DeclaringType?.Name == "IndustryInputsAdapter" && m.Name == "ToolingInvest");
        Assert.Contains(reached, m => m.DeclaringType == typeof(Fx6));
        Assert.Empty(TransitiveRngUses(types));
    }

    [Fact]
    public void TheTransitiveCheck_SeesARngBehindAnInterfaceAndBehindAHelper()
    {
        Assert.NotEmpty(TransitiveRngUses([typeof(MutantLayer)]));
        Assert.Contains(TransitiveRngUses([typeof(MutantLayer)]), u => u.Contains("DrawingInputs"));
    }

    internal interface IMutantInputs
    {
        long Roll();
    }

    internal sealed class DrawingInputs(CounterRng rng) : IMutantInputs
    {
        public long Roll() => (long)rng.NextUnit(0, 0, 0, 0);
    }

    internal sealed class MutantLayer(IMutantInputs inputs)
    {
        public long Step() => Helper(inputs);

        private static long Helper(IMutantInputs i) => i.Roll();
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
                uses.AddRange(RngUsesIn(m));
        }
        return uses;
    }

    private static List<string> RngUsesIn(MethodBase m)
    {
        var uses = new List<string>();
        string site = $"{m.DeclaringType}.{m.Name}";
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
        return uses;
    }

    private static readonly Assembly[] Own = [typeof(IndustrySystem).Assembly, typeof(Fx6).Assembly, typeof(Singularity.Session.SliceSession).Assembly, typeof(CapabilityNoRngTests).Assembly];

    private static bool IsOwn(MethodBase m) => Array.IndexOf(Own, m.Module.Assembly) >= 0;

    private static bool SameMethod(MethodBase a, MethodBase b) => a.MetadataToken == b.MetadataToken && a.Module == b.Module;

    /// <summary>The bodies a call can reach: the method itself, and for an interface method every implementation in our assemblies.</summary>
    private static IEnumerable<MethodBase> Targets(MethodBase callee)
    {
        yield return callee;
        if (callee.DeclaringType is not { IsInterface: true } iface)
            yield break;
        foreach (Type type in Own.SelectMany(a => a.GetTypes()).Where(t => t is { IsClass: true, IsAbstract: false } && iface.IsAssignableFrom(t)))
        {
            InterfaceMapping map = type.GetInterfaceMap(iface);
            for (int k = 0; k < map.InterfaceMethods.Length; k++)
            {
                if (SameMethod(map.InterfaceMethods[k], callee))
                    yield return map.TargetMethods[k];
            }
        }
    }

    /// <summary>Every method with a body in our assemblies that the methods of <paramref name="roots"/> reach through calls, the roots included.</summary>
    private static List<MethodBase> Reachable(IEnumerable<Type> roots)
    {
        var seen = new List<MethodBase>();
        var queue = new Queue<MethodBase>();
        void Visit(MethodBase m)
        {
            if (!IsOwn(m) || m.GetMethodBody() is null || seen.Any(x => SameMethod(x, m)))
                return;
            seen.Add(m);
            queue.Enqueue(m);
        }
        foreach (Type type in roots)
        {
            foreach (MethodBase m in type.GetMethods(IlScanner.AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(IlScanner.AllDeclared)))
                Visit(m);
        }
        while (queue.Count > 0)
        {
            foreach (IlScanner.Instruction i in IlScanner.Read(queue.Dequeue()))
            {
                if (i.Operand is MethodBase callee)
                {
                    foreach (MethodBase target in Targets(callee))
                        Visit(target);
                }
            }
        }
        return seen;
    }

    private static List<string> TransitiveRngUses(IEnumerable<Type> roots) => [.. Reachable(roots).SelectMany(RngUsesIn)];

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
