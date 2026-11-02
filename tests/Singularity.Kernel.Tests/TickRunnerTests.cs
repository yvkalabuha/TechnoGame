using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

public class TickRunnerTests
{
    private const int Rows = 10_000;
    private const int ChunkSize = 256;
    private const int Ticks = 40;
    private const int SchemaVersion = 1;

    /// <summary>Runs the test world and returns the state hash after every tick.</summary>
    private static List<string> Run(ulong seed, int threads)
    {
        var table = new Table(Rows);
        var system = new DiffusionTestSystem(table, chunkCount: (Rows + ChunkSize - 1) / ChunkSize);
        var runner = new TickRunner(table, seed, [system], ChunkSize, threads);

        var hashes = new List<string>(Ticks);
        for (int i = 0; i < Ticks; i++)
        {
            runner.RunTick();
            hashes.Add(StateHash.ComputeHex(table, SchemaVersion, runner.Tick));
        }
        return hashes;
    }

    [Fact]
    public void SameSeed_RepeatedRun_GivesSameHashes() // L1
    {
        Assert.Equal(Run(seed: 1910, threads: 4), Run(seed: 1910, threads: 4));
    }

    [Fact]
    public void ThreadCount_DoesNotChangeHashes() // L2
    {
        List<string> single = Run(seed: 1910, threads: 1);
        Assert.Equal(single, Run(seed: 1910, threads: 4));
        Assert.Equal(single, Run(seed: 1910, threads: 8));
    }

    [Fact]
    public void StateActuallyEvolves()
    {
        // Guards against a trivially constant world making the tests above vacuous.
        List<string> hashes = Run(seed: 1910, threads: 1);
        Assert.Equal(Ticks, hashes.Distinct().Count());
        Assert.NotEqual(hashes, Run(seed: 1911, threads: 1));
    }

    [Fact]
    public void ChunkCount_DependsOnlyOnChunkSize()
    {
        var table = new Table(1000);
        Assert.Equal(4, new TickRunner(table, 0, [], chunkSize: 256, maxThreads: 1).ChunkCount);
        Assert.Equal(4, new TickRunner(table, 0, [], chunkSize: 256, maxThreads: 8).ChunkCount);
    }

    [Fact]
    public void Tick_CountsCompletedTicks()
    {
        var table = new Table(10);
        var runner = new TickRunner(table, 0, [new DiffusionTestSystem(table, chunkCount: 1)], chunkSize: 16, maxThreads: 1);
        runner.RunTick();
        runner.RunTick();
        Assert.Equal(2, runner.Tick);
    }
}

/// <summary>
/// Test-only system, not a model. Compute: each row gains random noise plus a share of its
/// left neighbour (reads Current across chunk borders) and records a per-chunk double sum.
/// Merge: sums the chunk partials in chunk order (double addition is order-sensitive, so a
/// wrong merge order would show up in the hash) and marks rows above the mean.
/// </summary>
internal sealed class DiffusionTestSystem : ITickSystem
{
    private readonly LongColumn _stock;
    private readonly IntColumn _aboveMean;
    private readonly double[] _chunkSums;

    public DiffusionTestSystem(Table table, int chunkCount)
    {
        _stock = table.AddLong("stock");
        _aboveMean = table.AddInt("above_mean");
        _chunkSums = new double[chunkCount];

        var initial = new long[table.RowCount];
        for (int i = 0; i < initial.Length; i++)
            initial[i] = Fx6.FromInt(100 + i % 7).Raw;
        _stock.Load(initial);
    }

    public int SystemId => 1;

    public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
    {
        ReadOnlySpan<long> current = _stock.Current;
        Span<long> next = _stock.Next;
        double sum = 0;
        for (int row = rowStart; row < rowEnd; row++)
        {
            Fx6 own = Fx6.FromRaw(current[row]);
            Fx6 left = Fx6.FromRaw(current[(row + current.Length - 1) % current.Length]);
            double noise = context.Rng.NextUnit(context.Tick, SystemId, row, 0) * 3.7;
            Fx6 value = own + left * Fx6.FromRaw(10_000) + Fx6.FromDouble(noise) - own * Fx6.FromRaw(9_000);
            next[row] = value.Raw;
            sum += value.ToDouble() / 3.0;
        }
        _chunkSums[chunk] = sum;
    }

    public void Merge(in TickContext context, int chunkCount)
    {
        double total = 0;
        for (int chunk = 0; chunk < chunkCount; chunk++)
            total += _chunkSums[chunk];
        Fx6 mean = Fx6.FromDouble(total * 3.0 / _stock.Length);

        ReadOnlySpan<long> stock = _stock.Next;
        Span<int> above = _aboveMean.Next;
        for (int row = 0; row < stock.Length; row++)
            above[row] += stock[row] > mean.Raw ? 1 : 0;
    }

    public void Post(in TickContext context)
    {
        ReadOnlySpan<long> stock = _stock.Current;
        for (int row = 0; row < stock.Length; row++)
        {
            if (stock[row] < 0)
                throw new InvalidOperationException($"Negative stock at row {row}, tick {context.Tick}.");
        }
    }
}
