namespace Singularity.Kernel;

/// <summary>
/// One entity type: a fixed number of rows and named columns (ADR-0005).
/// Columns are kept in registration order; that order defines the state hash.
/// </summary>
public sealed class Table
{
    private readonly List<Column> _columns = new();

    public Table(int rowCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        RowCount = rowCount;
    }

    // Child table (ADR-0029 п. 5): rows grouped by parent row; offsets has parent.RowCount + 1 entries.
    internal Table(Table parent, int[] offsets, string name) : this(offsets[^1])
    {
        Parent = parent;
        Offsets = offsets;
        TraceName = name;
        Generation = AddInt("generation");
        parent._children.Add(this);
    }

    private readonly List<Table> _children = new();
    private bool _inCompute;
    private ColumnWriteTrace? _trace;
    private bool _fullCopyMode;

    /// <summary>Parent table of a child table, null for a root table.</summary>
    public Table? Parent { get; }

    /// <summary>Child tables of this table, in creation order.</summary>
    public IReadOnlyList<Table> Children => _children;

    /// <summary>Child tables: first child row of every parent row, plus the end (parent.RowCount + 1 entries). Null for a root table.</summary>
    internal int[]? Offsets { get; }

    /// <summary>Prefix of column names in the write trace (the table name for a child table), null for a root table.</summary>
    internal string? TraceName { get; }

    /// <summary>
    /// Slot generation of a child table (first column, hashed): a handle is live while it equals the generation of its row.
    /// Whoever closes and reopens a slot writes it (one writer, like any column). Null for a root table.
    /// </summary>
    public IntColumn? Generation { get; }

    /// <summary>First child row of a parent row; the row's slots are [OffsetOf(p), OffsetOf(p + 1)).</summary>
    public int OffsetOf(int parentRow)
    {
        if (Offsets is null)
            throw new InvalidOperationException("Not a child table.");
        return Offsets[parentRow];
    }

    /// <summary>The handle of a child row as it is now (its current generation).</summary>
    public RowHandle HandleOf(int row)
    {
        if (Generation is null)
            throw new InvalidOperationException("Not a child table.");
        return new RowHandle(row, Generation.Current[row]);
    }

    /// <summary>True if the row exists and its generation equals the handle's: a stale handle (slot reopened) is not live.</summary>
    public bool IsLive(RowHandle handle) =>
        Generation is not null && (uint)handle.Row < (uint)RowCount && Generation.Current[handle.Row] == handle.Generation;

    /// <summary>True while the runner computes chunks: <c>Column.Next</c> of a child table throws, use <c>NextRange</c> (ADR-0029 п. 5).</summary>
    internal bool InCompute
    {
        get => Volatile.Read(ref _inCompute);
        set => Volatile.Write(ref _inCompute, value);
    }

    private bool _inTick;
    private long _version;

    public int RowCount { get; }

    /// <summary>Grows with every change of any <c>Current</c>: a swap that exchanged a column or a <c>Column.Load</c> (ADR-0017 п. 6).</summary>
    public long Version => _version;

    /// <summary>True from the start of a tick to its swap; <c>Column.Next</c> works only then.</summary>
    internal bool InTick => Volatile.Read(ref _inTick);

    private int _runActive;

    /// <summary>True for the whole of <see cref="TickRunner.RunTick"/>, post included: the state is not settled, so no snapshot is loaded or taken.</summary>
    internal bool RunActive
    {
        get => Volatile.Read(ref _runActive) != 0;
        set => Volatile.Write(ref _runActive, value ? 1 : 0);
    }

    /// <summary>
    /// Test switch: copy every column at the start of each non-empty tick (the pre-ADR-0017 behaviour) instead of copy-on-write.
    /// The state and its hash must not depend on it.
    /// </summary>
    public bool FullCopyMode
    {
        get => Parent?.FullCopyMode ?? _fullCopyMode;
        set => _fullCopyMode = value;
    }

    /// <summary>Test-only one-writer trace (ADR-0006); null (off) by default. Set it before the first tick. A child table uses its parent's.</summary>
    public ColumnWriteTrace? Trace
    {
        get => Parent?.Trace ?? _trace;
        set => _trace = value;
    }

    private int _traceSystemId;

    /// <summary>Id of the system whose phase is running; set by <see cref="TickRunner"/> only while <see cref="Trace"/> is on.</summary>
    internal int TraceSystemId
    {
        get => Parent?.TraceSystemId ?? _traceSystemId;
        set => _traceSystemId = value;
    }

    internal void BumpVersion() => _version++;

    internal void BeginTick()
    {
        // Flags left open by an aborted tick are dropped here as well.
        for (int i = 0; i < _columns.Count; i++)
            _columns[i].Close();
        Volatile.Write(ref _inTick, true);
        if (FullCopyMode)
            CopyCurrentToNext();
    }

    internal void EndTick(bool completed)
    {
        Volatile.Write(ref _inTick, false);
        InCompute = false;
        if (completed)
            return;
        for (int i = 0; i < _columns.Count; i++)
            _columns[i].Close();
    }

    public IReadOnlyList<Column> Columns => _columns;

    public LongColumn AddLong(string name) => Add(new LongColumn(CheckName(name), RowCount));

    public IntColumn AddInt(string name) => Add(new IntColumn(CheckName(name), RowCount));

    public LongColumn GetLong(string name) => Get<LongColumn>(name);

    public IntColumn GetInt(string name) => Get<IntColumn>(name);

    internal void CopyCurrentToNext()
    {
        for (int i = 0; i < _columns.Count; i++)
            _columns[i].CopyCurrentToNext();
    }

    /// <summary>Swaps the open columns in registration order and closes them; untouched columns keep their Current.</summary>
    internal void Swap()
    {
        bool any = false;
        for (int i = 0; i < _columns.Count; i++)
            any |= _columns[i].SwapIfOpen();
        if (any)
            _version++;
    }

    private T Add<T>(T column) where T : Column
    {
        column.Attach(this);
        _columns.Add(column);
        return column;
    }

    private string CheckName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (Find(name) is not null)
            throw new ArgumentException($"Column '{name}' is already registered.", nameof(name));
        return name;
    }

    private T Get<T>(string name) where T : Column =>
        Find(name) switch
        {
            T column => column,
            null => throw new KeyNotFoundException($"Column '{name}' is not registered."),
            var other => throw new InvalidCastException($"Column '{name}' is {other.GetType().Name}, not {typeof(T).Name}."),
        };

    // Linear search: tables have few columns, and a list keeps the order explicit.
    private Column? Find(string name)
    {
        for (int i = 0; i < _columns.Count; i++)
        {
            if (string.Equals(_columns[i].Name, name, StringComparison.Ordinal))
                return _columns[i];
        }
        return null;
    }
}
