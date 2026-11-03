using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Data;

/// <summary>define/override/patch/delete across mods, dependency order and conflicts (ADR-0010).</summary>
public class ModOperationTests : IDisposable
{
    private readonly TestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    private const string DependsOnCore = "[{id: core, min: \"0.1.0\", below: \"1.0.0\"}]";

    private const string PatchTransonicKey =
        "kind: node\npatch:\n  - {id: s8.fnd.transonic_aerodynamics, hazard: key}\n";

    [Fact]
    public void Patch_ReplacesListedFieldsOnly()
    {
        string a = _mods.Mod("a", DependsOnCore, ("nodes.yaml", PatchTransonicKey));
        GameData data = ModLoader.Load(TestMods.CoreDir, a);
        int r = data.Nodes.IndexOf("s8.fnd.transonic_aerodynamics");
        Assert.Equal(Hazard.Key, data.Nodes.Hazard[r]);
        Assert.Equal(5, data.Nodes.Number[r]);
        Assert.Equal(1947, data.Nodes.AnchorYear[r]);
    }

    [Fact]
    public void Override_ReplacesWholeObject()
    {
        const string text = "kind: product_class\noverride:\n  - id: jet_fighter\n    axis: perf\n    lighten_order: [hot_section, airframe]\n" +
                            "    slots:\n      - id: engine\n        block: pu\n        base: true\n        alternatives:\n" +
                            "          - {id: eng_g1, rank: 1, eng_gen: 1, perf_mult: 1.0, prc_set: [s8.prc.jet_engine_assembly_test], requires_nodes: [s8.eng.turbojet_g1]}\n" +
                            "      - id: hot_section\n        block: pu\n        base: false\n        alternatives:\n" +
                            "          - {id: hot_nicr_forged, rank: 1, alloy_kg: 290, perf_mult: 1.0, prc_set: [s5.prc.nicr_alloy_melt], requires_nodes: []}\n" +
                            "      - id: airframe\n        block: af\n        base: false\n        alternatives:\n" +
                            "          - {id: af_straight, rank: 1, af_class: 1, perf_mult: 1.0, prc_set: [s8.prc.stressed_skin_assembly], requires_nodes: []}\n" +
                            "    h_pm: 175\n    wage_kh: 0.000125\n    maintainability: 1.0\n    r_ref: 25\n    c_ref: 10\n" +
                            "    processes:\n      - {prc: s8.prc.jet_engine_assembly_test, n_p: 1, labour_hours: 1350, base_cost: 0.000168}\n" +
                            "      - {prc: s5.prc.nicr_alloy_melt, n_p: 1, labour_hours: 225, base_cost: 0.000028}\n" +
                            "      - {prc: s8.prc.stressed_skin_assembly, n_p: 1, labour_hours: 3700, base_cost: 0.000462}\n" +
                            "    pu_ranks:\n      - {rank: 1, proc_scale_pu: 1}\n" +
                            "    af_classes:\n      - {af_class: 1, h_first: 4, h_floor: 1, materials_floor: 0, design_scale: 1, proc_scale_af: 1}\n" +
                            "    generations:\n      - {gen: 1, app: s8.app.jet_fighter_g1, crew_req: 1, platform_min_rank: 1}\n";
        string a = _mods.Mod("a", DependsOnCore, ("classes.yaml", text));
        GameData data = ModLoader.Load(TestMods.CoreDir, a);
        Assert.Equal(["engine", "hot_section", "airframe"], data.ProductClasses.SlotId.ToArray());
        Assert.Equal(3, data.ProductClasses.AltId.Length);
        Assert.Single(data.ProductClasses.GenApp);
    }

    [Fact]
    public void Delete_RemovesUnreferencedNode()
    {
        string a = _mods.Mod("a", DependsOnCore, ("nodes.yaml", "kind: node\ndelete: [s8.app.jet_fighter_g4]\n"), TestMods.DropG4Class());
        GameData data = ModLoader.Load(TestMods.CoreDir, a);
        Assert.Equal(60, data.Nodes.Count);
        Assert.Equal(-1, data.Nodes.IndexOf("s8.app.jet_fighter_g4"));
    }

    [Fact]
    public void Delete_OfReferencedNode_IsBrokenReference()
    {
        string a = _mods.Mod("a", DependsOnCore, ("nodes.yaml", "kind: node\ndelete: [s8.eng.turbojet_g1]\n"));
        var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(TestMods.CoreDir, a));
        Assert.Contains("unknown node 's8.eng.turbojet_g1'", e.Message);
    }

    [Fact]
    public void Define_OfExistingId_FromAnotherMod_IsDuplicate()
    {
        string a = _mods.Mod("a", DependsOnCore, ("s.yaml",
            "kind: scenario\ndefine:\n  - {id: jet_slice_1945}\n"));
        var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(TestMods.CoreDir, a));
        Assert.Contains("duplicate scenario ID 'jet_slice_1945'", e.Message);
    }

    [Fact]
    public void Patch_OfUnknownId_Fails()
    {
        string a = _mods.Mod("a", DependsOnCore, ("nodes.yaml", "kind: node\npatch:\n  - {id: s8.fnd.nothing, gen: 1}\n"));
        var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(TestMods.CoreDir, a));
        Assert.Contains("patch of unknown node ID", e.Message);
    }

    [Fact]
    public void TwoIndependentModsChangingOneId_Conflict()
    {
        string a = _mods.Mod("a", DependsOnCore, ("nodes.yaml", PatchTransonicKey));
        string b = _mods.Mod("b", DependsOnCore, ("nodes.yaml", PatchTransonicKey));
        var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(TestMods.CoreDir, a, b));
        Assert.Contains("conflict on node ID 's8.fnd.transonic_aerodynamics'", e.Message);
    }

    [Fact]
    public void DependentMod_MayChangeTheSameId_LastWins()
    {
        string a = _mods.Mod("a", DependsOnCore, ("nodes.yaml", PatchTransonicKey));
        string b = _mods.Mod("b", "[{id: a, min: \"1.0.0\"}]",
            ("nodes.yaml", "kind: node\npatch:\n  - {id: s8.fnd.transonic_aerodynamics, hazard: none}\n"));
        GameData data = ModLoader.Load(b, a, TestMods.CoreDir);
        Assert.Equal(["core", "a", "b"], data.Mods.Select(m => m.Id));
        Assert.Equal(Hazard.None, data.Nodes.Hazard[data.Nodes.IndexOf("s8.fnd.transonic_aerodynamics")]);
    }

    [Fact]
    public void LoadOrder_DoesNotDependOnArgumentOrder()
    {
        string a = _mods.Mod("a", DependsOnCore, ("nodes.yaml", PatchTransonicKey));
        string b = _mods.Mod("b", DependsOnCore, ("nodes.yaml", "kind: node\ndelete: [s8.app.jet_fighter_g4]\n"), TestMods.DropG4Class());
        GameData x = ModLoader.Load(TestMods.CoreDir, a, b);
        GameData y = ModLoader.Load(b, a, TestMods.CoreDir);
        Assert.Equal(["core", "a", "b"], x.Mods.Select(m => m.Id));
        Assert.Equal(x.Mods.Select(m => m.Id), y.Mods.Select(m => m.Id));
        Assert.Equal(x.DataHash, y.DataHash);
    }

    [Theory]
    [InlineData("[{id: missing, min: \"1.0.0\"}]", "depends on missing mod 'missing'")]
    [InlineData("[{id: core, min: \"0.2.0\"}]", "needs 'core' >= 0.2.0, found 0.1.0")]
    [InlineData("[{id: core, min: \"0.0.1\", below: \"0.1.0\"}]", "needs 'core' in [0.0.1, 0.1.0)")]
    [InlineData("[{id: core, min: 1.0}]", "is a number here")]
    public void BadDependency_Fails(string dependencies, string message)
    {
        string a = _mods.Mod("a", dependencies, ("nodes.yaml", PatchTransonicKey));
        var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(TestMods.CoreDir, a));
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void ModIdThatIsAYaml11Bool_MustBeQuoted()
    {
        string plain = _mods.Mod("no", DependsOnCore, ("nodes.yaml", PatchTransonicKey));
        var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(TestMods.CoreDir, plain));
        Assert.Contains("implicit bool", e.Message);

        File.WriteAllText(Path.Combine(plain, "mod.yaml"),
            "id: \"no\"\nversion: \"1.0.0\"\ndependencies: " + DependsOnCore + "\nfiles: [nodes.yaml]\n");
        Assert.Equal(["core", "no"], ModLoader.Load(TestMods.CoreDir, plain).Mods.Select(m => m.Id));
    }

    [Fact]
    public void DeletedId_CannotBeRedefinedOrOverridden()
    {
        // Rule v0 (mods/README.md): a deleted ID stays deleted; replace objects with override.
        string a = _mods.Mod("a", DependsOnCore, ("nodes.yaml", "kind: node\ndelete: [s8.app.jet_fighter_g4]\n"));
        string b = _mods.Mod("b", "[{id: a, min: \"1.0.0\"}]", ("nodes.yaml",
            "kind: node\noverride:\n  - {id: s8.app.jet_fighter_g4}\n"));
        var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(TestMods.CoreDir, a, b));
        Assert.Contains("override of unknown node ID 's8.app.jet_fighter_g4'", e.Message);
    }

    [Fact]
    public void DependencyCycle_Fails()
    {
        string a = _mods.Mod("a", "[{id: b, min: \"1.0.0\"}]", ("nodes.yaml", "kind: node\n"));
        string b = _mods.Mod("b", "[{id: a, min: \"1.0.0\"}]", ("nodes.yaml", "kind: node\n"));
        var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(TestMods.CoreDir, a, b));
        Assert.Contains("dependency cycle among: a, b", e.Message);
    }
}
