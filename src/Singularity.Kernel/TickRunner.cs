using System.Runtime.ExceptionServices;

namespace Singularity.Kernel;

/// <summary>What a system sees during one phase of a tick.</summary>
public readonly struct TickContext
{
    public TickContext(Table table, CounterRng rng, long tick, TickCalendar? calendar = null)
    {
        Table = table;
        Rng = rng;
        Tick = tick;
        Calendar = calendar;
    }

    public Table Table { get; }
    public CounterRng Rng { get; }

    /// <summary>
    /// Index of the state held in <c>Current</c>. Compute and Merge of the step t → t+1 see t;
    /// Post runs after the swap and sees t+1, the same value as <see cref="TickRunner.Tick"/>.
    /// </summary>
    public long Tick { get; }

    /// <summary>Calendar of the run (null when the runner has none: then every system is daily).</summary>
    public TickCalendar? Calendar { get; }
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

    /// <summary>
    /// How often the system runs. On a tick where it is not due, its Compute, Merge and Post are skipped
    /// and its columns keep their values. Anything other than <see cref="Kernel.Cadence.Daily"/> needs a
    /// runner with a <see cref="TickCalendar"/>.
    /// </summary>
    Cadence Cadence => Cadence.Daily;

    /// <summary>
    /// True when commands wait to be applied. They are applied at the start of the system's next due tick
    /// (a command decided in month step t acts from day d(t+1), slice spec v0.5 §2); a tick that applies
    /// commands is therefore never an empty one.
    /// </summary>
    bool HasPendingCommands => false;

    /// <summary>
    /// Start of tick: apply pending commands by writing <c>Next</c> of own columns; <c>Next</c> starts as a
    /// copy of <c>Current</c> and becomes <c>Current</c> before Compute of the same tick. Called only on a due
    /// tick when <see cref="HasPendingCommands"/> is true, in system list order.
    /// </summary>
    void ApplyCommands(in TickContext context) { }
}

/// <summary>
/// Runs ticks: commands → compute (parallel over chunks) → merge (systems in list order) → swap → post.
/// With a <see cref="TickCalendar"/>, only systems whose <see cref="ITickSystem.Cadence"/> is due run;
/// a tick with no due system and no pending commands only advances the clock (no copy, no swap).
/// Chunk boundaries depend only on <c>chunkSize</c>, never on thread count, so per-chunk
/// results and their merge order are identical for 1 or N threads.
/// An exception thrown by <see cref="ITickSystem.Compute"/> always surfaces as
/// <see cref="TickComputeException"/>, for any thread count. If several chunks fail, the lowest
/// failed chunk is reported: with one thread the run stops there; with N threads all chunks of
/// the system still run and the lowest failure is thrown afterwards (DEBT-0002).
/// </summary>
public sealed class TickRunner
{
    private readonly Table _table;
    private readonly CounterRng _rng;
    private readonly ITickSystem[] _systems;
    private readonly int _chunkSize;
    private readonly ParallelOptions _parallel;
    private readonly TickComputeException?[] _failures;

    private readonly TickCalendar? _calendar;
    private readonly bool[] _due;

    public TickRunner(Table table, ulong seed, IReadOnlyList<ITickSystem> systems, int chunkSize, int maxThreads,
        TickCalendar? calendar = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxThreads, 1);
        _table = table;
        _rng = new CounterRng(seed);
        _systems = systems.ToArray();
        _calendar = calendar;
        _due = new bool[_systems.Length];
        if (calendar is null)
        {
            foreach (ITickSystem system in _systems)
            {
                if (system.Cadence != Cadence.Daily)
                    throw new ArgumentException($"System {system.SystemId} has cadence {system.Cadence} but the runner has no calendar.", nameof(systems));
            }
        }
        _chunkSize = chunkSize;
        _parallel = new ParallelOptions { MaxDegreeOfParallelism = maxThreads };
        ChunkCount = ChunkCountFor(table.RowCount, chunkSize);
        _failures = new TickComputeException?[ChunkCount];
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

    public TickCalendar? Calendar => _calendar;

    public void RunTick()
    {
        var context = new TickContext(_table, _rng, Tick, _calendar);
        bool anyDue = false, anyCommands = false;
        for (int s = 0; s < _systems.Length; s++)
        {
            _due[s] = _calendar is null || _calendar.IsDue(_systems[s].Cadence, Tick);
            anyDue |= _due[s];
            anyCommands |= _due[s] && _systems[s].HasPendingCommands;
        }

        if (anyCommands)
        {
            _table.CopyCurrentToNext();
            for (int s = 0; s < _systems.Length; s++)
            {
                if (_due[s] && _systems[s].HasPendingCommands)
                    _systems[s].ApplyCommands(context);
            }
            _table.Swap();
        }

        if (!anyDue)
        {
            // Nothing runs today: the state is unchanged, only the clock moves (spec v0.5 §2).
            Tick++;
            return;
        }

        _table.CopyCurrentToNext();

        for (int s = 0; s < _systems.Length; s++)
        {
            if (!_due[s])
                continue;
            ITickSystem system = _systems[s];
            if (_parallel.MaxDegreeOfParallelism == 1)
            {
                for (int chunk = 0; chunk < ChunkCount; chunk++)
                    ComputeChunk(system, context, chunk);
            }
            else
            {
                // Each iteration writes only its own rows/slot (and its own failure slot), so there are
                // no shared writes. Failures are recorded, not thrown: every chunk runs, so the set of
                // failed chunks, and with it the reported lowest one, does not depend on scheduling.
                Array.Clear(_failures);
                Parallel.For(0, ChunkCount, _parallel, chunk =>
                {
                    try
                    {
                        ComputeChunk(system, context, chunk);
                    }
                    catch (TickComputeException e)
                    {
                        _failures[chunk] = e;
                    }
                });
                for (int chunk = 0; chunk < ChunkCount; chunk++)
                {
                    if (_failures[chunk] is { } failure)
                        ExceptionDispatchInfo.Capture(failure).Throw();
                }
            }
        }

        for (int s = 0; s < _systems.Length; s++)
        {
            if (_due[s])
                _systems[s].Merge(context, ChunkCount);
        }

        _table.Swap();
        Tick++;

        var postContext = new TickContext(_table, _rng, Tick, _calendar);
        for (int s = 0; s < _systems.Length; s++)
        {
            if (_due[s])
                _systems[s].Post(postContext);
        }
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
