using Singularity.Core.Data;

namespace Singularity.Core.Tests.Data;

/// <summary>L1 identity of loaded data: stable load order and hashes (ADR-0002, ADR-0007).</summary>
public class DataHashTests : IDisposable
{
    // scenario_hash of jet_slice_1945 (spec §1.1). Re-recorded for D1 (start-data package, spec v0.14) and for D2 (R-H-MS′ start rule, spec v0.19, #155, #159). Changing the scenario must be deliberate:
    // update this value together with the data and record it in the experiment manifest.
    private const string ScenarioHash1945 = "71160e6570889603c96ccc2e8fbce28008345b4f0e7841d07b24f0cc6fd9330d";

    // DataHash of mod core alone: nodes, product classes and scenarios. Same rule as above.
    private const string CoreDataHash = "7cc1120c511fb4f4e20725c2ffe152ae20b07529944454004a8b77d6d54ff253";

    private readonly TestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    [Fact]
    public void CoreDataHash_IsFrozen()
    {
        string hash = ModLoader.Load(TestMods.CoreDir).DataHash;
        Assert.True(hash == CoreDataHash, $"DataHash is {hash}");
    }

    [Fact]
    public void ScenarioHash_IsFrozen()
    {
        string hash = ModLoader.Load(TestMods.CoreDir).Scenario("jet_slice_1945").Hash;
        Assert.True(hash == ScenarioHash1945, $"scenario_hash is {hash}");
    }

    [Fact]
    public void RepeatedLoads_GiveIdenticalHashes()
    {
        GameData a = ModLoader.Load(TestMods.CoreDir);
        GameData b = ModLoader.Load(TestMods.CoreDir);
        Assert.Equal(a.DataHash, b.DataHash);
        Assert.Equal(a.Scenarios[0].Hash, b.Scenarios[0].Hash);
        Assert.Equal(64, a.DataHash.Length);
    }

    [Fact]
    public void CommentsAndNodeOrderInFile_DoNotChangeHash()
    {
        GameData original = ModLoader.Load(TestMods.CoreDir);
        string copy = _mods.CopyCore((name, text) => name == "nodes.yaml" ? ReverseNodes(text) : "# reformatted\n" + text);
        GameData shuffled = ModLoader.Load(copy);
        Assert.Equal(original.Nodes.Id.ToArray(), shuffled.Nodes.Id.ToArray());
        Assert.Equal(original.DataHash, shuffled.DataHash);
    }

    [Fact]
    public void ActorOrder_ChangesScenarioHash()
    {
        // Spec §3.5 A7: actor order fixes RNG keys, so it is part of the scenario identity.
        string copy = _mods.CopyCore((name, text) => name == "scenario_1945.yaml" ? SwapLastTwoActors(text) : text);
        GameData swapped = ModLoader.Load(copy);
        Assert.Equal(["USA", "GBR", "SUN", "SWE", "FRA"], swapped.Scenarios[0].ActorId.ToArray());
        Assert.NotEqual(ModLoader.Load(TestMods.CoreDir).Scenarios[0].Hash, swapped.Scenarios[0].Hash);
    }

    [Fact]
    public void AnyValueChange_ChangesScenarioHash()
    {
        string copy = _mods.CopyCore((name, text) =>
            name == "scenario_1945.yaml" ? TestMods.ReplaceOnce(text, "trophy_engineers: 1385", "trophy_engineers: 1386") : text);
        Assert.NotEqual(ModLoader.Load(TestMods.CoreDir).Scenarios[0].Hash, ModLoader.Load(copy).Scenarios[0].Hash);
    }

    private static string ReverseNodes(string text)
    {
        int define = text.IndexOf("define:\n", StringComparison.Ordinal) + "define:\n".Length;
        string[] items = text[define..].Split("  - id: ", StringSplitOptions.RemoveEmptyEntries);
        return text[..define] + string.Concat(items.Reverse().Select(i => "  - id: " + i));
    }

    private static string SwapLastTwoActors(string text)
    {
        int fra = text.IndexOf("      - id: FRA", StringComparison.Ordinal);
        int swe = text.IndexOf("      - id: SWE", StringComparison.Ordinal);
        return text[..fra] + text[swe..] + text[fra..swe];
    }
}
