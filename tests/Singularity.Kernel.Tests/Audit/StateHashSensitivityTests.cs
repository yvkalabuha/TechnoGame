using Singularity.Kernel;

namespace Singularity.Kernel.Tests.Audit;

public class StateHashSensitivityTests
{
    private static Table Build(int rows, Action<Table>? mutate = null)
    {
        var t = new Table(rows);
        var a = t.AddLong("a");
        var b = t.AddInt("b");
        var c = t.AddLong("c");
        a.Load(Enumerable.Range(0, rows).Select(i => (long)i * 31).ToArray());
        b.Load(Enumerable.Range(0, rows).Select(i => i * 7 - 3).ToArray());
        c.Load(Enumerable.Range(0, rows).Select(i => -(long)i).ToArray());
        mutate?.Invoke(t);
        return t;
    }

    [Fact]
    public void EverySingleBitInEveryCellOfEveryColumn_ChangesTheHash()
    {
        const int rows = 6;
        string baseline = StateHash.ComputeHex(Build(rows), 1, 10);
        var seen = new HashSet<string> { baseline };
        for (int row = 0; row < rows; row++)
        {
            foreach (int bit in new[] { 0, 7, 8, 31, 32, 63 })
            {
                int r = row, bb = bit;
                seen.Add(Changed(t => { var v = t.GetLong("a").Current.ToArray(); v[r] ^= 1L << bb; t.GetLong("a").Load(v); }));
                seen.Add(Changed(t => { var v = t.GetLong("c").Current.ToArray(); v[r] ^= 1L << bb; t.GetLong("c").Load(v); }));
                if (bit < 32)
                    seen.Add(Changed(t => { var v = t.GetInt("b").Current.ToArray(); v[r] ^= 1 << bb; t.GetInt("b").Load(v); }));
            }
        }
        // Each mutation must give a distinct hash (no two single-bit flips collide, none equals baseline).
        Assert.Equal(1 + rows * (6 + 6 + 4), seen.Count);

        string Changed(Action<Table> m)
        {
            string h = StateHash.ComputeHex(Build(rows, m), 1, 10);
            Assert.NotEqual(baseline, h);
            return h;
        }
    }

    [Fact]
    public void Tick_SchemaVersion_RowCount_AllAffectTheHash()
    {
        var t = Build(4);
        var hashes = new HashSet<string>();
        for (long tick = -2; tick <= 2; tick++)
            hashes.Add(StateHash.ComputeHex(t, 1, tick));
        hashes.Add(StateHash.ComputeHex(t, 1, long.MaxValue));
        hashes.Add(StateHash.ComputeHex(t, 1, long.MinValue));
        hashes.Add(StateHash.ComputeHex(t, 1, 1L << 32)); // high half of tick is hashed
        hashes.Add(StateHash.ComputeHex(t, 0, 0));
        hashes.Add(StateHash.ComputeHex(t, -1, 0));
        Assert.Equal(10, hashes.Count);
        Assert.NotEqual(StateHash.ComputeHex(new Table(0), 1, 0), StateHash.ComputeHex(new Table(1), 1, 0));
    }

    [Fact]
    public void ColumnRegistrationOrder_AffectsTheHash()
    {
        var t1 = new Table(2); t1.AddLong("x").Load([1, 2]); t1.AddLong("y").Load([3, 4]);
        var t2 = new Table(2); t2.AddLong("y").Load([3, 4]); t2.AddLong("x").Load([1, 2]);
        Assert.NotEqual(StateHash.ComputeHex(t1, 1, 0), StateHash.ComputeHex(t2, 1, 0));
    }

    [Fact]
    public void SwappingTwoRows_ChangesTheHash()
    {
        var t1 = new Table(2); t1.AddLong("x").Load([1, 2]);
        var t2 = new Table(2); t2.AddLong("x").Load([2, 1]);
        Assert.NotEqual(StateHash.ComputeHex(t1, 1, 0), StateHash.ComputeHex(t2, 1, 0));
    }

    [Fact]
    public void KnownLimitation_NamesAndTypesAreNotHashed()
    {
        // By design (StateHash doc): layout is covered by schema_version only.
        // One long column == two int columns with the same bytes; renaming a column is invisible.
        var longTable = new Table(1); longTable.AddLong("v").Load([(2L << 32) | 1L]);
        var intTable = new Table(1); intTable.AddInt("lo").Load([1]); intTable.AddInt("hi").Load([2]);
        Assert.Equal(StateHash.ComputeHex(longTable, 1, 0), StateHash.ComputeHex(intTable, 1, 0));

        var renamed = new Table(1); renamed.AddLong("other").Load([(2L << 32) | 1L]);
        Assert.Equal(StateHash.ComputeHex(longTable, 1, 0), StateHash.ComputeHex(renamed, 1, 0));
    }

    [Fact]
    public void Hash_IsStableAcrossCallsAndBatchBoundaries()
    {
        // 1024 longs = exactly one 8 KiB batch; 1025 = one batch + 1; 2048 int = one batch.
        foreach (int rows in new[] { 1023, 1024, 1025, 2047, 2048, 2049 })
        {
            var t = new Table(rows);
            t.AddLong("l").Load(Enumerable.Range(0, rows).Select(i => (long)i * i).ToArray());
            t.AddInt("i").Load(Enumerable.Range(0, rows).Select(i => -i).ToArray());
            var bytes = new List<byte>();
            bytes.AddRange(BitConverter.GetBytes(1));
            bytes.AddRange(BitConverter.GetBytes(9L));
            bytes.AddRange(BitConverter.GetBytes(rows));
            for (int i = 0; i < rows; i++) bytes.AddRange(BitConverter.GetBytes((long)i * i));
            for (int i = 0; i < rows; i++) bytes.AddRange(BitConverter.GetBytes(-i));
            string expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes.ToArray()));
            Assert.Equal(expected, StateHash.ComputeHex(t, 1, 9));
            Assert.Equal(expected, StateHash.ComputeHex(t, 1, 9));
        }
    }
}
