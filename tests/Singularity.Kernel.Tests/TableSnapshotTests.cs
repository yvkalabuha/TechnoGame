using System.Buffers.Binary;
using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

/// <summary>TASK-0124: the in-memory table snapshot (ADR-0013 v0) round-trips every column type and refuses what does not fit.</summary>
public class TableSnapshotTests
{
    private const int Schema = 5;

    private static Table Build(int rows, bool withExtra = false)
    {
        var t = new Table(rows);
        t.AddLong("a");
        t.AddInt("b");
        t.AddLong("c");
        if (withExtra)
            t.AddInt("d");
        return t;
    }

    private static byte[] Take(Table t, long tick)
    {
        using var s = new MemoryStream();
        TableSnapshot.Write(t, Schema, tick, s);
        return s.ToArray();
    }

    [Fact]
    public void RoundTrip_AllColumnTypes_SameHashAndTick()
    {
        Table src = Build(4);
        src.GetLong("a").Load([long.MinValue, -1, 0, long.MaxValue]);
        src.GetInt("b").Load([int.MinValue, -7, 0, int.MaxValue]);
        src.GetLong("c").Load([1, 2, 3, 4]);
        byte[] bytes = Take(src, 123);

        Table dst = Build(4);
        long tick = TableSnapshot.Read(dst, Schema, new MemoryStream(bytes));

        Assert.Equal(123, tick);
        HashAssert.Equal(StateHash.ComputeHex(src, Schema), StateHash.ComputeHex(dst, Schema));
        Assert.Equal(src.GetInt("b").Current.ToArray(), dst.GetInt("b").Current.ToArray());
    }

    [Fact]
    public void RoundTrip_EmptyTable()
    {
        Table src = Build(0);
        Table dst = Build(0);
        Assert.Equal(9, TableSnapshot.Read(dst, Schema, new MemoryStream(Take(src, 9))));
        HashAssert.Equal(StateHash.ComputeHex(src, Schema), StateHash.ComputeHex(dst, Schema));
    }

    [Fact]
    public void Read_ReplacesPriorState()
    {
        Table src = Build(2);
        Table dst = Build(2);
        dst.GetLong("a").Load([9, 9]);
        TableSnapshot.Read(dst, Schema, new MemoryStream(Take(src, 0)));
        Assert.Equal([0L, 0L], dst.GetLong("a").Current.ToArray());
    }

    [Fact]
    public void Read_OtherSchemaVersion_Rejected()
    {
        byte[] bytes = Take(Build(2), 0);
        Assert.Throws<InvalidDataException>(() => TableSnapshot.Read(Build(2), Schema + 1, new MemoryStream(bytes)));
    }

    [Fact]
    public void Read_OtherFingerprint_RejectedAndTableUnchanged()
    {
        // Same version, row count and column count; only the name differs.
        var other = new Table(2);
        other.AddLong("a");
        other.AddInt("b");
        other.AddLong("renamed");
        other.GetLong("a").Load([5, 6]);
        byte[] bytes = Take(other, 0);

        Table dst = Build(2);
        dst.GetLong("a").Load([1, 1]);
        Assert.Throws<InvalidDataException>(() => TableSnapshot.Read(dst, Schema, new MemoryStream(bytes)));
        Assert.Equal([1L, 1L], dst.GetLong("a").Current.ToArray());
    }

    [Fact]
    public void Read_OtherColumnType_Rejected()
    {
        var other = new Table(2);
        other.AddLong("a");
        other.AddLong("b");   // int in the target
        other.AddLong("c");
        Assert.Throws<InvalidDataException>(() => TableSnapshot.Read(Build(2), Schema, new MemoryStream(Take(other, 0))));
    }

    [Fact]
    public void Read_OtherRowOrColumnCount_Rejected()
    {
        Assert.Throws<InvalidDataException>(() => TableSnapshot.Read(Build(3), Schema, new MemoryStream(Take(Build(2), 0))));
        Assert.Throws<InvalidDataException>(() => TableSnapshot.Read(Build(2, withExtra: true), Schema, new MemoryStream(Take(Build(2), 0))));
    }

    [Fact]
    public void Read_TruncatedOrTrailingOrBadMagic_Rejected_TableUnchanged()
    {
        byte[] bytes = Take(Build(2), 0);
        Table dst = Build(2);
        dst.GetInt("b").Load([3, 3]);

        Assert.Throws<EndOfStreamException>(() => TableSnapshot.Read(dst, Schema, new MemoryStream(bytes[..^1])));
        Assert.Throws<InvalidDataException>(() => TableSnapshot.Read(dst, Schema, new MemoryStream([.. bytes, 0])));
        byte[] bad = (byte[])bytes.Clone();
        bad[0] ^= 1;
        Assert.Throws<InvalidDataException>(() => TableSnapshot.Read(dst, Schema, new MemoryStream(bad)));
        Assert.Equal([3, 3], dst.GetInt("b").Current.ToArray());
    }

    [Fact]
    public void Fingerprint_NamesThatWouldCollideWithoutLengthPrefix_Differ()
    {
        static Table T(string x, string y)
        {
            var t = new Table(1);
            t.AddLong(x);
            t.AddLong(y);
            return t;
        }
        // "a" + ":l\n" + "b:l\nc"  ==  "a:l\nb" + ":l\n" + "c" under the old name:type\n encoding.
        Assert.NotEqual(TableSnapshot.Fingerprint(T("a", "b:l\nc")), TableSnapshot.Fingerprint(T("a:l\nb", "c")));
    }

    [Fact]
    public void Fingerprint_LoneSurrogateAndReplacementCharacter_Differ()
    {
        static Table T(string name)
        {
            var t = new Table(1);
            t.AddLong(name);
            return t;
        }
        // UTF-8 encoding turns a lone surrogate into U+FFFD; the fingerprint must still tell them apart.
        Assert.NotEqual(TableSnapshot.Fingerprint(T("a\uD800")), TableSnapshot.Fingerprint(T("a\uFFFD")));
        Assert.NotEqual(TableSnapshot.Fingerprint(T("a\uD800")), TableSnapshot.Fingerprint(T("a\uDC00")));
    }

    [Fact]
    public void Inspect_ChecksWithoutChangingTheTable_AndReturnsTheTick()
    {
        Table src = Build(3);
        src.GetLong("a").Load([1, 2, 3]);
        byte[] bytes = Take(src, 77);
        Table dst = Build(3);
        Assert.Equal(bytes.Length, TableSnapshot.ExpectedBytes(dst));
        Assert.Equal(77, TableSnapshot.Inspect(dst, Schema, bytes));
        Assert.Equal([0L, 0L, 0L], dst.GetLong("a").Current.ToArray());
        Assert.Throws<InvalidDataException>(() => TableSnapshot.Inspect(dst, Schema, bytes[..^1]));
        Assert.Throws<InvalidDataException>(() => TableSnapshot.Inspect(dst, Schema + 1, bytes));
        Assert.Throws<InvalidDataException>(() => TableSnapshot.Inspect(Build(2), Schema, bytes));
    }

    [Fact]
    public void Write_NegativeTick_Rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Take(Build(1), -1));
    }

    [Fact]
    public void Read_NegativeTick_Rejected()
    {
        byte[] bytes = Take(Build(1), 0);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(20), -1);
        Assert.Throws<InvalidDataException>(() => TableSnapshot.Read(Build(1), Schema, new MemoryStream(bytes)));
    }

    // Load inside a tick is forbidden (ADR-0017 p. 12); so is loading a snapshot there.
    private sealed class SnapshotInTick(Table t, byte[] bytes) : ITickSystem
    {
        public int SystemId => 1;
        public void Compute(in TickContext ctx, int chunk, int rowStart, int rowEnd) =>
            TableSnapshot.Read(t, Schema, new MemoryStream(bytes));
        public void Merge(in TickContext ctx, int chunkCount) { }
        public void Post(in TickContext ctx) { }
    }

    [Fact]
    public void Read_InsideATick_Throws()
    {
        Table t = Build(1);
        var runner = new TickRunner(t, 1, [new SnapshotInTick(t, Take(Build(1), 0))], 1, 1);
        Assert.Throws<TickComputeException>(() => runner.RunTick());
    }

    private sealed class Counter : ITickSystem
    {
        private readonly LongColumn _n;
        public Counter(Table t) => _n = t.AddLong("n");
        public int SystemId => 1;
        public Cadence Cadence => Cadence.Monthly;
        public void Compute(in TickContext ctx, int chunk, int rowStart, int rowEnd) => _n.Next[rowStart]++;
        public void Merge(in TickContext ctx, int chunkCount) { }
        public void Post(in TickContext ctx) { }
    }

    [Fact]
    public void RunnerRestore_ContinuesLikeAnUninterruptedRun_AndMonthlyDueIsRecomputed()
    {
        static (Table, TickRunner) New()
        {
            var t = new Table(1);
            var r = new TickRunner(t, 1, [new Counter(t)], 1, 1, new TickCalendar(new CivilDate(1945, 1, 1)));
            return (t, r);
        }
        var (ta, ra) = New();
        for (int i = 0; i < 100; i++)
            ra.RunTick();

        var (tb, rb) = New();
        for (int i = 0; i < 45; i++)
            rb.RunTick();
        using var s = new MemoryStream();
        TableSnapshot.Write(tb, 1, rb.Tick, s);
        var (tc, rc) = New();
        rc.Restore(TableSnapshot.Read(tc, 1, new MemoryStream(s.ToArray())));
        for (int i = 45; i < 100; i++)
            rc.RunTick();

        Assert.Equal(100, rc.Tick);
        HashAssert.Equal(StateHash.ComputeHex(ta, 1), StateHash.ComputeHex(tc, 1));
        Assert.Equal(4, tc.GetLong("n").Current[0]);   // Jan 1, Feb 1, Mar 1, Apr 1
    }

    [Fact]
    public void RunnerRestore_MidMonth_DoesNotRunTheMonthlySystemOnTheNextDay()
    {
        // Documents why Restore recomputes the due ticks: a fresh runner is due at tick 0, a restored one is not.
        var t = new Table(1);
        var r = new TickRunner(t, 1, [new Counter(t)], 1, 1, new TickCalendar(new CivilDate(1945, 1, 1)));
        r.Restore(10);
        r.RunTick();
        Assert.False(r.LastTickRan);
    }

    [Fact]
    public void StateHashInTheHeader_IsTheStateHashOfTheTable_AndAFlippedPayloadByteIsRefused()
    {
        Table src = Build(3);
        src.GetLong("a").Load([5, 6, 7]);
        byte[] bytes = Take(src, 4);
        HashAssert.Equal(StateHash.ComputeHex(src, Schema), TableSnapshot.StoredStateHash(bytes));

        byte[] flipped = (byte[])bytes.Clone();
        flipped[TableSnapshot.HeaderBytes] ^= 1;
        Table dst = Build(3);
        dst.GetLong("a").Load([9, 9, 9]);
        Assert.Throws<InvalidDataException>(() => TableSnapshot.Read(dst, Schema, new MemoryStream(flipped)));
        Assert.Throws<InvalidDataException>(() => TableSnapshot.Inspect(dst, Schema, flipped));
        Assert.Equal([9L, 9L, 9L], dst.GetLong("a").Current.ToArray());   // refused before the first column was replaced
    }

    [Fact]
    public void SaveLoadSave_GivesTheSameBytes()
    {
        Table src = Build(3);
        src.GetLong("a").Load([5, -6, 7]);
        src.GetInt("b").Load([1, 2, 3]);
        byte[] first = Take(src, 12);
        Table dst = Build(3);
        long tick = TableSnapshot.Read(dst, Schema, new MemoryStream(first));
        Assert.Equal(first, Take(dst, tick));
    }

    // ---- the runner: restore and snapshot calls are refused for the whole tick, post included ----

    private sealed class PostCaller : ITickSystem
    {
        public Action? OnPost;
        private readonly LongColumn _n;
        public PostCaller(Table t) => _n = t.AddLong("n");
        public int SystemId => 1;
        public void Compute(in TickContext ctx, int chunk, int rowStart, int rowEnd) => _n.Next[rowStart]++;
        public void Merge(in TickContext ctx, int chunkCount) { }
        public void Post(in TickContext ctx) => OnPost?.Invoke();
    }

    [Fact]
    public void RestoreAndRead_AreRefusedFromPost()
    {
        var t = new Table(1);
        var system = new PostCaller(t);
        var runner = new TickRunner(t, 1, [system], 1, 1);
        byte[] bytes = Take(t, 0);
        Exception? restore = null, read = null, write = null;
        system.OnPost = () =>
        {
            restore = Record(() => runner.Restore(3));
            read = Record(() => TableSnapshot.Read(t, Schema, new MemoryStream(bytes)));
            write = Record(() => Take(t, 1));
        };
        runner.RunTick();
        Assert.IsType<InvalidOperationException>(restore);
        Assert.IsType<InvalidOperationException>(read);
        Assert.IsType<InvalidOperationException>(write);
        Assert.Equal(1, runner.Tick);
        // Between ticks they work again (the flag is cleared).
        system.OnPost = null;
        runner.Restore(5);
        Assert.Equal(5, runner.Tick);
    }

    private static Exception? Record(Action a)
    {
        try
        {
            a();
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }

    private sealed class DailyAndMonthly(Table t, Cadence cadence, string column) : ITickSystem
    {
        private readonly LongColumn _n = t.AddLong(column);
        public int SystemId => cadence == Cadence.Daily ? 1 : 2;
        public Cadence Cadence => cadence;
        public void Compute(in TickContext ctx, int chunk, int rowStart, int rowEnd) => _n.Next[rowStart]++;
        public void Merge(in TickContext ctx, int chunkCount) { }
        public void Post(in TickContext ctx) { }
    }

    [Fact]
    public void Restore_OfAHugeTick_ThrowsAndChangesNothing()
    {
        static (Table, TickRunner) New()
        {
            var t = new Table(1);
            var r = new TickRunner(t, 1, [new DailyAndMonthly(t, Cadence.Daily, "d"), new DailyAndMonthly(t, Cadence.Monthly, "m")], 1, 1,
                new TickCalendar(new CivilDate(1945, 1, 1)));
            return (t, r);
        }
        var (ta, ra) = New();
        var (tb, rb) = New();
        for (int i = 0; i < 40; i++)
        {
            ra.RunTick();
            rb.RunTick();
        }
        Assert.ThrowsAny<Exception>(() => rb.Restore(long.MaxValue));
        Assert.Equal(40, rb.Tick);
        for (int i = 0; i < 70; i++)
        {
            ra.RunTick();
            rb.RunTick();
        }
        HashAssert.Equal(StateHash.ComputeHex(ta, 1), StateHash.ComputeHex(tb, 1));
        Assert.Equal(ta.GetLong("m").Current[0], tb.GetLong("m").Current[0]);
    }
}
