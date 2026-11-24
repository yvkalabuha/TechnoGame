using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0134: Hcap(t) and the mastery rules act from 1978-01 (month 388) only, so 1945-1977 is the run of the build before them. The hash over all columns
/// but the new ones at month 387 (seeds 1-3, default AI run, current G5/G6 data) is the one recorded on the train-2 build before the change
/// (Fixtures/mastery-baseline.txt); after month 388 the rules change the run (the second test), otherwise they would not be connected.
/// </summary>
public class MasteryIdentityTests
{
    [Fact]
    public void Month387_WithoutTheNewColumns_EqualsTheBuildBeforeTheChange_Seeds1To3()
    {
        string path = Path.Combine(SliceTestData.RepoRoot, "tests", "Singularity.Session.Tests", "Fixtures", "mastery-baseline.txt");
        string[][] rows = [.. File.ReadAllLines(path).Where(l => !l.StartsWith('#')).Select(l => l.Split(' '))];
        Assert.Equal(3, rows.Length);
        foreach (string[] r in rows)
        {
            SliceSession s = SliceTestData.Create(HorizonConfigs.Config("Main", ulong.Parse(r[0])));
            s.StepThroughMonth(long.Parse(r[1]));
            Assert.True(r[2] == ColumnProjection.Hash(s, n => !ColumnProjection.IsMasteryColumn(n) && !n.StartsWith("run.k.", StringComparison.Ordinal)),
                $"seed {r[0]} month {r[1]}: the state differs from the build before the change");
        }
    }
}
