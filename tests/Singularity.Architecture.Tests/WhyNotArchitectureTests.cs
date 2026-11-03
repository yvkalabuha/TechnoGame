using System.Reflection;
using Singularity.Core.Knowledge;
using Singularity.Core.Policy;
using Singularity.Core.WhyNot;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Architecture.Tests;

/// <summary>
/// Contract §6: <c>why_not</c> is a pure read of the snapshot. The namespace and the Session adapter of its inputs draw no random number
/// and never write a table column (no <c>Next</c> span, no <c>Load</c>). The scan is not transitive: it sees only the calls these types make.
/// </summary>
public class WhyNotArchitectureTests
{
    private static readonly Assembly Core = typeof(WhyNotAnalyzer).Assembly;
    private static readonly Assembly SessionAssembly = typeof(SliceSession).Assembly;

    private static IEnumerable<Type> WhyNotTypes() =>
        Core.GetTypes().Where(t => t.Namespace == "Singularity.Core.WhyNot")
            .Concat(SessionAssembly.GetTypes().Where(t => t.FullName!.Contains("WhyNotInputsAdapter")));

    private static IEnumerable<MethodBase> Methods(Type type) =>
        type.GetMethods(IlScanner.AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(IlScanner.AllDeclared));

    [Fact]
    public void WhyNot_UsesNoRng()
    {
        Type[] types = [.. WhyNotTypes()];
        Assert.Contains(typeof(WhyNotAnalyzer), types);
        var uses = new List<string>();
        foreach (Type type in types)
        {
            foreach (MethodBase m in Methods(type))
            {
                foreach (IlScanner.Instruction i in IlScanner.Read(m))
                {
                    if (i.Operand is MemberInfo { DeclaringType: var d } && (d == typeof(CounterRng) || d == typeof(KnowledgeRng) || d == typeof(PolicyRng)
                        || (d == typeof(TickContext) && ((MemberInfo)i.Operand).Name == "get_Rng")))
                        uses.Add($"{type}.{m.Name} → {d.Name}");
                }
            }
        }
        Assert.Empty(uses);
    }

    [Fact]
    public void WhyNot_WritesNoColumn()
    {
        var writes = new List<string>();
        foreach (Type type in WhyNotTypes())
        {
            foreach (MethodBase m in Methods(type))
            {
                foreach (IlScanner.Instruction i in IlScanner.Read(m))
                {
                    if (i.Operand is MethodBase { DeclaringType: { IsGenericType: true } d, Name: "get_Next" or "Load" } && d.GetGenericTypeDefinition() == typeof(Column<>))
                        writes.Add($"{type}.{m.Name}");
                }
            }
        }
        Assert.Empty(writes);
    }

    [Fact]
    public void WhyNot_Check_SeesAColumnWrite()
    {
        // Canary: the same scan flags a type that writes a column (the knowledge system does).
        bool seen = Methods(typeof(KnowledgeSystem)).SelectMany(IlScanner.Read)
            .Any(i => i.Operand is MethodBase { DeclaringType: { IsGenericType: true } d, Name: "get_Next" } && d.GetGenericTypeDefinition() == typeof(Column<>));
        Assert.True(seen);
    }
}
