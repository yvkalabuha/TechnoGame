using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Data;

/// <summary>The research_speed effect of a node (#562, #568): read from the data, validated, part of the hash only where it is used.</summary>
public class ResearchSpeedDataTests : IDisposable
{
    private readonly TestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    private GameData With(string line) =>
        ModLoader.Load(_mods.CopyCore((name, text) => name == "nodes.yaml" ? text.Replace("    directions: [prop, chem]\n", "    directions: [prop, chem]\n" + line + "\n") : text));

    [Fact]
    public void ANodeWithTheEffect_CarriesBonusAndDirections_AndShowsItAsAnEffect()
    {
        GameData d = With("    research_speed: {bonus: 0.05, directions: [prop, elec]}");
        int r = d.Nodes.IndexOf("s7.fnd.brayton_cycle");
        Assert.Equal(Fx6.FromRaw(50_000), d.Nodes.SpeedBonus[r]);
        Assert.Equal([1, 7], Enumerable.Range(d.Nodes.SpeedDirStart[r], d.Nodes.SpeedDirStart[r + 1] - d.Nodes.SpeedDirStart[r]).Select(k => d.Nodes.SpeedDir[k]));
        var fx = new List<NodeEffect>();
        NodeEffects.Derive(d, r, fx);
        Assert.Contains(fx, e => e.Kind == NodeEffectKind.ResearchSpeed && e.Value == Fx6.FromRaw(50_000));
        Assert.NotEqual(ModLoader.Load(TestMods.CoreDir).DataHash, d.DataHash);
    }

    [Fact]
    public void WithoutDirections_TheBonusCountsForEveryDirection()
    {
        GameData d = With("    research_speed: {bonus: 0.03}");
        int r = d.Nodes.IndexOf("s7.fnd.brayton_cycle");
        Assert.Equal(d.Nodes.SpeedDirStart[r], d.Nodes.SpeedDirStart[r + 1]);
        Assert.Equal(Fx6.FromRaw(30_000), d.Nodes.SpeedBonus[r]);
    }

    [Theory]
    [InlineData("    research_speed: {bonus: 0.09}")]                       // above 0.08 per node
    [InlineData("    research_speed: {bonus: 0}")]                          // no effect
    [InlineData("    research_speed: {bonus: 0.05, directions: [warp]}")]   // unknown direction
    [InlineData("    research_speed: {bonus: 0.05, extra: 1}")]             // unknown key
    public void AnInvalidEffect_IsRejected(string line) => Assert.Throws<DataLoadException>(() => With(line));

    [Fact]
    public void TheShippedData_HasNoSuchNode_SoTheHashAndTheSpeedAreThoseWithoutIt()
    {
        GameData d = ModLoader.Load(TestMods.CoreDir);
        Assert.All(d.Nodes.SpeedBonus, b => Assert.Equal(Fx6.Zero, b));
    }
}
