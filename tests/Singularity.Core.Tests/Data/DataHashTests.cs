using Singularity.Core.Data;

namespace Singularity.Core.Tests.Data;

/// <summary>L1 identity of loaded data: stable load order and hashes (ADR-0002, ADR-0007).</summary>
public class DataHashTests : IDisposable
{
    // scenario_hash of jet_slice_1945 (spec §1.1). Re-recorded for D1 (start-data package, spec v0.14) and for D2 (R-H-MS′ start rule, spec v0.19, #155, #159). Changing the scenario must be deliberate:
    // update this value together with the data and record it in the experiment manifest.
    private const string ScenarioHash1945 = "2ccb4adbe59a0795fc98c82413b6be188c067e90072eaed18d23d2b1d300878f";

    // Re-recorded for «exposure to data» (TASK-0033): the actor field exposure (spec 8.1) is part of the scenario.
    // DataHash of mod core alone: nodes, product classes and scenarios. Same rule as above.
    // Re-recorded once for ADR-0018 §4 (TASK-0074): anchor.event left the data, text lives in mods/core/locale. Old value 6eba0cea…585bce.
    // Re-recorded in TASK-0147 (train 2): nodes 82-99 and the rev 3.1 requires/informs of nodes 65-81 change the core data.
    // Re-recorded in TASK-0180 (#555): real prerequisites (requires) of 11 early G5/G6 nodes (62, 63, 82, 83, 86, 90, 91, 92, 95, 97, 99), no new nodes.
    // Re-recorded once in the golden batch of TASK-0187 (merged state): slot model on by default + 57 new nodes (#575) + directions and research_speed effects in the hashes + FRA starts with 2 institutes (CEA, 1945-10). Old value 68a3defd…159030.
    // Re-recorded once in TASK-0193: 55 new nodes 160-214 (1990-2030: avionics, stealth, composites, computing, space, missiles), 3 new processes, 7 research_speed effects. Old value 2cdb9927…bf9067.
    private const string CoreDataHash = "a81f3ba5f2b1d1f45ed172fffdec6d4331a2de1adf5461fdad785d6c70b5f96a";

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
    public void CrlfMod_LoadsToTheSameHashes()
    {
        // DEBT-0017: a mod saved with Windows line endings (yaml and mod.yaml) is the same mod.
        GameData original = ModLoader.Load(TestMods.CoreDir);
        string copy = _mods.CopyCore((_, text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal));
        GameData crlf = ModLoader.Load(copy);
        Assert.Equal(original.DataHash, crlf.DataHash);
        Assert.Equal(original.Scenarios[0].Hash, crlf.Scenarios[0].Hash);
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
