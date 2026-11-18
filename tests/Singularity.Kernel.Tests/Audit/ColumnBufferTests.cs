using Singularity.Kernel;

namespace Singularity.Kernel.Tests.Audit;

/// <summary>Double-buffer misuse and contract edges for Column/Table.</summary>
public class ColumnBufferTests
{
    [Fact]
    public void NextOutsideATick_Throws_LoadIsTheOnlyWayToWriteState()
    {
        var table = new Table(3);
        var x = table.AddLong("x");
        x.Load([1, 2, 3]);
        Assert.Throws<InvalidOperationException>(() => x.Next[0] = 999);
        new TickRunner(table, 0, [], chunkSize: 2, maxThreads: 1).RunTick();
        Assert.Equal([1, 2, 3], x.Current.ToArray());
    }

    [Fact]
    public void SwapOfUnopenedColumns_ChangesNothing_AndKeepsTheVersion()
    {
        // Copy-on-write (ADR-0017): a column nobody touched is not swapped, so a bare swap never shows a stale buffer.
        var table = new Table(2);
        var x = table.AddInt("x");
        x.Load([5, 6]);
        long version = table.Version;
        table.Swap();
        Assert.Equal([5, 6], x.Current.ToArray());
        Assert.Equal(version, table.Version);
    }

    [Fact]
    public void UnwrittenColumns_CarryOverAcrossManyTicks()
    {
        var table = new Table(5);
        var x = table.AddLong("x");
        var y = table.AddInt("y");
        x.Load([1, -2, 3, long.MinValue, long.MaxValue]);
        y.Load([int.MinValue, 0, 1, 2, int.MaxValue]);
        var runner = new TickRunner(table, 1, [], chunkSize: 2, maxThreads: 4);
        string before = StateHash.ComputeHex(table, 1);
        for (int i = 0; i < 7; i++)
            runner.RunTick();
        HashAssert.Equal(before, StateHash.ComputeHex(table, 1));
    }

    [Fact]
    public void Load_DoesNotAliasCallerArray()
    {
        var table = new Table(2);
        var x = table.AddLong("x");
        long[] source = [10, 20];
        x.Load(source);
        source[0] = -1;
        Assert.Equal(10, x.Current[0]);
    }

    [Fact]
    public void Load_RejectsShortInput_AndZeroRowColumnsWork()
    {
        Assert.Throws<ArgumentException>(() => new Table(3).AddInt("x").Load([1, 2]));
        var empty = new Table(0);
        var c = empty.AddLong("x");
        c.Load([]);
        Assert.Equal(0, c.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Table(-1));
    }

    [Fact]
    public void ColumnNames_AreCaseSensitiveOrdinal()
    {
        var table = new Table(1);
        table.AddLong("Stock");
        table.AddLong("stock"); // distinct under ordinal comparison
        Assert.Equal(2, table.Columns.Count);
        Assert.Throws<ArgumentNullException>(() => table.AddLong(null!));
        Assert.Throws<ArgumentException>(() => table.AddLong(""));
    }
}
