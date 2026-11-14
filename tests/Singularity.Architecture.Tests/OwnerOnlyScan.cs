using System.Reflection;

namespace Singularity.Architecture.Tests;

/// <summary>
/// The IL scan shared by the rules «readable only inside its own class»: step traces (<c>[Observation]</c>, #189) and scratch buffers
/// (<c>[Scratch]</c>, ADR-0013 §4). A nested type or lambda of the class counts as the class.
/// </summary>
internal static class OwnerOnlyScan
{
    public static Type Outermost(Type t)
    {
        while (t.DeclaringType is { } outer)
            t = outer;
        return t;
    }

    /// <summary>The methods of <paramref name="types"/> that call or touch a marked member from outside the member's own class.</summary>
    public static List<string> Offenders(IEnumerable<Type> types, ICollection<MemberInfo> marked)
    {
        var found = new List<string>();
        foreach (Type type in types)
        {
            foreach (MethodBase caller in type.GetMethods(IlScanner.AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(IlScanner.AllDeclared)))
            {
                foreach (IlScanner.Instruction i in IlScanner.Read(caller))
                {
                    if (i.Operand is MemberInfo callee && marked.Any(m => m.MetadataToken == callee.MetadataToken && m.Module == callee.Module)
                        && Outermost(callee.DeclaringType!) != Outermost(type))
                        found.Add($"{type.FullName}.{caller.Name} uses {callee.DeclaringType!.Name}.{callee.Name}");
                }
            }
        }
        return found;
    }
}
