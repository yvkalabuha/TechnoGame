using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

/// <summary>ADR-0017 п. 12: <c>Column.Load</c> is allowed only outside a tick.</summary>
public class LoadInTickTests
{
    private sealed class LoadingSystem : ITickSystem
    {
        private readonly LongColumn _a;
        private readonly bool _inMerge;

        public LoadingSystem(Table t, bool inMerge)
        {
            _a = t.AddLong("a");
            _inMerge = inMerge;
        }

        public int SystemId => 1;

        public void Compute(in TickContext ctx, int chunk, int rowStart, int rowEnd)
        {
            if (!_inMerge)
                _a.Load(new long[ctx.Table.RowCount]);
        }

        public void Merge(in TickContext ctx, int chunkCount)
        {
            if (_inMerge)
                _a.Load(new long[ctx.Table.RowCount]);
        }

        public void Post(in TickContext ctx) { }
    }

    [Fact]
    public void LoadInCompute_Throws_AndLoadWorksAgainAfterwards()
    {
        var table = new Table(4);
        var runner = new TickRunner(table, 1, [new LoadingSystem(table, false)], 4, 1);
        var e = Assert.Throws<TickComputeException>(() => runner.RunTick());
        Assert.IsType<InvalidOperationException>(e.InnerException);
        table.GetLong("a").Load([1, 2, 3, 4]);
    }

    [Fact]
    public void LoadInMerge_Throws()
    {
        var table = new Table(4);
        var runner = new TickRunner(table, 1, [new LoadingSystem(table, true)], 4, 1);
        Assert.Throws<InvalidOperationException>(() => runner.RunTick());
        table.GetLong("a").Load([1, 2, 3, 4]);
    }
}
