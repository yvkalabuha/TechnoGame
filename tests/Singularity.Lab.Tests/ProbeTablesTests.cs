using Singularity.Core.Data;
using Singularity.Lab.Models.Slice;
using Singularity.Lab.Simulation;
using Singularity.Session;

namespace Singularity.Lab.Tests;

/// <summary>Codex #10 on TASK-0187: with INST_PROBE and LIC_PROBE on together every probe row goes to its own table (the column count matches).</summary>
public sealed class ProbeTablesTests
{
    private sealed class Rows : IMetricSink
    {
        public readonly List<(int Table, string[] Cells)> All = [];

        public void Row(int table, params string[] cells) => All.Add((table, cells));
    }

    private static ParamSet Params(params (string Key, ParamValue Value)[] kv) =>
        new(kv.Select(x => new KeyValuePair<string, ParamValue>(x.Key, x.Value)));

    [Fact]
    public void Institute_and_market_probes_together_write_rows_of_their_own_tables()
    {
        GameData data = SliceData.Load(AppContext.BaseDirectory);
        var bools = new ParamValue(ParamKind.Bool, "true");
        SliceParameters p = SliceParameters.Parse(Params(("LEVERS", new ParamValue(ParamKind.String, "ON")), ("INST_PROBE", bools), ("LIC_PROBE", bools)), 1, 1, 1, calibration: false);
        var model = new JetSliceModel(data, p, ticks: 400, calibration: false);
        var sink = new Rows();
        model.Run(sink);
        int goal = model.MetricTables.ToList().FindIndex(t => t.Name == InstituteSpec.GoalTable.Name);
        int build = model.MetricTables.ToList().FindIndex(t => t.Name == InstituteSpec.BuildTable.Name);
        Assert.Contains(sink.All, r => r.Table == goal);
        Assert.Contains(sink.All, r => r.Table == build);
        foreach ((int table, string[] cells) in sink.All)
            Assert.True(model.MetricTables[table].Columns.Count == cells.Length, $"table {model.MetricTables[table].Name}: {cells.Length} cells for {model.MetricTables[table].Columns.Count} columns");
    }
}
