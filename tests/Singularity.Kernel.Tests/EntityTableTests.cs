using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

/// <summary>ADR-0029: child tables, NextRange, handles, single-threaded copy-on-write opening.</summary>
public class EntityTableTests
{
    private const int SystemIdValue = 7;

    private static int[] Counts(int actors, int shape) =>
        Enumerable.Range(0, actors).Select(a => shape == 0 ? 12 : (a * 5 + 1) % 9).ToArray(); // shape 1: 0..8, includes empty rows

    private sealed class Toy
    {
        public World World = new();
        public Table Actors = null!;
        public Table Designs = null!;
        public LongColumn Score = null!;
        public LongColumn Val = null!;
    }

    private static Toy Build(int actors, int capacityOrShape, bool variable)
    {
        var t = new Toy();
        t.Actors = t.World.AddTable("actors", actors);
        t.Score = t.Actors.AddLong("score");
        t.Designs = variable
            ? t.World.AddChildTable("designs", t.Actors, Counts(actors, 1))
            : t.World.AddChildTable("designs", t.Actors, capacityOrShape);
        t.Val = t.Designs.AddLong("val");
        return t;
    }

    private sealed class ToySystem : ITickSystem
    {
        private readonly Toy _t;
        private readonly bool _declare;
        public ToySystem(Toy t, bool declare = true) { _t = t; _declare = declare; }
        public int SystemId => SystemIdValue;

        public IReadOnlyList<Column> WrittenColumns => _declare ? [_t.Score, _t.Val] : Array.Empty<Column>();

        public void Compute(in TickContext ctx, int chunk, int rowStart, int rowEnd)
        {
            Span<long> score = _t.Score.Next;
            Span<long> val = _t.Val.NextRange(ctx, chunk);
            int first = _t.Designs.OffsetOf(rowStart);
            for (int a = rowStart; a < rowEnd; a++)
                score[a] += (long)(ctx.Rng.NextU64(ctx.Tick, SystemIdValue, a, 0) % 1000);
            for (int i = 0; i < val.Length; i++)
                val[i] += (long)(ctx.Rng.NextU64(ctx.Tick, SystemIdValue, first + i, 1) % 1000);
        }

        public void Merge(in TickContext context, int chunkCount) { }
        public void Post(in TickContext context) { }
    }

    private static List<string> Run(int threads, int chunkSize, int capacity, bool variable, bool declare = true)
    {
        Toy t = Build(50, capacity, variable);
        var runner = new TickRunner(t.World, t.Actors, 77, [new ToySystem(t, declare)], chunkSize, threads);
        var hashes = new List<string>();
        for (int d = 0; d < 6; d++)
        {
            runner.RunTick();
            hashes.Add(StateHash.ComputeHex(t.World, 1));
        }
        return hashes;
    }

    [Theory] // L2
    [InlineData(1, false)]
    [InlineData(12, false)]
    [InlineData(0, true)]
    public void L2_ThreadsAndChunkSize_DoNotChangeDailyHashes(int capacity, bool variable)
    {
        List<string> reference = Run(1, 8, capacity, variable);
        Assert.Equal(6, reference.Distinct().Count());
        foreach (int threads in new[] { 1, 4, 8, 16 })
            foreach (int chunkSize in new[] { 1, 8, 32 })
            {
                Assert.Equal(reference, Run(threads, chunkSize, capacity, variable));
                Assert.Equal(reference, Run(threads, chunkSize, capacity, variable, declare: false));
            }
    }

    [Fact]
    public void NextRange_IsExactlyTheChunkRange_ForRandomOffsets()
    {
        var rng = new CounterRng(5);
        for (int round = 0; round < 40; round++)
        {
            int actors = 1 + (int)(rng.NextU64(round, 0, 0, 0) % 40);
            int chunkSize = 1 + (int)(rng.NextU64(round, 0, 0, 1) % 9);
            var counts = new int[actors];
            for (int a = 0; a < actors; a++)
                counts[a] = (int)(rng.NextU64(round, 0, a, 2) % 6);
            var world = new World();
            Table parent = world.AddTable("p", actors);
            Table child = world.AddChildTable("c", parent, counts);
            LongColumn col = child.AddLong("x");
            int chunks = TickRunner.ChunkCountFor(actors, chunkSize);
            var seen = new List<(int Start, int Len)>();
            var sys = new RangeProbe(col, child, seen);
            var runner = new TickRunner(world, parent, 1, [sys], chunkSize, 1);
            runner.RunTick();
            Assert.Equal(chunks, seen.Count);
            int expectStart = 0;
            for (int c = 0; c < chunks; c++)
            {
                int rs = c * chunkSize, re = Math.Min(rs + chunkSize, actors);
                int len = counts.Skip(rs).Take(re - rs).Sum();
                Assert.Equal((expectStart, len), seen[c]);
                expectStart += len;
            }
            // Every child row was written exactly by its own chunk.
            Assert.All(col.Current.ToArray(), v => Assert.Equal(1, v));
        }
    }

    private sealed class RangeProbe(LongColumn col, Table child, List<(int, int)> seen) : ITickSystem
    {
        public int SystemId => 1;
        public void Compute(in TickContext ctx, int chunk, int rowStart, int rowEnd)
        {
            Span<long> r = col.NextRange(ctx, chunk);
            seen.Add((child.OffsetOf(rowStart), r.Length));
            r.Fill(1);
        }
        public void Merge(in TickContext context, int chunkCount) { }
        public void Post(in TickContext context) { }
    }

    [Fact]
    public void EmptyParent_GivesEmptySpan()
    {
        var world = new World();
        Table parent = world.AddTable("p", 0);
        Table child = world.AddChildTable("c", parent, 3);
        LongColumn col = child.AddLong("x");
        Assert.Equal(0, child.RowCount);
        var ctx = new TickContext(parent, new CounterRng(1), 0, null, 4);
        Assert.Equal(0, col.NextRange(ctx, 0).Length);
    }

    private sealed class DelegateSystem(Action<TickContext, int> compute, Action<TickContext>? merge = null) : ITickSystem
    {
        public int SystemId => 2;
        public void Compute(in TickContext ctx, int chunk, int rowStart, int rowEnd) => compute(ctx, chunk);
        public void Merge(in TickContext ctx, int chunkCount) => merge?.Invoke(ctx);
        public void Post(in TickContext context) { }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void NextOfChildColumn_InCompute_Throws(int threads)
    {
        Toy t = Build(8, 2, false);
        var runner = new TickRunner(t.World, t.Actors, 1, [new DelegateSystem((_, _) => _ = t.Val.Next.Length)], 2, threads);
        TickComputeException e = Assert.Throws<TickComputeException>(runner.RunTick);
        Assert.IsType<InvalidOperationException>(e.InnerException);
    }

    [Fact]
    public void NextOfChildColumn_InMerge_Works_AndGuardIsReleasedAfterwards()
    {
        Toy t = Build(8, 2, false);
        var runner = new TickRunner(t.World, t.Actors, 1,
            [new DelegateSystem((_, _) => { }, _ => t.Val.Next[3] = 9)], 2, 4);
        runner.RunTick();
        Assert.Equal(9, t.Val.Current[3]);
        runner.RunTick(); // guard was released: Next works again outside compute
        Assert.Equal(9, t.Val.Current[3]);
    }

    [Fact]
    public void NextRange_WithForeignContext_Throws()
    {
        Toy t = Build(8, 2, false);
        Table other = new World().AddTable("o", 8);
        var runner = new TickRunner(t.World, t.Actors, 1,
            [new DelegateSystem((ctx, chunk) => t.Val.NextRange(new TickContext(other, ctx.Rng, ctx.Tick, null, 2), chunk))], 2, 1);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TickComputeException>(runner.RunTick).InnerException);
        Assert.Throws<InvalidOperationException>(() => t.Score.NextRange(default, 0));
    }

    [Fact]
    public void DeclaredColumns_AreOpenedBeforeCompute()
    {
        Toy t = Build(8, 2, false);
        t.Actors.Trace = new ColumnWriteTrace();
        bool sawOpen = false;
        var sys = new OpenProbe(t, () => sawOpen = t.Actors.Trace!.Pairs.Contains((2, "designs.val")));
        new TickRunner(t.World, t.Actors, 1, [sys], 2, 1).RunTick();
        Assert.True(sawOpen);
    }

    private sealed class OpenProbe(Toy t, Action probe) : ITickSystem
    {
        public int SystemId => 2;
        public IReadOnlyList<Column> WrittenColumns => [t.Val];
        public void Compute(in TickContext ctx, int chunk, int rowStart, int rowEnd)
        {
            if (chunk == 0)
                probe(); // before any Next of this chunk
        }
        public void Merge(in TickContext context, int chunkCount) { }
        public void Post(in TickContext context) { }
    }

    [Fact]
    public void Trace_NamesChildColumnsByTable_AndSeesOneWriterViolations()
    {
        Toy t = Build(4, 2, false);
        LongColumn same = t.Actors.AddLong("val"); // same short name in two tables must not collide
        t.Actors.Trace = new ColumnWriteTrace();
        var a = new DelegateSystem((ctx, c) => t.Val.NextRange(ctx, c));
        new TickRunner(t.World, t.Actors, 1, [a], 2, 1).RunTick();
        Assert.Contains((2, "designs.val"), t.Actors.Trace.Pairs);
        Assert.DoesNotContain((2, "val"), t.Actors.Trace.Pairs);
        Assert.Empty(t.Actors.Trace.Violations());
        _ = same;
    }

    [Fact]
    public void Handle_IsStale_WhenSlotGenerationChanges()
    {
        Toy t = Build(3, 2, false);
        RowHandle h = t.Designs.HandleOf(4);
        Assert.True(t.Designs.IsLive(h));
        var gen = t.Designs.Generation!;
        var reopen = new DelegateSystem((_, _) => { }, _ => gen.Next[4]++);
        new TickRunner(t.World, t.Actors, 1, [reopen], 2, 1).RunTick();
        Assert.False(t.Designs.IsLive(h));
        Assert.True(t.Designs.IsLive(t.Designs.HandleOf(4)));
        Assert.False(t.Designs.IsLive(new RowHandle(99, 0)));
        Assert.False(t.Designs.IsLive(new RowHandle(-1, 0)));
    }

    [Fact]
    public void ChildTable_OffsetsAndRows()
    {
        var w = new World();
        Table p = w.AddTable("p", 3);
        Table c = w.AddChildTable("c", p, new[] { 2, 0, 3 });
        Assert.Equal(5, c.RowCount);
        Assert.Equal([0, 2, 2, 5], Enumerable.Range(0, 4).Select(c.OffsetOf));
        Assert.Throws<ArgumentException>(() => w.AddChildTable("c", p, 1));
        Assert.Throws<ArgumentException>(() => w.AddChildTable("d", p, new[] { 1 }));
        Assert.Throws<ArgumentException>(() => new World().AddChildTable("d", p, 1));
    }

    [Fact]
    public void WorldHash_CoversChildTables()
    {
        Toy t = Build(4, 2, false);
        string before = StateHash.ComputeHex(t.World, 1);
        t.Val.Load(Enumerable.Range(1, 8).Select(i => (long)i).ToArray());
        Assert.NotEqual(before, StateHash.ComputeHex(t.World, 1));
    }
}
