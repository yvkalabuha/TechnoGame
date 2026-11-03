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
    /// runner with a <see cref="TickCalendar"/> whose start is aligned with the cadence (ADR-0004).
    /// </summary>
    Cadence Cadence => Cadence.Daily;
}

/// <summary>
/// Runs ticks: compute (parallel over chunks) → merge (systems in list order) → swap → post.
/// With a <see cref="TickCalendar"/>, only systems whose <see cref="ITickSystem.Cadence"/> is due run;
/// a tick with no due system only advances the clock (no copy, no swap; ADR-0006 "Пустой тик").
/// Commands are not handled here: the command queue belongs to Session (ADR-0014).
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

    // Next due tick per system and their minimum: an empty day costs one comparison (ADR-0006 budget).
    private readonly long[] _nextDue;
    private long _minNextDue;

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
        _nextDue = new long[_systems.Length];
        foreach (ITickSystem system in _systems)
        {
            if (calendar is null && system.Cadence != Cadence.Daily)
                throw new ArgumentException($"System {system.SystemId} has cadence {system.Cadence} but the runner has no calendar.", nameof(systems));
            // ADR-0004: firing index 0 must be the first full period, so tick 0 must be a firing tick.
            if (calendar is not null && !calendar.IsDue(system.Cadence, 0))
                throw new ArgumentException(
                    $"Calendar start {calendar.Start} is not aligned with cadence {system.Cadence} of system {system.SystemId} (ADR-0004).",
                    nameof(calendar));
        }
        _minNextDue = 0;
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
        if (Tick < _minNextDue)
        {
            // Nothing runs today: the state is unchanged, only the clock moves (spec v0.5 §2).
            Tick++;
            return;
        }

        var context = new TickContext(_table, _rng, Tick, _calendar);
        for (int s = 0; s < _systems.Length; s++)
            _due[s] = _nextDue[s] == Tick;

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
        UpdateNextDue();

        var postContext = new TickContext(_table, _rng, Tick, _calendar);
        for (int s = 0; s < _systems.Length; s++)
        {
            if (_due[s])
                _systems[s].Post(postContext);
        }
    }

    // After a tick at which systems ran: their next due tick is the first due tick from the new Tick on.
    private void UpdateNextDue()
    {
        long min = long.MaxValue;
        for (int s = 0; s < _systems.Length; s++)
        {
            if (_due[s])
                _nextDue[s] = _calendar is null ? Tick : _calendar.NextDue(_systems[s].Cadence, Tick);
            min = Math.Min(min, _nextDue[s]);
        }
        _minNextDue = min;
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
