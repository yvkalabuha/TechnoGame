using System.Globalization;
using System.Reflection;
using System.Text;

namespace Singularity.Session.Tests.Api;

/// <summary>
/// The public surface of <c>Singularity.Session</c> is pinned (ADR-0014 п. 6, SLICE_COMPOSITION §12.3): every public type and member as text, compared with
/// <c>session-surface.v{TestApiVersion}.txt</c>. A change of the surface needs a new <see cref="SliceSession.TestApiVersion"/> and a new file; to write the file
/// run the tests once with <c>SINGULARITY_UPDATE_API=1</c> and review the diff.
/// </summary>
public class ApiSurfaceTests
{
    private static string FilePath(int version) =>
        Path.Combine(SliceTestData.RepoRoot, "tests", "Singularity.Session.Tests", "Api", $"session-surface.v{version}.txt");

    [Fact]
    public void PublicSurface_MatchesThePinnedFile_OfTheCurrentTestApiVersion()
    {
        string actual = Describe(typeof(SliceSession).Assembly);
        string path = FilePath(SliceSession.TestApiVersion);
        if (Environment.GetEnvironmentVariable("SINGULARITY_UPDATE_API") == "1")
            File.WriteAllText(path, actual);
        Assert.True(File.Exists(path), $"no pinned surface for TestApiVersion {SliceSession.TestApiVersion}: {path}");
        string pinned = File.ReadAllText(path).ReplaceLineEndings("\n");
        Assert.True(pinned == actual,
            $"the public surface of Singularity.Session changed: raise SliceSession.TestApiVersion (now {SliceSession.TestApiVersion}) and pin a new session-surface.v*.txt");
    }

    [Fact]
    public void TestApiVersion_IsOne_AndThePinnedFileCarriesIt()
    {
        Assert.Equal(1, SliceSession.TestApiVersion);
        Assert.True(File.Exists(FilePath(1)));
        Assert.StartsWith($"# TestApiVersion = {SliceSession.TestApiVersion}\n", File.ReadAllText(FilePath(SliceSession.TestApiVersion)).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Describe_ListsTheViewsAndTheSwitches()
    {
        string text = Describe(typeof(SliceSession).Assembly);
        foreach (string name in new[] { "SliceView", "MonthInputsView", "ActorView", "NodeView", "ProcessView", "DesignView", "PolicyDecisionView", "ToolingLedgerView",
            "CapabilityView", "BureauView", "SliceLayoutView", "SliceViewMode", "LayerControl", "GridCell", "EffortShareArm" })
            Assert.Contains($"\ntype Singularity.Session.{name} ", text);
        foreach (string member in new[] { "prop Layer ", "prop LkTables ", "prop Grid ", "prop EffortShare ", "prop ViewMode " })
            Assert.Contains("\n  " + member, text);
    }

    /// <summary>Canonical text of the public surface: types by full name, members by their line, both ordinal.</summary>
    internal static string Describe(Assembly assembly)
    {
        var sb = new StringBuilder();
        sb.Append("# TestApiVersion = ").Append(SliceSession.TestApiVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (Type t in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            sb.Append("type ").Append(t.FullName).Append(" [").Append(Kind(t)).Append("]");
            if (t.BaseType is { } b && b != typeof(object) && b != typeof(ValueType) && b != typeof(Enum))
                sb.Append(" : ").Append(b);
            sb.Append('\n');
            const BindingFlags all = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var lines = new List<string>();
            foreach (FieldInfo f in t.GetFields(all))
                lines.Add($"field {f.Name} : {f.FieldType}{(f.IsLiteral ? " = " + Convert.ToString(f.GetRawConstantValue(), CultureInfo.InvariantCulture) : "")}{(f.IsInitOnly ? " readonly" : "")}{(f.IsStatic ? " static" : "")}");
            foreach (ConstructorInfo c in t.GetConstructors(all))
                lines.Add($"ctor({Parameters(c)})");
            foreach (PropertyInfo p in t.GetProperties(all))
                lines.Add($"prop {p.Name} : {p.PropertyType} {{{(p.GetMethod?.IsPublic == true ? " get;" : "")}{(p.SetMethod?.IsPublic == true ? (IsInit(p) ? " init;" : " set;") : "")} }}{(p.GetMethod?.IsStatic == true ? " static" : "")}");
            foreach (MethodInfo m in t.GetMethods(all).Where(m => !m.IsSpecialName))
                lines.Add($"method {m.Name}({Parameters(m)}) : {m.ReturnType}{(m.IsStatic ? " static" : "")}");
            foreach (string line in lines.OrderBy(l => l, StringComparer.Ordinal))
                sb.Append("  ").Append(line).Append('\n');
        }
        return sb.ToString();
    }

    private static bool IsInit(PropertyInfo p) => p.SetMethod!.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit));

    private static string Kind(Type t) =>
        t.IsEnum ? "enum" : t.IsValueType ? "struct" : t.IsInterface ? "interface" : t.IsAbstract && t.IsSealed ? "static class" : t.IsSealed ? "sealed class" : "class";

    private static string Parameters(MethodBase m) => string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType} {p.Name}"));
}
