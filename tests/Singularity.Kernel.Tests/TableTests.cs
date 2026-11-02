using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

public class TableTests
{
    [Fact]
    public void Columns_KeepRegistrationOrder()
    {
        var table = new Table(3);
        var b = table.AddLong("b");
        var a = table.AddInt("a");
        Assert.Equal(["b", "a"], table.Columns.Select(c => c.Name));
        Assert.Same(b, table.GetLong("b"));
        Assert.Same(a, table.GetInt("a"));
    }

    [Fact]
    public void Register_RejectsDuplicatesAndLookupChecksType()
    {
        var table = new Table(1);
        table.AddLong("x");
        Assert.Throws<ArgumentException>(() => table.AddInt("x"));
        Assert.Throws<ArgumentException>(() => table.AddLong(" "));
        Assert.Throws<InvalidCastException>(() => table.GetInt("x"));
        Assert.Throws<KeyNotFoundException>(() => table.GetLong("missing"));
    }

    [Fact]
    public void Load_RequiresExactLength()
    {
        var column = new Table(2).AddLong("x");
        Assert.Throws<ArgumentException>(() => column.Load([1, 2, 3]));
    }

    [Fact]
    public void Swap_ExchangesBuffersAndCopyCarriesValuesOver()
    {
        var table = new Table(2);
        var x = table.AddLong("x");
        x.Load([10, 20]);

        table.CopyCurrentToNext();
        x.Next[1] = 21;
        Assert.Equal([10, 20], x.Current.ToArray()); // next is not visible before the swap

        table.Swap();
        Assert.Equal([10, 21], x.Current.ToArray());
    }
}
