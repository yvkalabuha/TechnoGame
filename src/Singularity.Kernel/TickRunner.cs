using System.Runtime.ExceptionServices;

namespace Singularity.Kernel;

/// <summary>What a system sees during one phase of a tick.</summary>
public readonly struct TickContext
{
    // Internal: only the runner builds a context, so a callback cannot forge the chunk or chunk size that NextRange trusts.
    internal TickContext(Table table, CounterRng rng, long tick, TickCalendar? calendar = null, int chunkSize = 0, int chunk = -1)
    {
        Chunk = chunk;
        ChunkSize = chunkSize;
        Table = table;
        Rng = rng;
        Tick = tick;
        Calendar = calendar;
    }

    /// <summary>The chunk table of the run (the parent of child tables).</summary>
    public Table Table { get; }
    public CounterRng Rng { get; }

    /// <summary>The chunk of this Compute callback (-1 outside compute); <c>NextRange</c> accepts only this chunk.</summary>
    public int Chunk { get; }

    /// <summary>Rows of the chunk table per chunk (0 in <c>default</c>); <c>NextRange</c> needs it.</summary>
    public int ChunkSize { get; }

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

    /// <summary>
    /// Columns this system writes through <c>Next</c> or <c>NextRange</c>. The runner opens them (copy-on-write) single-threaded before
    /// its compute, so no chunk opens a column in parallel (ADR-0029 п. 5). `NextRange` of a child column that is not listed throws in compute.
    /// Called every tick: return a cached array or list, never a new one (no allocation on the tick path). Empty by default.
    /// </summary>
    IReadOnlyList<Column> WrittenColumns => Array.Empty<Column>();

    /// <summary>
    /// The table whose rows are split into chunks for this system; null means the runner's primary table. It must belong to the same
    /// world. <see cref="TickContext.Table"/> and the chunk count of <see cref="Merge"/> follow it (ADR-0020 п. 1).
    /// </summary>
    Table? ChunkTable => null;
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
    private readonly Table[] _tables;   // chunk table first, then the rest in creation order
    private readonly Table[] _childTables;
    private readonly Table[] _chunkTables;  // per system: its chunk table (ADR-0020 п. 1)
    private readonly int[] _chunkCounts;
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
        : this(table, Flatten(table), seed, systems, chunkSize, maxThreads, calendar)
    {
    }

    /// <summary>Runs every table of the world; chunks follow <paramref name="chunkTable"/>.</summary>
    public TickRunner(World world, Table chunkTable, ulong seed, IReadOnlyList<ITickSystem> systems, int chunkSize, int maxThreads,
        TickCalendar? calendar = null)
        : this(chunkTable, Ordered(world, chunkTable), seed, systems, chunkSize, maxThreads, calendar)
    {
    }

    // Checked before the private constructor locks any table: a rejected call must leave the world open.
    private static Table[] Ordered(World world, Table chunkTable)
    {
        if (!world.Tables.Contains(chunkTable))
            throw new ArgumentException("The chunk table is not in the world.", nameof(chunkTable));
        return world.Tables.Where(t => !ReferenceEquals(t, chunkTable)).Prepend(chunkTable).ToArray();
    }

    private static Table[] Flatten(Table root)
    {
        var all = new List<Table> { root };
        for (int i = 0; i < all.Count; i++)
            all.AddRange(all[i].Children);
        return all.ToArray();
    }

    private TickRunner(Table table, Table[] tables, ulong seed, IReadOnlyList<ITickSystem> systems, int chunkSize, int maxThreads,
        TickCalendar? calendar)
    {
        _tables = tables;
        _childTables = tables.Where(t => t.Parent is not null).ToArray();
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
        _chunkTables = new Table[_systems.Length];
        _chunkCounts = new int[_systems.Length];
        int maxChunks = ChunkCount;
        for (int s = 0; s < _systems.Length; s++)
        {
            Table chunkTable = _systems[s].ChunkTable ?? table;
            if (Array.IndexOf(tables, chunkTable) < 0)
                throw new ArgumentException($"System {_systems[s].SystemId}: its chunk table is not in the world.", nameof(systems));
            _chunkTables[s] = chunkTable;
            _chunkCounts[s] = ChunkCountFor(chunkTable.RowCount, chunkSize);
            maxChunks = Math.Max(maxChunks, _chunkCounts[s]);
        }
        // The set of tables is fixed from here on: a table added later would be hashed but never ticked.
        foreach (Table t in tables)
            t.Locked = true;
        _failures = new TickComputeException?[maxChunks];
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

    /// <summary>True if the last <see cref="RunTick"/> ran at least one system (a non-empty tick, ADR-0006); false for an empty day.</summary>
    public bool LastTickRan { get; private set; }

    public TickCalendar? Calendar => _calendar;

    /// <summary>
    /// Puts the clock at <paramref name="tick"/> (the number of completed ticks) after the table was loaded from a snapshot taken at that tick,
    /// and recomputes when each system is next due. Not allowed inside a tick; nothing but the clock and the due ticks lives in the runner.
    /// </summary>
    public void Restore(long tick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tick, MaxTick);
        if (_table.InTick || _table.RunActive)
            throw new InvalidOperationException("Restore is not allowed during a tick (compute, merge or post).");
        // The whole schedule is computed first (a calendar can overflow for a huge tick), then committed: no partial write.
        var due = new long[_systems.Length];
        long min = long.MaxValue;
        for (int s = 0; s < _systems.Length; s++)
        {
            due[s] = _calendar is null ? tick : _calendar.NextDue(_systems[s].Cadence, tick);
            min = Math.Min(min, due[s]);
        }
        due.CopyTo(_nextDue, 0);
        _minNextDue = min;
        Tick = tick;
        LastTickRan = false;
    }

    /// <summary>
    /// The last tick the clock may show. <see cref="Restore"/> accepts a tick up to it and <see cref="RunTick"/> refuses to step past it, so the
    /// clock stays in 0…MaxTick for ever (an increment can never overflow into a negative tick). 2^40 days is far beyond any calendar.
    /// </summary>
    public const long MaxTick = 1L << 40;

    public void RunTick()
    {
        if (Tick >= MaxTick)
            throw new InvalidOperationException($"The clock is at its limit ({MaxTick}).");
        if (Tick < _minNextDue)
        {
            // Nothing runs today: the state is unchanged, only the clock moves (spec v0.5 §2).
            LastTickRan = false;
            Tick++;
            return;
        }

        LastTickRan = true;
        // The flag spans the whole tick, post included: no snapshot is loaded or taken and no restore happens while the state is not settled.
        // Every table of the world: a Post callback must not load or take a snapshot of a secondary table either.
        for (int t = 0; t < _tables.Length; t++)
            _tables[t].RunActive = true;
        try
        {
            for (int s = 0; s < _systems.Length; s++)
                _due[s] = _nextDue[s] == Tick;

            for (int t = 0; t < _tables.Length; t++)
                _tables[t].BeginTick();
            bool completed = false;
            try
            {
                for (int s = 0; s < _systems.Length; s++)
                {
                    if (!_due[s])
                        continue;
                    ITickSystem system = _systems[s];
                    var context = new TickContext(_chunkTables[s], _rng, Tick, _calendar, _chunkSize);
                    int chunkCount = _chunkCounts[s];
                    SetTraceSystem(system.SystemId);
                    IReadOnlyList<Column> written = system.WrittenColumns;
                    for (int c = 0; c < written.Count; c++)
                    {
                        written[c].OpenNext();
                        written[c].Declared = true;
                    }
                    SetCompute(true, _chunkTables[s]);
                    if (_parallel.MaxDegreeOfParallelism == 1)
                    {
                        for (int chunk = 0; chunk < chunkCount; chunk++)
                            ComputeChunk(system, context, chunk);
                    }
                    else
                    {
                        // Each iteration writes only its own rows/slot (and its own failure slot), so there are
                        // no shared writes. Failures are recorded, not thrown: every chunk runs, so the set of
                        // failed chunks, and with it the reported lowest one, does not depend on scheduling.
                        Array.Clear(_failures);
                        Parallel.For(0, chunkCount, _parallel, chunk =>
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
                        for (int chunk = 0; chunk < chunkCount; chunk++)
                        {
                            if (_failures[chunk] is { } failure)
                                ExceptionDispatchInfo.Capture(failure).Throw();
                        }
                    }
                    SetCompute(false, _chunkTables[s]);
                    for (int c = 0; c < written.Count; c++)
                        written[c].Declared = false;
                }

                for (int s = 0; s < _systems.Length; s++)
                {
                    if (!_due[s])
                        continue;
                    SetTraceSystem(_systems[s].SystemId);
                    _systems[s].Merge(new TickContext(_chunkTables[s], _rng, Tick, _calendar, _chunkSize), _chunkCounts[s]);
                }

                for (int t = 0; t < _tables.Length; t++)
                    _tables[t].Swap();
                completed = true;
            }
            finally
            {
                for (int t = 0; t < _tables.Length; t++)
                    _tables[t].EndTick(completed);
            }
            Tick++;
            UpdateNextDue();

            for (int s = 0; s < _systems.Length; s++)
            {
                if (_due[s])
                    _systems[s].Post(new TickContext(_chunkTables[s], _rng, Tick, _calendar, _chunkSize));
            }
        }
        finally
        {
            for (int t = 0; t < _tables.Length; t++)
                _tables[t].RunActive = false;
        }
    }

    // Root tables keep their own system id for the one-writer trace (children follow their parent); tracing is per table.
    private void SetTraceSystem(int systemId)
    {
        for (int t = 0; t < _tables.Length; t++)
        {
            if (_tables[t].Parent is null && _tables[t].Trace is not null)
                _tables[t].TraceSystemId = systemId;
        }
    }

    // In compute only the chunk table may use Next (its own rows); every other table is written in Merge (ADR-0029 п. 5, ADR-0020).
    private void SetCompute(bool on, Table chunkTable)
    {
        for (int t = 0; t < _tables.Length; t++)
            _tables[t].InCompute = on && (_tables[t].Parent is not null || !ReferenceEquals(_tables[t], chunkTable));
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
        long end = Math.Min(start + _chunkSize, context.Table.RowCount);
        try
        {
            var chunkContext = new TickContext(context.Table, context.Rng, context.Tick, context.Calendar, context.ChunkSize, chunk);
            system.Compute(chunkContext, chunk, (int)start, (int)end);
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
