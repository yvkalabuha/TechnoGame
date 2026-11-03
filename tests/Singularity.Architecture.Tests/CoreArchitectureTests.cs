using System.Reflection;
using System.Xml.Linq;
using Singularity.Core.Simulation;

namespace Singularity.Architecture.Tests;

/// <summary>Core is world-state code: same determinism rules as Kernel, and it depends only on Kernel.</summary>
public class CoreArchitectureTests
{
    private static readonly Assembly Core = typeof(ParamSet).Assembly;

    [Fact]
    public void Core_FollowsDeterminismRules()
    {
        Assert.Empty(DeterminismRules.Check(Core.GetTypes()));
    }

    // ADR-0003 R4: the only Core namespaces still allowed to use double, matched exactly
    // (sub-namespaces get no rights). A new one needs an ADR.
    private static readonly string[] DoubleExceptions =
    [
        "Singularity.Core.Science.ToyFunnel",
        "Singularity.Core.Simulation",
    ];

    [Fact]
    public void Core_OutsideR4Exceptions_HasNoDouble()
    {
        Type[] checkedTypes = Core.GetTypes().Where(t => !IsDoubleException(t)).ToArray();
        Assert.Empty(DeterminismRules.Check(checkedTypes, forbidDouble: true));
    }

    [Fact]
    public void R4Exceptions_AreNotStale()
    {
        // Each exception must still name real code; a dead entry would silently admit future code.
        foreach (string ns in DoubleExceptions)
            Assert.Contains(Core.GetTypes(), t => t.Namespace == ns);
    }

    private static bool IsDoubleException(Type type) => Array.IndexOf(DoubleExceptions, type.Namespace) >= 0;

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
