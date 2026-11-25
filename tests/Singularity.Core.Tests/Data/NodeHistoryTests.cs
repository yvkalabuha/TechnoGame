using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Data;

/// <summary>TASK-0157 / #486: every node has a history with sources; effects are derived; history is display data only.</summary>
public class NodeHistoryTests : IDisposable
{
    private static readonly GameData Data = ModLoader.Load(TestMods.CoreDir);
    private readonly TestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    [Fact]
    public void EveryNode_HasHistoryWithOneToThreeSources()
    {
        NodeHistoryTable h = Data.History;
        for (int r = 0; r < Data.Nodes.Count; r++)
        {
            Assert.True(h.Has[r], Data.Nodes.Id[r]);
            int count = h.SourceStart[r + 1] - h.SourceStart[r];
            Assert.InRange(count, 1, NodeHistoryTable.MaxSources);
        }
    }

    [Fact]
    public void Sources_AreDatedAndNothingNonFreeIsQuoted()
    {
        NodeHistoryTable h = Data.History;
        for (int i = 0; i < h.SourceUrl.Length; i++)
        {
            Assert.True(string.CompareOrdinal(h.SourceAccessed[i], "2026-11-24") >= 0, h.SourceUrl[i]);
            if (h.SourceUse[i] == SourceUse.Quote)
                Assert.NotEqual(SourceLicense.Other, h.SourceLicense[i]);
        }
        Assert.DoesNotContain(SourceUse.Quote, h.SourceUse);   // all text is retold in our words
    }

    [Fact]
    public void Unconfirmed_IsSetOnlyWhereResearchLeftOpenFacts()
    {
        NodeHistoryTable h = Data.History;
        Assert.True(h.Unconfirmed[Data.Nodes.IndexOf("s5.prn.gaas_wafer_growth")]);
        Assert.False(h.Unconfirmed[Data.Nodes.IndexOf("s8.eng.delta_wing_airframe")]);
        Assert.InRange(h.Unconfirmed.Count(u => u), 1, Data.Nodes.Count - 1);
    }

    [Fact]
    public void EpicNodes_AreFewAndKnown()
    {
        NodeHistoryTable h = Data.History;
        Assert.InRange(h.Epic.Count(e => e), 1, Data.Nodes.Count / 5);
        Assert.True(h.Epic[Data.Nodes.IndexOf("s8.eng.turbojet_g1")]);
        Assert.False(h.Epic[Data.Nodes.IndexOf("s8.eng.delta_wing_airframe")]);
    }

    [Fact]
    public void DeltaWing_HasDerivedEffects_WithPartnerPathAndMandatoryProcesses()
    {
        var fx = new List<NodeEffect>();
        int delta = Data.Nodes.IndexOf("s8.eng.delta_wing_airframe"), ruled = Data.Nodes.IndexOf("s8.eng.area_ruled_airframe");
        NodeEffects.Derive(Data, delta, fx);
        ProductClassTable pc = Data.ProductClasses;
        NodeEffect milled = Assert.Single(fx, e => e.Kind == NodeEffectKind.AlternativeUnlock && pc.AltId[e.AltRow] == "af_milled");
        Assert.Equal(pc.AltPerfMult[milled.AltRow], milled.Value);
        Assert.Equal(pc.AltPerfMult[milled.AltRow - 1], milled.Previous);
        Assert.True(milled.Value > milled.Previous);

        // The any_of partner is an alternative path; the manufacturing processes of af_milled are mandatory co-requirements:
        // knowing the node alone does not make the variant available (DesignRules.Available checks both lists).
        var alternatives = new List<int>();
        var coRequired = new List<int>();
        var processes = new List<int>();
        NodeEffects.Requirements(Data, milled, delta, alternatives, coRequired, processes);
        Assert.Equal([ruled], alternatives);
        Assert.Empty(coRequired);
        Assert.Contains(Data.Nodes.IndexOf("s8.prc.machined_integral_skins"), processes);
        Assert.Equal(pc.PrcSet(milled.AltRow).Length, processes.Count);
        Assert.True(milled.Together);

        // af_titanium additionally needs the titanium alloy node.
        NodeEffect titanium = Assert.Single(fx, e => e.Kind == NodeEffectKind.AlternativeUnlock && pc.AltId[e.AltRow] == "af_titanium");
        alternatives.Clear();
        processes.Clear();
        NodeEffects.Requirements(Data, titanium, delta, alternatives, coRequired, processes);
        Assert.Equal([ruled], alternatives);
        Assert.Equal([Data.Nodes.IndexOf("s5.cmp.titanium_alloy")], coRequired);
        Assert.True(titanium.Together);
    }

    [Fact]
    public void Together_IsExactlyOtherGroupsOrProcesses_ForEveryAlternative()
    {
        var fx = new List<NodeEffect>();
        var alternatives = new List<int>();
        var coRequired = new List<int>();
        var processes = new List<int>();
        for (int r = 0; r < Data.Nodes.Count; r++)
        {
            fx.Clear();
            NodeEffects.Derive(Data, r, fx);
            foreach (NodeEffect e in fx.Where(e => e.Kind == NodeEffectKind.AlternativeUnlock))
            {
                alternatives.Clear();
                coRequired.Clear();
                processes.Clear();
                NodeEffects.Requirements(Data, e, r, alternatives, coRequired, processes);
                Assert.Equal(coRequired.Count > 0 || processes.Count > 0, e.Together);
            }
        }
    }

    [Fact]
    public void AnyOfMember_IsAnAlternativePath_NotAMandatoryPrerequisite()
    {
        NodeTable n = Data.Nodes;
        int mach2 = n.IndexOf("s8.eng.mach2_airframe_g4");
        var fx = new List<NodeEffect>();
        NodeEffects.Derive(Data, n.IndexOf("s8.eng.delta_wing_airframe"), fx);
        Assert.Contains(fx, e => e.Kind == NodeEffectKind.AlternativePathFor && e.Node == mach2);
        Assert.DoesNotContain(fx, e => e.Kind == NodeEffectKind.RequiredFor && e.Node == mach2);
        fx.Clear();
        NodeEffects.Derive(Data, n.IndexOf("s8.fnd.boundary_layer"), fx);
        Assert.Contains(fx, e => e.Kind == NodeEffectKind.RequiredFor && e.Node == n.IndexOf("s8.fnd.compressible_flow"));
    }

    [Fact]
    public void GenerationEffect_CarriesTheAirframeRankThatGatesIt()
    {
        ProductClassTable pc = Data.ProductClasses;
        var fx = new List<NodeEffect>();
        NodeEffects.Derive(Data, Data.Nodes.IndexOf("s8.app.jet_fighter_g6"), fx);
        NodeEffect g6 = Assert.Single(fx, e => e.Kind == NodeEffectKind.Generation);
        Assert.Equal(Fx6.FromInt(6), g6.Value);
        int g = pc.GenStart[g6.ClassRow] + 5;
        Assert.Equal(pc.GenPlatformMinRank[g], g6.MinRank);
        Assert.True(g6.MinRank > 1);
        Assert.True(g6.Together);   // the APP node alone does not give the generation: an airframe of that rank is needed too
    }

    [Fact]
    public void FlyByWire_IsShownAsALossAgainstItsSameClassPeer()
    {
        ProductClassTable pc = Data.ProductClasses;
        var fx = new List<NodeEffect>();
        NodeEffects.Derive(Data, Data.Nodes.IndexOf("s8.eng.fly_by_wire_relaxed_stability"), fx);
        NodeEffect fbw = Assert.Single(fx, e => e.Kind == NodeEffectKind.AlternativeUnlock && pc.AltId[e.AltRow] == "af_fbw");
        int mach2 = pc.AlternativeIndex(fbw.ClassRow, "airframe", "af_mach2");
        Assert.True(mach2 >= 0);
        Assert.Equal(pc.AltAfClass[mach2], pc.AltAfClass[fbw.AltRow]);
        Assert.Equal(pc.AltPerfMult[mach2], fbw.Previous);
        Assert.True(fbw.Value < fbw.Previous);   // honest: 1.75 against 1.80 of the same airframe class
        // The first alternative of a class has no same-class peer and is compared with the best lower class.
        NodeEffects.Derive(Data, Data.Nodes.IndexOf("s8.eng.mach2_airframe_g4"), fx);
        NodeEffect m2 = fx.First(e => e.Kind == NodeEffectKind.AlternativeUnlock && pc.AltId[e.AltRow] == "af_mach2");
        Assert.True(m2.Value > m2.Previous);
    }

    [Fact]
    public void ProcessNodes_AndKnowledgeNodes_GetTheirSimulationEffects()
    {
        var fx = new List<NodeEffect>();
        NodeEffects.Derive(Data, Data.Nodes.IndexOf("s5.prc.single_crystal_casting"), fx);
        Assert.Contains(fx, e => e.Kind == NodeEffectKind.ProcessUnlock && e.Value > Fx6.Zero);
        Assert.Contains(fx, e => e.Kind == NodeEffectKind.Mastery);   // tier 5 and more: mastery programme

        int bl = Data.Nodes.IndexOf("s8.fnd.boundary_layer");
        fx.Clear();
        NodeEffects.Derive(Data, bl, fx);
        NodeTable n = Data.Nodes;
        var informs = fx.Where(e => e.Kind == NodeEffectKind.Informs).ToArray();
        Assert.Equal(n.InformsStart[bl + 1] - n.InformsStart[bl], informs.Length);
        Assert.All(informs, e => Assert.True(e.Value > Fx6.Zero && e.Node >= 0));
        Assert.Contains(fx, e => e.Kind == NodeEffectKind.RequiredFor && e.Node == n.IndexOf("s8.fnd.compressible_flow"));
    }

    [Fact]
    public void NodesWithoutAnyDerivedEffect_AreExactlyTheKnownHistoryOnlyOnes()
    {
        var fx = new List<NodeEffect>();
        var without = new List<string>();
        for (int r = 0; r < Data.Nodes.Count; r++)
        {
            fx.Clear();
            NodeEffects.Derive(Data, r, fx);
            if (fx.Count == 0)
                without.Add(Data.Nodes.Id[r]);
        }
        // History only: no product alternative, process, generation, research edge or dependant uses the node today,
        // so a bonus line would be a fake mechanic.
        // #575: the leaves of the new tree (nothing requires them, no product slice uses them yet; their research_speed is unused until TASK-0187).
        string[] expected =
        [
            "s10.eng.crewed_lunar_landing", "s10.eng.reconnaissance_satellite", "s10.eng.satellite_navigation_gps", "s10.eng.sounding_rocket_science",
            "s10.eng.space_telescope", "s11.eng.fission_weapon_g1", "s11.eng.light_water_power_reactor", "s11.prc.gas_centrifuge_enrichment",
            "s11.prn.laser_inertial_confinement", "s11.prn.neutron_transport_monte_carlo", "s5.prc.additive_manufacturing_metal", "s7.eng.reusable_booster_landing",
            "s7.prn.composite_solid_propellant", "s8.eng.digital_beamforming", "s8.eng.infrared_air_to_air_missile", "s9.prc.euv_lithography",
            "s9.prn.deep_learning_science", "s9.prn.high_level_language_compiler",
        ];
        Assert.Equal(expected, without.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void CoreHistoryFile_NamesOnlyExistingNodes()
    {
        foreach (string line in File.ReadLines(Path.Combine(TestMods.CoreDir, "node_history.yaml")).Where(l => l.StartsWith("  - id: ", StringComparison.Ordinal)))
            Assert.True(Data.Nodes.IndexOf(line[8..].Trim()) >= 0, line);
    }

    [Fact]
    public void History_IsNotPartOfDataHash_AndNeutralToIt()
    {
        string dir = _mods.CopyCore((name, text) => name == "node_history.yaml"
            ? text.Replace("epic: false", "epic: true", StringComparison.Ordinal) : text);
        GameData edited = ModLoader.Load(dir);
        Assert.NotEqual(Data.History.Epic, edited.History.Epic);
        Assert.Equal(Data.DataHash, edited.DataHash);
    }

    [Fact]
    public void CoreWithoutHistoryFile_LoadsWithTheSameDataHash()
    {
        string dir = _mods.CopyCore((name, text) => name == "mod.yaml"
            ? text.Replace("node_history.yaml, ", "", StringComparison.Ordinal) : text);
        File.Delete(Path.Combine(dir, "node_history.yaml"));
        GameData bare = ModLoader.Load(dir);
        Assert.Equal(Data.DataHash, bare.DataHash);
        Assert.DoesNotContain(true, bare.History.Has);
    }

    [Fact]
    public void QuotingNonFreeSource_AndTooManySources_AreRejected()
    {
        Assert.Throws<DataLoadException>(() => Load(t => t.Replace("license: other, use: paraphrase", "license: other, use: quote", StringComparison.Ordinal)));
        Assert.Throws<DataLoadException>(() => Load(t => t.Replace("sources:\n", "sources:\n      - {url: \"https://example.org/a\", accessed: \"2026-11-24\", check: opened, license: PD, use: paraphrase}\n      - {url: \"https://example.org/b\", accessed: \"2026-11-24\", check: opened, license: PD, use: paraphrase}\n      - {url: \"https://example.org/c\", accessed: \"2026-11-24\", check: opened, license: PD, use: paraphrase}\n", StringComparison.Ordinal)));
    }

    [Fact]
    public void ModThatDeletesACoreNode_LoadsWithAWarning_NotAnError()
    {
        string mod = _mods.Mod("a", "[{id: core, min: \"0.1.0\"}]", ("nodes.yaml", "kind: node\ndelete: [s8.app.jet_fighter_g4]\n"), TestMods.DropG4Class());
        GameData modded = ModLoader.Load(TestMods.CoreDir, mod);
        Assert.Equal(-1, modded.Nodes.IndexOf("s8.app.jet_fighter_g4"));
        Assert.Contains(modded.Warnings, w => w.Contains("s8.app.jet_fighter_g4", StringComparison.Ordinal));
        Assert.Empty(Data.Warnings);   // the unmodified core has nothing to warn about
    }

    [Fact]
    public void HistoryOfUnknownNode_IsADataError()
    {
        var e = Assert.Throws<DataLoadException>(() => Load(t => t.Replace("id: s7.fnd.brayton_cycle", "id: s7.fnd.nonexistent_node", StringComparison.Ordinal)));
        Assert.Contains("unknown node 's7.fnd.nonexistent_node'", e.Message, StringComparison.Ordinal);
    }

    private GameData Load(Func<string, string> edit) =>
        ModLoader.Load(_mods.CopyCore((name, text) => name == "node_history.yaml" ? edit(text) : text));
}
