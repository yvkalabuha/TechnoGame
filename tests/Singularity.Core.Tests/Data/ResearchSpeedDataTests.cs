using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Data;

/// <summary>The research_speed effects of the nodes (#562, #568; data format of TASK-0188) as the slot model reads them: part of the hash, shown as a node effect.</summary>
public class ResearchSpeedDataTests : IDisposable
{
    private readonly TestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    private GameData With(string effects) =>
        ModLoader.Load(_mods.CopyCore((name, text) => name == "nodes.yaml"
            ? text.Insert(text.IndexOf("    directions: [prop, chem]\n", StringComparison.Ordinal), "    effects:\n" + effects)
            : text));

    [Fact]
    public void AnEffectOfANode_IsShownAsAnEffect_AndChangesTheIdentityOfTheData()
    {
        GameData d = With("      - {kind: research_speed, dirs: [prop, elec], bonus: 0.05}\n");
        int r = d.Nodes.IndexOf("s7.fnd.brayton_cycle");
        Assert.Equal(1, d.Nodes.SpeedStart[r + 1] - d.Nodes.SpeedStart[r]);
        Assert.Equal((1 << 1) | (1 << 7), d.Nodes.SpeedMask[d.Nodes.SpeedStart[r]]);
        var fx = new List<NodeEffect>();
        NodeEffects.Derive(d, r, fx);
        Assert.Contains(fx, e => e.Kind == NodeEffectKind.ResearchSpeed && e.Value == Fx6.FromRaw(50_000) && e.MinRank == ((1 << 1) | (1 << 7)));
        Assert.NotEqual(ModLoader.Load(TestMods.CoreDir).DataHash, d.DataHash);
    }

    [Fact]
    public void TheShippedEffects_AreWithinTheBoundOfOneNode_AndHaveDirections()
    {
        NodeTable n = ModLoader.Load(TestMods.CoreDir).Nodes;
        int effects = 0;
        for (int r = 0; r < n.Count; r++)
        {
            for (int k = n.SpeedStart[r]; k < n.SpeedStart[r + 1]; k++)
            {
                effects++;
                Assert.InRange(n.SpeedBonus[k].Raw, 1, ResearchSpeedEffects.MaxBonus.Raw);
                Assert.NotEqual(0, n.SpeedMask[k]);
            }
        }
        Assert.True(effects > 0, "the data of TASK-0188 has research_speed nodes");
    }

    [Fact]
    public void TheMilitaryValueWord_IsPartOfTheIdentityOfTheData()
    {
        string a = ModLoader.Load(TestMods.CoreDir).DataHash;
        string b = ModLoader.Load(_mods.CopyCore((name, text) => name == "nodes.yaml" ? text.Replace("mil_value: base", "mil_value: direct") : text)).DataHash;
        Assert.NotEqual(a, b);
    }
}
