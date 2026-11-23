using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>TASK-0121 (#423): the slice computes to 2030; the trajectory up to 1977 is that of the build before the horizon change.</summary>
public class HorizonTests
{
    [Fact]
    public void OldColumns_EqualTheBuildBeforeTheHorizonChange_AtEveryFixturePoint()
    {
        string path = Path.Combine(SliceTestData.RepoRoot, "tests", "Singularity.Session.Tests", "Fixtures", "horizon-baseline.txt");
        string[][] rows = [.. File.ReadAllLines(path).Where(l => !l.StartsWith('#')).Select(l => l.Split(' '))];
        Assert.Equal(60, rows.Length);   // 48 points of TASK-0121 and 12 at month 176 (TASK-0129)
        // TASK-0129 (#431): from month 177 the fleet is no longer written off at the service life, so the trajectory differs from the pinned build by design;
        // the identity of the old columns holds to month 176 (memo §2.9 item 12), where the rows of TASK-0129 pin the last month before the change.
        foreach (var group in rows.Where(r => long.Parse(r[2]) < 177).GroupBy(r => (r[0], r[1])))
        {
            SliceSession s = SliceTestData.Create(HorizonConfigs.Config(group.Key.Item1, ulong.Parse(group.Key.Item2)));
            foreach (string[] r in group)
            {
                s.StepThroughMonth(long.Parse(r[2]));
                Assert.True(r[3] == ColumnProjection.Hash(s, n => !ColumnProjection.IsHorizonColumn(n) && !ColumnProjection.IsReplCreditColumn(n)),
                    $"{r[0]} seed {r[1]} month {r[2]}: an old column differs from the build before the horizon change");
            }
        }
    }

    [Fact]
    public void HorizonColumns_AreExactlyTheNewOnes()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 1 });
        int designs = SliceTestData.Data.Scenario(SliceRunConfig.DefaultScenario).MaxDesigns;
        Assert.Equal(86, SliceWorld.Years);
        string[] names = [.. ColumnProjection.Names(s).Where(ColumnProjection.IsHorizonColumn)];
        Assert.Equal(designs * (2030 - 1977), names.Count(n => n.Contains(".cohort.")));
        Assert.Equal(designs, names.Count(n => n.EndsWith(".q.1", StringComparison.Ordinal)));
    }

    [Fact]
    public void CohortYears_BeyondLayout_FailsLoudly()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 1, Threads = 4, ChunkSize = 2 });
        s.StepThroughMonth(1023);   // December 2030 (month 0 is 1945-09) is still inside
        var e = Assert.Throws<TickComputeException>(() => s.StepThroughMonth(1024));
        Assert.Contains("outside the cohort years", e.Message);
    }

    [Fact]
    public void Run1020Months_Seeds1To3_NoException_L1AndL2Hold()
    {
        string[] fin = new string[3], at387 = new string[3];
        for (ulong seed = 1; seed <= 3; seed++)
        {
            SliceSession s = SliceTestData.Create(HorizonConfigs.Config("Main", seed));
            s.StepThroughMonth(387);
            at387[seed - 1] = ColumnProjection.Hash(s, _ => true);
            s.StepThroughMonth(1019);
            fin[seed - 1] = ColumnProjection.Hash(s, _ => true);
            // Past 2006 the cohorts of the years 2007+ live in the second block of the mask.
            Assert.Contains(s.World.Table.Columns.OfType<LongColumn>().Where(c => c.Name.EndsWith(".q.1", StringComparison.Ordinal)), c => c.Current.ToArray().Any(v => v != 0));
        }
        // L1: the same run again.
        SliceSession again = SliceTestData.Create(HorizonConfigs.Config("Main", 1));
        again.StepThroughMonth(1019);
        Assert.Equal(fin[0], ColumnProjection.Hash(again, _ => true));
        // L2: one thread gives the same hashes at months 387 and 1019.
        SliceSession one = SliceTestData.Create(new SliceRunConfig { Seed = 1, Threads = 1, ChunkSize = 2 });
        one.StepThroughMonth(387);
        Assert.Equal(at387[0], ColumnProjection.Hash(one, _ => true));
        one.StepThroughMonth(1019);
        Assert.Equal(fin[0], ColumnProjection.Hash(one, _ => true));
    }
}
