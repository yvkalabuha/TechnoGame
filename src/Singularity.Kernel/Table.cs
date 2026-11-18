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

    private bool _inTick;
    private long _version;

    public int RowCount { get; }

    /// <summary>Grows with every change of any <c>Current</c>: a swap that exchanged a column or a <c>Column.Load</c> (ADR-0017 п. 6).</summary>
    public long Version => _version;

    /// <summary>True from the start of a tick to its swap; <c>Column.Next</c> works only then.</summary>
    internal bool InTick => Volatile.Read(ref _inTick);

    /// <summary>
    /// Test switch: copy every column at the start of each non-empty tick (the pre-ADR-0017 behaviour) instead of copy-on-write.
    /// The state and its hash must not depend on it.
    /// </summary>
    public bool FullCopyMode { get; set; }

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
