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
            Assert.Equal("2026-11-24", h.SourceAccessed[i]);
            if (h.SourceUse[i] == SourceUse.Quote)
                Assert.NotEqual(SourceLicense.Other, h.SourceLicense[i]);
        }
        Assert.DoesNotContain(SourceUse.Quote, h.SourceUse);   // all text is retold in our words
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
    public void DeltaWing_HasDerivedEffects_SharedWithAreaRuled()
    {
        var fx = new List<NodeEffect>();
        NodeEffects.Derive(Data, Data.Nodes.IndexOf("s8.eng.delta_wing_airframe"), fx);
        ProductClassTable pc = Data.ProductClasses;
        NodeEffect milled = Assert.Single(fx, e => e.Kind == NodeEffectKind.AlternativeUnlock && pc.AltId[e.AltRow] == "af_milled");
        Assert.True(milled.Shared);
        Assert.Equal(pc.AltPerfMult[milled.AltRow], milled.Value);
        Assert.Equal(pc.AltPerfMult[milled.AltRow - 1], milled.Previous);
        Assert.True(milled.Value > milled.Previous);

        // The any_of partner of delta_wing for af_milled is area_ruled_airframe, and only it.
        int delta = Data.Nodes.IndexOf("s8.eng.delta_wing_airframe"), ruled = Data.Nodes.IndexOf("s8.eng.area_ruled_airframe");
        bool partnerFound = false;
        for (int g = pc.AltRequires.GroupStart[milled.AltRow]; g < pc.AltRequires.GroupStart[milled.AltRow + 1]; g++)
        {
            var members = pc.AltRequires.GroupMembers(g).ToArray();
            if (members.Contains(delta))
            {
                Assert.Equal(new[] { delta, ruled }.Order().ToArray(), members);
                partnerFound = true;
            }
        }
        Assert.True(partnerFound);
    }

    [Fact]
    public void SameAirframeClassPeers_AreNotReadAsADowngrade()
    {
        ProductClassTable pc = Data.ProductClasses;
        var fx = new List<NodeEffect>();
        NodeEffects.Derive(Data, Data.Nodes.IndexOf("s8.eng.fly_by_wire_relaxed_stability"), fx);
        NodeEffect fbw = Assert.Single(fx, e => e.Kind == NodeEffectKind.AlternativeUnlock && pc.AltId[e.AltRow] == "af_fbw");
        Assert.True(fbw.Value > fbw.Previous);   // compared with the last lower class, not with af_mach2 (same class, higher perf)
    }

    [Fact]
    public void NodesWithoutDerivedEffect_AreExactlyTheKnownHistoryOnlyOnes()
    {
        var fx = new List<NodeEffect>();
        var without = new List<string>();
        for (int r = 0; r < Data.Nodes.Count; r++)
        {
            if (Data.Nodes.Type[r] is NodeType.FND or NodeType.PRN)
                continue;
            fx.Clear();
            NodeEffects.Derive(Data, r, fx);
            if (fx.Count == 0)
                without.Add(Data.Nodes.Id[r]);
        }
        // These ENG/CMP nodes change no product parameter today; the card shows them as "history only" (no fake bonus).
        string[] expected =
        [
            "s5.cmp.aluminium_alloy_sheet", "s5.cmp.gaas_mmic", "s5.cmp.high_modulus_fibre", "s5.cmp.silicon_carbide_fibre",
            "s8.cmp.digital_flight_control_computer", "s8.cmp.solid_state_tr_module", "s8.eng.aesa_sensor_fusion",
            "s8.eng.air_cooled_turbine_blade", "s8.eng.all_moving_tail", "s8.eng.digital_beamforming",
            "s8.eng.integrated_avionics_architecture", "s8.eng.variable_geometry_inlet",
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
    public void HistoryOfUnknownNode_IsADataError()
    {
        var e = Assert.Throws<DataLoadException>(() => Load(t => t.Replace("id: s7.fnd.brayton_cycle", "id: s7.fnd.nonexistent_node", StringComparison.Ordinal)));
        Assert.Contains("unknown node 's7.fnd.nonexistent_node'", e.Message, StringComparison.Ordinal);
    }

    private GameData Load(Func<string, string> edit) =>
        ModLoader.Load(_mods.CopyCore((name, text) => name == "node_history.yaml" ? edit(text) : text));
}
