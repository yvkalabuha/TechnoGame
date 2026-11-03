using System.Globalization;
using System.Text.Json.Nodes;
using LateApexEarlySpeed.Json.Schema;
using Singularity.Core.Data.Yaml;

namespace Singularity.Core.Tests.Data;

/// <summary>
/// CI schema check (ADR-0010): every mod file validates against its JSON Schema 2020-12.
/// YAML is mapped to JSON with the loader's typing: plain numbers → numbers, plain true/false →
/// booleans, everything else → strings. So "type: string" in a schema also rejects unquoted
/// numbers, the same way the loader does.
/// </summary>
public class SchemaTests : IDisposable
{
    private readonly TestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    public static TheoryData<string, string> CoreFiles => new()
    {
        { "mod.yaml", "mod.schema.json" },
        { "nodes.yaml", "node.schema.json" },
        { "product_classes.yaml", "product_class.schema.json" },
        { "scenario_1945.yaml", "scenario.schema.json" },
    };

    [Theory]
    [MemberData(nameof(CoreFiles))]
    public void CoreFile_MatchesSchema(string file, string schema)
    {
        (bool valid, string error) = Validate(Path.Combine(TestMods.CoreDir, file), schema);
        Assert.True(valid, error);
    }

    [Theory]
    [InlineData("nodes.yaml", "node.schema.json", "cluster: AERO\n    requires: []\n    hazard: none\n    informs:\n      - {to: s8.eng.turbojet_g1", "cluster: JETS\n    requires: []\n    hazard: none\n    informs:\n      - {to: s8.eng.turbojet_g1")]
    [InlineData("nodes.yaml", "node.schema.json", "    number: 2\n", "    number: 2\n    tier: 3\n")]
    [InlineData("nodes.yaml", "node.schema.json", "      source: \"S32\"", "      source: 1904")]
    [InlineData("scenario_1945.yaml", "scenario.schema.json", "tech_lead: USA", "tech_lead: usa")]
    [InlineData("scenario_1945.yaml", "scenario.schema.json", "        trophy_engineers: 86\n", "")]
    [InlineData("product_classes.yaml", "product_class.schema.json", "axis: perf", "axis: speed")]
    [InlineData("mod.yaml", "mod.schema.json", "version: \"0.1.0\"", "version: \"0.1\"")]
    public void BrokenFile_FailsSchema(string file, string schema, string from, string to)
    {
        string dir = _mods.CopyCore((name, text) => name == file ? TestMods.ReplaceOnce(text, from, to) : text);
        Assert.False(Validate(Path.Combine(dir, file), schema).Valid);
    }

    private static (bool Valid, string Error) Validate(string yamlPath, string schemaFile)
    {
        var validator = new JsonValidator(File.ReadAllText(Path.Combine(TestMods.SchemaDir, schemaFile)));
        validator.AddExternalDocument(File.ReadAllText(Path.Combine(TestMods.SchemaDir, "common.schema.json")));
        JsonNode? json = ToJson(StrictYaml.Parse(File.ReadAllText(yamlPath), yamlPath));
        var result = validator.Validate(json!.ToJsonString());
        return (result.IsValid, string.Join("; ", result.ValidationErrors.Select(e => $"{e.InstanceLocation}: {e.ErrorMessage}")));
    }

    private static JsonNode? ToJson(YNode node) => node switch
    {
        YMap map => new JsonObject(map.Entries.Select(e => KeyValuePair.Create(e.Key, ToJson(e.Value)))),
        YSeq seq => new JsonArray([.. seq.Items.Select(ToJson)]),
        YScalar { Kind: ScalarKind.Bool } s => JsonValue.Create(s.Value == "true"),
        YScalar { Kind: ScalarKind.Number } s => JsonValue.Create(decimal.Parse(s.Value, CultureInfo.InvariantCulture)),
        YScalar s => JsonValue.Create(s.Value),
        _ => throw new InvalidOperationException(node.GetType().Name),
    };
}
