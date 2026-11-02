using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

public class StateHashTests
{
    // Reference hashes, cross-checked by tools/reference/kernel_vectors.py.
    private const string FixedTableHash = "cb10cdbf139ed51f9d5653b9dae64bfb831046d9637b8f0a4348fb3a52e62d6f";
    private const string EmptyTableHash = "4cbbd8ca5215b8d161aec181a74b694f4e24b001d5b081dc0030ed797a8973e0";

    private static Table FixedTable()
    {
        var table = new Table(4);
        table.AddLong("stock").Load([0, 1, -1, long.MaxValue]);
        table.AddInt("level").Load([0, 1, -1, int.MinValue]);
        return table;
    }

    [Fact]
    public void FixedTable_MatchesReference()
    {
        Assert.Equal(FixedTableHash, StateHash.ComputeHex(FixedTable(), schemaVersion: 1, tick: 1910));
    }

    [Fact]
    public void EmptyTable_HashesHeaderOnly()
    {
        Assert.Equal(EmptyTableHash, StateHash.ComputeHex(new Table(0), schemaVersion: 1, tick: 0));
    }

    [Fact]
    public void HeaderFieldsAndValues_AllAffectTheHash()
    {
        string baseline = StateHash.ComputeHex(FixedTable(), 1, 1910);
        Assert.NotEqual(baseline, StateHash.ComputeHex(FixedTable(), 2, 1910));
        Assert.NotEqual(baseline, StateHash.ComputeHex(FixedTable(), 1, 1911));

        var changed = FixedTable();
        changed.GetInt("level").Load([0, 1, -1, int.MaxValue]);
        Assert.NotEqual(baseline, StateHash.ComputeHex(changed, 1, 1910));
    }

    [Fact]
    public void HashesCurrentNotNext()
    {
        var table = FixedTable();
        table.CopyCurrentToNext();
        table.GetLong("stock").Next[0] = 999;
        Assert.Equal(FixedTableHash, StateHash.ComputeHex(table, 1, 1910));
    }

    [Fact]
    public void LargeColumn_IsHashedAcrossBufferBatches()
    {
        // More rows than one 8 KiB hashing batch; must equal hashing the same bytes in one go.
        const int rows = 5000;
        var table = new Table(rows);
        var values = new long[rows];
        for (int i = 0; i < rows; i++)
            values[i] = i * 1_000_003L - 7;
        table.AddLong("x").Load(values);

        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(3));
        bytes.AddRange(BitConverter.GetBytes(5L));
        bytes.AddRange(BitConverter.GetBytes(rows));
        foreach (long v in values)
            bytes.AddRange(BitConverter.GetBytes(v));
        Assert.True(BitConverter.IsLittleEndian, "Expected bytes are built with the host byte order.");

        string expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes.ToArray()));
        Assert.Equal(expected, StateHash.ComputeHex(table, 3, 5));
    }
}
