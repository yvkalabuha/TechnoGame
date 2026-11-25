using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Singularity.Kernel;

/// <summary>
/// One field of a table stored as a dense array (SoA, ADR-0005), double-buffered:
/// systems read <c>Current</c> (state at tick t) and write <c>Next</c> (state at t+1).
/// <c>Next</c> is copy-on-write per column (ADR-0017): it is opened, and filled from <c>Current</c>, on the first touch inside a tick.
/// </summary>
public abstract class Column
{
    protected Column(string name) => Name = name;

    public string Name { get; }

    /// <summary>The table that registered the column; set once by <see cref="Table"/>.</summary>
    private protected Table? Owner { get; private set; }

    internal void Attach(Table table) => Owner = table;

    public abstract int Length { get; }

    private long _version;

    /// <summary>
    /// Grows by one whenever <c>Current</c> of this column is replaced: a swap that exchanged it or a <c>Load</c>. A column nobody wrote keeps its
    /// value across ticks. Read between ticks only; it is not part of the state and never hashed. Lets a reader reuse what it derived from the column (TASK-0103 R2).
    /// </summary>
    public long Version => _version;

    private protected void BumpVersion() => _version++;

    /// <summary>Drops the open flag without swapping (a tick aborted by an exception).</summary>
    internal abstract void Close();

    /// <summary>Opens <c>Next</c> (copy-on-write) now; the scheduler calls it single-threaded before a system's compute (ADR-0029 п. 5).</summary>
    internal abstract void OpenNext();

    /// <summary>Full-copy test mode: copies <c>Current</c> to <c>Next</c> and opens the column.</summary>
    internal abstract void CopyCurrentToNext();

    /// <summary>Swaps an open column and closes it; returns false (and does nothing) for a column nobody touched.</summary>
    internal abstract bool SwapIfOpen();

    /// <summary>Appends <c>Current</c> to the hash as little-endian values, independent of CPU byte order.</summary>
    internal abstract void AppendCurrentTo(IncrementalHash hash);

    /// <summary>One letter per element type, part of the schema fingerprint of a snapshot.</summary>
    internal abstract char TypeTag { get; }

    internal abstract int ElementBytes { get; }

    /// <summary>Writes <c>Current</c> as little-endian values; <paramref name="destination"/> is exactly <c>Length * ElementBytes</c> long.</summary>
    internal abstract void WriteCurrentTo(Span<byte> destination);

    /// <summary>Sets <c>Current</c> from little-endian values (the same checks and version bumps as <c>Load</c> of the typed column).</summary>
    internal abstract void LoadFrom(ReadOnlySpan<byte> source);
}

public abstract class Column<T> : Column where T : unmanaged
{
    private const int HashBufferBytes = 8192;

    private T[] _current;
    private T[] _next;
    private bool _open;   // fast-path flag: true only when no trace is active
    private bool _copied;  // Next holds the copy of Current (guarded by _openLock / sequential phases)
    private readonly object _openLock = new();

    // private protected: only Kernel column types exist, so the hash layout stays under Kernel control.
    private protected Column(string name, int length) : base(name)
    {
        _current = new T[length];
        _next = new T[length];
    }

    public override int Length => _current.Length;

    public ReadOnlySpan<T> Current => _current;

    /// <summary>
    /// The state at t+1, opened on the first touch of a tick: <c>Current</c> is copied in (copy-on-write, ADR-0017 п. 10-13).
    /// Take the span once per chunk, not per element. Outside a tick it throws: set the state with <see cref="Load"/>.
    /// </summary>
    public Span<T> Next
    {
        get
        {
            if (Owner is { InCompute: true })
                throw new InvalidOperationException($"Column '{Name}': Next of a child table is not allowed in compute; use NextRange (ADR-0029 п. 5).");
            return NextCore();
        }
    }

    private Span<T> NextCore()
    {
        if (!Volatile.Read(ref _open))
            Open();
        return _next;
    }

    /// <summary>
    /// Child tables: exactly the rows of this chunk's parent rows, [offset[rowStart], offset[rowEnd]) (ADR-0029 п. 5). Pass the context of the
    /// system whose chunk table is the parent. Empty for an empty parent. Opens <c>Next</c> if it is not open yet; no allocation.
    /// </summary>
    public Span<T> NextRange(in TickContext context, int chunk)
    {
        Table? parent = Owner?.Parent;
        if (parent is null || Owner!.Offsets is not { } offsets)
            throw new InvalidOperationException($"Column '{Name}': NextRange needs a column of a child table.");
        if (!ReferenceEquals(context.Table, parent))
            throw new InvalidOperationException($"Column '{Name}': the chunk table of the context is not the parent of its table.");
        if (context.ChunkSize < 1)
            throw new InvalidOperationException("The context has no chunk size.");
        if (parent.RowCount == 0)
            return Span<T>.Empty;
        long start = (long)chunk * context.ChunkSize;
        if (chunk < 0 || start >= parent.RowCount)
            throw new ArgumentOutOfRangeException(nameof(chunk));
        int end = (int)Math.Min(start + context.ChunkSize, parent.RowCount);
        int lo = offsets[(int)start];
        return NextCore().Slice(lo, offsets[end] - lo);
    }

    // Idempotent: whichever thread opens the column, Next equals Current until the first write, and a writer
    // only gets the span after the copy is done (release/acquire on _open). The result does not depend on thread timing.
    private void Open()
    {
        if (Owner is { InTick: false })
            throw new InvalidOperationException($"Column '{Name}': Next is only available inside a tick; use Load to set the state outside a tick.");
        lock (_openLock)
        {
            if (Volatile.Read(ref _open))
                return;
            Owner?.Trace?.Record(Owner.TraceSystemId, Owner.TraceName is null ? Name : Owner.TraceName + "." + Name);
            if (!_copied)
            {
                _current.CopyTo(_next, 0);
                _copied = true;
            }
            // With a trace on, _open stays false so every Next access is recorded.
            if (Owner?.Trace is null)
                Volatile.Write(ref _open, true);
        }
    }

    /// <summary>Sets the current state directly. Only for setup and loading, never inside a tick (it throws there, ADR-0017 п. 12).</summary>
    public void Load(ReadOnlySpan<T> values)
    {
        if (Owner is { InTick: true })
            throw new InvalidOperationException($"Column '{Name}': Load is not allowed inside a tick.");
        if (values.Length != _current.Length)
            throw new ArgumentException($"Column '{Name}' expects {_current.Length} values, got {values.Length}.", nameof(values));
        values.CopyTo(_current);
        BumpVersion();
        Owner?.BumpVersion();
    }

    internal override void OpenNext()
    {
        if (!Volatile.Read(ref _open))
            Open();
    }

    internal override void Close()
    {
        _copied = false;
        Volatile.Write(ref _open, false);
    }

    internal override void CopyCurrentToNext()
    {
        _current.CopyTo(_next, 0);
        _copied = true;
        Volatile.Write(ref _open, Owner?.Trace is null);
    }

    internal override bool SwapIfOpen()
    {
        if (!_copied)
            return false;
        (_current, _next) = (_next, _current);
        BumpVersion();
        _copied = false;
        Volatile.Write(ref _open, false);
        return true;
    }

    private protected abstract int ElementSize { get; }

    private protected abstract void WriteLittleEndian(Span<byte> destination, T value);

    private protected abstract T ReadLittleEndian(ReadOnlySpan<byte> source);

    internal override int ElementBytes => ElementSize;

    internal override void WriteCurrentTo(Span<byte> destination)
    {
        for (int i = 0; i < _current.Length; i++)
            WriteLittleEndian(destination.Slice(i * ElementSize, ElementSize), _current[i]);
    }

    internal override void LoadFrom(ReadOnlySpan<byte> source)
    {
        var values = new T[_current.Length];
        for (int i = 0; i < values.Length; i++)
            values[i] = ReadLittleEndian(source.Slice(i * ElementSize, ElementSize));
        Load(values);
    }

    internal override void AppendCurrentTo(IncrementalHash hash)
    {
        // Little-endian host: the memory is already the hashed byte layout, one call (the same bytes as the element-wise path).
        if (BitConverter.IsLittleEndian)
            hash.AppendData(MemoryMarshal.AsBytes(_current.AsSpan()));
        else
            AppendCurrentElementwise(hash);
    }

    /// <summary>The portable path: element-wise little-endian. Kept for big-endian hosts and as the reference of the fast path.</summary>
    internal void AppendCurrentElementwise(IncrementalHash hash)
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

    private protected override int ElementSize => sizeof(long);

    internal override char TypeTag => 'l';

    private protected override long ReadLittleEndian(ReadOnlySpan<byte> source) => BinaryPrimitives.ReadInt64LittleEndian(source);

    private protected override void WriteLittleEndian(Span<byte> destination, long value) =>
        BinaryPrimitives.WriteInt64LittleEndian(destination, value);
}

public sealed class IntColumn : Column<int>
{
    internal IntColumn(string name, int length) : base(name, length) { }

    private protected override int ElementSize => sizeof(int);

    internal override char TypeTag => 'i';

    private protected override int ReadLittleEndian(ReadOnlySpan<byte> source) => BinaryPrimitives.ReadInt32LittleEndian(source);

    private protected override void WriteLittleEndian(Span<byte> destination, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination, value);
}
