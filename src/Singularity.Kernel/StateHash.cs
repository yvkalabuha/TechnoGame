using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Singularity.Kernel;

/// <summary>
/// SHA-256 of the current state of a table (L1/L2 determinism checks, ADR-0002).
/// Byte layout, all little-endian:
/// header = int32 schema_version, int32 n_rows (the tick is NOT hashed: ADR-0006/0013, it is a separate field);
/// then every column's <c>Current</c> values in registration order (int64 or int32 each).
/// Column names are not hashed: the schema version stands for the column layout.
/// </summary>
public static class StateHash
{
    public static byte[] Compute(Table table, int schemaVersion)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        Span<byte> header = stackalloc byte[sizeof(int) + sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, schemaVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], table.RowCount);
        hash.AppendData(header);

        for (int i = 0; i < table.Columns.Count; i++)
            table.Columns[i].AppendCurrentTo(hash);

        return hash.GetHashAndReset();
    }

    public static string ComputeHex(Table table, int schemaVersion) =>
        Convert.ToHexStringLower(Compute(table, schemaVersion));
}
