namespace Singularity.Localization.Tests;

/// <summary>ADR-0018 §3.2 L5: ids used by the client exist, and scenes carry no visible text.</summary>
public class ClientReferenceTests
{
    [Fact]
    public void UnknownLiteralIds_AreReported_KnownOnesPass()
    {
        const string Source = """
            var a = Loc.T("node-s8-fnd-boundary_layer");
            var b = Loc.A("node-s8-fnd-boundary_layer", Field.Anchor);
            var c = Loc.T("ui-does-not-exist");
            var d = new DataRef("actor-SUN");
            var e = new DataRef("actor-NOPE");
            var f = Loc.T(Keys.Node(id));
            """;
        List<string> problems = ClientReferences.CheckCode("X.cs", Source, TestKit.Catalog);
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("ui-does-not-exist", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("actor-NOPE", StringComparison.Ordinal));
    }

    [Fact]
    public void SceneLiterals_AreReported_EmptyTextIsFine()
    {
        const string Scene = "[node name=\"Label\" type=\"Label\"]\ntext = \"Hello\"\ntooltip_text = \"Tip\"\nplaceholder_text = \"\"\nvisible = true\n";
        List<string> problems = ClientReferences.CheckScene("a.tscn", Scene);
        Assert.Equal(2, problems.Count);
        Assert.Contains("a.tscn:2", problems[0], StringComparison.Ordinal);
    }
}
