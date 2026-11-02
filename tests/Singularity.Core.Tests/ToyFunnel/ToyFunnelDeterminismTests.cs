namespace Singularity.Core.Tests.ToyFunnel;

/// <summary>L1 (repeat) and L2 (thread count) for 6 and 64 countries, every tick hashed.</summary>
public class ToyFunnelDeterminismTests
{
    public static TheoryData<string, int> Worlds() => new()
    {
        { "A", 6 }, { "B", 6 }, { "C", 6 }, { "A", 64 }, { "B", 64 }, { "C", 64 },
    };

    private static (List<string> Hashes, List<string> Rows) Run(string model, int countries, ulong seed, int threads, int chunkSize)
    {
        ToyRun.Result r = ToyRun.Run(ToyRun.MainSeries(model, 1.0, countries), seed, threads, chunkSize, hashEveryTick: true);
        var rows = new List<string>();
        for (int t = 0; t < r.Metrics.Tables.Length; t++)
        {
            foreach (string[] row in r.Metrics.Tables[t])
                rows.Add(t + ":" + string.Join(",", row));
        }
        return (r.TickHashes, rows);
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void SameSeed_GivesSameHashesAndMetrics(string model, int countries) // L1
    {
        var first = Run(model, countries, seed: 77, threads: 4, chunkSize: 3);
        var second = Run(model, countries, seed: 77, threads: 4, chunkSize: 3);
        Assert.Equal(first.Hashes, second.Hashes);
        Assert.Equal(first.Rows, second.Rows);
        Assert.Equal(ToyRun.Ticks, first.Hashes.Count);
        Assert.NotEqual(first.Hashes, Run(model, countries, seed: 78, threads: 4, chunkSize: 3).Hashes);
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void ThreadCount_DoesNotChangeHashesOrMetrics(string model, int countries) // L2
    {
        // Chunk size 1 gives one chunk per country, the most parallel split.
        var single = Run(model, countries, seed: 1910, threads: 1, chunkSize: 1);
        foreach (int threads in new[] { 4, 8 })
        {
            var parallel = Run(model, countries, seed: 1910, threads, chunkSize: 1);
            Assert.Equal(single.Hashes, parallel.Hashes);
            Assert.Equal(single.Rows, parallel.Rows);
        }
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void ChunkSize_DoesNotChangeHashes(string model, int countries)
    {
        // Rows are independent within a tick, so the split must not matter either.
        Assert.Equal(Run(model, countries, 5, 1, 1).Hashes, Run(model, countries, 5, 8, 64).Hashes);
    }
}
