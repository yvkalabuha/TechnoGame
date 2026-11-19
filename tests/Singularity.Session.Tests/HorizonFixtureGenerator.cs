using Singularity.Core.Policy;

namespace Singularity.Session.Tests;

/// <summary>Run configs of the horizon fixture (TASK-0121) and the generator of its rows.</summary>
internal static class HorizonConfigs
{
    public static readonly string[] Names = ["Main", "NoAi", "T6dOff", "NoRetire"];
    public static readonly int[] Months = [149, 265, 383, 387];

    // NoAi = the scripted-player path (ProductAi = false); the others run the AI (default).
    public static SliceRunConfig Config(string name, ulong seed) => name switch
    {
        "Main" => new SliceRunConfig { Seed = seed, Threads = 4, ChunkSize = 2 },
        "NoAi" => new SliceRunConfig { Seed = seed, Threads = 4, ChunkSize = 2, ProductAi = false },
        "T6dOff" => new SliceRunConfig { Seed = seed, Threads = 4, ChunkSize = 2, T6dOff = true },
        "NoRetire" => new SliceRunConfig { Seed = seed, Threads = 4, ChunkSize = 2, FleetArm = FleetArm.NoRetire },
        _ => throw new ArgumentException(name),
    };
}

public class HorizonFixtureGenerator
{
    /// <summary>
    /// Prints the fixture rows (<c>config seed month hash</c>, hash over ALL columns). To reproduce the fixture run it on the base commit
    /// ea0ff81 (main before TASK-0121): copy this file and ColumnProjection.cs there, remove the Skip, read the output file named in the test.
    /// </summary>
    [Fact(Skip = "generator: run on the base commit")]
    public void PrintRows()
    {
        var rows = new List<string>();
        foreach (string name in HorizonConfigs.Names)
            for (ulong seed = 1; seed <= 3; seed++)
            {
                SliceSession s = SliceTestData.Create(HorizonConfigs.Config(name, seed));
                foreach (int m in HorizonConfigs.Months)
                {
                    s.StepThroughMonth(m);
                    rows.Add($"{name} {seed} {m} {ColumnProjection.Hash(s, _ => true)}");
                }
            }
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "horizon-rows.txt"), rows);
    }
}
