using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

/// <summary>ADR-0017 p. 9 (extra): a threaded tick aborted after several columns were opened leaves no open flag and no leaked Next data.</summary>
public class CopyOnWriteAbortTests
{
    private sealed class TwoColumnSystem : ITickSystem
    {
        private readonly LongColumn _a, _b;
        public bool Throw { get; set; } = true;

        public TwoColumnSystem(Table t) { _a = t.AddLong("a"); _b = t.AddLong("b"); }

        public int SystemId => 3;

        public void Compute(in TickContext ctx, int chunk, int rowStart, int rowEnd)
        {
            Span<long> a = _a.Next;
            Span<long> b = _b.Next;
            for (int r = rowStart; r < rowEnd; r++)
            {
                a[r] = 1000 + r;   // garbage if the tick is aborted
                b[r] = _b.Current[r] + 1;
            }
            if (Throw && chunk == 5)
                throw new InvalidOperationException("boom");
        }

        public void Merge(in TickContext ctx, int chunkCount) { }

        public void Post(in TickContext ctx) { }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(16)]
    public void AbortedThreadedTick_LeavesNoFlagAndNoGarbage(int threads)
    {
        for (int repeat = 0; repeat < 10; repeat++)
        {
            var table = new Table(64);
            var system = new TwoColumnSystem(table);
            var runner = new TickRunner(table, 1, [system], 1, threads);
            string before = StateHash.ComputeHex(table, 1);
            Assert.ThrowsAny<Exception>(() => runner.RunTick());
            Assert.Throws<InvalidOperationException>(() => table.GetLong("a").Next[0] = 1);
            Assert.Throws<InvalidOperationException>(() => table.GetLong("b").Next[0] = 1);
            HashAssert.Equal(before, StateHash.ComputeHex(table, 1), "aborted tick changed Current");
            system.Throw = false;
            runner.RunTick();
            Assert.Equal(Enumerable.Range(0, 64).Select(i => (long)(1000 + i)), table.GetLong("a").Current.ToArray());
            Assert.Equal(Enumerable.Repeat(1L, 64), table.GetLong("b").Current.ToArray());
        }
    }
}
