using Singularity.Core.Policy;

namespace Singularity.Session.Tests;

/// <summary>TASK-0028 (T-fleet, spec §11 п. 5): the ablation arms of the fleet rule.</summary>
public class FleetAblationTests
{
    /// <summary>
    /// <c>NO_RETIRE</c> gives the trajectory of T6c2 by projection on the columns that existed then (contract r6 §8.5): the hash of the older
    /// columns, in their order, equals the hash captured from the T6c2 build (Fixtures/t6c2-*.txt) at three months of two seeds.
    /// </summary>
    [Fact]
    public void NoRetire_ReproducesTheT6c2ColumnsBitForBit()
    {
        string dir = Path.Combine(SliceTestData.RepoRoot, "tests", "Singularity.Session.Tests", "Fixtures");
        var oldNames = new HashSet<string>(File.ReadAllLines(Path.Combine(dir, "t6c2-columns.txt")), StringComparer.Ordinal);
        string[] baseline = File.ReadAllLines(Path.Combine(dir, "t6c2-baseline.txt"));
        foreach (ulong seed in new ulong[] { 7, 3 })
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, FleetArm = FleetArm.NoRetire });
            foreach (long month in new long[] { 59, 149, 383 })
            {
                s.StepThroughMonth(month);
                string expected = baseline.Single(l => l.StartsWith($"{seed} {month} ", StringComparison.Ordinal))[($"{seed} {month} ").Length..];
                Assert.True(expected == ColumnProjection.Hash(s, oldNames.Contains), $"seed {seed}, month {month}: an old column differs");
            }
        }
    }
}
