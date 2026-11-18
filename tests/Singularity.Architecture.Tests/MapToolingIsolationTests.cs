using System.Reflection;
using System.Xml.Linq;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Architecture.Tests;

/// <summary>
/// ADR-0011 §2.9 (DEBT-0043): NetTopologySuite is an offline map-builder dependency (tools/map).
/// Core, Kernel, Session and the client reference it neither in assemblies nor in project files.
/// </summary>
public class MapToolingIsolationTests
{
    private const string Forbidden = "NetTopologySuite";

    private static readonly (string Project, Assembly? Assembly)[] Guarded =
    [
        ("Singularity.Kernel", typeof(Fx6).Assembly),
        ("Singularity.Core", typeof(Singularity.Core.Simulation.ParamSet).Assembly),
        ("Singularity.Session", typeof(SliceSession).Assembly),
        ("Singularity.Client.Spike", null), // Godot project: checked through its project file only
    ];

    [Fact]
    public void GuardedAssemblies_DoNotReferenceNetTopologySuite()
    {
        foreach ((string project, Assembly? assembly) in Guarded.Where(g => g.Assembly is not null))
        {
            string[] bad = [.. assembly!.GetReferencedAssemblies().Select(a => a.Name ?? "")
                .Where(n => n.StartsWith(Forbidden, StringComparison.Ordinal))];
            Assert.True(bad.Length == 0, $"{project} references {string.Join(", ", bad)}");
        }
    }

    [Fact]
    public void GuardedProjects_DoNotListNetTopologySuite()
    {
        foreach ((string project, _) in Guarded)
        {
            string csproj = Directory.GetFiles(Path.Combine(RepoRoot(), "src", project), "*.csproj").Single();
            Assert.True(ForbiddenReferences(XDocument.Load(csproj)).Length == 0, $"{project} project file lists NetTopologySuite or MapBuilder");
        }
    }

    [Fact]
    public void ForbiddenReferences_DetectsAnArtificialReference()
    {
        var clean = XDocument.Parse("<Project><ItemGroup><PackageReference Include=\"Other\" /></ItemGroup></Project>");
        var dirty = XDocument.Parse("<Project><ItemGroup><PackageReference Include=\"NetTopologySuite\" Version=\"2.5.0\" /></ItemGroup></Project>");
        var viaProject = XDocument.Parse("<Project><ItemGroup><ProjectReference Include=\"..\\..\\tools\\map\\MapBuilder\\MapBuilder.csproj\" /></ItemGroup></Project>");
        Assert.Empty(ForbiddenReferences(clean));
        Assert.Single(ForbiddenReferences(dirty));
        Assert.Single(ForbiddenReferences(viaProject));
    }

    private static string[] ForbiddenReferences(XDocument doc) =>
    [.. doc.Descendants("PackageReference").Concat(doc.Descendants("ProjectReference"))
        .Select(r => (string?)r.Attribute("Include") ?? "")
        .Where(i => i.Contains(Forbidden, StringComparison.Ordinal) || i.Contains("MapBuilder", StringComparison.Ordinal))];

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
