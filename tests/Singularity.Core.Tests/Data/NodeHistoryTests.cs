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
            Assert.True(h.TextRu[r].Length >= 40, Data.Nodes.Id[r]);
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
    public void Generations5And6_AreLabelledForWhatTheyAre()
    {
        NodeHistoryTable h = Data.History;
        Assert.Contains("4+", h.TextRu[Data.Nodes.IndexOf("s8.app.jet_fighter_g5")], StringComparison.Ordinal);
        string g6 = h.TextRu[Data.Nodes.IndexOf("s8.app.jet_fighter_g6")];
        Assert.Contains("малозаметный, класс F-22", g6, StringComparison.Ordinal);
        Assert.Contains("перспектив", g6, StringComparison.Ordinal);
        string ru = File.ReadAllText(Path.Combine(TestMods.CoreDir, "locale", "ru", "data.ftl"));
        Assert.Contains("4+ (электродистанционное управление)", ru, StringComparison.Ordinal);
        Assert.Contains("малозаметный, класс F-22", ru, StringComparison.Ordinal);
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

    private GameData Load(Func<string, string> edit) =>
        ModLoader.Load(_mods.CopyCore((name, text) => name == "node_history.yaml" ? edit(text) : text));
}
