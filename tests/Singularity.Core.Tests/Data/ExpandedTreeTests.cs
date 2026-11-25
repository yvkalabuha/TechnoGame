using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Data;

/// <summary>#575 / TASK-0188: the 57 nodes 100-156 (computing, electronics, materials, rockets, space, nuclear), their directions, research_speed effects and military words.</summary>
public class ExpandedTreeTests : IDisposable
{
    private static readonly GameData Data = ModLoader.Load(TestMods.CoreDir);
    private readonly TestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    private const string Eniac = "{kind: research_speed, dirs: [ord, aero], bonus: 0.02}";

    private static int[] NewRows => [.. Enumerable.Range(0, Data.Nodes.Count).Where(r => Data.Nodes.Number[r] >= 100)];

    [Fact]
    public void Core_HasThe57NewNodes_WithPrefixesAndNoGapInNumbers()
    {
        Assert.Equal(57, NewRows.Length);
        Assert.Equal(Enumerable.Range(100, 57), NewRows.Select(r => Data.Nodes.Number[r]));
        string[] stages = [.. NewRows.Select(r => Data.Nodes.Id[r].Split('.')[0]).Distinct().Order(StringComparer.Ordinal)];
        Assert.Equal(["s10", "s11", "s5", "s6", "s7", "s8", "s9"], stages);   // sN is only part of the id: nothing parses it
    }

    [Fact]
    public void NewNodes_RequirementsAreOlderOrEqual_InYearAndGeneration_SoTheGraphIsAcyclicByConstruction()
    {
        NodeTable n = Data.Nodes;
        foreach (int r in NewRows)
        {
            for (int g = n.Requires.GroupStart[r]; g < n.Requires.GroupStart[r + 1]; g++)
            {
                foreach (int p in n.Requires.GroupMembers(g))
                {
                    Assert.True(n.AnchorYear[p] <= n.AnchorYear[r], $"{n.Id[r]} ({n.AnchorYear[r]}) requires {n.Id[p]} ({n.AnchorYear[p]})");
                    Assert.True(n.Gen[p] <= n.Gen[r], $"{n.Id[r]} gen");
                }
            }
        }
    }

    [Fact]
    public void NewNodes_HaveDirections_Anchors_AndMilitaryWords()
    {
        NodeTable n = Data.Nodes;
        foreach (int r in NewRows)
        {
            Assert.InRange(n.DirectionCount(r), 2, 4);
            Assert.StartsWith("R575-", n.AnchorSource[r]);
            Assert.InRange(n.AnchorYear[r], 1942, 2021);
            if (n.Type[r] == NodeType.PRC)
                Assert.Equal(MilValue.Prep, n.MilValue[r]);
        }
        // Words of the note: direct = changes perf or the generation of a product.
        string[] direct = ["s8.eng.fire_control_radar_interceptor", "s8.eng.infrared_air_to_air_missile", "s8.prn.pulse_doppler_look_down_radar", "s5.cmp.thermal_barrier_coating"];
        Assert.Equal(direct.Order(StringComparer.Ordinal), NewRows.Where(r => n.MilValue[r] == MilValue.Direct).Select(r => n.Id[r]).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void SpaceDirection_IsMainForNineNodes_AndSecondaryForTwo()
    {
        NodeTable n = Data.Nodes;
        int space = Directions.Ids.IndexOf("space");
        int main = 0, secondary = 0;
        for (int r = 0; r < n.Count; r++)
        {
            if (n.DirectionAt(r, 0) == space)
                main++;
            else if (Enumerable.Range(0, n.DirectionCount(r)).Any(k => n.DirectionAt(r, k) == space))
                secondary++;
        }
        Assert.Equal((9, 2), (main, secondary));
        Assert.All(NewRows.Where(r => n.Id[r].StartsWith("s10.", StringComparison.Ordinal)), r => Assert.Equal(space, n.DirectionAt(r, 0)));
    }

    [Fact]
    public void ResearchSpeed_Is15Nodes_EachWithinTheCap_AndTheSumPerDirectionBelowTheCeiling()
    {
        NodeTable n = Data.Nodes;
        int[] with = [.. Enumerable.Range(0, n.Count).Where(r => n.SpeedStart[r + 1] > n.SpeedStart[r])];
        Assert.Equal(15, with.Length);
        Assert.All(with, r => Assert.True(n.Number[r] >= 100));
        var sum = new Fx6[Directions.Ids.Length];
        foreach (int r in with)
        {
            for (int e = n.SpeedStart[r]; e < n.SpeedStart[r + 1]; e++)
            {
                Assert.InRange(n.SpeedBonus[e].Raw, 1, ResearchSpeedEffects.MaxBonus.Raw);
                for (int d = 0; d < sum.Length; d++)
                {
                    if ((n.SpeedMask[e] & (1 << d)) != 0)
                        sum[d] += n.SpeedBonus[e];
                }
            }
        }
        // Ceiling R = 0.25 [placeholder until TASK-0185] (note tree-expansion-575).
        Assert.All(sum, s => Assert.True(s <= Fx6.FromRaw(250_000), s.ToString()));
        Assert.Equal(Fx6.FromRaw(220_000), sum[Directions.Ids.IndexOf("aero")]);
    }

    [Fact]
    public void Informs_FromNewNodes_AreAllowedAcrossDisciplines_ButNotFromFndOrToApp()
    {
        NodeTable n = Data.Nodes;
        int micro = n.IndexOf("s9.cmp.microprocessor"), dfcc = n.IndexOf("s8.cmp.digital_flight_control_computer");
        Assert.Contains(dfcc, n.InformsTo.Skip(n.InformsStart[micro]).Take(n.InformsStart[micro + 1] - n.InformsStart[micro]));
        Assert.Equal(23, NewRows.Sum(r => n.InformsStart[r + 1] - n.InformsStart[r]));
    }

    [Fact]
    public void Informs_FndToOtherDiscipline_AndAnyNodeToApp_AreRejected()
    {
        const string Brayton = "      - {to: s8.eng.turbojet_g1, weight: 0.6}\n";
        foreach (string target in new[] { "s5.prc.light_alloy_rolling", "s8.app.jet_fighter_g1", "s8.prn.gas_turbine" })
        {
            string core = _mods.CopyCore((name, text) => name == "nodes.yaml" ? TestMods.ReplaceOnce(text, Brayton, $"      - {{to: {target}, weight: 0.6}}\n") : text);
            var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(core));
            Assert.Contains("informs edge", e.Message);
        }
        string fromPrn = _mods.CopyCore((name, text) => name == "nodes.yaml"
            ? TestMods.ReplaceOnce(text, "    informs:\n      - {to: s8.prc.machined_integral_skins, weight: 0.6}", "    informs:\n      - {to: s8.app.jet_fighter_g1, weight: 0.6}\n      - {to: s8.prc.machined_integral_skins, weight: 0.6}")
            : text);
        Assert.Contains("informs edge", Assert.Throws<DataLoadException>(() => ModLoader.Load(fromPrn)).Message);
    }

    [Theory]
    [InlineData("{kind: research_speed, dirs: [ord, ord], bonus: 0.02}", "listed twice")]
    [InlineData("{kind: research_speed, dirs: [ord, warp], bonus: 0.02}", "unknown direction 'warp'")]
    [InlineData("{kind: research_speed, dirs: [], bonus: 0.02}", "dirs is empty")]
    [InlineData("{kind: research_speed, dirs: [ord, aero], bonus: 0.09}", "bonus")]
    [InlineData("{kind: research_speed, dirs: [ord, aero], bonus: 0}", "bonus")]
    [InlineData("{kind: speed, dirs: [ord, aero], bonus: 0.02}", "unknown effect kind")]
    [InlineData("{kind: research_speed, dirs: [ord, aero]}", "missing field 'bonus'")]
    public void BadEffect_IsRejected(string effect, string message)
    {
        string core = _mods.CopyCore((name, text) => name == "nodes.yaml" ? TestMods.ReplaceOnce(text, Eniac, effect) : text);
        var e = Assert.Throws<DataLoadException>(() => ModLoader.Load(core));
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void AllDirectionsEffect_IsReadAsFullMask()
    {
        string core = _mods.CopyCore((name, text) => name == "nodes.yaml" ? TestMods.ReplaceOnce(text, Eniac, "{kind: research_speed, dirs: all, bonus: 0.02}") : text);
        NodeTable n = ModLoader.Load(core).Nodes;
        int r = n.IndexOf("s9.eng.electronic_computer_g1");
        Assert.Equal(ResearchSpeedEffects.AllMask, n.SpeedMask[n.SpeedStart[r]]);
    }

    [Fact]
    public void BadMilValue_IsRejected()
    {
        string core = _mods.CopyCore((name, text) => name == "nodes.yaml" ? TestMods.ReplaceOnce(text, "    mil_value: prep\n    informs:\n      - {to: s8.prc.machined_integral_skins", "    mil_value: huge\n    informs:\n      - {to: s8.prc.machined_integral_skins") : text);
        Assert.Contains("unknown value 'huge'", Assert.Throws<DataLoadException>(() => ModLoader.Load(core)).Message);
    }
}
