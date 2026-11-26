using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Data;

/// <summary>TASK-0193: the 55 nodes 160-214 (1990-2030: avionics, stealth, composites, computing, space, missiles).</summary>
public class Expanded1990TreeTests
{
    private static readonly GameData Data = ModLoader.Load(TestMods.CoreDir);

    // O-102: the nine nodes whose year the sources did not confirm (note tree-expansion-1990-2030, rev 2).
    private static readonly string[] Unconfirmed =
    [
        "s8.eng.tactical_data_link16", "s8.prn.digital_rf_memory_jamming", "s8.eng.cooperative_engagement_capability", "s9.prn.computational_materials_design",
        "s7.eng.stealth_cruise_missile", "s8.eng.electro_optical_targeting_pod", "s8.cmp.gan_tr_module", "s8.prn.irst_infrared_search_track", "s8.eng.multifunction_rf_aperture_ew",
    ];

    private static int[] Rows => [.. Enumerable.Range(0, Data.Nodes.Count).Where(r => Data.Nodes.Number[r] >= 160)];

    [Fact]
    public void Core_HasThe55NewNodes_Numbered160To214_WithoutGaps()
    {
        Assert.Equal(Enumerable.Range(160, 55), Rows.Select(r => Data.Nodes.Number[r]));
    }

    [Fact]
    public void Requirements_ArePresent_AndNotNewerInYearOrGeneration_SoTheGraphIsAcyclic()
    {
        NodeTable n = Data.Nodes;
        foreach (int r in Rows)
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
    public void Nodes_HaveDirections_AnchorsInTheRange_AndEstimatedFlagsOfO102()
    {
        NodeTable n = Data.Nodes;
        foreach (int r in Rows)
        {
            Assert.InRange(n.DirectionCount(r), 2, 4);
            Assert.StartsWith("R1990-", n.AnchorSource[r]);
            Assert.InRange(n.AnchorYear[r], 1990, 2023);
            Assert.InRange((int)n.Gen[r], 5, 6);
            if (n.Type[r] == NodeType.PRC)
                Assert.Equal(MilValue.Prep, n.MilValue[r]);
            if (Unconfirmed.Contains(n.Id[r]))
                Assert.True(n.AnchorEstimated[r], n.Id[r]);
        }
        Assert.Equal(3, Rows.Count(r => n.Type[r] == NodeType.PRC));
    }

    [Fact]
    public void HistoryFile_MarksExactlyTheNineO102Nodes_AsUnconfirmed()
    {
        string[] lines = File.ReadAllLines(Path.Combine(TestMods.CoreDir, "node_history.yaml"));
        var marked = new List<string>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("  - id: ", StringComparison.Ordinal) && Data.Nodes.Number[Data.Nodes.IndexOf(lines[i][8..].Trim())] >= 160
                && lines[i + 2].Trim() == "unconfirmed: true")
                marked.Add(lines[i][8..].Trim());
        }
        Assert.Equal(Unconfirmed.Order(StringComparer.Ordinal), marked.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Names_DoNotClaimFirsts_ThatTheSourcesDidNotConfirm()
    {
        // O-102: no «first commercial» / «first non-...» wording in names.
        string en = File.ReadAllText(Path.Combine(TestMods.CoreDir, "locale", "en", "data.ftl"));
        string ru = File.ReadAllText(Path.Combine(TestMods.CoreDir, "locale", "ru", "data.ftl"));
        foreach (int r in Rows)
        {
            string key = "node-" + Data.Nodes.Id[r].Replace('.', '-') + " =\n    .name = ";
            Assert.Contains(key, en);
            Assert.Contains(key, ru);
            foreach ((string text, string bad) in new[] { (en, "First"), (en, "first commercial"), (ru, "Первый"), (ru, "Первая") })
            {
                string line = text[(text.IndexOf(key, StringComparison.Ordinal) + key.Length)..].Split('\n')[0];
                Assert.DoesNotContain(bad, line);
            }
        }
    }

    [Fact]
    public void ResearchSpeed_Is7Nodes_OfOneHalfPercent_AndTheSumPerDirectionStaysBelowTheCeiling()
    {
        NodeTable n = Data.Nodes;
        int[] with = [.. Rows.Where(r => n.SpeedStart[r + 1] > n.SpeedStart[r])];
        Assert.Equal(7, with.Length);
        Assert.All(with, r => Assert.Equal(Fx6.FromRaw(5_000), n.SpeedBonus[n.SpeedStart[r]]));
        var sum = new Fx6[Directions.Ids.Length];
        for (int r = 0; r < n.Count; r++)
        {
            for (int e = n.SpeedStart[r]; e < n.SpeedStart[r + 1]; e++)
            {
                for (int d = 0; d < sum.Length; d++)
                {
                    if ((n.SpeedMask[e] & (1 << d)) != 0)
                        sum[d] += n.SpeedBonus[e];
                }
            }
        }
        // Ceiling R = 0.25 [placeholder until TASK-0185]; the 22 nodes with an effect add 0.025 to aero over the 0.22 of #575.
        Assert.All(sum, s => Assert.True(s <= Fx6.FromRaw(250_000), s.ToString()));
        Assert.Equal(Fx6.FromRaw(245_000), sum[Directions.Ids.IndexOf("aero")]);
    }

    [Fact]
    public void SpaceDirection_IsMainForTheElevenSpaceNodes()
    {
        NodeTable n = Data.Nodes;
        int space = Directions.Ids.IndexOf("space");
        Assert.Equal(11, Rows.Count(r => n.DirectionAt(r, 0) == space));
        Assert.All(Rows.Where(r => n.Id[r].StartsWith("s10.", StringComparison.Ordinal)), r => Assert.Equal(space, n.DirectionAt(r, 0)));
    }
}
