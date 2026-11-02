using Singularity.Kernel;

namespace Singularity.Kernel.Tests.Audit;

public class TickRunnerEdgeTests
{
    /// <summary>Records which rows/chunks were computed and the phase order.</summary>
    private sealed class ProbeSystem : ITickSystem
    {
        private readonly int[] _rowHits;
        private readonly (int Start, int End)[] _chunkRanges;
        private readonly List<string> _log;
        private readonly string _name;

        public ProbeSystem(int rows, int chunkCountHint, List<string> log, string name, int id)
        {
            _rowHits = new int[rows];
            _chunkRanges = new (int, int)[Math.Max(chunkCountHint, 0)];
            _log = log;
            _name = name;
            SystemId = id;
        }

        public int SystemId { get; }
        public int[] RowHits => _rowHits;
        public (int Start, int End)[] ChunkRanges => _chunkRanges;
        public long PostTick = -100;
        public int ComputeCalls;

        public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
        {
            Interlocked.Increment(ref ComputeCalls);
            _chunkRanges[chunk] = (rowStart, rowEnd);
            for (int r = rowStart; r < rowEnd; r++)
                _rowHits[r]++; // rows are disjoint between chunks, so no race if the runner is correct
        }

        public void Merge(in TickContext context, int chunkCount)
        {
            lock (_log) _log.Add($"merge:{_name}:{chunkCount}");
        }

        public void Post(in TickContext context)
        {
            PostTick = context.Tick;
            lock (_log) _log.Add($"post:{_name}");
        }
    }

    public static TheoryData<int, int, int> Shapes => new()
    {
        // rows, chunkSize, threads
        { 0, 1, 1 }, { 0, 7, 8 },             // zero rows
        { 1, 1, 8 }, { 1, 100, 8 },           // one row, threads > chunks
        { 10, 3, 1 }, { 10, 3, 4 }, { 10, 3, 64 }, // uneven last chunk (10 = 3+3+3+1)
        { 1000, 999, 16 },                    // last chunk of 1 row
        { 1000, 1000, 16 }, { 1000, 1001, 16 },
        { 1023, 1, 8 },                       // chunk per row
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void EveryRowComputedExactlyOnce_ChunksContiguous(int rows, int chunkSize, int threads)
    {
        var table = new Table(rows);
        int expectedChunks = (rows + chunkSize - 1) / chunkSize;
        var log = new List<string>();
        var probe = new ProbeSystem(rows, expectedChunks, log, "p", 1);
        var runner = new TickRunner(table, 0, [probe], chunkSize, threads);
        Assert.Equal(expectedChunks, runner.ChunkCount);

        runner.RunTick();
        Assert.Equal(expectedChunks, probe.ComputeCalls);
        Assert.All(probe.RowHits, h => Assert.Equal(1, h));
        int cursor = 0;
        foreach (var (start, end) in probe.ChunkRanges)
        {
            Assert.Equal(cursor, start);
            Assert.True(end > start && end - start <= chunkSize);
            cursor = end;
        }
        Assert.Equal(rows, cursor);
    }

    [Fact]
    public void PhaseOrder_AllMergesInListOrder_ThenAllPosts()
    {
        var log = new List<string>();
        var table = new Table(5);
        var a = new ProbeSystem(5, 3, log, "a", 1);
        var b = new ProbeSystem(5, 3, log, "b", 2);
        new TickRunner(table, 0, [a, b], chunkSize: 2, maxThreads: 4).RunTick();
        Assert.Equal(["merge:a:3", "merge:b:3", "post:a", "post:b"], log);
    }

    [Fact]
    public void Post_ContextTick_IsTheTickOfTheNewState()
    {
        // Post runs after the swap: Current holds state t+1, so context.Tick == runner.Tick == t+1 (F3, TASK-0007).
        var log = new List<string>();
        var table = new Table(1);
        var probe = new ProbeSystem(1, 1, log, "p", 1);
        var runner = new TickRunner(table, 0, [probe], chunkSize: 1, maxThreads: 1);
        runner.RunTick();
        runner.RunTick();
        Assert.Equal(2, probe.PostTick);
        Assert.Equal(2, runner.Tick);
    }

    [Fact]
    public void ZeroRows_HashEvolvesOnlyByTick()
    {
        var table = new Table(0);
        table.AddLong("x");
        var runner = new TickRunner(table, 0, [], chunkSize: 4, maxThreads: 8);
        Assert.Equal(0, runner.ChunkCount);
        runner.RunTick();
        Assert.Equal(1, runner.Tick);
    }

    [Fact]
    public void Constructor_RejectsInvalidArguments()
    {
        var table = new Table(4);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TickRunner(table, 0, [], chunkSize: 0, maxThreads: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TickRunner(table, 0, [], chunkSize: 1, maxThreads: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TickRunner(table, 0, [], chunkSize: -5, maxThreads: 1));
    }

    [Theory]
    [InlineData(10, int.MaxValue)]
    [InlineData(10, int.MaxValue - 5)]
    [InlineData(2, int.MaxValue)]
    public void HugeChunkSize_StillGivesOneChunk(int rows, int chunkSize)
    {
        // BUG F1: ChunkCount = (RowCount + chunkSize - 1) / chunkSize overflows int (unchecked),
        // giving 0 or -1 chunks: Compute is silently never called.
        var table = new Table(rows);
        var log = new List<string>();
        var probe = new ProbeSystem(rows, 1, log, "p", 1);
        var runner = new TickRunner(table, 0, [probe], chunkSize, maxThreads: 4);
        Assert.Equal(1, runner.ChunkCount);
        runner.RunTick();
        Assert.All(probe.RowHits, h => Assert.Equal(1, h));
    }
}
