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
    public void TestApiVersion_IsEight_AllPinnedFilesExist_AndTheCurrentOneCarriesIt()
    {
        Assert.Equal(8, SliceSession.TestApiVersion);
        Assert.All(Enumerable.Range(1, SliceSession.TestApiVersion), v => Assert.True(File.Exists(FilePath(v)), $"no pinned surface file for v{v}"));   // v1 stays as the record of the surface before TASK-0047
        Assert.StartsWith("# TestApiVersion = 1\n", File.ReadAllText(FilePath(1)).ReplaceLineEndings("\n"));
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

    [Fact]
    public void Describe_WritesTheNullabilityOfReferenceTypes_SoDroppingAQuestionMarkChangesTheText()
    {
        string text = Describe(typeof(SliceSession).Assembly);
        Assert.Contains("prop Inputs : Singularity.Session.MonthInputsView? {", text);
        Assert.Contains("prop Step : Singularity.Session.MonthStepView? {", text);
        Assert.Contains("prop LkTables : Singularity.Core.Knowledge.LkTables? {", text);
        Assert.Contains("prop Actors : System.Collections.Immutable.ImmutableArray`1[Singularity.Session.ActorView] {", text);   // a non-null struct stays as it was
        Assert.Contains("prop KnownSince : System.Nullable`1[System.Int64] {", text);                                          // a nullable value type keeps its own form
        Assert.Contains("prop StateHash : System.String {", text);
        Assert.DoesNotContain("prop StateHash : System.String? {", text);
        Assert.Contains("prop Requirement : Singularity.Session.RequirementView? {", text);
    }

    /// <summary>Canonical text of the public surface: types by full name, members by their line, both ordinal. Reference types carry <c>?</c> when annotated nullable (v2, SLICE_COMPOSITION §13.7).</summary>
    internal static string Describe(Assembly assembly)
    {
        var sb = new StringBuilder();
        var ctx = new NullabilityInfoContext();
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
                lines.Add($"field {f.Name} : {TypeText(f.FieldType, ctx.Create(f))}{(f.IsLiteral ? " = " + Convert.ToString(f.GetRawConstantValue(), CultureInfo.InvariantCulture) : "")}{(f.IsInitOnly ? " readonly" : "")}{(f.IsStatic ? " static" : "")}");
            foreach (ConstructorInfo c in t.GetConstructors(all))
                lines.Add($"ctor({Parameters(c, ctx)})");
            foreach (PropertyInfo p in t.GetProperties(all))
                lines.Add($"prop {p.Name} : {TypeText(p.PropertyType, ctx.Create(p))} {{{(p.GetMethod?.IsPublic == true ? " get;" : "")}{(p.SetMethod?.IsPublic == true ? (IsInit(p) ? " init;" : " set;") : "")} }}{(p.GetMethod?.IsStatic == true ? " static" : "")}");
            foreach (MethodInfo m in t.GetMethods(all).Where(m => !m.IsSpecialName))
                lines.Add($"method {m.Name}({Parameters(m, ctx)}) : {TypeText(m.ReturnType, ctx.Create(m.ReturnParameter))}{(m.IsStatic ? " static" : "")}");
            foreach (string line in lines.OrderBy(l => l, StringComparer.Ordinal))
                sb.Append("  ").Append(line).Append('\n');
        }
        return sb.ToString();
    }

    private static bool IsInit(PropertyInfo p) => p.SetMethod!.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit));

    private static string Kind(Type t) =>
        t.IsEnum ? "enum" : t.IsValueType ? "struct" : t.IsInterface ? "interface" : t.IsAbstract && t.IsSealed ? "static class" : t.IsSealed ? "sealed class" : "class";

    private static string Parameters(MethodBase m, NullabilityInfoContext ctx) =>
        string.Join(", ", m.GetParameters().Select(p => $"{TypeText(p.ParameterType, ctx.Create(p))} {p.Name}"));

    // Limit: only the top-level annotation is written; nullability inside generic arguments (ImmutableArray<T?>) is not part of the text.
    // The annotation of a reference type is part of the surface; Nullable<T> of a value type already shows in the type name.
    private static string TypeText(Type type, NullabilityInfo info) =>
        !type.IsValueType && info.ReadState == NullabilityState.Nullable || !type.IsValueType && info.WriteState == NullabilityState.Nullable ? type + "?" : type.ToString();
}
