using System.Reflection;
using System.Xml.Linq;

namespace Singularity.Architecture.Tests;

/// <summary>Core is world-state code: same determinism rules as Kernel, and it depends only on Kernel.</summary>
public class CoreArchitectureTests
{
    private static readonly Assembly Core = typeof(Singularity.Core.Industry.IndustrySystem).Assembly;

    [Fact]
    public void Core_FollowsDeterminismRules()
    {
        Assert.Empty(DeterminismRules.Check(Core.GetTypes()));
    }

    // ADR-0003 R4: no exceptions, Core has no double at all.
    [Fact]
    public void Core_HasNoDouble()
    {
        Assert.Empty(DeterminismRules.Check(Core.GetTypes(), forbidDouble: true));
    }

    [Fact]
    public void Core_ReferencesOnlyKernel()
    {
        var references = Core.GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .Where(name => name.StartsWith("Singularity", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(["Singularity.Kernel"], references);
    }

    [Fact]
    public void CoreProject_ReferencesOnlyKernelProject()
    {
        // The compiler drops unused references, so the assembly check alone could miss one (e.g. Lab).
        string csproj = Path.Combine(RepoRoot(), "src", "Singularity.Core", "Singularity.Core.csproj");
        var fileNames = XDocument.Load(csproj).Descendants("ProjectReference")
            .Select(r => ((string?)r.Attribute("Include") ?? "").Replace('\\', '/'))
            .Select(path => path[(path.LastIndexOf('/') + 1)..]);
        Assert.Equal(["Singularity.Kernel.csproj"], fileNames);
    }

    [Fact]
    public void CoreProject_PackageReferences_AreExactlyApproved()
    {
        // A NuGet package in world-state code needs review (licence ADR-0009, determinism); adding
        // one must change this list. YamlDotNet: event parser for mod data (TASK-0013, ADR-0010).
        string csproj = Path.Combine(RepoRoot(), "src", "Singularity.Core", "Singularity.Core.csproj");
        var packages = XDocument.Load(csproj).Descendants("PackageReference")
            .Select(r => (string?)r.Attribute("Include") ?? "")
            .Order(StringComparer.Ordinal);
        Assert.Equal(["YamlDotNet"], packages);
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
