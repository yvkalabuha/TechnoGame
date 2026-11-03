using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Data;

/// <summary>
/// Conflict cases of define/override/patch/delete, the transitive dependency rule and manifest
/// errors (ADR-0010). Test-engineer audit of TASK-0013.
/// </summary>
public class ModOperationEdgeTests : IDisposable
{
    private const string DependsOnCore = "[{id: core, min: \"0.1.0\"}]";
    private const string Transonic = "s8.fnd.transonic_aerodynamics";
    private readonly TestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    private static string PatchHazard(string hazard) => $"kind: node\npatch:\n  - {{id: {Transonic}, hazard: {hazard}}}\n";

    private static void AssertFails(string message, params string[] dirs)
    {
        var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(dirs));
        Assert.True(e.Message.Contains(message, StringComparison.Ordinal), e.Message);
    }

    private Hazard TransonicHazard(params string[] dirs)
    {
        GameData d = ModLoader.Load(dirs);
        return d.Nodes.Hazard[d.Nodes.IndexOf(Transonic)];
    }

    [Fact]
    public void TransitiveDependency_AllowsChangingAnObjectTouchedByAnIndirectDependency()
    {
        // c -> b -> a -> core: c depends on a only through b.
        string a = _mods.Mod("a", DependsOnCore, ("n.yaml", PatchHazard("key")));
        string b = _mods.Mod("b", "[{id: a, min: \"1.0.0\"}]", ("n.yaml", "kind: node\n"));
        string c = _mods.Mod("c", "[{id: b, min: \"1.0.0\"}]", ("n.yaml", PatchHazard("none")));
        Assert.Equal(Hazard.None, TransonicHazard(c, b, a, TestMods.CoreDir));
    }

    [Fact]
    public void Diamond_ModMustDependOnEveryEarlierToucher()
    {
        // a and b both patch independently -> conflict already; here only a patches, and c depends
        // on b (not a), so c's change would depend on the tie-break order a < c.
        string a = _mods.Mod("a", DependsOnCore, ("n.yaml", PatchHazard("key")));
        string b = _mods.Mod("b", DependsOnCore, ("n.yaml", "kind: node\n"));
        string c = _mods.Mod("c", "[{id: b, min: \"1.0.0\"}]", ("n.yaml", PatchHazard("none")));
        AssertFails("mod 'c' changes it but does not depend on mod 'a'", TestMods.CoreDir, a, b, c);
    }

    [Fact]
    public void Conflict_IsDetectedWhicheverModLoadsFirst()
    {
        // Tie-break by ID: with IDs swapped the other mod is "second"; both orders must fail.
        string p = _mods.Mod("p", DependsOnCore, ("n.yaml", PatchHazard("key")));
        string q = _mods.Mod("q", DependsOnCore, ("n.yaml", "kind: node\ndelete: [s8.app.jet_fighter_g4]\npatch:\n  - {id: s8.fnd.transonic_aerodynamics, hazard: none}\n"));
        AssertFails("conflict on node ID", q, p, TestMods.CoreDir);
        AssertFails("conflict on node ID", TestMods.CoreDir, p, q);
    }

    [Theory]
    [InlineData("kind: node\npatch:\n  - {id: s8.fnd.transonic_aerodynamics, hazard: key}\ndelete: [s8.fnd.transonic_aerodynamics]\n")]
    [InlineData("kind: node\npatch:\n  - {id: s8.fnd.transonic_aerodynamics, hazard: key}\n  - {id: s8.fnd.transonic_aerodynamics, gen: 0}\n")]
    public void SameModTouchingAnIdTwice_Fails(string text)
    {
        string a = _mods.Mod("a", DependsOnCore, ("n.yaml", text));
        AssertFails("is changed twice in mod 'a'", TestMods.CoreDir, a);
    }

    [Fact]
    public void SameModTouchingAnIdTwice_AcrossFiles_Fails()
    {
        string a = _mods.Mod("a", DependsOnCore, ("n1.yaml", PatchHazard("key")), ("n2.yaml", PatchHazard("none")));
        AssertFails("is changed twice in mod 'a'", TestMods.CoreDir, a);
    }

    [Fact]
    public void DefineThenPatchOfANewId_InOneMod_Fails()
    {
        string scenario = File.ReadAllText(Path.Combine(TestMods.CoreDir, "scenario_1945.yaml"))
            .Replace("id: jet_slice_1945", "id: other_1945", StringComparison.Ordinal);
        string a = _mods.Mod("a", DependsOnCore,
            ("s.yaml", scenario + "patch:\n  - {id: other_1945, tech_lead: GBR}\n"));
        AssertFails("scenario ID 'other_1945' is changed twice in mod 'a'", TestMods.CoreDir, a);
    }

    [Fact]
    public void DefineInOneModThenPatchInDependent_Works()
    {
        string scenario = File.ReadAllText(Path.Combine(TestMods.CoreDir, "scenario_1945.yaml"))
            .Replace("id: jet_slice_1945", "id: other_1945", StringComparison.Ordinal);
        string a = _mods.Mod("a", DependsOnCore, ("s.yaml", scenario));
        string b = _mods.Mod("b", "[{id: a, min: \"1.0.0\"}]", ("s.yaml", "kind: scenario\npatch:\n  - {id: other_1945, tech_lead: GBR}\n"));
        GameData d = ModLoader.Load(TestMods.CoreDir, a, b);
        Assert.Equal(["jet_slice_1945", "other_1945"], d.Scenarios.Select(s => s.Id));
        Assert.Equal(d.Scenarios[0].ActorId.IndexOf("USA"), d.Scenarios[0].TechLead);
        Assert.Equal(d.Scenarios[1].ActorId.IndexOf("GBR"), d.Scenarios[1].TechLead);
    }

    [Theory]
    [InlineData("kind: node\noverride:\n  - {id: s8.app.jet_fighter_g4, number: 61}\n", "override of unknown node ID 's8.app.jet_fighter_g4'")]
    [InlineData("kind: node\npatch:\n  - {id: s8.app.jet_fighter_g4, gen: 4}\n", "patch of unknown node ID 's8.app.jet_fighter_g4'")]
    [InlineData("kind: node\ndelete: [s8.app.jet_fighter_g4]\n", "delete of unknown node ID 's8.app.jet_fighter_g4'")]
    [InlineData("kind: node\ndefine:\n  - {id: s8.app.jet_fighter_g4}\n", "was deleted by mod 'a'")]
    public void AfterDelete_OnlyErrors(string text, string message)
    {
        string a = _mods.Mod("a", DependsOnCore, ("n.yaml", "kind: node\ndelete: [s8.app.jet_fighter_g4]\n"));
        string b = _mods.Mod("b", "[{id: a, min: \"1.0.0\"}]", ("n.yaml", text));
        AssertFails(message, TestMods.CoreDir, a, b);
    }

    [Theory]
    [InlineData("kind: node\ndelete: [s8.fnd.nothing]\n", "delete of unknown node ID 's8.fnd.nothing'")]
    [InlineData("kind: node\noverride:\n  - {id: s8.fnd.nothing, gen: 0}\n", "override of unknown node ID")]
    [InlineData("kind: node\npatch:\n  - {id: s8.fnd.transonic_aerodynamics}\n", "changes nothing")]
    [InlineData("kind: node\npatch:\n  - {hazard: key}\n", "missing field 'id'")]
    [InlineData("kind: node\npatch:\n  - {id: s8.fnd.transonic_aerodynamics, hazzard: key}\n", "unknown field 'hazzard'")]
    [InlineData("kind: node\nremove: [s8.fnd.transonic_aerodynamics]\n", "unknown field 'remove'")]
    [InlineData("kind: nodes\n", "unknown kind 'nodes'")]
    [InlineData("define: []\n", "missing field 'kind'")]
    [InlineData("kind: node\ndelete: s8.fnd.transonic_aerodynamics\n", "expected a list")]
    [InlineData("kind: node\ndelete: [{id: s8.fnd.transonic_aerodynamics}]\n", "expected a scalar")]
    [InlineData("kind: product_class\npatch:\n  - {id: s8.fnd.transonic_aerodynamics, axis: perf}\n", "patch of unknown product_class ID")]
    public void BadOperations_Fail(string text, string message)
    {
        string a = _mods.Mod("a", DependsOnCore, ("n.yaml", text));
        AssertFails(message, TestMods.CoreDir, a);
    }

    [Fact]
    public void PatchIsShallow_AFieldIsReplacedAsAWhole()
    {
        // Patching anchor with only 'year' drops the other anchor fields -> missing field error.
        string a = _mods.Mod("a", DependsOnCore, ("n.yaml", "kind: node\npatch:\n  - {id: s8.fnd.transonic_aerodynamics, anchor: {year: 1947}}\n"));
        AssertFails("missing field", TestMods.CoreDir, a);
    }

    [Fact]
    public void ModWithoutObjects_IsAllowed_AndKeepsHash()
    {
        string a = _mods.Mod("a", DependsOnCore, ("n.yaml", "kind: node\n"));
        Assert.Equal(ModLoader.Load(TestMods.CoreDir).DataHash, ModLoader.Load(TestMods.CoreDir, a).DataHash);
    }

    [Fact]
    public void ThreeModCycle_Fails()
    {
        string a = _mods.Mod("a", "[{id: c, min: \"1.0.0\"}]", ("n.yaml", "kind: node\n"));
        string b = _mods.Mod("b", "[{id: a, min: \"1.0.0\"}]", ("n.yaml", "kind: node\n"));
        string c = _mods.Mod("c", "[{id: b, min: \"1.0.0\"}]", ("n.yaml", "kind: node\n"));
        AssertFails("dependency cycle among: a, b, c", TestMods.CoreDir, a, b, c);
    }

    [Fact]
    public void SameModTwice_IsDuplicateModId()
    {
        string a = _mods.Mod("a", DependsOnCore, ("n.yaml", "kind: node\n"));
        AssertFails("duplicate mod ID 'a'", TestMods.CoreDir, a, a);
    }

    [Theory]
    [InlineData("[{id: core, min: \"0.1.0\", below: \"0.1.1\"}]")]
    [InlineData("[{id: core, min: \"0.0.0\"}]")]
    [InlineData("[{id: core, min: \"0.1.0\", below: \"1.0.0\"}]")]
    public void VersionRange_Accepts(string deps)
    {
        string a = _mods.Mod("a", deps, ("n.yaml", "kind: node\n"));
        Assert.Equal(2, ModLoader.Load(TestMods.CoreDir, a).Mods.Length);
    }

    [Theory]
    [InlineData("[{id: core, min: \"0.1.1\"}]")]
    [InlineData("[{id: core, min: \"0.0.9\", below: \"0.1.0\"}]")]
    [InlineData("[{id: core, min: \"0.1.0\", below: \"0.1.0\"}]")]
    public void VersionRange_Rejects(string deps)
    {
        string a = _mods.Mod("a", deps, ("n.yaml", "kind: node\n"));
        AssertFails("needs 'core'", TestMods.CoreDir, a);
    }

    [Theory]
    [InlineData("id: a\nversion: \"1.0.0\"\ndependencies: [{id: a, min: \"1.0.0\"}]\nfiles: [n.yaml]\n", "cannot depend on itself")]
    [InlineData("id: a\nversion: \"1.0.0\"\ndependencies: [{id: core, min: \"0.1.0\"}, {id: core, min: \"0.0.1\"}]\nfiles: [n.yaml]\n", "duplicate dependency 'core'")]
    [InlineData("id: a\nversion: \"1.0.0\"\ndependencies: []\nfiles: [n.yaml, n.yaml]\n", "listed twice")]
    [InlineData("id: a\nversion: \"1.0.0\"\ndependencies: []\nfiles: [sub/n.yaml]\n", "must be a plain *.yaml name")]
    [InlineData("id: a\nversion: \"1.0.0\"\ndependencies: []\nfiles: [n.yml]\n", "must be a plain *.yaml name")]
    [InlineData("id: a\nversion: \"1.0.0\"\ndependencies: []\nfiles: [mod.yaml]\n", "must be a plain *.yaml name")]
    [InlineData("id: a\nversion: \"1.0.0\"\ndependencies: []\nfiles: []\n", "at least one data file")]
    [InlineData("id: A\nversion: \"1.0.0\"\ndependencies: []\nfiles: [n.yaml]\n", "must match [a-z][a-z0-9_]*")]
    [InlineData("id: a\nversion: \"01.0.0\"\ndependencies: []\nfiles: [n.yaml]\n", "is not MAJOR.MINOR.PATCH")]
    [InlineData("id: a\nversion: \"1.0\"\ndependencies: []\nfiles: [n.yaml]\n", "is not MAJOR.MINOR.PATCH")]
    [InlineData("id: a\nversion: \"1.0.0-rc1\"\ndependencies: []\nfiles: [n.yaml]\n", "is not MAJOR.MINOR.PATCH")]
    [InlineData("id: a\nversion: \"1.0.0\"\nfiles: [n.yaml]\n", "missing field 'dependencies'")]
    [InlineData("id: a\nversion: \"1.0.0\"\ndependencies: []\nfiles: [n.yaml]\nauthor: me\n", "unknown field 'author'")]
    [InlineData("id: a\nversion: \"1.0.0\"\ndependencies: [{id: core, min: \"0.1.0\", max: \"1.0.0\"}]\nfiles: [n.yaml]\n", "unknown field 'max'")]
    [InlineData("id: a\nversion: \"1.0.0\"\ndependencies: []\nfiles: [missing.yaml]\n", "does not exist")]
    public void BadManifest_Fails(string manifest, string message)
    {
        string dir = _mods.Mod("a", "[]", ("n.yaml", "kind: node\n"));
        File.WriteAllText(Path.Combine(dir, "mod.yaml"), manifest);
        if (!manifest.Contains("n.yaml", StringComparison.Ordinal))
            File.Delete(Path.Combine(dir, "n.yaml"));
        AssertFails(message, TestMods.CoreDir, dir);
    }

    [Fact]
    public void ModIdThatIsAYaml11Bool_MustBeQuoted()
    {
        // [a-z][a-z0-9_]* allows 'y', 'no', 'on'...; plain they are rejected, quoted they load.
        string dir = _mods.Mod("y", DependsOnCore, ("n.yaml", "kind: node\n"));
        AssertFails("implicit bool/null 'y'", TestMods.CoreDir, dir);
        File.WriteAllText(Path.Combine(dir, "mod.yaml"), "id: \"y\"\nversion: \"1.0.0\"\ndependencies: [{id: core, min: \"0.1.0\"}]\nfiles: [n.yaml]\n");
        Assert.Equal(["core", "y"], ModLoader.Load(dir, TestMods.CoreDir).Mods.Select(m => m.Id));
    }

    [Fact]
    public void MissingManifest_Fails()
    {
        string dir = _mods.Mod("a", "[]", ("n.yaml", "kind: node\n"));
        File.Delete(Path.Combine(dir, "mod.yaml"));
        AssertFails("mod manifest not found", TestMods.CoreDir, dir);
    }
}
