namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0131 (train 2, T-G): the Core changes for G5/G6 (second licence word, generation cost multipliers, Lab guards) change nothing on the
/// 61-node data of the build before TASK-0133 (Fixtures/core-g4). The hashes over ALL columns at months 387 and 1019 (seeds 1-3, default AI run) are those of the build before the change.
/// </summary>
public class Train2IdentityTests
{
    private static readonly int[] Months = [387, 1019];

    [Fact]
    public void CurrentData_AllColumnsEqualTheBuildBeforeTheChange_Seeds1To3_Months387And1019()
    {
        string path = Path.Combine(SliceTestData.RepoRoot, "tests", "Singularity.Session.Tests", "Fixtures", "train2-baseline.txt");
        string[][] rows = [.. File.ReadAllLines(path).Where(l => !l.StartsWith('#')).Select(l => l.Split(' '))];
        Assert.Equal(6, rows.Length);
        foreach (var group in rows.GroupBy(r => r[0]))
        {
            SliceSession s = SliceTestData.CreateLegacy(HorizonConfigs.Config("Main", ulong.Parse(group.Key)));
            foreach (string[] r in group)
            {
                s.StepThroughMonth(long.Parse(r[1]));
                Assert.True(r[2] == ColumnProjection.Hash(s, _ => true), $"seed {r[0]} month {r[1]}: the state differs from the build before the change");
            }
        }
    }

    /// <summary>Prints the fixture rows; run on the base commit (2590da6) with the Skip removed.</summary>
    [Fact(Skip = "generator: run on the base commit")]
    public void PrintRows()
    {
        var rows = new List<string>();
        for (ulong seed = 1; seed <= 3; seed++)
        {
            SliceSession s = SliceTestData.CreateLegacy(HorizonConfigs.Config("Main", seed));
            foreach (int m in Months)
            {
                s.StepThroughMonth(m);
                rows.Add($"{seed} {m} {ColumnProjection.Hash(s, _ => true)}");
            }
        }
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "train2-rows.txt"), rows);
    }
}
