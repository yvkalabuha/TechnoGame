using Singularity.Core.Policy;

namespace Singularity.Session.Tests;

/// <summary>TASK-0028 (T-fleet, spec §11 п. 5): the ablation arms of the fleet rule.</summary>
public class FleetAblationTests
{
    /// <summary>
    /// <c>NO_RETIRE</c> gives the trajectory of T6c2 by projection on the columns that existed then (contract r6 §8.5): the hash of the older
    /// columns, in their order, equals the hash captured from the T6c2 build (Fixtures/t6c2-*.txt) at three months of two seeds. The capture predates the start rule R-H-MS′ (D2), so it
    /// is reproduced on the <c>RHMS_OFF</c> data set (the D1 scenario, mod rhms_off), which is the data of T6c2.
    /// </summary>
    private static readonly Singularity.Core.Data.GameData OffData = Singularity.Core.Data.ModLoader.Load(
        Path.Combine(SliceTestData.RepoRoot, "mods", "core"), Path.Combine(SliceTestData.RepoRoot, "mods", "rhms_off"));

    [Fact]
    public void NoRetire_ReproducesTheT6c2ColumnsBitForBit()
    {
        string dir = Path.Combine(SliceTestData.RepoRoot, "tests", "Singularity.Session.Tests", "Fixtures");
        var oldNames = new HashSet<string>(File.ReadAllLines(Path.Combine(dir, "t6c2-columns.txt")), StringComparer.Ordinal);
        string[] baseline = File.ReadAllLines(Path.Combine(dir, "t6c2-baseline.txt"));
        foreach (ulong seed in new ulong[] { 7, 3 })
        {
            SliceSession s = SliceSession.Create(OffData, new SliceRunConfig { Seed = seed, FleetArm = FleetArm.NoRetire, SkipStartValidation = true });
            foreach (long month in new long[] { 59, 149, 383 })
            {
                s.StepThroughMonth(month);
                string expected = baseline.Single(l => l.StartsWith($"{seed} {month} ", StringComparison.Ordinal))[($"{seed} {month} ").Length..];
                Assert.True(expected == ColumnProjection.Hash(s, oldNames.Contains), $"seed {seed}, month {month}: an old column differs");
            }
        }
    }
}
