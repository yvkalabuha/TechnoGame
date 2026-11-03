using Singularity.Kernel;

namespace Singularity.Kernel.Tests.Audit;

/// <summary>
/// Cadence in <see cref="TickRunner"/> (ADR-0006 "Пустой тик", "Применение команд"; TASK-0014 audit).
/// The command queue belongs to Session (ADR-0014) and is not tested here; system decisions are table
/// state, read by the next run of the system.
/// </summary>
public class CadenceCommandTests
{
    // 1945-01-01 was a Monday: aligned with every cadence.
    private static readonly CivilDate AllAligned = new(1945, 1, 1);

    [Fact]
    public void DecisionAtStepT_StoredInTable_ActsFromStepTPlus1()
    {
        // Decision = state (ADR-0006): written in month t into the system's own column, read by month t+1.
        var cal = new TickCalendar(new CivilDate(1945, 9, 1));
        var table = new Table(1);
        var m = new DecidingSystem(table, "m", Cadence.Monthly) { Decision = 10 };
        var runner = new TickRunner(table, 1, [m], 1, 1, cal);
        for (long t = 0; t < 6; t++)
        {
            while (runner.Tick <= cal.TickOfMonth(t))
                runner.RunTick();
        }
        // Compute of month t sees: own count (t) + decisions of months 0..t−1 (10 each).
        Assert.Equal([0, 11, 22, 33, 44, 55], m.SeenAtCompute);
    }

    [Fact]
    public void Weekly_RunsOnMondays_AcrossMonthBoundaries()
    {
        var cal = new TickCalendar(AllAligned);
        var table = new Table(1);
        var w = new DecidingSystem(table, "w", Cadence.Weekly);
        var runner = new TickRunner(table, 1, [w], 1, 1, cal);
        for (int i = 0; i < 400; i++)
            runner.RunTick();
        Assert.Equal((400 + 6) / 7, w.Runs.Count);
        Assert.All(w.Runs, t => Assert.Equal(0, t % 7));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 3)]
    [InlineData(8, 2)]
    public void MixedCadences_ThreadCountIndependent(int threads, int chunkSize)
    {
        Assert.Equal(MixedRun(1, 1), MixedRun(threads, chunkSize));
    }

    private static string MixedRun(int threads, int chunkSize)
    {
        var cal = new TickCalendar(AllAligned);
        var table = new Table(13);
        DecidingSystem[] systems =
        [
            new(table, "d", Cadence.Daily) { Decision = 1 },
            new(table, "w", Cadence.Weekly) { Decision = 3, Watch = "d" },
            new(table, "m", Cadence.Monthly) { Decision = 5, Watch = "w" },
            new(table, "y", Cadence.Yearly) { Decision = 7, Watch = "m" },
        ];
        var runner = new TickRunner(table, 9, systems, chunkSize, threads, cal);
        for (int i = 0; i < 1000; i++)
            runner.RunTick();
        return StateHash.ComputeHex(table, 9, 0);
    }

    /// <summary>
    /// Adds 1 per run to its own column (plus its stored decision and the watched column at row 0); the
    /// decision of a run is kept in its own decision column and read by its next run.
    /// </summary>
    private sealed class DecidingSystem : ITickSystem
    {
        private readonly Table _table;
        private readonly LongColumn _col, _decided;

        public DecidingSystem(Table table, string name, Cadence cadence)
        {
            _table = table;
            _col = table.AddLong(name);
            _decided = table.AddLong(name + ".decided");
            Cadence = cadence;
        }

        public int SystemId => 3;
        public Cadence Cadence { get; }
        public long Decision;
        public string? Watch;
        public List<long> SeenAtCompute { get; } = new();
        public List<long> Runs { get; } = new();

        public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
        {
            long watched = Watch is null ? 0 : _table.GetLong(Watch).Current[0];
            if (rowStart == 0)
                SeenAtCompute.Add(_col.Current[0] + _decided.Current[0]);
            for (int r = rowStart; r < rowEnd; r++)
            {
                _col.Next[r] = _col.Current[r] + _decided.Current[r] + 1 + (watched % 5) * r;
                _decided.Next[r] = Decision;
            }
        }

        public void Merge(in TickContext context, int chunkCount) => Runs.Add(context.Tick);
        public void Post(in TickContext context) { }
    }
}
