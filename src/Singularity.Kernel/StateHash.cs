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
    /// World hash (ADR-0020 п. 7), all little-endian: int32 table_count; then per table in creation order: name (int32 UTF-8 length, bytes),
    /// int32 <see cref="Table.SchemaVersion"/>, int32 n_rows, int32 n_cols, columns in registration order. The single-table hash above is unchanged.
    /// </summary>
    public static byte[] Compute(World world)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> word = stackalloc byte[sizeof(int)];
        AppendInt(hash, word, world.Tables.Count);
        for (int t = 0; t < world.Tables.Count; t++)
        {
            Table table = world.Tables[t];
            byte[] name = System.Text.Encoding.UTF8.GetBytes(world.Names[t]);
            AppendInt(hash, word, name.Length);
            hash.AppendData(name);
            AppendInt(hash, word, table.SchemaVersion);
            AppendInt(hash, word, table.RowCount);
            AppendInt(hash, word, table.Columns.Count);
            for (int i = 0; i < table.Columns.Count; i++)
                table.Columns[i].AppendCurrentTo(hash);
        }
        return hash.GetHashAndReset();
    }

    public static string ComputeHex(World world) => Convert.ToHexStringLower(Compute(world));

    private static void AppendInt(IncrementalHash hash, Span<byte> word, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(word, value);
        hash.AppendData(word);
    }

    public static string ComputeHex(Table table, int schemaVersion) =>
        Convert.ToHexStringLower(Compute(table, schemaVersion));
}
