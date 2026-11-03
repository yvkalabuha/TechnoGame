using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Data;

/// <summary>Mod core reproduces the TASK-0011 node tables (nodes.csv, informs.csv) exactly.</summary>
public class CoreNodesTests
{
    private static readonly GameData Data = ModLoader.Load(TestMods.CoreDir);

    private static List<Dictionary<string, string>> Csv(string name)
    {
        string[] lines = File.ReadAllLines(Path.Combine(TestMods.SliceDataDir, name));
        string[] header = lines[0].Split(',');
        return [.. lines.Skip(1).Where(l => l.Length > 0).Select(l =>
        {
            string[] cells = l.Split(',');
            Assert.Equal(header.Length, cells.Length);
            return header.Zip(cells).ToDictionary(p => p.First, p => p.Second);
        })];
    }

    private static Fx6 Fx(string text) => Fx6.FromRaw((long)(decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture) * Fx6.Scale));

    [Fact]
    public void Core_Has61NodesWithTypeCounts()
    {
        NodeTable n = Data.Nodes;
        Assert.Equal(61, n.Count);
        int[] counts = [.. Enum.GetValues<NodeType>().Select(t => n.Type.Count(x => x == t))];
        Assert.Equal([8, 8, 13, 8, 20, 4], counts);   // FND PRN ENG CMP PRC APP
        Assert.Equal(Enumerable.Range(1, 61), n.Number.ToArray());
    }

    [Fact]
    public void Nodes_MatchTask0011Csv()
    {
        NodeTable n = Data.Nodes;
        foreach (Dictionary<string, string> row in Csv("nodes.csv"))
        {
            int r = n.IndexOf(row["id"]);
            Assert.True(r >= 0, row["id"]);
            Assert.Equal(int.Parse(row["idx"]), n.Number[r]);
            Assert.Equal(row["type"], n.Type[r].ToString());
            Assert.Equal(row["gen"], $"G{n.Gen[r]}");
            Assert.Equal(row["discipline"], DataNames.NameOf(DataNames.Disciplines, n.Discipline[r]));
            Assert.Equal(row["cluster"], n.Cluster[r].ToString());
            Assert.Equal(row["tier"] == "" ? 0 : int.Parse(row["tier"]), n.Tier[r]);
            Assert.Equal(row["req_prec"] == "" ? Fx6.Zero : Fx(row["req_prec"]), n.ReqPrecision[r]);
            Assert.Equal(row["req_pur"] == "" ? Fx6.Zero : Fx(row["req_pur"]), n.ReqPurity[r]);
            Assert.Equal(row["req_qc"] == "" ? Fx6.Zero : Fx(row["req_qc"]), n.ReqQualityControl[r]);
            Assert.Equal(row["hazard"], DataNames.NameOf(DataNames.Hazards, n.Hazard[r]));
            Assert.Equal(CsvRequires(row["requires"]), LoadedRequires(n, r));
            Assert.Equal(row["anchor_year"], (n.AnchorBefore[r] ? "<" : "") + n.AnchorYear[r]);
            Assert.Equal(row["anchor_event"], n.AnchorEvent[r]);
            Assert.Equal(row["anchor_src"], n.AnchorSource[r]);
            Assert.Equal(bool.Parse(row["anchor_estimated"]), n.AnchorEstimated[r]);
        }
    }

    [Fact]
    public void Informs_MatchTask0011Csv()
    {
        NodeTable n = Data.Nodes;
        var expected = Csv("informs.csv").Select(e => $"{e["from"]}>{e["to"]}={Fx(e["weight"])}").Order(StringComparer.Ordinal);
        var loaded = new List<string>();
        for (int r = 0; r < n.Count; r++)
            for (int e = n.InformsStart[r]; e < n.InformsStart[r + 1]; e++)
                loaded.Add($"{n.Id[r]}>{n.Id[n.InformsTo[e]]}={n.InformsWeight[e]}");
        Assert.Equal(46, loaded.Count);
        Assert.Equal(expected, loaded.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void HazardScope_Is11KeyPlus9Hz()
    {
        // Spec §1.3 (F-N1): closed hazard list of 20 nodes.
        int[] key = [.. Enumerable.Range(0, 61).Where(r => Data.Nodes.Hazard[r] == Hazard.Key).Select(r => Data.Nodes.Number[r])];
        int[] hz = [.. Enumerable.Range(0, 61).Where(r => Data.Nodes.Hazard[r] == Hazard.Hz).Select(r => Data.Nodes.Number[r])];
        Assert.Equal([17, 19, 23, 27, 28, 30, 40, 41, 49, 51, 52], key);
        Assert.Equal([5, 6, 7, 8, 12, 13, 14, 15, 16], hz);
    }

    // Requires as a set of groups, each group a sorted '|'-joined string.
    private static List<string> CsvRequires(string text) =>
        [.. text.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(g => string.Join("|", g.Split('|').Order(StringComparer.Ordinal)))
            .Order(StringComparer.Ordinal)];

    private static List<string> LoadedRequires(NodeTable n, int r)
    {
        var groups = new List<string>();
        for (int g = n.Requires.GroupStart[r]; g < n.Requires.GroupStart[r + 1]; g++)
            groups.Add(string.Join("|", n.Requires.GroupMembers(g).ToArray().Select(m => n.Id[m]).Order(StringComparer.Ordinal)));
        return [.. groups.Order(StringComparer.Ordinal)];
    }
}
