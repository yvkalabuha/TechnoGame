namespace Singularity.Kernel;

/// <summary>
/// Test-only trace for the one-writer rule (ADR-0006): records which system touched <c>Next</c> of which column.
/// Off by default (<see cref="Table.Trace"/> is null). While it is on, <c>Column.Next</c> takes the slow path on every
/// access so each touch is seen; the hot path of normal play is unchanged. The system id comes from the tick context
/// (set by <see cref="TickRunner"/> before each system phase), not from a stack walk.
/// </summary>
public sealed class ColumnWriteTrace
{
    private readonly object _lock = new();
    private readonly List<(int SystemId, string Column)> _pairs = new();

    /// <summary>Distinct (system id, column name) pairs in first-seen order.</summary>
    public IReadOnlyList<(int SystemId, string Column)> Pairs
    {
        get
        {
            lock (_lock)
                return _pairs.ToArray();
        }
    }

    /// <summary>Columns touched by more than one system, with the systems' ids; empty when the rule holds.</summary>
    public IReadOnlyList<(string Column, int[] SystemIds)> Violations()
    {
        lock (_lock)
        {
            var result = new List<(string, int[])>();
            var seen = new List<string>();
            foreach ((_, string column) in _pairs)
            {
                if (seen.Contains(column))
                    continue;
                seen.Add(column);
                int[] ids = _pairs.Where(p => p.Column == column).Select(p => p.SystemId).Order().ToArray();
                if (ids.Length > 1)
                    result.Add((column, ids));
            }
            return result;
        }
    }

    internal void Record(int systemId, string column)
    {
        lock (_lock)
        {
            foreach (var p in _pairs)
            {
                if (p.SystemId == systemId && p.Column == column)
                    return;
            }
            _pairs.Add((systemId, column));
        }
    }
}
