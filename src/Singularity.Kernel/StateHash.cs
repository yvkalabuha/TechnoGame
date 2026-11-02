using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Singularity.Kernel;

/// <summary>
/// SHA-256 of the current state of a table (L1/L2 determinism checks, ADR-0002).
/// Byte layout, all little-endian:
/// header = int32 schema_version, int64 tick, int32 n_rows;
/// then every column's <c>Current</c> values in registration order (int64 or int32 each).
/// Column names are not hashed: the schema version stands for the column layout.
/// </summary>
public static class StateHash
{
    public static byte[] Compute(Table table, int schemaVersion, long tick)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        Span<byte> header = stackalloc byte[sizeof(int) + sizeof(long) + sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, schemaVersion);
        BinaryPrimitives.WriteInt64LittleEndian(header[4..], tick);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], table.RowCount);
        hash.AppendData(header);

        for (int i = 0; i < table.Columns.Count; i++)
            table.Columns[i].AppendCurrentTo(hash);

        return hash.GetHashAndReset();
    }

    public static string ComputeHex(Table table, int schemaVersion, long tick) =>
        Convert.ToHexStringLower(Compute(table, schemaVersion, tick));
}
