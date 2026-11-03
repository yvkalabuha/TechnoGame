using Singularity.Core.Data;

namespace Singularity.Core.Tests.Data;

/// <summary>
/// One negative fixture per validator rule not covered by <see cref="LoaderNegativeTests"/>:
/// each edits one spot of a copy of mods/core and expects a loud, named load error.
/// Test-engineer audit of TASK-0013.
/// </summary>
public class ValidatorRuleTests : IDisposable
{
    private readonly TestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    private const string Comp =
        "alternatives:\n          - {id: comp_centrifugal, rank: 1, perf_mult: 1.0, prc_set: [s6.prc.centrifugal_impeller_machining], requires_nodes: []}\n" +
        "          - {id: comp_axial, rank: 2, perf_mult: 1.15, prc_set: [s6.prc.axial_compressor_machining], requires_nodes: [s8.cmp.axial_compressor_assembly]}\n";

    private const string BraytonInforms =
        "      - {to: s8.eng.turbojet_g1, weight: 0.6}\n      - {to: s8.prc.jet_engine_assembly_test, weight: 0.6}\n";

    private const string JetPrcReq = "    tier: 4\n    capability_req:\n      precision: 3.5\n      quality_control: 4\n    requires: [s8.eng.turbojet_g1]";

    private void AssertFails(string file, string from, string to, string expectedMessage)
    {
        string core = _mods.CopyCore((name, text) => name == file ? TestMods.ReplaceOnce(text, from, to) : text);
        var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(core));
        Assert.True(e.Message.Contains(expectedMessage, StringComparison.Ordinal), e.Message);
        Assert.Contains(file, e.Message);
    }

    // ---- nodes.yaml ----

    [Theory]
    [InlineData("  - id: s8.eng.turbojet_g1\n    number: 17\n", "  - id: turbojet_g1\n    number: 17\n", "must look like sN.type.name")]
    [InlineData("  - id: s8.eng.turbojet_g1\n    number: 17\n", "  - id: s8.eng.turbojet_g1\n    number: 0\n", "node number must be at least 1")]
    [InlineData("    number: 17\n    type: ENG\n", "    number: 17\n    type: CMP\n", "does not match its type")]
    [InlineData("    number: 17\n    type: ENG\n    gen: 1\n", "    number: 17\n    type: ENG\n    gen: 10\n", "gen must be in 0..9")]
    [InlineData(JetPrcReq, "    capability_req:\n      precision: 3.5\n      quality_control: 4\n    requires: [s8.eng.turbojet_g1]", "needs tier, capability_req and labour_p")]
    [InlineData("    labour_p: 1350\n", "", "needs tier, capability_req and labour_p")]
    [InlineData("    labour_p: 1350\n", "    labour_p: 0\n", "labour_p must be greater than 0")]
    [InlineData("    number: 17\n    type: ENG\n", "    number: 17\n    type: ENG\n    labour_p: 5\n", "only PRC nodes have tier, capability_req and labour_p")]
    [InlineData(JetPrcReq, "    tier: 10\n    capability_req:\n      precision: 3.5\n      quality_control: 4\n    requires: [s8.eng.turbojet_g1]", "tier must be in 1..9")]
    [InlineData(JetPrcReq, "    tier: 4\n    capability_req:\n      precision: 3.5\n      quality_control: 3.5\n    requires: [s8.eng.turbojet_g1]", "has no key dimension")]
    [InlineData(JetPrcReq, "    tier: 4\n    capability_req:\n      precision: 2.999999\n      quality_control: 4\n    requires: [s8.eng.turbojet_g1]", "outside [tier - 1, tier + 0.5]")]
    [InlineData(JetPrcReq, "    tier: 4\n    capability_req:\n      precision: 3.5\n      quality_control: 4.500001\n    requires: [s8.eng.turbojet_g1]", "outside [tier - 1, tier + 0.5]")]
    [InlineData(JetPrcReq, "    tier: 4\n    capability_req:\n      precision: 3.5\n      quality_control: 4\n      speed: 4\n    requires: [s8.eng.turbojet_g1]", "unknown field 'speed'")]
    [InlineData("requires: [s8.prn.gas_turbine]\n    hazard: key", "requires: [s8.eng.turbojet_g1]\n    hazard: key", "requires itself")]
    [InlineData("requires: [s8.prn.gas_turbine]\n    hazard: key", "requires: [s8.prn.gas_turbine, s8.prc.jet_engine_assembly_test]\n    hazard: key", "requires cycle")]
    [InlineData("requires: [s8.prn.gas_turbine]\n    hazard: key", "requires: [s8.prn.gas_turbine, s8.prn.gas_turbine]\n    hazard: key", "required twice")]
    [InlineData("requires: [s8.prn.gas_turbine]\n    hazard: key", "requires: [{any_of: [s8.prn.gas_turbine]}]\n    hazard: key", "any_of needs at least two")]
    [InlineData(BraytonInforms, "      - {to: s8.eng.turbojet_g1, weight: 0.6}\n      - {to: s8.eng.turbojet_g1, weight: 0.3}\n", "duplicate informs edge")]
    [InlineData(BraytonInforms, "      - {to: s8.eng.turbojet_g1, weight: 1.000001}\n      - {to: s8.prc.jet_engine_assembly_test, weight: 0.6}\n", "must be in (0, 1]")]
    [InlineData(BraytonInforms, "      - {to: s8.eng.turbojet_g1, weight: 0}\n      - {to: s8.prc.jet_engine_assembly_test, weight: 0.6}\n", "must be in (0, 1]")]
    [InlineData(BraytonInforms, "      - {to: s8.eng.turbojet_g1, weight: -0.5}\n      - {to: s8.prc.jet_engine_assembly_test, weight: 0.6}\n", "must be in (0, 1]")]
    public void NodeRule(string from, string to, string message) => AssertFails("nodes.yaml", from, to, message);

    [Fact]
    public void InformsWeightOne_IsAccepted()
    {
        string core = _mods.CopyCore((name, text) => name == "nodes.yaml"
            ? TestMods.ReplaceOnce(text, BraytonInforms, "      - {to: s8.eng.turbojet_g1, weight: 1}\n      - {to: s8.prc.jet_engine_assembly_test, weight: 0.6}\n")
            : text);
        ModLoader.Load(core);
    }

    // ---- product_classes.yaml ----

    [Theory]
    [InlineData("  - id: jet_fighter\n", "  - id: Jet_fighter\n", "must match [a-z][a-z0-9_]*")]
    [InlineData("axis: perf", "axis: speed", "axis")]
    [InlineData("- id: compressor\n        base: false", "- id: hot_section\n        base: false", "duplicate slot 'hot_section'")]
    [InlineData("- id: compressor\n        base: false", "- id: compressor\n        base: true", "exactly one base slot, found 2")]
    [InlineData("- id: engine\n        base: true", "- id: engine\n        base: false", "exactly one base slot, found 0")]
    [InlineData(Comp, "alternatives: []\n", "slot 'compressor' has no alternatives")]
    [InlineData("{id: comp_axial, rank: 2,", "{id: comp_axial, rank: 1,", "duplicate rank 1 in slot 'compressor'")]
    [InlineData("{id: comp_axial, rank: 2,", "{id: comp_centrifugal, rank: 2,", "duplicate alternative 'comp_centrifugal'")]
    [InlineData("{id: comp_axial, rank: 2,", "{id: eng_g1, rank: 2,", "duplicate alternative 'eng_g1'")]
    [InlineData("{id: comp_axial, rank: 2,", "{id: Comp_axial, rank: 2,", "alternative ID 'Comp_axial' must match")]
    [InlineData("{id: comp_axial, rank: 2,", "{id: comp_axial, rank: 0,", "rank must be at least 1")]
    [InlineData("rank: 2, perf_mult: 1.15, prc_set: [s6.prc.axial", "rank: 2, perf_mult: 0, prc_set: [s6.prc.axial", "perf_mult must be positive")]
    [InlineData("prc_set: [s6.prc.axial_compressor_machining]", "prc_set: [s8.cmp.axial_compressor_assembly]", "is not a PRC node")]
    [InlineData("prc_set: [s6.prc.axial_compressor_machining]", "prc_set: [s6.prc.axial_compressor_machining, s6.prc.axial_compressor_machining]", "lists 's6.prc.axial_compressor_machining' twice")]
    [InlineData("requires_nodes: [s8.cmp.axial_compressor_assembly]}", "requires_nodes: [s8.cmp.axial_compressor_assembly, s8.cmp.axial_compressor_assembly]}", "required twice")]
    [InlineData("s8.eng.area_ruled_airframe, s8.eng.delta_wing_airframe]}]}", "s8.eng.area_ruled_airframe]}]}", "any_of needs at least two")]
    [InlineData("lighten_order: [hot_section, compressor, afterburner, airframe]", "lighten_order: [hot_section, compressor, afterburner, airframe, wings]", "unknown slot 'wings'")]
    [InlineData("lighten_order: [hot_section, compressor, afterburner, airframe]", "lighten_order: [hot_section, compressor, afterburner, airframe, engine]", "is the base slot or listed twice")]
    [InlineData("lighten_order: [hot_section, compressor, afterburner, airframe]", "lighten_order: [hot_section, compressor, afterburner, airframe, compressor]", "is the base slot or listed twice")]
    [InlineData("lighten_order: [hot_section, compressor, afterburner, airframe]", "lighten_order: [hot_section, compressor, afterburner]", "must list every non-base slot")]
    public void ProductClassRule(string from, string to, string message) => AssertFails("product_classes.yaml", from, to, message);

    // ---- scenario_1945.yaml ----

    private const string UsaKnownEnd = "s8.app.jet_fighter_g1]\n        k_d: {aero: 0.30, prop: 0.30";
    private const string UsaFleet = "fleet:\n          - {product_class: jet_fighter, gen: 1, variant: superior, count: 100, cohort_year: 1945}\n        trophy_engineers: 86";

    [Theory]
    [InlineData("  - id: jet_slice_1945\n", "  - id: Jet_slice_1945\n", "scenario ID 'Jet_slice_1945' must match")]
    [InlineData("start: {year: 1945, month: 9}", "start: {year: 1945, month: 13}", "start.month must be in 1..12")]
    [InlineData("start: {year: 1945, month: 9}", "start: {year: 1945, month: 0}", "start.month must be in 1..12")]
    [InlineData("start: {year: 1945, month: 9}", "start: {year: 1945, month: 9.0}", "expected an integer")]
    [InlineData("mastered_history: {since_month: -6, proc_x_half_lives: 3}", "mastered_history: {since_month: 1, proc_x_half_lives: 3}", "since_month must be at most 0")]
    [InlineData("mastered_history: {since_month: -6, proc_x_half_lives: 3}", "mastered_history: {since_month: -6, proc_x_half_lives: -1}", "proc_x_half_lives at least 0")]
    [InlineData("clusters: [MET, CHEM, MECH, AERO]", "clusters: [MET, CHEM, MECH, AERO, MET]", "lists 'MET' twice")]
    [InlineData("disciplines: [aero, prop, metal, chem, mech]", "disciplines: []", "must not be empty")]
    [InlineData("{field: hcap, rule: R-HC,", "{field: hcapp, rule: R-HC,", "unknown actor field 'hcapp'")]
    [InlineData("{field: hcap, rule: R-HC,", "{field: hcap, rule: HC,", "must be an R-* rule ID")]
    [InlineData("{field: hcap, rule: R-HC, sources: [], estimated: true}\n", "{field: hcap, rule: R-HC, sources: [], estimated: true}\n      - {field: hcap, rule: R-HC, sources: [], estimated: true}\n", "provenance of 'hcap' is given twice")]
    [InlineData("sources: [S8, S36]", "sources: [S8, S8]", "a source is listed twice")]
    [InlineData("      - id: USA\n", "      - id: US\n", "must be three upper-case letters")]
    [InlineData("      - id: GBR\n", "      - id: USA\n", "duplicate actor ID 'USA'")]
    [InlineData("trophy_engineers: 86\n", "trophy_engineers: -1\n", "trophy_engineers must not be negative")]
    [InlineData("          AERO: {precision: 4.00, purity: 3.80, quality_control: 3.70}\n        tooling_m:\n          MET: {precision: 4.20",
                "        tooling_m:\n          MET: {precision: 4.20", "culture_h is missing cluster AERO")]
    [InlineData("          AERO: {precision: 4.20, purity: 4.00, quality_control: 3.90}\n        hcap: 5.50",
                "        hcap: 5.50", "tooling_m is missing cluster AERO")]
    [InlineData("k_d: {aero: 0.30, prop: 0.30, metal: 0.25, chem: 0.10, mech: 0.15}", "k_d: {aero: 0.30, prop: 0.30, metal: 0.25, chem: 0.10}", "is missing mech")]
    [InlineData("k_d: {aero: 0.30, prop: 0.30, metal: 0.25, chem: 0.10, mech: 0.15}", "k_d: {aero: 0.30, prop: 0.30, metal: 0.25, chem: -0.10, mech: 0.15}", "chem must not be negative")]
    [InlineData(UsaKnownEnd, "s8.app.jet_fighter_g1, s8.app.jet_fighter_g1]\n        k_d: {aero: 0.30, prop: 0.30", "lists 's8.app.jet_fighter_g1' twice")]
    [InlineData(UsaFleet, "fleet:\n          - {product_class: bomber, gen: 1, variant: superior, count: 100, cohort_year: 1945}\n        trophy_engineers: 86", "unknown product class 'bomber'")]
    [InlineData(UsaFleet, "fleet:\n          - {product_class: jet_fighter, gen: 1, variant: superior, count: 0, cohort_year: 1945}\n        trophy_engineers: 86", "fleet count must be at least 1")]
    [InlineData(UsaFleet, "fleet:\n          - {product_class: jet_fighter, gen: 1, variant: superb, count: 100, cohort_year: 1945}\n        trophy_engineers: 86", "unknown value 'superb'")]
    [InlineData("estimated: [gdp_1945, gdppc_1945, growth_pa, known]", "estimated: [gdp_1945, gdppc_1945, growth_pa, known, wealth]", "'wealth' is not an actor field")]
    [InlineData("estimated: [gdp_1945, gdppc_1945, growth_pa, known]", "estimated: [gdp_1945, gdppc_1945, growth_pa, known, known]", "'known' is not an actor field or is listed twice")]
    [InlineData("gdppc_1945: 16478\n", "gdppc_1945: 0\n", "gdppc_1945 must be positive")]
    [InlineData("budget_index: 1.000\n", "budget_index: -1.0\n", "budget_index must be positive")]
    [InlineData("hcap: 5.50\n", "hcap: -0.000001\n", "hcap must not be negative")]
    [InlineData("gdp_1945: 2314.67\n", "gdp_1945: \"2314.67\"\n", "expected a plain number")]
    [InlineData("trophy_engineers: 86\n", "trophy_engineers: 2147483648\n", "out of range")]
    public void ScenarioRule(string from, string to, string message) => AssertFails("scenario_1945.yaml", from, to, message);

    [Fact]
    public void EstimatedFieldWithoutProvenance_Fails()
    {
        string core = _mods.CopyCore((name, text) => name == "scenario_1945.yaml"
            ? TestMods.ReplaceOnce(TestMods.ReplaceOnce(text, "      - {field: hcap, rule: R-HC, sources: [], estimated: true}\n", ""),
                "estimated: [gdp_1945, gdppc_1945, growth_pa, known]", "estimated: [gdp_1945, gdppc_1945, growth_pa, known, hcap]")
            : text);
        var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(core));
        Assert.Contains("hcap", e.Message);
    }
}
