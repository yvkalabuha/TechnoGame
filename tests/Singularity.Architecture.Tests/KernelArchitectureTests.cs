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
