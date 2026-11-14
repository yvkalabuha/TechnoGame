using System.Reflection;
using Singularity.Core;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Core.Simulation;

namespace Singularity.Architecture.Tests;

/// <summary>
/// SLICE_COMPOSITION §13.1, decision #189: a step trace is an observation, so nothing in <c>Singularity.Core</c> may read it. Every member marked
/// <see cref="ObservationAttribute"/> may be called only from code of its own class (the class writes it; a nested type or lambda of the class counts as the class).
/// Session, Lab and tests may read it freely: they are outside the simulation.
/// </summary>
public class ObservationTests
{
    private static readonly Assembly Core = typeof(ParamSet).Assembly;

    private static IEnumerable<MethodBase> Marked(Assembly assembly) =>
        assembly.GetTypes().SelectMany(t => t.GetMethods(IlScanner.AllDeclared))
            .Where(m => m.GetCustomAttribute<ObservationAttribute>() is not null);

    private static Type Outermost(Type t)
    {
        while (t.DeclaringType is { } outer)
            t = outer;
        return t;
    }

    /// <summary>The methods of <paramref name="types"/> that call a marked member from outside the member's own class.</summary>
    private static List<string> Offenders(IEnumerable<Type> types, ICollection<MethodBase> marked)
    {
        var found = new List<string>();
        foreach (Type type in types)
        {
            foreach (MethodBase caller in type.GetMethods(IlScanner.AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(IlScanner.AllDeclared)))
            {
                foreach (IlScanner.Instruction i in IlScanner.Read(caller))
                {
                    if (i.Operand is MethodBase callee && marked.Any(m => m.MetadataToken == callee.MetadataToken && m.Module == callee.Module)
                        && Outermost(callee.DeclaringType!) != Outermost(type))
                        found.Add($"{type.FullName}.{caller.Name} calls {callee.DeclaringType!.Name}.{callee.Name}");
                }
            }
        }
        return found;
    }

    [Fact]
    public void TheTracesOfTheStepsAreMarked()
    {
        // A guard against a vacuous test: the four traces of TASK-0047 carry the mark.
        string[] names = [.. Marked(Core).Select(m => $"{m.DeclaringType!.Name}.{m.Name}").Order(StringComparer.Ordinal)];
        Assert.Equal(["PolicySystem.LastCloseReason", "PolicySystem.LastMoneyRefusals", "ProductsSystem.LastLeftStart", "ProductsSystem.LastLineMoney"], names);
    }

    [Fact]
    public void Core_NeverCallsAnObservation_FromOutsideTheOwningClass()
    {
        Assert.Empty(Offenders(Core.GetTypes(), [.. Marked(Core)]));
    }

    /// <summary>A reader of a trace, as a mutant of Core would be: the scan must see it.</summary>
    private sealed class MutantReader(ProductsSystem products, PolicySystem policy)
    {
        public Singularity.Kernel.Fx6? Left() => products.LastLeftStart(0);

        public Func<LineCloseReason> ViaLambda() => () => policy.LastCloseReason(0, 0);
    }

    [Fact]
    public void TheScan_SeesADirectCallAndACallInsideALambda()
    {
        var marked = Marked(Core).ToList();
        List<string> found = Offenders([typeof(MutantReader), .. typeof(MutantReader).GetNestedTypes(IlScanner.AllDeclared)], marked);
        Assert.Contains(found, f => f.Contains("MutantReader.Left") && f.Contains("LastLeftStart"));
        Assert.Contains(found, f => f.Contains("LastCloseReason"));
    }
}
