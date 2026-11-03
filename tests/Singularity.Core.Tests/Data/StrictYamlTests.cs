using Singularity.Core.Data;
using Singularity.Core.Data.Yaml;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Data;

/// <summary>The strict YAML subset of ADR-0010: no implicit typing, no anchors or tags.</summary>
public class StrictYamlTests
{
    private static YScalar Value(string yaml) =>
        (YScalar)((YMap)StrictYaml.Parse(yaml, "t.yaml")).Find("k")!;

    [Theory]
    [InlineData("k: true", ScalarKind.Bool)]
    [InlineData("k: false", ScalarKind.Bool)]
    [InlineData("k: 12", ScalarKind.Number)]
    [InlineData("k: -0.25", ScalarKind.Number)]
    [InlineData("k: text", ScalarKind.String)]
    [InlineData("k: \"yes\"", ScalarKind.String)]
    [InlineData("k: '1945'", ScalarKind.String)]
    [InlineData("k: \"1e5\"", ScalarKind.String)]
    public void Scalars_KeepTextAndKind(string yaml, ScalarKind kind) => Assert.Equal(kind, Value(yaml).Kind);

    [Fact]
    public void QuotedKeys_MayLookLikeBoolOrNumber()
    {
        var map = (YMap)StrictYaml.Parse("\"yes\": 1\n'12': 2", "t.yaml");
        Assert.Equal(["yes", "12"], map.Entries.Select(e => e.Key));
    }

    [Theory]
    [InlineData("k: yes", "implicit bool")]
    [InlineData("k: No", "implicit bool")]
    [InlineData("k: ON", "implicit bool")]
    [InlineData("k: y", "implicit bool")]
    [InlineData("k: TRUE", "implicit bool")]
    [InlineData("k: null", "implicit bool/null")]
    [InlineData("k: ~", "implicit bool/null")]
    [InlineData("k:", "empty value")]
    [InlineData("k: 1e5", "ambiguous")]
    [InlineData("k: 0x1F", "ambiguous")]
    [InlineData("k: 1_000", "ambiguous")]
    [InlineData("k: .5", "ambiguous")]
    [InlineData("k: .inf", "ambiguous")]
    [InlineData("k: 007", "ambiguous")]
    [InlineData("k: 1945-09-01", "ambiguous")]
    [InlineData("k: +1", "ambiguous")]
    [InlineData("yes: 1", "implicit bool")]
    [InlineData("on: 1", "implicit bool")]
    [InlineData("null: 1", "implicit bool/null")]
    [InlineData("1e5: 1", "ambiguous")]
    [InlineData("12: 1", "plain key '12' reads as a number or boolean")]
    [InlineData("true: 1", "plain key 'true' reads as a number or boolean")]
    [InlineData("a: &x 1\nk: *x", "anchors are not allowed")]
    [InlineData("k: !!str 5", "tags are not allowed")]
    [InlineData("k: 1\nk: 2", "duplicate key 'k'")]
    [InlineData("b: {x: 1}\nk:\n  <<: {x: 1}", "merge keys are not allowed")]
    [InlineData("? [a]\n: 1", "mapping keys must be scalars")]
    [InlineData("k: 1\n---\nk: 2", "more than one YAML document")]
    [InlineData("", "empty document")]
    [InlineData("k: [1, 2", "YAML syntax error")]
    public void Rejected(string yaml, string message)
    {
        var e = Assert.Throws<DataLoadException>(() => StrictYaml.Parse(yaml, "t.yaml"));
        Assert.Contains(message, e.Message);
        Assert.StartsWith("t.yaml", e.Message);
    }

    [Theory]
    [InlineData("2314.67", 2_314_670_000L)]
    [InlineData("0.0299", 29_900L)]
    [InlineData("-1.5", -1_500_000L)]
    [InlineData("0.000001", 1L)]
    [InlineData("1.000", 1_000_000L)]
    [InlineData("0", 0L)]
    public void ParseFixed_IsExact(string text, long raw) =>
        Assert.Equal(Fx6.FromRaw(raw), Singularity.Core.Data.Scalars.ParseFixed(text));

    [Theory]
    [InlineData("0.0000001")]
    [InlineData("9300000000000.0")]
    [InlineData("1.")]
    [InlineData("1e3")]
    public void ParseFixed_RejectsUnrepresentable(string text) =>
        Assert.Null(Singularity.Core.Data.Scalars.ParseFixed(text));
}
