using System.Runtime.ExceptionServices;

namespace Singularity.Kernel;

/// <summary>What a system sees during one phase of a tick.</summary>
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

    /// <summary>
    /// Index of the state held in <c>Current</c>. Compute and Merge of the step t → t+1 see t;
    /// Post runs after the swap and sees t+1, the same value as <see cref="TickRunner.Tick"/>.
    /// </summary>
    public long Tick { get; }
}

/// <summary>
/// A simulation system run by <see cref="TickRunner"/> (ADR-0006).
/// Contract: <see cref="Compute"/> reads only <c>Current</c> and writes only rows
/// [rowStart, rowEnd) of its own columns' <c>Next</c> or its own per-chunk buffer slot
/// <paramref name="chunk"/>. Each column is written by one system only.
/// All persistent state of a system lives in <see cref="Table"/> columns: anything kept in
/// fields between ticks (other than scratch buffers rebuilt every tick) is invisible to
/// <see cref="StateHash"/> and to save/load, so it breaks determinism checks.
/// Chunk count and sizes come from <see cref="TickRunner.ChunkCountFor"/>; systems must not derive them.
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
/// An exception thrown by <see cref="ITickSystem.Compute"/> always surfaces as
/// <see cref="TickComputeException"/>, for any thread count. If several chunks fail in parallel,
/// the lowest chunk among the observed failures is reported (Parallel.For may stop early).
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
        ChunkCount = ChunkCountFor(table.RowCount, chunkSize);
    }

    /// <summary>
    /// Number of chunks for a table, available before systems are created so they can size
    /// their per-chunk buffers. Computed in 64 bits: a huge chunkSize must not overflow.
    /// </summary>
    public static int ChunkCountFor(int rowCount, int chunkSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        return (int)(((long)rowCount + chunkSize - 1) / chunkSize);
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
                try
                {
                    Parallel.For(0, ChunkCount, _parallel, chunk => ComputeChunk(system, context, chunk));
                }
                catch (AggregateException aggregate)
                {
                    ExceptionDispatchInfo.Capture(LowestChunkFailure(aggregate)).Throw();
                }
            }
        }

        for (int s = 0; s < _systems.Length; s++)
            _systems[s].Merge(context, ChunkCount);

        _table.Swap();
        Tick++;

        var postContext = new TickContext(_table, _rng, Tick);
        for (int s = 0; s < _systems.Length; s++)
            _systems[s].Post(postContext);
    }

    private void ComputeChunk(ITickSystem system, in TickContext context, int chunk)
    {
        // 64-bit bounds: chunk * chunkSize can exceed int when chunkSize is huge.
        long start = (long)chunk * _chunkSize;
        long end = Math.Min(start + _chunkSize, _table.RowCount);
        try
        {
            system.Compute(context, chunk, (int)start, (int)end);
        }
        catch (Exception e)
        {
            throw new TickComputeException(system.SystemId, chunk, context.Tick, e);
        }
    }

    // Parallel.For wraps failures and may stop early, so the set of failed chunks varies;
    // picking the lowest one keeps the reported failure as stable as the input allows.
    private static Exception LowestChunkFailure(AggregateException aggregate)
    {
        Exception? lowest = null;
        int lowestChunk = int.MaxValue;
        foreach (Exception inner in aggregate.Flatten().InnerExceptions)
        {
            if (inner is not TickComputeException compute)
                return inner;
            if (compute.Chunk < lowestChunk)
            {
                lowest = compute;
                lowestChunk = compute.Chunk;
            }
        }
        return lowest ?? aggregate;
    }
}

/// <summary>A failure inside <see cref="ITickSystem.Compute"/>; the original exception is the inner one.</summary>
public sealed class TickComputeException : Exception
{
    public TickComputeException(int systemId, int chunk, long tick, Exception inner)
        : base($"System {systemId} failed in Compute, chunk {chunk}, tick {tick}: {inner.Message}", inner)
    {
        SystemId = systemId;
        Chunk = chunk;
        Tick = tick;
    }

    public int SystemId { get; }
    public int Chunk { get; }
    public long Tick { get; }
}
