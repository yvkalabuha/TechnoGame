using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Singularity.Kernel;

/// <summary>
/// One field of a table stored as a dense array (SoA, ADR-0005), double-buffered:
/// systems read <c>Current</c> (state at tick t) and write <c>Next</c> (state at t+1).
/// </summary>
public abstract class Column
{
    protected Column(string name) => Name = name;

    public string Name { get; }

    public abstract int Length { get; }

    /// <summary>Carries unchanged values over, so a system only writes what it changes.</summary>
    internal abstract void CopyCurrentToNext();

    internal abstract void Swap();

    /// <summary>Appends <c>Current</c> to the hash as little-endian values, independent of CPU byte order.</summary>
    internal abstract void AppendCurrentTo(IncrementalHash hash);
}

public abstract class Column<T> : Column where T : unmanaged
{
    private const int HashBufferBytes = 8192;

    private T[] _current;
    private T[] _next;

    protected Column(string name, int length) : base(name)
    {
        _current = new T[length];
        _next = new T[length];
    }

    public override int Length => _current.Length;

    public ReadOnlySpan<T> Current => _current;

    public Span<T> Next => _next;

    /// <summary>Sets the current state directly. Only for setup and loading, never inside a tick.</summary>
    public void Load(ReadOnlySpan<T> values)
    {
        if (values.Length != _current.Length)
            throw new ArgumentException($"Column '{Name}' expects {_current.Length} values, got {values.Length}.", nameof(values));
        values.CopyTo(_current);
    }

    internal override void CopyCurrentToNext() => _current.CopyTo(_next, 0);

    internal override void Swap() => (_current, _next) = (_next, _current);

    protected abstract int ElementSize { get; }

    protected abstract void WriteLittleEndian(Span<byte> destination, T value);

    internal override void AppendCurrentTo(IncrementalHash hash)
    {
        Span<byte> buffer = stackalloc byte[HashBufferBytes];
        int perBatch = HashBufferBytes / ElementSize;
        for (int start = 0; start < _current.Length; start += perBatch)
        {
            int count = Math.Min(perBatch, _current.Length - start);
            for (int i = 0; i < count; i++)
                WriteLittleEndian(buffer.Slice(i * ElementSize, ElementSize), _current[start + i]);
            hash.AppendData(buffer[..(count * ElementSize)]);
        }
    }
}

public sealed class LongColumn : Column<long>
{
    internal LongColumn(string name, int length) : base(name, length) { }

    protected override int ElementSize => sizeof(long);

    protected override void WriteLittleEndian(Span<byte> destination, long value) =>
        BinaryPrimitives.WriteInt64LittleEndian(destination, value);
}

public sealed class IntColumn : Column<int>
{
    internal IntColumn(string name, int length) : base(name, length) { }

    protected override int ElementSize => sizeof(int);

    protected override void WriteLittleEndian(Span<byte> destination, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination, value);
}
