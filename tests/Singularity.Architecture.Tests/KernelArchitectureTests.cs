using System.Reflection;
using System.Xml.Linq;
using Singularity.Kernel;

namespace Singularity.Architecture.Tests;

public class KernelArchitectureTests
{
    private static readonly Assembly Kernel = typeof(Fx6).Assembly;

    [Fact]
    public void Kernel_FollowsDeterminismRules()
    {
        // GetTypes includes nested and compiler-generated types (lambdas, iterators).
        Assert.Empty(DeterminismRules.Check(Kernel.GetTypes()));
    }

    // ADR-0003 R3: the exact set of Kernel members that may touch double. Adding a member here
    // means the double path in Kernel grows, which needs review against the R3 rule.
    private static readonly string[] DoubleAllowList =
    [
        "CounterRng.NextUnit",
        "CounterRng.TwoPowMinus53", // const used only by NextUnit
        "DetMath.CheckArguments",
        "DetMath.CheckFinite",
        "DetMath.Finish",
        "DetMath.PowAbs",
        "DetMath.PowDyadic",
        "DetMath.PowFrac",
        "DetMath.PowFracFx",
        "DetMath.PowInt",
        "DetMath.Sqrt",
        "Fx6.FromDouble",
        "Fx6.ToDouble",
    ];

    [Fact]
    public void Kernel_UsesDoubleOnlyInR3AllowList()
    {
        Assert.Equal(DoubleAllowList, DeterminismRules.DoubleUsers(Kernel.GetTypes()));
    }

    [Fact]
    public void Kernel_DoesNotReferenceOtherSolutionAssemblies()
    {
        var references = Kernel.GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .Where(name => name.StartsWith("Singularity", StringComparison.Ordinal));
        Assert.Empty(references);
    }

    [Fact]
    public void KernelProject_HasNoProjectReferences()
    {
        // The compiler drops unused references, so the assembly check alone could miss one.
        string csproj = Path.Combine(RepoRoot(), "src", "Singularity.Kernel", "Singularity.Kernel.csproj");
        var projectReferences = XDocument.Load(csproj).Descendants("ProjectReference");
        Assert.Empty(projectReferences);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Singularity.sln")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Singularity.sln not found above the test output directory.");
    }
}
