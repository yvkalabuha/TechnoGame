using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>ADR-0006: each column is written by one system only (ADR-0019/0021 condition). Uses the Kernel test trace; the exception list is empty.</summary>
public class OneWriterPerColumnTests
{
    private static string Describe(ColumnWriteTrace t) =>
        string.Join("; ", t.Violations().Select(v => $"column '{v.Column}' written by systems [{string.Join(", ", v.SystemIds)}]"));

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
    }

    [Fact]
    public void TraceOff_ByDefault()
    {
        Assert.Null(SliceTestData.Create().World.Table.Trace);
    }

    [Fact]
    public void SecondWriter_IsReported_WithSystemsAndColumn()
    {
        var table = new Table(4);
        IntColumn col = table.AddInt("x");
        var trace = new ColumnWriteTrace();
        table.Trace = trace;
        var runner = new TickRunner(table, 1, [new Writer(10, col), new Writer(20, col)], 4, 1);
        runner.RunTick();
        var v = Assert.Single(trace.Violations());
        Assert.Equal("x", v.Column);
        Assert.Equal([10, 20], v.SystemIds);
    }

    private sealed class Writer(int id, IntColumn col) : ITickSystem
    {
        public int SystemId => id;
        public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd) => col.Next[rowStart] = 1;
        public void Merge(in TickContext context, int chunkCount) { }
        public void Post(in TickContext context) { }
    }
}
