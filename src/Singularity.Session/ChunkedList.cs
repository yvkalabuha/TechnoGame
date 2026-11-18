using System.Collections;

namespace Singularity.Session;

/// <summary>
/// An append-only list in fixed chunks of 1024 items: it never copies and no array of it reaches the large object heap (a <c>List</c> of the
/// session's command logs doubled past 85 000 bytes and set off gen2 collections, TASK-0107). Owner thread only.
/// </summary>
internal sealed class ChunkedList<T> : IEnumerable<T>
{
    private const int ChunkSize = 1024;
    private readonly List<T[]> _chunks = new();

    public int Count { get; private set; }

    public T this[int index] =>
        (uint)index >= (uint)Count ? throw new ArgumentOutOfRangeException(nameof(index), index, "Index outside the list.") : _chunks[index / ChunkSize][index % ChunkSize];

    public void Add(T item)
    {
        if (Count == _chunks.Count * ChunkSize)
            _chunks.Add(new T[ChunkSize]);
        _chunks[Count / ChunkSize][Count % ChunkSize] = item;
        Count++;
    }

    public void AddRange(List<T> items)
    {
        foreach (T item in items)
            Add(item);
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
