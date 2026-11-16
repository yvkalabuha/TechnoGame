using System.Reflection;
using Singularity.Core.Products;

namespace Singularity.Architecture.Tests;

/// <summary>
/// Contract §9.1 r13a (#247, T-17): <c>fielded_L</c> has one implementation, <see cref="ProductsSystem.FieldedL"/> with its key functions. Policy, why_not, Session
/// (<c>ActorView</c>, the adapters of the AI) and Lab call only <c>FieldedL</c>/<c>KeyOf</c>/<c>DefK</c>/<c>GFront</c>/<c>FieldedGen</c>; none of them declares a function
/// of that name nor reads the r6 pieces (<c>FieldedMastered</c>, <c>UnitsBuiltMastered</c>, <c>CohortMastered</c>) itself. The IL is scanned, so lambdas and aliases count.
/// </summary>
public class FieldedHasOneImplementationTests
{
    private static readonly Assembly[] Assemblies = [typeof(ProductsSystem).Assembly, typeof(Singularity.Session.SliceSession).Assembly, typeof(Singularity.Lab.Program).Assembly];

    private static readonly string[] Names = ["FieldedL", "FieldedMastered", "FieldedGen", "KeyOf", "DefK", "GFront", "UnitsBuiltMastered", "CohortMastered"];
    private static readonly string[] PartsOfR6 = ["FieldedMastered", "UnitsBuiltMastered", "CohortMastered"];

    private static IEnumerable<MethodBase> AllMethods() => Assemblies.SelectMany(a => a.GetTypes()).SelectMany(t =>
        t.GetMethods(IlScanner.AllDeclared).Cast<MethodBase>().Concat(t.GetConstructors(IlScanner.AllDeclared)));

    [Fact]
    public void FieldedHasOneImplementation()
    {
        // 1. No type but ProductsSystem declares a function of these names (the interface member GFront of the policy is the adapter's contract, not a rule).
        var declared = Assemblies.SelectMany(a => a.GetTypes()).SelectMany(t => t.GetMethods(IlScanner.AllDeclared).Where(m => Names.Contains(m.Name)).Select(m => (Type: t, m.Name)))
            .Where(x => x.Type != typeof(ProductsSystem) && x.Type.Name is not "IPolicyProducts" and not "PolicyProductsAdapter").ToList();
        Assert.Empty(declared);

        // 2. Outside ProductsSystem nobody calls the r6 pieces: the decisions and the metric read fielded_L only.
        var offenders = new List<string>();
        int callsOfFieldedL = 0;
        foreach (MethodBase m in AllMethods().Where(m => m.DeclaringType != typeof(ProductsSystem) && !(m.DeclaringType?.DeclaringType == typeof(ProductsSystem))))
        {
            foreach (var i in IlScanner.Read(m))
            {
                if (i.Operand is not MethodBase c || c.DeclaringType != typeof(ProductsSystem))
                    continue;
                if (PartsOfR6.Contains(c.Name))
                    offenders.Add($"{m.DeclaringType?.FullName}.{m.Name} -> {c.Name}");
                if (c.Name is "FieldedL" or "KeyOf" or "GFront" or "FieldedGen")
                    callsOfFieldedL++;
            }
        }
        Assert.Empty(offenders);
        Assert.True(callsOfFieldedL > 0, "the scan found no caller of the key functions: the test is vacuous");

        // 3. why_not asks FieldedL for «requirement met».
        Type whyNot = typeof(ProductsSystem).Assembly.GetType("Singularity.Core.WhyNot.WhyNotAnalyzer")!;
        Assert.Contains(whyNot.GetMethods(IlScanner.AllDeclared), m => IlScanner.Read(m).Any(i => i.Operand is MethodBase c && c.Name == "FieldedL"));
    }
}
