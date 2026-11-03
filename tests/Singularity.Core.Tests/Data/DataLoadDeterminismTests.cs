using System.Collections.Concurrent;
using Singularity.Core.Data;

namespace Singularity.Core.Tests.Data;

/// <summary>
/// L1 data identity under permutations and parallel loads (ADR-0002, ADR-0007, ADR-0010).
/// Test-engineer audit of TASK-0013.
/// </summary>
public class DataLoadDeterminismTests : IDisposable
{
    private const string DependsOnCore = "[{id: core, min: \"0.1.0\"}]";
    private readonly TestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    private static IEnumerable<T[]> Permutations<T>(T[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }
        for (int i = 0; i < items.Length; i++)
        {
            T[] rest = [.. items[..i], .. items[(i + 1)..]];
            foreach (T[] p in Permutations(rest))
                yield return [items[i], .. p];
        }
    }

    [Fact]
    public void ManifestFileOrder_DoesNotChangeHash_WhenFilesAreIndependent()
    {
        string expected = ModLoader.Load(TestMods.CoreDir).DataHash;
        string[] files = ["nodes.yaml", "product_classes.yaml", "scenario_1945.yaml"];
        int n = 0;
        foreach (string[] order in Permutations(files))
        {
            using var mods = new TestMods();
            string core = mods.CopyCore((name, text) => name == "mod.yaml"
                ? TestMods.ReplaceOnce(text, "files: [nodes.yaml, product_classes.yaml, scenario_1945.yaml]", $"files: [{string.Join(", ", order)}]")
                : text);
            Assert.Equal(expected, ModLoader.Load(core).DataHash);
            n++;
        }
        Assert.Equal(6, n);
    }

    [Fact]
    public void DirectoryArgumentOrder_AllPermutations_GiveOneOrderAndOneHash()
    {
        string a = _mods.Mod("a", DependsOnCore, ("n.yaml", "kind: node\npatch:\n  - {id: s8.fnd.transonic_aerodynamics, hazard: key}\n"));
        string b = _mods.Mod("b", DependsOnCore, ("n.yaml", "kind: node\ndelete: [s8.app.jet_fighter_g4]\n"), TestMods.DropG4Class());
        string c = _mods.Mod("c", "[{id: a, min: \"1.0.0\"}]", ("n.yaml", "kind: node\npatch:\n  - {id: s8.fnd.transonic_aerodynamics, hazard: none}\n"));
        string[] dirs = [TestMods.CoreDir, a, b, c];
        var hashes = new HashSet<string>();
        var orders = new HashSet<string>();
        foreach (string[] order in Permutations(dirs))
        {
            GameData d = ModLoader.Load(order);
            hashes.Add(d.DataHash);
            orders.Add(string.Join(",", d.Mods.Select(m => m.Id)));
        }
        Assert.Single(hashes);
        Assert.Equal(["core,a,b,c"], orders);
    }

    [Fact]
    public void ParallelLoads_GiveTheSameHashAsASequentialLoad()
    {
        GameData reference = ModLoader.Load(TestMods.CoreDir);
        var data = new ConcurrentBag<(string Data, string Scenario)>();
        Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
        {
            GameData d = ModLoader.Load(TestMods.CoreDir);
            data.Add((d.DataHash, d.Scenarios[0].Hash));
        });
        Assert.Equal(16, data.Count);
        Assert.All(data, h =>
        {
            Assert.Equal(reference.DataHash, h.Data);
            Assert.Equal(reference.Scenarios[0].Hash, h.Scenario);
        });
    }

    [Theory]
    // Same data, other spelling: key order, quoting, flow vs block, trailing zeros, negative zero.
    [InlineData("nodes.yaml", "  - id: s7.fnd.brayton_cycle\n    number: 1\n    type: FND\n", "  - id: s7.fnd.brayton_cycle\n    type: FND\n    number: 1\n")]
    [InlineData("nodes.yaml", "  - id: s7.fnd.brayton_cycle\n    number: 1\n    type: FND\n    gen: 0\n    discipline: prop\n    cluster: AERO\n",
                              "  - id: s7.fnd.brayton_cycle\n    number: 1\n    type: \"FND\"\n    gen: 0\n    discipline: 'prop'\n    cluster: AERO\n")]
    [InlineData("nodes.yaml", "      - {to: s8.eng.turbojet_g1, weight: 0.6}\n      - {to: s8.prc.jet_engine_assembly_test, weight: 0.6}\n",
                              "      - to: s8.eng.turbojet_g1\n        weight: 0.600000\n      - {weight: 0.6, to: s8.prc.jet_engine_assembly_test}\n")]
    [InlineData("scenario_1945.yaml", "        hcap: 5.50\n", "        hcap: 5.5\n")]
    [InlineData("scenario_1945.yaml", "        budget_index: 1.000\n", "        budget_index: 1\n")]
    public void EquivalentSpelling_KeepsDataHash(string file, string from, string to)
    {
        GameData original = ModLoader.Load(TestMods.CoreDir);
        string copy = _mods.CopyCore((name, text) => name == file ? TestMods.ReplaceOnce(text, from, to) : text);
        GameData edited = ModLoader.Load(copy);
        Assert.Equal(original.DataHash, edited.DataHash);
        Assert.Equal(original.Scenarios[0].Hash, edited.Scenarios[0].Hash);
    }

    [Fact]
    public void NegativeZero_KeepsDataHash()
    {
        GameData original = ModLoader.Load(TestMods.CoreDir);
        string copy = _mods.CopyCore((name, text) =>
            name == "scenario_1945.yaml" ? text.Replace("        k_ind: 0\n", "        k_ind: -0.0\n", StringComparison.Ordinal) : text);
        Assert.Equal(original.DataHash, ModLoader.Load(copy).DataHash);
    }

    [Fact]
    public void EquivalentModResults_GiveTheSameHash()
    {
        // patch to a value and patch back to the original: data equal to core, so the hash is too.
        string a = _mods.Mod("a", DependsOnCore, ("n.yaml", "kind: node\npatch:\n  - {id: s8.fnd.transonic_aerodynamics, hazard: key}\n"));
        string b = _mods.Mod("b", "[{id: a, min: \"1.0.0\"}]", ("n.yaml", "kind: node\npatch:\n  - {id: s8.fnd.transonic_aerodynamics, hazard: hz}\n"));
        Assert.Equal(ModLoader.Load(TestMods.CoreDir).DataHash, ModLoader.Load(TestMods.CoreDir, a, b).DataHash);
    }

    [Fact]
    public void DataHash_DependsOnData_NotOnModDirectoryLocation()
    {
        string copy = _mods.CopyCore();
        Assert.NotEqual(TestMods.CoreDir, copy);
        Assert.Equal(ModLoader.Load(TestMods.CoreDir).DataHash, ModLoader.Load(copy).DataHash);
    }
}
