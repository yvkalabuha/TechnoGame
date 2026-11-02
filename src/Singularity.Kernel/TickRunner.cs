namespace Singularity.Kernel;

/// <summary>What a system sees during one tick. <see cref="Tick"/> is the tick being computed (t → t+1).</summary>
public readonly struct TickContext
{
    public TickContext(Table table, CounterRng rng, long tick)
    {
        Table = table;
        Rng = rng;
        Tick = tick;
    }

    public Table Table { get; }
    public CounterRng Rng { get; }
    public long Tick { get; }
}

/// <summary>
/// A simulation system run by <see cref="TickRunner"/> (ADR-0006).
/// Contract: <see cref="Compute"/> reads only <c>Current</c> and writes only rows
/// [rowStart, rowEnd) of its own columns' <c>Next</c> or its own per-chunk buffer slot
/// <paramref name="chunk"/>. Each column is written by one system only.
/// </summary>
public interface ITickSystem
{
    /// <summary>Stable id used as the RNG "system" field.</summary>
    int SystemId { get; }

    /// <summary>Parallel phase; may run concurrently for different chunks.</summary>
    void Compute(in TickContext context, int chunk, int rowStart, int rowEnd);

    /// <summary>Sequential phase: apply per-chunk buffers to <c>Next</c> in chunk order 0..N-1.</summary>
    void Merge(in TickContext context, int chunkCount);

    /// <summary>Sequential phase after the swap: invariants and metrics on the new <c>Current</c>.</summary>
    void Post(in TickContext context);
}

/// <summary>
/// Runs ticks: compute (parallel over chunks) → merge (systems in list order) → swap → post.
/// Chunk boundaries depend only on <c>chunkSize</c>, never on thread count, so per-chunk
/// results and their merge order are identical for 1 or N threads.
/// </summary>
public sealed class TickRunner
{
    private readonly Table _table;
    private readonly CounterRng _rng;
    private readonly ITickSystem[] _systems;
    private readonly int _chunkSize;
    private readonly ParallelOptions _parallel;

    public TickRunner(Table table, ulong seed, IReadOnlyList<ITickSystem> systems, int chunkSize, int maxThreads)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxThreads, 1);
        _table = table;
        _rng = new CounterRng(seed);
        _systems = systems.ToArray();
        _chunkSize = chunkSize;
        _parallel = new ParallelOptions { MaxDegreeOfParallelism = maxThreads };
        ChunkCount = (table.RowCount + chunkSize - 1) / chunkSize;
    }

    /// <summary>Index of the state held in <c>Current</c>: number of completed ticks.</summary>
    public long Tick { get; private set; }

    public int ChunkCount { get; }

    public void RunTick()
    {
        var context = new TickContext(_table, _rng, Tick);
        _table.CopyCurrentToNext();

        for (int s = 0; s < _systems.Length; s++)
        {
            ITickSystem system = _systems[s];
            if (_parallel.MaxDegreeOfParallelism == 1)
            {
                for (int chunk = 0; chunk < ChunkCount; chunk++)
                    ComputeChunk(system, context, chunk);
            }
            else
            {
                // Each iteration writes only its own rows/slot, so no shared writes happen here.
                Parallel.For(0, ChunkCount, _parallel, chunk => ComputeChunk(system, context, chunk));
            }
        }

        for (int s = 0; s < _systems.Length; s++)
            _systems[s].Merge(context, ChunkCount);

        _table.Swap();
        Tick++;

        for (int s = 0; s < _systems.Length; s++)
            _systems[s].Post(context);
    }

    private void ComputeChunk(ITickSystem system, in TickContext context, int chunk)
    {
        int start = chunk * _chunkSize;
        int end = Math.Min(start + _chunkSize, _table.RowCount);
        system.Compute(context, chunk, start, end);
    }
}
