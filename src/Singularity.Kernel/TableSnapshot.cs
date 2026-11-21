using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Singularity.Kernel;

/// <summary>
/// In-memory snapshot of a table (ADR-0013 v0: binary SoA + header). Not a player save file: no versions to migrate, no compression.
/// Layout, all little-endian: magic "SGTS", int32 format (2), int32 schema_version, int32 n_rows, int32 n_columns, int64 tick,
/// 32 bytes schema fingerprint, 32 bytes state hash (the SHA-256 of <see cref="StateHash"/>: schema version, rows, the column values), then the <c>Current</c> values of every column in registration order (int64 or int32 each), then a 32-byte SHA-256 digest of every byte before it.
/// The digest is checked first, so a flipped byte anywhere is refused before any field is parsed.
/// The fingerprint covers column names, types and row count, so a snapshot of another layout is refused even under the same schema version.
/// </summary>
public static class TableSnapshot
{
    private const int Format = 2;

    /// <summary>Size of the trailing digest over the whole snapshot.</summary>
    public const int DigestBytes = 32;
    private const int FingerprintBytes = 32;
    private static ReadOnlySpan<byte> Magic => "SGTS"u8;
    private const int StateHashOffset = 4 + 4 + 4 + 4 + 4 + 8 + FingerprintBytes;

    /// <summary>Size of the header in bytes; the column values follow.</summary>
    public const int HeaderBytes = StateHashOffset + 32;

    /// <summary>SHA-256 of the column names and types in table order and the row count.</summary>
    public static byte[] Fingerprint(Table table)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int i = 0; i < table.Columns.Count; i++)
        {
            Column c = table.Columns[i];
            // Length-prefixed (so no name can run into its neighbour) and written as raw UTF-16 code units: UTF-8 would turn every
            // lone surrogate into U+FFFD and give two different names the same bytes.
            string name = c.Name;
            var bytes = new byte[sizeof(int) + name.Length * sizeof(char) + 1];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, name.Length);
            for (int j = 0; j < name.Length; j++)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(sizeof(int) + j * sizeof(char)), name[j]);
            bytes[^1] = (byte)c.TypeTag;
            hash.AppendData(bytes);
        }
        Span<byte> rows = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(rows, table.RowCount);
        hash.AppendData(rows);
        return hash.GetHashAndReset();
    }

    /// <summary>Exact size in bytes of a snapshot of this table.</summary>
    public static int ExpectedBytes(Table table) => checked(HeaderBytes + BodyBytes(table) + DigestBytes);

    /// <summary>
    /// Checks a whole snapshot against the table (length, magic, format, schema version, counts, fingerprint, tick) without changing anything
    /// and returns its tick. <see cref="Read"/> does the same checks while reading.
    /// </summary>
    public static long Inspect(Table table, int schemaVersion, ReadOnlySpan<byte> snapshot)
    {
        if (snapshot.Length != ExpectedBytes(table))
            throw new InvalidDataException($"Snapshot is {snapshot.Length} bytes, the table needs {ExpectedBytes(table)}.");
        if (!SHA256.HashData(snapshot[..^DigestBytes]).AsSpan().SequenceEqual(snapshot[^DigestBytes..]))
            throw new InvalidDataException("Snapshot digest does not match its bytes.");
        long tick = ValidateHeader(table, schemaVersion, snapshot[..HeaderBytes]);
        VerifyBody(table, schemaVersion, snapshot[..HeaderBytes], snapshot[HeaderBytes..^DigestBytes]);
        return tick;
    }

    /// <summary>The state hash a snapshot carries (hex, the form of <see cref="StateHash.ComputeHex"/>); call <see cref="Inspect"/> first.</summary>
    public static string StoredStateHash(ReadOnlySpan<byte> snapshot) =>
        Convert.ToHexStringLower(snapshot.Slice(StateHashOffset, 32));

    private static byte[] HashOfBody(int schemaVersion, int rows, ReadOnlySpan<byte> body)
    {
        // The same bytes as StateHash.Compute: header (schema version, rows), then every column's values little-endian in order.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> head = stackalloc byte[sizeof(int) + sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(head, schemaVersion);
        BinaryPrimitives.WriteInt32LittleEndian(head[4..], rows);
        hash.AppendData(head);
        hash.AppendData(body);
        return hash.GetHashAndReset();
    }

    private static void VerifyBody(Table table, int schemaVersion, ReadOnlySpan<byte> header, ReadOnlySpan<byte> body)
    {
        if (!HashOfBody(schemaVersion, table.RowCount, body).AsSpan().SequenceEqual(header.Slice(StateHashOffset, 32)))
            throw new InvalidDataException("Snapshot state hash differs from its column values.");
    }

    /// <summary>Writes the <c>Current</c> state of every column, the schema version and the tick. Outside a tick only.</summary>
    public static void Write(Table table, int schemaVersion, long tick, Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        if (table.InTick || table.RunActive)
            throw new InvalidOperationException("A snapshot is taken between ticks.");
        int columns = table.Columns.Count;
        var buffer = new byte[ExpectedBytes(table)];
        Span<byte> b = buffer;
        Magic.CopyTo(b);
        BinaryPrimitives.WriteInt32LittleEndian(b[4..], Format);
        BinaryPrimitives.WriteInt32LittleEndian(b[8..], schemaVersion);
        BinaryPrimitives.WriteInt32LittleEndian(b[12..], table.RowCount);
        BinaryPrimitives.WriteInt32LittleEndian(b[16..], columns);
        BinaryPrimitives.WriteInt64LittleEndian(b[20..], tick);
        Fingerprint(table).CopyTo(b[28..]);
        int at = HeaderBytes;
        for (int i = 0; i < columns; i++)
        {
            Column c = table.Columns[i];
            int size = c.Length * c.ElementBytes;
            c.WriteCurrentTo(b.Slice(at, size));
            at += size;
        }
        HashOfBody(schemaVersion, table.RowCount, b[HeaderBytes..^DigestBytes]).CopyTo(b[StateHashOffset..]);
        SHA256.HashData(b[..^DigestBytes]).CopyTo(b[^DigestBytes..]);
        output.Write(buffer);
    }

    /// <summary>
    /// Reads a snapshot into <paramref name="table"/> and returns its tick. The table must have the same schema version and fingerprint;
    /// everything is checked before the first column is replaced, so a refused snapshot leaves the table unchanged. Outside a tick only.
    /// </summary>
    public static long Read(Table table, int schemaVersion, Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (table.InTick || table.RunActive)
            throw new InvalidOperationException("A snapshot is loaded between ticks.");
        // The size comes from the table, never from the stream; the digest over everything before it is checked before any field is read.
        var all = new byte[ExpectedBytes(table)];
        input.ReadExactly(all);
        if (input.ReadByte() >= 0)
            throw new InvalidDataException("Snapshot has data after the digest.");
        long tick = Inspect(table, schemaVersion, all);
        ReadOnlySpan<byte> body = all.AsSpan(HeaderBytes, all.Length - HeaderBytes - DigestBytes);
        int at = 0;
        for (int i = 0; i < table.Columns.Count; i++)
        {
            Column c = table.Columns[i];
            int size = c.Length * c.ElementBytes;
            c.LoadFrom(body.Slice(at, size));
            at += size;
        }
        return tick;
    }

    private static long ValidateHeader(Table table, int schemaVersion, ReadOnlySpan<byte> h)
    {
        if (!h[..4].SequenceEqual(Magic))
            throw new InvalidDataException("Not a table snapshot (bad magic).");
        int format = BinaryPrimitives.ReadInt32LittleEndian(h[4..]);
        if (format != Format)
            throw new InvalidDataException($"Snapshot format {format} is not supported (expected {Format}).");
        int version = BinaryPrimitives.ReadInt32LittleEndian(h[8..]);
        if (version != schemaVersion)
            throw new InvalidDataException($"Snapshot schema version {version} differs from {schemaVersion}.");
        if (BinaryPrimitives.ReadInt32LittleEndian(h[12..]) != table.RowCount || BinaryPrimitives.ReadInt32LittleEndian(h[16..]) != table.Columns.Count)
            throw new InvalidDataException("Snapshot row or column count differs from the table.");
        if (!h.Slice(28, FingerprintBytes).SequenceEqual(Fingerprint(table)))
            throw new InvalidDataException("Snapshot schema fingerprint differs from the table.");
        long tick = BinaryPrimitives.ReadInt64LittleEndian(h[20..]);
        if (tick < 0)
            throw new InvalidDataException($"Snapshot tick {tick} is negative.");
        return tick;
    }

    private static int BodyBytes(Table table)
    {
        long total = 0;
        for (int i = 0; i < table.Columns.Count; i++)
            total += (long)table.Columns[i].Length * table.Columns[i].ElementBytes;
        return checked((int)total);
    }
}
