using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

public class StateHashTests
{
    // Reference hashes, cross-checked by tools/reference/kernel_vectors.py.
    private const string FixedTableHash = "1a3cba714ac0f01aacdf4116c845ef1c6a0ad14c602e554252ac6ca6d02f9367";
    private const string EmptyTableHash = "7c9fa136d4413fa6173637e883b6998d32e1d675f88cddff9dcbcf331820f4b8";

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
        HashAssert.Equal(FixedTableHash, StateHash.ComputeHex(FixedTable(), schemaVersion: 1));
    }

    [Fact]
    public void EmptyTable_HashesHeaderOnly()
    {
        HashAssert.Equal(EmptyTableHash, StateHash.ComputeHex(new Table(0), schemaVersion: 1));
    }

    [Fact]
    public void SchemaVersionAndValues_AffectTheHash()
    {
        string baseline = StateHash.ComputeHex(FixedTable(), 1);
        HashAssert.NotEqual(baseline, StateHash.ComputeHex(FixedTable(), 2));

        var changed = FixedTable();
        changed.GetInt("level").Load([0, 1, -1, int.MaxValue]);
        HashAssert.NotEqual(baseline, StateHash.ComputeHex(changed, 1));
    }

    [Fact]
    public void Hash_DoesNotDependOnTick()
    {
        // The tick is not an argument at all (ADR-0006/0013); a runner that advances with an unchanged table keeps the hash.
        var table = new Table(2);
        table.AddLong("x").Load([5, 6]);
        var runner = new TickRunner(table, 1, [], 2, 1);
        string before = StateHash.ComputeHex(table, 1);
        runner.RunTick();
        runner.RunTick();
        Assert.Equal(2, runner.Tick);
        HashAssert.Equal(before, StateHash.ComputeHex(table, 1));
    }

    [Fact]
    public void HashesCurrentNotNext()
    {
        var table = FixedTable();
        table.CopyCurrentToNext();
        table.GetLong("stock").Next[0] = 999;
        HashAssert.Equal(FixedTableHash, StateHash.ComputeHex(table, 1));
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
        bytes.AddRange(BitConverter.GetBytes(rows));
        foreach (long v in values)
            bytes.AddRange(BitConverter.GetBytes(v));
        Assert.True(BitConverter.IsLittleEndian, "Expected bytes are built with the host byte order.");

        string expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes.ToArray()));
        HashAssert.Equal(expected, StateHash.ComputeHex(table, 3));
    }
}
