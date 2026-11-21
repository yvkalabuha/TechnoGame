using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Singularity.Kernel;

/// <summary>
/// In-memory snapshot of a table (ADR-0013 v0: binary SoA + header). Not a player save file: no versions to migrate, no compression.
/// Layout, all little-endian: magic "SGTS", int32 format (0), int32 schema_version, int32 n_rows, int32 n_columns, int64 tick,
/// 32 bytes schema fingerprint, then the <c>Current</c> values of every column in registration order (int64 or int32 each).
/// The fingerprint covers column names, types and row count, so a snapshot of another layout is refused even under the same schema version.
/// </summary>
public static class TableSnapshot
{
    private const int Format = 0;
    private const int FingerprintBytes = 32;
    private static ReadOnlySpan<byte> Magic => "SGTS"u8;
    private const int HeaderBytes = 4 + 4 + 4 + 4 + 4 + 8 + FingerprintBytes;

    /// <summary>SHA-256 of the column names and types in table order and the row count.</summary>
    public static byte[] Fingerprint(Table table)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int i = 0; i < table.Columns.Count; i++)
        {
            Column c = table.Columns[i];
            hash.AppendData(Encoding.UTF8.GetBytes(c.Name + ":" + c.TypeTag + "\n"));
        }
        Span<byte> rows = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(rows, table.RowCount);
        hash.AppendData(rows);
        return hash.GetHashAndReset();
    }

    /// <summary>Writes the <c>Current</c> state of every column, the schema version and the tick. Outside a tick only.</summary>
    public static void Write(Table table, int schemaVersion, long tick, Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (table.InTick)
            throw new InvalidOperationException("A snapshot is taken between ticks.");
        int columns = table.Columns.Count;
        var buffer = new byte[HeaderBytes + BodyBytes(table)];
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
        output.Write(buffer);
    }

    /// <summary>
    /// Reads a snapshot into <paramref name="table"/> and returns its tick. The table must have the same schema version and fingerprint;
    /// everything is checked before the first column is replaced, so a refused snapshot leaves the table unchanged. Outside a tick only.
    /// </summary>
    public static long Read(Table table, int schemaVersion, Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (table.InTick)
            throw new InvalidOperationException("A snapshot is loaded between ticks.");
        var header = new byte[HeaderBytes];
        input.ReadExactly(header);
        ReadOnlySpan<byte> h = header;
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

        var body = new byte[BodyBytes(table)];
        input.ReadExactly(body);
        if (input.ReadByte() >= 0)
            throw new InvalidDataException("Snapshot has data after the last column.");
        int at = 0;
        for (int i = 0; i < table.Columns.Count; i++)
        {
            Column c = table.Columns[i];
            int size = c.Length * c.ElementBytes;
            c.LoadFrom(body.AsSpan(at, size));
            at += size;
        }
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
