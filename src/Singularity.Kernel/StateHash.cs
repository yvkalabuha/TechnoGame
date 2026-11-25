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

    /// <summary>
    /// World hash: header int32 schema_version, int32 table count; then per table in creation order int32 n_rows and its columns as above.
    /// Names are not hashed (the schema version stands for the layout).
    /// </summary>
    public static byte[] Compute(World world, int schemaVersion)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> word = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(word, schemaVersion);
        hash.AppendData(word);
        BinaryPrimitives.WriteInt32LittleEndian(word, world.Tables.Count);
        hash.AppendData(word);
        foreach (Table table in world.Tables)
        {
            BinaryPrimitives.WriteInt32LittleEndian(word, table.RowCount);
            hash.AppendData(word);
            for (int i = 0; i < table.Columns.Count; i++)
                table.Columns[i].AppendCurrentTo(hash);
        }
        return hash.GetHashAndReset();
    }

    public static string ComputeHex(World world, int schemaVersion) =>
        Convert.ToHexStringLower(Compute(world, schemaVersion));

    public static string ComputeHex(Table table, int schemaVersion) =>
        Convert.ToHexStringLower(Compute(table, schemaVersion));
}
