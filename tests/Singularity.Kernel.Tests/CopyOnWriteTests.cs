using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

/// <summary>ADR-0017 п. 10-13: copy-on-write columns, swap of open columns only, tick boundary, table version.</summary>
public class CopyOnWriteTests
{
    // Every chunk first-touches the same column "hot": the race of opening it. Also writes "own" (per row) and never touches "cold".
    private sealed class HotColumnSystem : ITickSystem
    {
        private readonly LongColumn _hot, _own;

        public HotColumnSystem(Table t)
        {
            _hot = t.AddLong("hot");
            _own = t.AddLong("own");
            t.AddLong("cold");
        }

        public int SystemId => 1;

        public void Compute(in TickContext ctx, int chunk, int rowStart, int rowEnd)
        {
            // Non-owned write would break the contract; each chunk writes its own rows of "hot", all chunks open it first.
            Span<long> hot = _hot.Next;
            Span<long> own = _own.Next;
            for (int r = rowStart; r < rowEnd; r++)
            {
                hot[r] = _hot.Current[r] + r + 1 + ctx.Tick;
                if (r % 3 == 0)
                    own[r] = _own.Current[r] * 7 + 1;
            }
        }

        public void Merge(in TickContext ctx, int chunkCount) { }

        public void Post(in TickContext ctx) { }
    }

    private static Table Build(out TickRunner runner, int threads, int chunkSize, bool fullCopy, int rows = 64)
    {
        var table = new Table(rows) { FullCopyMode = fullCopy };
        var system = new HotColumnSystem(table);
        table.GetLong("cold").Load([.. Enumerable.Range(0, rows).Select(i => (long)i * 11)]);
        runner = new TickRunner(table, 5, [system], chunkSize, threads);
        return table;
    }

    [Fact]
    public void OpenRace_Threads16Chunk1_SameHashInEveryRepeat_EqualToFullCopy()
    {
        Table reference = Build(out TickRunner refRunner, 1, 1, fullCopy: true);
        for (int i = 0; i < 10; i++)
            refRunner.RunTick();
        string expected = StateHash.ComputeHex(reference, 1);

        for (int repeat = 0; repeat < 20; repeat++)
        {
            Table table = Build(out TickRunner runner, 16, 1, fullCopy: false);
            for (int i = 0; i < 10; i++)
                runner.RunTick();
            HashAssert.Equal(expected, StateHash.ComputeHex(table, 1), $"repeat {repeat}");
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 8)]
    [InlineData(8, 32)]
    [InlineData(16, 8)]
    public void CowAndFullCopy_GiveTheSameHashOnEveryTick(int threads, int chunkSize)
    {
        Table cow = Build(out TickRunner cowRunner, threads, chunkSize, fullCopy: false);
        Table full = Build(out TickRunner fullRunner, 1, 1, fullCopy: true);
        for (int i = 0; i < 12; i++)
        {
            cowRunner.RunTick();
            fullRunner.RunTick();
            HashAssert.Equal(StateHash.ComputeHex(full, 1), StateHash.ComputeHex(cow, 1), $"tick {i}");
        }
    }

    [Fact]
    public void OnlyOpenColumns_AreSwapped_AndTheVersionGrowsOnlyWhenSomethingWasSwapped()
    {
        Table table = Build(out TickRunner runner, 1, 8, fullCopy: false);
        long v0 = table.Version;
        runner.RunTick();
        Assert.True(table.Version > v0);
        long cold = table.GetLong("cold").Current[3];
        Assert.Equal(33, cold);

        long v1 = table.Version;
        table.GetLong("cold").Load([.. Enumerable.Repeat(1L, 64)]);   // Load raises the version
        Assert.True(table.Version > v1);

        var idle = new Table(4);
        idle.AddLong("x");
        var idleRunner = new TickRunner(idle, 1, [], 2, 1);
        long vIdle = idle.Version;
        idleRunner.RunTick();                                         // a tick with no system: nothing opened, nothing swapped
        Assert.Equal(vIdle, idle.Version);
    }

    [Fact]
    public void ColumnVersion_GrowsOnSwapAndLoad_AndStaysForAColumnNobodyWrote()
    {
        Table table = Build(out TickRunner runner, 1, 8, fullCopy: false);
        LongColumn hot = table.GetLong("hot"), cold = table.GetLong("cold");
        long h0 = hot.Version, c0 = cold.Version;
        runner.RunTick();
        Assert.Equal(h0 + 1, hot.Version);          // written: swapped once
        Assert.Equal(c0, cold.Version);             // never opened: Current kept
        runner.RunTick();
        Assert.Equal(h0 + 2, hot.Version);
        Assert.Equal(c0, cold.Version);
        cold.Load([.. Enumerable.Repeat(1L, 64)]);
        Assert.Equal(c0 + 1, cold.Version);
    }

    [Fact]
    public void NextOutsideATick_Throws_AlsoAfterATickThatWasAbortedByAnException()
    {
        var table = new Table(4);
        LongColumn x = table.AddLong("x");
        var runner = new TickRunner(table, 1, [new ThrowingSystem(x)], 2, 1);
        Assert.Throws<TickComputeException>(() => runner.RunTick());
        Assert.Throws<InvalidOperationException>(() => x.Next[0] = 1);   // no flag stays open for the code outside the tick
        Assert.Equal(0, x.Current[0]);                                   // the aborted tick changed nothing
    }

    [Fact]
    public void AbortedTick_LeavesNoOpenFlag_TheNextTickStartsFromCurrent()
    {
        var table = new Table(4);
        LongColumn x = table.AddLong("x");
        var system = new ThrowingSystem(x);
        var runner = new TickRunner(table, 1, [system], 2, 1);
        Assert.Throws<TickComputeException>(() => runner.RunTick());
        system.Throw = false;
        runner.RunTick();
        // The garbage written by the aborted tick (99) must not leak: the column was reopened from Current.
        Assert.Equal([1, 1, 1, 1], x.Current.ToArray());
    }

    private sealed class ThrowingSystem(LongColumn x) : ITickSystem
    {
        public bool Throw { get; set; } = true;

        public int SystemId => 2;

        public void Compute(in TickContext ctx, int chunk, int rowStart, int rowEnd)
        {
            Span<long> next = x.Next;
            if (Throw)
            {
                next[rowStart] = 99;
                throw new InvalidOperationException("boom");
            }
            for (int r = rowStart; r < rowEnd; r++)
                next[r] = x.Current[r] + 1;
        }

        public void Merge(in TickContext ctx, int chunkCount) { }

        public void Post(in TickContext ctx) { }
    }
}
