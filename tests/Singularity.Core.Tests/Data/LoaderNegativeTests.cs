using Singularity.Core.Data;

namespace Singularity.Core.Tests.Data;

/// <summary>Every broken mod fails loudly with a message that names the problem (ADR-0010).</summary>
public class LoaderNegativeTests : IDisposable
{
    private readonly TestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    private void AssertFails(string file, string from, string to, string expectedMessage)
    {
        string core = _mods.CopyCore((name, text) => name == file ? TestMods.ReplaceOnce(text, from, to) : text);
        var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(core));
        Assert.True(e.Message.Contains(expectedMessage, StringComparison.Ordinal), e.Message);
        Assert.Contains(file, e.Message);
    }

    [Fact]
    public void BrokenReference_InRequires() =>
        AssertFails("nodes.yaml", "requires: [s8.fnd.boundary_layer]\n    hazard: none\n    anchor:\n      year: 1944",
            "requires: [s8.fnd.no_such_node]\n    hazard: none\n    anchor:\n      year: 1944", "unknown node 's8.fnd.no_such_node'");

    [Fact]
    public void BrokenReference_InScenarioKnown() =>
        AssertFails("scenario_1945.yaml", "known: [s7.fnd.brayton_cycle, s8.fnd.boundary_layer, s8.fnd.compressible_flow, s5.fnd.precipitation_hardening, s8.prn.gas_turbine, s5.cmp.aluminium_alloy_sheet,",
            "known: [s7.fnd.brayton_cycle, s8.fnd.typo, s8.fnd.compressible_flow, s5.fnd.precipitation_hardening, s8.prn.gas_turbine, s5.cmp.aluminium_alloy_sheet,", "unknown node 's8.fnd.typo'");

    [Fact]
    public void BrokenReference_InProductClass() =>
        AssertFails("product_classes.yaml", "prc_set: [s6.prc.centrifugal_impeller_machining]",
            "prc_set: [s6.prc.centrifugal_impeller_machinin]", "unknown node");

    [Fact]
    public void DuplicateId_InDefine() =>
        AssertFails("nodes.yaml", "  - id: s8.fnd.boundary_layer", "  - id: s7.fnd.brayton_cycle", "duplicate node ID 's7.fnd.brayton_cycle'");

    [Fact]
    public void DuplicateNodeNumber() =>
        AssertFails("nodes.yaml", "    number: 2\n", "    number: 1\n", "node number 1 is used by");

    [Fact]
    public void UnknownCluster() =>
        AssertFails("nodes.yaml", "    discipline: prop\n    cluster: AERO\n    requires: []",
            "    discipline: prop\n    cluster: JETS\n    requires: []", "unknown value 'JETS'");

    [Fact]
    public void UnknownDiscipline() =>
        AssertFails("nodes.yaml", "    discipline: prop\n    cluster: AERO\n    requires: []",
            "    discipline: rocketry\n    cluster: AERO\n    requires: []", "unknown value 'rocketry'");

    [Theory]
    [InlineData("yes")]
    [InlineData("on")]
    [InlineData("True")]
    [InlineData("no")]
    public void ImplicitBool_IsRejected(string word) =>
        AssertFails("nodes.yaml", "      source: \"S32\"\n      estimated: false", $"      source: \"S32\"\n      estimated: {word}", "implicit bool");

    [Fact]
    public void ImplicitBool_InStringFieldIsRejected() =>
        AssertFails("nodes.yaml", "    hazard: none\n    informs:\n      - {to: s8.eng.turbojet_g1",
            "    hazard: off\n    informs:\n      - {to: s8.eng.turbojet_g1", "implicit bool");

    [Fact]
    public void NumberWhereTextExpected_MustBeQuoted() =>
        AssertFails("nodes.yaml", "      source: \"S32\"", "      source: 1904", "quote it");

    [Fact]
    public void UnknownField() =>
        AssertFails("nodes.yaml", "    number: 2\n", "    number: 2\n    numbr: 2\n", "unknown field 'numbr'");

    [Fact]
    public void TierOnNonPrcNode() =>
        AssertFails("nodes.yaml", "    number: 2\n", "    number: 2\n    tier: 3\n", "only PRC nodes have tier");

    [Fact]
    public void CapabilityReqOutsideTierRule() =>
        AssertFails("nodes.yaml", "      precision: 5.5\n", "      precision: 6.5\n", "outside [tier - 1, tier + 0.5]");

    [Fact]
    public void MoreThanSixFractionalDigits() =>
        AssertFails("scenario_1945.yaml", "growth_pa: 0.0299", "growth_pa: 0.02990001", "more than 6 fractional digits");

    [Fact]
    public void InformsAcrossDisciplines() =>
        AssertFails("nodes.yaml", "{to: s8.eng.turbojet_g1, weight: 0.6}", "{to: s8.eng.straight_wing_jet_airframe, weight: 0.6}", "same discipline");

    [Fact]
    public void RequiresLaterGeneration() =>
        AssertFails("nodes.yaml", "requires: [s7.fnd.brayton_cycle]\n    hazard: none", "requires: [s8.eng.turbofan_g4]\n    hazard: none", "later generation");

    [Fact]
    public void StartKnowledgeNotClosed()
    {
        // F-SC1: SUN cannot know jet_fighter_g1 without the alloy.
        AssertFails("scenario_1945.yaml", "s8.prc.stressed_skin_assembly, s8.prc.jet_engine_assembly_test]\n        k_d: {aero: 0.25",
            "s8.prc.stressed_skin_assembly, s8.prc.jet_engine_assembly_test, s8.app.jet_fighter_g1]\n        k_d: {aero: 0.25", "but not its requires");
    }

    [Fact]
    public void MasteredMustBeKnownPrc() =>
        AssertFails("scenario_1945.yaml", "mastered: [s5.prc.light_alloy_rolling, s5.prc.alloying_element_refining, s6.prc.precision_gear_bearing, s8.prc.stressed_skin_assembly]\n        fleet: []\n        trophy_engineers: 1385",
            "mastered: [s5.prc.light_alloy_rolling, s5.prc.nicr_alloy_melt]\n        fleet: []\n        trophy_engineers: 1385", "must be a known PRC node");

    [Fact]
    public void UnknownTechLead() =>
        AssertFails("scenario_1945.yaml", "tech_lead: USA", "tech_lead: DEU", "not an actor");

    [Fact]
    public void MissingProvenance() =>
        AssertFails("scenario_1945.yaml", "      - {field: hcap, rule: R-HC, sources: [], estimated: true}\n", "", "'hcap' has no source or rule");

    [Fact]
    public void UnlistedDataFile()
    {
        string core = _mods.CopyCore();
        File.WriteAllText(Path.Combine(core, "extra.yaml"), "kind: node\n");
        var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(core));
        Assert.Contains("not listed", e.Message);
    }

    [Fact]
    public void YmlFile_IsRejectedEvenIfUnused()
    {
        // Only .yaml is a data extension; a stray .yml would otherwise be ignored silently.
        string core = _mods.CopyCore();
        File.WriteAllText(Path.Combine(core, "extra.yml"), "kind: node\n");
        var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(core));
        Assert.Contains("must use the .yaml extension", e.Message);
    }
}
