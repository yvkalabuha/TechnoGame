using System.Security.Cryptography;
using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

/// <summary>TASK-0068 F8: the bulk hash path feeds the same bytes as the element-wise little-endian path.</summary>
public class ColumnHashPathTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(1025)]
    [InlineData(5000)]
    public void BulkPath_EqualsElementwisePath(int length)
    {
        var table = new Table(length);
        LongColumn l = table.AddLong("l");
        IntColumn i = table.AddInt("i");
        long[] lv = new long[length];
        int[] iv = new int[length];
        for (int k = 0; k < length; k++)
        {
            lv[k] = k % 3 == 0 ? long.MinValue + k : unchecked((long)k * (long)0x1E3779B97F4A7C15L);
            iv[k] = k % 2 == 0 ? int.MaxValue - k : -k * 7919;
        }
        l.Load(lv);
        i.Load(iv);
        foreach (Column col in new Column[] { l, i })
        {
            using var bulk = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var slow = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            col.AppendCurrentTo(bulk);
            if (col is LongColumn lc)
                lc.AppendCurrentElementwise(slow);
            else
                ((IntColumn)col).AppendCurrentElementwise(slow);
            Assert.Equal(slow.GetHashAndReset(), bulk.GetHashAndReset());
        }
    }
}
