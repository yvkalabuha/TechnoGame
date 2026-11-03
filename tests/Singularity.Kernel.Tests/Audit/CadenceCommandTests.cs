using Singularity.Kernel;

namespace Singularity.Kernel.Tests.Audit;

/// <summary>
/// Cadence × commands in <see cref="TickRunner"/> (ADR-0006 "Применение команд", "Пустой тик"; TASK-0014 audit).
/// </summary>
public class CadenceCommandTests
{
    private static readonly CivilDate Start = new(1945, 9, 1);

    [Fact]
    public void SystemCommand_SetOnIdleDay_WaitsForNextDueTick_IdleDaysStayEmpty()
    {
        var cal = new TickCalendar(Start);
        var table = new Table(1);
        var m = new CmdSystem(table, "m", Cadence.Monthly);
        var runner = new TickRunner(table, 1, [m], 1, 1, cal);
        Run(runner, 15);           // day 0 due, days 1..14 idle
        m.Pending = 100;           // decided on 15 September (idle)
        string before = StateHash.ComputeHex(table, 1, 0);
        Run(runner, 15);           // days 15..29: idle, command waits
        Assert.Equal(0, m.ApplyCalls);
        Assert.Equal(1, m.PostCalls);
        Assert.Equal(before, StateHash.ComputeHex(table, 1, 0));
        runner.RunTick();          // 1 October
        Assert.Equal(new CivilDate(1945, 10, 1), cal.DateOf(runner.Tick - 1));
        Assert.Equal(1, m.ApplyCalls);
        Assert.Equal([0, 101], m.SeenAtCompute); // compute of the due tick already sees the command
    }

    [Fact]
    public void MonthlyCommand_NotAppliedOnDaysWhereOnlyADailySystemRuns()
    {
        var cal = new TickCalendar(Start);
        var table = new Table(1);
        var d = new CmdSystem(table, "d", Cadence.Daily);
        var m = new CmdSystem(table, "m", Cadence.Monthly);
        var runner = new TickRunner(table, 1, [d, m], 1, 1, cal);
        runner.RunTick();                  // day 0
        m.Pending = 50;
        d.Pending = 7;
        runner.RunTick();                  // day 1: daily due → its command applies; monthly waits
        Assert.Equal((1, 0), (d.ApplyCalls, m.ApplyCalls));
        Assert.Equal(1 + 7 + 1, table.GetLong("d").Current[0]);
        Run(runner, 28);                   // days 2..29
        Assert.Equal(0, m.ApplyCalls);
        Assert.Equal(1, table.GetLong("m").Current[0]);
        runner.RunTick();                  // day 30 = 1 October
        Assert.Equal(1, m.ApplyCalls);
        Assert.Equal(1 + 50 + 1, table.GetLong("m").Current[0]);
    }

    [Fact]
    public void DecisionAtStepT_ActsFromStepTPlus1_NotInStepT()
    {
        // System decision = state (ADR-0006): decided in the merge of month t, in force from d(t+1).
        var cal = new TickCalendar(Start);
        var table = new Table(1);
        var m = new CmdSystem(table, "m", Cadence.Monthly) { DecideInMerge = 10 };
        var runner = new TickRunner(table, 1, [m], 1, 1, cal);
        for (long t = 0; t < 6; t++)
        {
            while (runner.Tick <= cal.TickOfMonth(t))
                runner.RunTick();
        }
        // Compute sees: month 0 → 0; month t ≥ 1 → previous + 1 (own run) + 10 (decision of t−1).
        Assert.Equal([0, 11, 22, 33, 44, 55], m.SeenAtCompute);
        Assert.Equal(5, m.ApplyCalls);
    }

    [Fact]
    public void Commands_AppliedInListOrder_BeforeAnyCompute()
    {
        var cal = new TickCalendar(Start);
        var table = new Table(1);
        var a = new CmdSystem(table, "a", Cadence.Monthly);
        var b = new CmdSystem(table, "b", Cadence.Monthly) { Watch = "a" };
        var runner = new TickRunner(table, 1, [a, b], 1, 1, cal);
        runner.RunTick();
        a.Pending = 1000;
        b.Pending = 5;
        Run(runner, 30);
        Assert.Equal((1, 1), (a.ApplyCalls, b.ApplyCalls));
        Assert.Equal(1 + 1000, b.WatchedAtCompute[1]); // b's compute sees a's command, not a's compute output
    }

    [Fact]
    public void WeeklyCommand_CrossingMonthBoundary_AppliesOnNextWeeklyTick()
    {
        var cal = new TickCalendar(Start);
        var table = new Table(1);
        var w = new CmdSystem(table, "w", Cadence.Weekly);
        var m = new CmdSystem(table, "m", Cadence.Monthly);
        var runner = new TickRunner(table, 1, [w, m], 1, 1, cal);
        Run(runner, 29);          // through day 28 (weekly due on 28)
        w.Pending = 3;
        runner.RunTick();         // day 29: idle for both
        Assert.Equal(0, w.ApplyCalls);
        runner.RunTick();         // day 30 = 1 Oct: only monthly due; weekly waits for day 35
        Assert.Equal(0, w.ApplyCalls);
        Run(runner, 5);           // days 31..35
        Assert.Equal(1, w.ApplyCalls);
        Assert.Equal(35, runner.Tick - 1);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 3)]
    [InlineData(8, 2)]
    public void CommandsAndCadences_ThreadCountIndependent(int threads, int chunkSize)
    {
        Assert.Equal(MixedRun(1, 1), MixedRun(threads, chunkSize));
    }

    [Fact(Skip = "FINDING (TASK-0014 audit): ADR-0006 says external (player/harness) commands apply at the next tick " +
                 "boundary for all systems and such a tick runs post and the hash; the runner applies commands only on a due " +
                 "tick of the target system and has no external-command path, so a monthly system's command set on 15 Sep waits to 1 Oct.")]
    public void ExternalCommand_OnIdleDay_AppliesAtNextTick_AndRunsPost()
    {
        var cal = new TickCalendar(Start);
        var table = new Table(1);
        var m = new CmdSystem(table, "m", Cadence.Monthly);
        var runner = new TickRunner(table, 1, [m], 1, 1, cal);
        Run(runner, 15);
        m.Pending = 100;           // external command arriving on 15 September
        runner.RunTick();          // 16 September: next tick boundary
        Assert.Equal(1, m.ApplyCalls);
        Assert.Equal(1 + 100, table.GetLong("m").Current[0]);
        Assert.Equal(2, m.PostCalls);
    }

    private static string MixedRun(int threads, int chunkSize)
    {
        var cal = new TickCalendar(new CivilDate(1947, 12, 20));
        var table = new Table(13);
        CmdSystem[] systems =
        [
            new(table, "d", Cadence.Daily) { DecideInMerge = 1 },
            new(table, "w", Cadence.Weekly) { DecideInMerge = 3, Watch = "d" },
            new(table, "m", Cadence.Monthly) { DecideInMerge = 5, Watch = "w" },
            new(table, "y", Cadence.Yearly) { DecideInMerge = 7, Watch = "m" },
        ];
        var runner = new TickRunner(table, 9, systems, chunkSize, threads, cal);
        for (int i = 0; i < 1000; i++)
        {
            runner.RunTick();
            if (i % 37 == 0)
                systems[2].Pending += i;
        }
        return StateHash.ComputeHex(table, 9, runner.Tick);
    }

    private static void Run(TickRunner runner, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            runner.RunTick();
    }

    /// <summary>
    /// Adds 1 per run to every row of its own column (plus the value of a watched column at row 0);
    /// <see cref="Pending"/> is a command added by <see cref="ApplyCommands"/>.
    /// </summary>
    private sealed class CmdSystem : ITickSystem
    {
        private readonly Table _table;
        private readonly LongColumn _col;

        public CmdSystem(Table table, string name, Cadence cadence)
        {
            _table = table;
            _col = table.AddLong(name);
            Cadence = cadence;
        }

        public int SystemId => 3;
        public Cadence Cadence { get; }
        public long Pending;
        public long DecideInMerge;
        public string? Watch;
        public int ApplyCalls, PostCalls;
        public List<long> SeenAtCompute { get; } = new();
        public List<long> WatchedAtCompute { get; } = new();

        public bool HasPendingCommands => Pending != 0;

        public void ApplyCommands(in TickContext context)
        {
            ApplyCalls++;
            for (int r = 0; r < _col.Current.Length; r++)
                _col.Next[r] = _col.Current[r] + Pending;
            Pending = 0;
        }

        public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
        {
            long watched = Watch is null ? 0 : _table.GetLong(Watch).Current[0];
            if (rowStart == 0)
            {
                SeenAtCompute.Add(_col.Current[0]);
                WatchedAtCompute.Add(watched);
            }
            for (int r = rowStart; r < rowEnd; r++)
                _col.Next[r] = _col.Current[r] + 1 + (watched % 5) * r;
        }

        public void Merge(in TickContext context, int chunkCount)
        {
            if (DecideInMerge != 0)
                Pending += DecideInMerge;
        }

        public void Post(in TickContext context) => PostCalls++;
    }
}
