using Singularity.Core.Data;

namespace Singularity.Core.Tests.Data;

/// <summary>TASK-0180 (#555): early G5/G6 nodes have real prerequisites, so none opens decades before its own era (docs/research/notes/g5-g6-prerequisites.md).</summary>
public class EarlyG5G6PrerequisitesTests
{
    private static readonly GameData Data = ModLoader.Load(TestMods.CoreDir);

    // Node id -> earliest year it can open: max over requirement groups of (min over members of the member anchor year).
    public static TheoryData<string, int> Cases => new()
    {
        { "s8.fnd.electronic_flight_control_theory", 1960 },
        { "s8.fnd.edge_wave_diffraction", 1954 },
        { "s8.prn.phased_array_radar_pesa", 1962 },
        { "s5.prn.gaas_wafer_growth", 1960 },
        { "s8.prn.programmable_signal_processing", 1969 },
        { "s8.prn.electronic_engine_control", 1964 },
        { "s5.cmp.high_modulus_fibre", 1953 },
        { "s8.prn.thrust_vectoring_nozzle", 1976 },
        { "s5.cmp.silicon_carbide_fibre", 1963 },
        { "s5.prn.environmental_barrier_coating", 1990 },
        { "s8.prn.adaptive_third_stream_fan", 1997 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void EarlyNode_CannotOpenBeforeItsPrerequisitesAnchors(string id, int expectedYear)
    {
        NodeTable n = Data.Nodes;
        int r = n.IndexOf(id);
        Assert.True(r >= 0, id);
        Assert.Equal(expectedYear, EarliestOpenYear(n, r));
    }

    [Fact]
    public void NoG5G6Node_OpensBefore1953()
    {
        NodeTable n = Data.Nodes;
        for (int r = 0; r < n.Count; r++)
            if (n.Number[r] is >= 62 and <= 99)   // nodes 100+ (#575) are anchored by their own year
                Assert.True(EarliestOpenYear(n, r) >= 1953, $"{n.Id[r]} opens in {EarliestOpenYear(n, r)}");
    }

    private static int EarliestOpenYear(NodeTable n, int r)
    {
        int year = 1945;
        for (int g = n.Requires.GroupStart[r]; g < n.Requires.GroupStart[r + 1]; g++)
        {
            int best = int.MaxValue;
            foreach (int m in n.Requires.GroupMembers(g))
                best = Math.Min(best, n.AnchorYear[m]);
            year = Math.Max(year, best);
        }
        return year;
    }
}
