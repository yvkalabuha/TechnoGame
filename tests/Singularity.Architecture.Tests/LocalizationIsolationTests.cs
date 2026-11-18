using System.Reflection;
using System.Xml.Linq;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Architecture.Tests;

/// <summary>
/// ADR-0018 §3, §7: text stays outside the simulation. Kernel, Core, Session and Lab reference neither
/// <c>Singularity.Localization</c> nor Linguini (assemblies and project files).
/// </summary>
public class LocalizationIsolationTests
{
    private static readonly (string Project, Assembly Assembly)[] Simulation =
    [
        ("Singularity.Kernel", typeof(Fx6).Assembly),
        ("Singularity.Core", typeof(Singularity.Core.Industry.IndustrySystem).Assembly),
        ("Singularity.Session", typeof(SliceSession).Assembly),
        ("Singularity.Lab", typeof(Singularity.Lab.Program).Assembly),
    ];

    [Fact]
    public void SimulationAssemblies_DoNotReferenceLocalizationOrLinguini()
    {
        foreach ((string project, Assembly assembly) in Simulation)
        {
            string[] bad = [.. assembly.GetReferencedAssemblies().Select(a => a.Name ?? "")
                .Where(n => n.StartsWith("Singularity.Localization", StringComparison.Ordinal) || n.StartsWith("Linguini", StringComparison.Ordinal))];
            Assert.True(bad.Length == 0, $"{project} references {string.Join(", ", bad)}");
        }
    }

    [Fact]
    public void SimulationProjects_DoNotListLocalizationOrLinguini()
    {
        foreach ((string project, _) in Simulation)
        {
            string csproj = Path.Combine(RepoRoot(), "src", project, project + ".csproj");
            XDocument doc = XDocument.Load(csproj);
            string[] bad = [.. doc.Descendants("ProjectReference").Select(r => (string?)r.Attribute("Include") ?? "")
                .Concat(doc.Descendants("PackageReference").Select(r => (string?)r.Attribute("Include") ?? ""))
                .Where(i => i.Contains("Localization", StringComparison.Ordinal) || i.Contains("Linguini", StringComparison.Ordinal))];
            Assert.True(bad.Length == 0, $"{project}.csproj lists {string.Join(", ", bad)}");
        }
    }

    [Fact]
    public void LocalizationAssembly_DoesNotReferenceGodot()
    {
        string csproj = Path.Combine(RepoRoot(), "src", "Singularity.Localization", "Singularity.Localization.csproj");
        XDocument doc = XDocument.Load(csproj);
        string[] references = [.. doc.Descendants("ProjectReference").Concat(doc.Descendants("PackageReference"))
            .Select(r => (string?)r.Attribute("Include") ?? "")];
        Assert.DoesNotContain(references, r => r.Contains("Godot", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, r => r.Contains("Client", StringComparison.Ordinal));
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
