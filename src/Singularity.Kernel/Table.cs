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

    public int RowCount { get; }

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

    internal void Swap()
    {
        for (int i = 0; i < _columns.Count; i++)
            _columns[i].Swap();
    }

    private T Add<T>(T column) where T : Column
    {
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
