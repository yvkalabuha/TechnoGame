namespace Singularity.Kernel;

/// <summary>A reference to a row of a child table: the global row and the slot generation it was taken at (ADR-0029 п. 2).</summary>
public readonly record struct RowHandle(int Row, int Generation);

/// <summary>
/// Named tables of one simulation, in creation order (that order defines the world hash). A child table groups its rows by the rows
/// of its parent so that a chunk of the parent owns a contiguous range of the child (ADR-0029 п. 5).
/// </summary>
public sealed class World
{
    private readonly List<Table> _tables = new();
    private readonly List<string> _names = new();
    private readonly IReadOnlyList<Table> _tablesView;
    private readonly IReadOnlyList<string> _namesView;
    private ColumnWriteTrace? _trace;

    /// <summary>Test-only one-writer trace shared by every root table of the world (child tables follow their parent). Set it before the first table is added or the first tick.</summary>
    public ColumnWriteTrace? Trace
    {
        get => _trace;
        set
        {
            _trace = value;
            foreach (Table t in _tables)
                t.Trace = t.Parent is null ? value : null;
        }
    }

    public World()
    {
        // Read-only wrappers: a cast to IList cannot change the registered set behind the registration lock.
        _tablesView = _tables.AsReadOnly();
        _namesView = _names.AsReadOnly();
    }

    public IReadOnlyList<Table> Tables => _tablesView;

    public IReadOnlyList<string> Names => _namesView;

    public Table AddTable(string name, int rowCount, int schemaVersion = 1)
    {
        CheckName(name);
        return Register(name, new Table(rowCount) { SchemaVersion = schemaVersion, TraceName = name, Trace = _trace });
    }

    /// <summary>Child table with a fixed capacity per parent row (<c>offset[a] = a * capacity</c>).</summary>
    public Table AddChildTable(string name, Table parent, int capacityPerParentRow, int schemaVersion = 1)
    {
        CheckName(name);
        CheckParent(parent);
        ArgumentOutOfRangeException.ThrowIfNegative(capacityPerParentRow);
        var offsets = new int[parent.RowCount + 1];
        for (int a = 1; a < offsets.Length; a++)
            offsets[a] = checked(offsets[a - 1] + capacityPerParentRow);
        return Register(name, new Table(parent, offsets, name) { SchemaVersion = schemaVersion });
    }

    /// <summary>Child table with its own count of rows per parent row (prefix sums are built here).</summary>
    public Table AddChildTable(string name, Table parent, ReadOnlySpan<int> countPerParentRow, int schemaVersion = 1)
    {
        CheckName(name);
        CheckParent(parent);
        if (countPerParentRow.Length != parent.RowCount)
            throw new ArgumentException($"Expected {parent.RowCount} counts, got {countPerParentRow.Length}.", nameof(countPerParentRow));
        var offsets = new int[parent.RowCount + 1];
        for (int a = 0; a < countPerParentRow.Length; a++)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(countPerParentRow[a]);
            offsets[a + 1] = checked(offsets[a] + countPerParentRow[a]);
        }
        return Register(name, new Table(parent, offsets, name) { SchemaVersion = schemaVersion });
    }

    public Table Get(string name)
    {
        int i = _names.IndexOf(name);
        return i < 0 ? throw new KeyNotFoundException($"Table '{name}' is not registered.") : _tables[i];
    }

    // Checked before a table is built: a rejected call must leave no child attached to a parent.
    private void CheckOpen()
    {
        foreach (Table t in _tables)
        {
            if (t.Locked)
                throw new InvalidOperationException("The world is run by a TickRunner: no tables can be added.");
        }
    }

    private void CheckParent(Table parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (!_tables.Contains(parent))
            throw new ArgumentException("The parent table is not in this world.", nameof(parent));
    }

    // Checked before a table is built: a failed registration must leave nothing attached to a parent.
    private void CheckName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        // A dot would make "table.column" ambiguous in the one-writer trace.
        if (name.Contains('.'))
            throw new ArgumentException("A table name must not contain '.'.", nameof(name));
        CheckOpen();
        if (_names.Contains(name))
            throw new ArgumentException($"Table '{name}' is already registered.", nameof(name));
    }

    private Table Register(string name, Table table)
    {
        _names.Add(name);
        _tables.Add(table);
        return table;
    }
}
