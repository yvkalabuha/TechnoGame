using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>ADR-0006: each column is written by one system only (ADR-0019/0021 condition). Uses the Kernel test trace; the exception list is empty.</summary>
public class OneWriterPerColumnTests
{
    // Systems have no names; the label is the test-side type name plus the id.
    private static string Describe(ColumnWriteTrace t) =>
        string.Join("; ", t.Violations().Select(v => $"column '{v.Column}' written by systems [{string.Join(", ", v.SystemIds.Select(id => $"{nameof(Writer)}#{id}"))}]"));

    private static string RunSlice(ColumnWriteTrace? trace)
    {
        SliceSession s = SliceSession.Create(SliceTestData.Data, new SliceRunConfig { Seed = 7, DailyTickProbe = true });
        if (trace is not null) s.World.Table.Trace = trace;
        var script = new PlayerScript(s);
        for (long month = 0; month < 6; month++)
        {
            script.Issue(month);
            s.StepThroughMonth(month + 1);
        }
        return s.LastHash;
    }

    [Fact]
    public void TraceOnAndOff_GiveTheSameHash()
    {
        string off = RunSlice(null);
        string on = RunSlice(new ColumnWriteTrace());
        HashAssert.Equal(off, on, "tracing must not change the state");
    }

    [Fact]
    public void M0Slice_WithCommandsAndProbe_EveryColumnHasAtMostOneWriter()
    {
        SliceSession s = SliceSession.Create(SliceTestData.Data, new SliceRunConfig { Seed = 7, DailyTickProbe = true });
        var trace = new ColumnWriteTrace();
        s.World.Table.Trace = trace;
        var script = new PlayerScript(s);
        for (long month = 0; month < 6; month++)
        {
            script.Issue(month);
            s.StepThroughMonth(month + 1);
        }
        Assert.NotEmpty(trace.Pairs);
        Assert.True(trace.Violations().Count == 0, Describe(trace));
        // Commands write via Load between ticks, not inside a system.
        Assert.DoesNotContain(trace.Pairs, p => p.Column.StartsWith("cmd.", StringComparison.Ordinal));
    }

    [Fact]
    public void TraceOff_ByDefault()
    {
        Assert.Null(SliceTestData.Create().World.Table.Trace);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SecondWriter_IsReported_WithSystemsAndColumn(int threads)
    {
        var table = new Table(8);
        IntColumn col = table.AddInt("x");
        var trace = new ColumnWriteTrace();
        table.Trace = trace;
        var runner = new TickRunner(table, 1, [new Writer(10, col), new Writer(20, col)], 4, threads);
        runner.RunTick();
        var v = Assert.Single(trace.Violations());
        Assert.Equal("x", v.Column);
        Assert.Equal([10, 20], v.SystemIds);
        string text = Describe(trace);
        Assert.Contains("column 'x'", text);
        Assert.Contains("Writer#10", text);
        Assert.Contains("Writer#20", text);
    }

    private sealed class Writer(int id, IntColumn col) : ITickSystem
    {
        public int SystemId => id;
        public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd) => col.Next[rowStart] = 1;
        public void Merge(in TickContext context, int chunkCount) { }
        public void Post(in TickContext context) { }
    }
}
