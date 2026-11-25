using System.Collections.Immutable;
using System.Globalization;
using Singularity.Core.Commands;
using Singularity.Core.Institutes;
using Singularity.Core.Knowledge;
using Singularity.Core.Policy;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Lab.Models.Slice;

/// <summary>When the scripted player starts the projects of its candidate institutes (TASK-0173, P-7 of research-institutes-priorities rev 4 §9).</summary>
public enum InstituteBuild
{
    /// <summary>Never.</summary>
    None = 0,

    /// <summary>At the first month the window allows (ref_year − 10, or the start of the run).</summary>
    Early = 1,

    /// <summary>When the AI rule would start it: the project opens about the historical year (§7).</summary>
    Hist = 2,

    /// <summary>Five years before the AI rule (not before the window).</summary>
    HistMinus5 = 3,

    /// <summary>Five years after the AI rule.</summary>
    HistPlus5 = 4,
}

/// <summary>
/// The institute keys of the Lab (TASK-0173): &lt;c&gt;INST_PLAYER&lt;/c&gt; (the actor the script plays; null = no player, the AI plays every actor),
/// &lt;c&gt;INST_BUILD&lt;/c&gt;, &lt;c&gt;INST_GOALS&lt;/c&gt; (the player takes the adviser's goal for every free slot, or sets none), &lt;c&gt;INST_FUND&lt;/c&gt; (the share of
/// the planned research money the player funds while one of its projects runs; the rest of the slot is left unspent in Quality) and
/// &lt;c&gt;INST_PROBE&lt;/c&gt; (tables inst_goal, inst_build and the P-8 snapshot counters).
/// </summary>
public sealed record InstituteSpec(string? Player, InstituteBuild Build, bool Goals, Fx6 Fund, bool Probe)
{
    /// <summary>One row per goal a slot of any actor worked on: from the month it was first seen to the month it left the slot.</summary>
    public static readonly MetricTableSpec GoalTable = new("inst_goal", ["actor", "slot", "node", "mult", "t_set", "t_end", "known"]);

    /// <summary>One row per project of any actor: planned and actual opening, the premium paid and the one the actual opening year would give, money paid.</summary>
    public static readonly MetricTableSpec BuildTable = new("inst_build",
        ["actor", "slot", "id", "ref_year", "levels", "start", "months", "open", "open_year", "planned_year", "premium", "premium_actual", "paid", "months_run"]);

    /// <summary>
    /// The month the player starts the project of a slot under &lt;paramref name="plan"/&gt;: &lt;paramref name="windowStart"/&gt; is the first month the window allows
    /// (clamped to 0), &lt;paramref name="aiStart"/&gt; the month the AI rule would start it. −1 = never.
    /// </summary>
    public static long PlannedStart(InstituteBuild plan, long windowStart, long aiStart)
    {
        long w = Math.Max(0, windowStart);
        return plan switch
        {
            InstituteBuild.None => -1,
            InstituteBuild.Early => w,
            InstituteBuild.Hist => Math.Max(w, aiStart),
            InstituteBuild.HistMinus5 => Math.Max(w, aiStart - 60),
            InstituteBuild.HistPlus5 => Math.Max(w, aiStart + 60),
            _ => throw new ArgumentOutOfRangeException(nameof(plan)),
        };
    }

    /// <summary>The AI shares with the research item cut to &lt;c&gt;fund·research&lt;/c&gt; (floored); the cut sits in Quality, which no system reads.</summary>
    public static BudgetShares Cut(BudgetShares ai, Fx6 fund)
    {
        if (fund <= Fx6.Zero || fund > Fx6.One)
            throw new ArgumentOutOfRangeException(nameof(fund), fund, "INST_FUND lies in (0; 1].");
        Fx6 r = ai.Research * fund;
        var s = new BudgetShares(r, ai.Capability, ai.Effort, ai.Procurement, ai.Quality + (ai.Research - r));
        return s.IsValid ? s : throw new InvalidOperationException($"INST_FUND: computed shares {s} are not valid.");
    }
}

/// <summary>
/// The scripted institute player of TASK-0173 and the probes of P-7/P-8 (docs/simulation/institutes-p7-measurement.md). Called on the owner thread between
/// monthly ticks only (as &lt;see cref="LeverScript"/&gt;): reads the world through Session getters, writes nothing but commands (source Harness, id 2). Buffers per instance.
/// </summary>
internal sealed class InstituteScript
{
    private const int SourceId = 2;

    private readonly SliceSession _s;
    private readonly InstituteSpec _spec;
    private readonly IMetricSink? _sink;
    private readonly int _goalTable, _buildTable;
    private readonly int _player = -1;
    private readonly InstituteSystem _inst;
    private readonly KnowledgeInputsAdapter _k;
    private readonly int _actors, _slots;
    private readonly int[] _goal, _goalSet;
    private readonly long[] _goalMult;
    private readonly long[] _paid, _monthsRun;
    private readonly bool[] _reported;
    private readonly long[] _maxTake;
    private readonly int[] _node;      // slot model: Resolve buffers of one actor (instance, never static)
    private readonly byte[] _focus;
    private readonly int[] _maxConcurrent;
    private int _local;
    private BudgetShares? _sent;

    public InstituteScript(SliceSession s, InstituteSpec spec, IMetricSink? sink, int goalTable, int buildTable)
    {
        _s = s;
        _spec = spec;
        _inst = s.World.Institutes ?? throw new ParamException("The INST_* keys need INSTITUTES = true.");
        if (spec.Probe && (sink is null || goalTable < 0 || buildTable < 0))
            throw new ArgumentException("INST_PROBE needs the sink and the indexes of the inst_goal and inst_build tables.", nameof(sink));
        _sink = sink;
        _goalTable = goalTable;
        _buildTable = buildTable;
        ImmutableArray<string> ids = s.Layout.ActorIds;
        if (spec.Player is { } id)
        {
            _player = ids.IndexOf(id);
            if (_player < 0)
                throw new ParamException($"INST_PLAYER '{id}' is not an actor of the scenario ({string.Join(", ", ids)}).");
            if (s.World.Config.PlayerActor != _player)
                throw new InvalidOperationException("INST_PLAYER and SliceRunConfig.PlayerActor disagree.");
        }
        _k = new KnowledgeInputsAdapter(s.World);
        _actors = ids.Length;
        _slots = _inst.Layout.MaxSlots;
        _goal = new int[_actors * _slots];
        _goalSet = new int[_actors * _slots];
        _goalMult = new long[_actors * _slots];
        _paid = new long[_actors * _slots];
        _monthsRun = new long[_actors * _slots];
        _reported = new bool[_actors * _slots];
        _maxTake = new long[_actors];
        _maxConcurrent = new int[_actors];
        _node = new int[InstituteSystem.MaxSlotsLimit];
        _focus = new byte[InstituteSystem.MaxSlotsLimit];
    }

    public int Commands { get; private set; }

    public int Refused { get; private set; }

    public long P8GoalMonths { get; private set; }

    public long P8Slower { get; private set; }

    public long P8OffMonths { get; private set; }

    public long P8OffSlower { get; private set; }

    public long P8WorstShortfall { get; private set; }

    /// <summary>Lowest with/without rate ratio seen, ppm (1_000_000 = equal).</summary>
    public long P8MinRatioPpm { get; private set; } = 1_000_000;

    /// <summary>Before month 0 and after the step of each month t: the orders the step of &lt;paramref name="nextMonth"/&gt; reads.</summary>
    public void Issue(long nextMonth)
    {
        if (_player < 0)
            return;
        InstituteLayout l = _inst.Layout;
        int slots = l.SlotCount(_player);
        bool building = false;
        for (int k = 0; k < slots; k++)
        {
            var h = new InstituteHandle(_player, k);
            if (_inst.Standing(h))
                continue;
            if (_inst.Building(h))
            {
                building = true;
                continue;
            }
            long window = l.FirstMonthOf(l.RefYear(h) - _inst.Parameters.WindowYears);
            long ai = l.FirstMonthOf(l.RefYear(h)) - _inst.BuildMonths(h);
            long start = InstituteSpec.PlannedStart(_spec.Build, window, ai);
            if (start < 0 || nextMonth < start || _inst.CanBuild(h, nextMonth) != BuildCheck.Ok)
                continue;
            Submit(new BuildInstitute(_player, k), nextMonth);
            building = true;
        }
        if (_spec.Goals)
        {
            Span<int> goals = stackalloc int[InstituteSystem.MaxSlotsLimit];
            Span<bool> starts = stackalloc bool[InstituteSystem.MaxSlotsLimit];
            _inst.Propose(_player, nextMonth, goals, starts);
            for (int k = 0; k < slots; k++)
            {
                // Slot model (TASK-0187): the adviser's node goes into the list of the free slot (assign_node), the shares model keeps set_priority_node.
                // The script confirms a start the money covers badly (Acknowledged, §1.2) as the AI would: without it a short country is held back at
                // apply time, which Submit does not report (TASK-0187 part 2: GBR idled 19.6 % of its slot-months 1945-1990 in 3 seeds).
                if (goals[k] != 0)
                    Submit(_inst.SlotsOn ? new AssignNode(_player, k, goals[k], Acknowledged: true) : new SetPriorityNode(_player, k, goals[k]), nextMonth);
            }
        }
        if (_spec.Fund < Fx6.One)
        {
            BudgetShares ai = _s.World.Policy.Shares(_player);
            BudgetShares want = building ? InstituteSpec.Cut(ai, _spec.Fund) : ai;
            if (_sent != want)
            {
                Submit(new SetBudgetShares(_player, want), nextMonth);
                _sent = want;
            }
        }
    }

    private void Submit(SliceCommand c, long month)
    {
        CommandVerdict v = _s.Submit(SourceClass.Harness, SourceId, _local++, c);
        Commands++;
        if (!v.Accepted)
        {
            // A refused order is a defect of the script (it checks the same rules first), except a goal the adviser proposes again for a slot ordered this month
            // (slot model: or a node that already stands in another list of the player).
            Refused++;
            if (c is not (SetPriorityNode or AssignNode))
                throw new InvalidOperationException($"the institute script was refused at month {month}: {c} — {v.Message}");
        }
    }

    /// <summary>Before the step of month t: the money each running project takes in that step (its rate on the research money of the snapshot).</summary>
    public void BeforeStep(long month)
    {
        InstituteLayout l = _inst.Layout;
        for (int a = 0; a < _actors; a++)
        {
            Fx6 money = _s.World.SharesOf(a).Research * _s.World.Policy.Budget(a);
            long take = 0;
            int running = 0;
            for (int k = 0; k < l.SlotCount(a); k++)
            {
                var h = new InstituteHandle(a, k);
                if (_inst.BuiltByProject(h))
                    take += (_inst.Parameters.UpkeepPerLevel * Fx6.FromInt(l.TotalLevels(h))).Raw;
                if (!_inst.Building(h))
                    continue;
                Fx6 rate = _inst.BuildRate(h, month);
                take += rate.Raw;
                running++;
                _paid[a * _slots + k] += (rate * money).Raw;
                _monthsRun[a * _slots + k]++;
            }
            _maxTake[a] = Math.Max(_maxTake[a], take);
            _maxConcurrent[a] = Math.Max(_maxConcurrent[a], running);
        }
    }

    /// <summary>After the step of month t: goal rows, opened projects, and the P-8 check of the goals the step of t + 1 will fund.</summary>
    public void Observe(long month)
    {
        if (!_spec.Probe)
            return;
        Span<int> rows = stackalloc int[InstituteSystem.MaxSlotsLimit];
        Span<Fx6> mults = stackalloc Fx6[InstituteSystem.MaxSlotsLimit];
        InstituteLayout l = _inst.Layout;
        KnowledgeSystem know = _s.World.Knowledge;
        bool slotsOn = _inst.SlotsOn;
        for (int a = 0; a < _actors; a++)
        {
            // Slot model: the node a slot works on in month + 1, the same pure function the step of month + 1 calls.
            if (slotsOn)
                _inst.Resolve(a, month + 1, _node.AsSpan(0, l.SlotCount(a)), _focus.AsSpan(0, l.SlotCount(a)));
            for (int k = 0; k < l.SlotCount(a); k++)
            {
                var h = new InstituteHandle(a, k);
                int i = a * _slots + k;
                int g = slotsOn ? (_node[k] > 0 ? _node[k] : 0) : _inst.GoalNode(h);
                if (g != _goal[i])
                {
                    if (_goal[i] != 0)
                        GoalRow(a, k, month);
                    _goal[i] = g;
                    _goalSet[i] = checked((int)month);
                    int row = _s.World.Graph.RowOf(g);
                    int disc = row < 0 ? -1 : _s.World.Graph.Discipline[row];
                    _goalMult[i] = g == 0 || row < 0 ? 0
                        : slotsOn ? _inst.InstituteMultiplier(h, row).Raw
                        : InstituteMath.LevelMultiplier(disc < 0 ? 0 : _inst.Level(h, disc), _inst.Parameters).Raw;
                }
                if (!_reported[i] && _inst.BuiltByProject(h))
                {
                    BuildRow(h, _inst.OpenedMonth(h));
                    _reported[i] = true;
                }
            }
            // P-8 compares the shares path with and without goals; the slot model has no shares path.
            int n = slotsOn ? 0 : _inst.Goals(a, rows, mults);
            if (n == 0)
                continue;
            long t = month + 1;
            Fx6 wsum = Fx6.Zero;
            foreach (int r in _s.World.Graph.Order)
            {
                if (know.InFrontier(a, r))
                    wsum += _k.Priority(a, r, t) * _k.J(a, r, t);
            }
            if (wsum == Fx6.Zero)
                continue;
            Fx6 budget = _k.ResearchBudget(a, t);
            for (int j = 0; j < n; j++)
            {
                int r = rows[j];
                if (!know.InFrontier(a, r))
                    continue;
                // Without any goal the old path of KnowledgeSystem.ProgressRate: budget · (w / wsum) / c_eff.
                Fx6 without = budget * (_k.Priority(a, r, t) * _k.J(a, r, t) / wsum) / know.CEff(a, r, t);
                Fx6 with = know.ProgressRate(a, r, t);
                bool off = mults[j] == Fx6.One;
                P8GoalMonths++;
                if (off)
                    P8OffMonths++;
                if (with < without)
                {
                    P8Slower++;
                    if (off)
                        P8OffSlower++;
                    P8WorstShortfall = Math.Max(P8WorstShortfall, without.Raw - with.Raw);
                }
                if (without > Fx6.Zero)
                    P8MinRatioPpm = Math.Min(P8MinRatioPpm, (long)((Int128)with.Raw * 1_000_000 / without.Raw));
            }
        }
    }

    /// <summary>At the end of the run: the goals still open and the projects not finished.</summary>
    public void End(long lastMonth)
    {
        if (!_spec.Probe)
            return;
        InstituteLayout l = _inst.Layout;
        for (int a = 0; a < _actors; a++)
        {
            for (int k = 0; k < l.SlotCount(a); k++)
            {
                var h = new InstituteHandle(a, k);
                int i = a * _slots + k;
                if (_goal[i] != 0)
                    GoalRow(a, k, lastMonth + 1);
                if (!_reported[i] && (_inst.Building(h) || _monthsRun[i] > 0))
                    BuildRow(h, -1);
            }
        }
    }

    private void GoalRow(int a, int k, long end)
    {
        int i = a * _slots + k;
        int row = _s.World.Graph.RowOf(_goal[i]);
        bool known = row >= 0 && _s.World.Knowledge.IsKnown(a, row);
        _sink!.Row(_goalTable, _s.Layout.ActorIds[a], MetricFormat.Int(k), MetricFormat.Int(_goal[i]), MetricFormat.Fx6(_goalMult[i]),
            MetricFormat.Int(_goalSet[i]), MetricFormat.Int(end), known ? "1" : "0");
    }

    private void BuildRow(InstituteHandle h, long opened)
    {
        InstituteLayout l = _inst.Layout;
        int i = h.Actor * _slots + h.Slot;
        long start = _inst.BuildStart(h);
        int months = _inst.BuildMonths(h);
        int refYear = l.RefYear(h);
        int planned = start >= 0 ? l.YearOf(start + months - 1) : 0;
        int openYear = opened >= 0 ? l.YearOf(opened) : 0;
        // Since TASK-0175 the price follows the expected opening year at the current funding: the premium is the one of the actual opening year.
        Fx6 premium = start >= 0 && opened >= 0 ? InstituteMath.AheadMultiplier(refYear - openYear, _inst.Parameters) : Fx6.Zero;
        Fx6 actual = opened >= 0 ? InstituteMath.AheadMultiplier(refYear - openYear, _inst.Parameters) : Fx6.Zero;
        _sink!.Row(_buildTable, _s.Layout.ActorIds[h.Actor], MetricFormat.Int(h.Slot), l.IdOf(h), MetricFormat.Int(refYear), MetricFormat.Int(l.TotalLevels(h)),
            MetricFormat.Int(start), MetricFormat.Int(months), MetricFormat.Int(opened), MetricFormat.Int(openYear), MetricFormat.Int(planned),
            MetricFormat.Fx6(premium.Raw), MetricFormat.Fx6(actual.Raw), MetricFormat.Fx6(_paid[i]), MetricFormat.Int(_monthsRun[i]));
    }

    /// <summary>model_info entries: commands, refusals, the P-8 snapshot counters and the largest share of research money the projects and upkeep took.</summary>
    public IEnumerable<KeyValuePair<string, string>> Counters()
    {
        static string I(long v) => v.ToString(CultureInfo.InvariantCulture);
        yield return new("inst_commands", I(Commands));
        yield return new("inst_refused", I(Refused));
        if (_spec.Probe && !_inst.SlotsOn)
        {
            yield return new("inst_p8_goal_months", I(P8GoalMonths));
            yield return new("inst_p8_slower", I(P8Slower));
            yield return new("inst_p8_off_months", I(P8OffMonths));
            yield return new("inst_p8_off_slower", I(P8OffSlower));
            yield return new("inst_p8_worst_shortfall_raw", I(P8WorstShortfall));
            yield return new("inst_p8_min_ratio_ppm", I(P8MinRatioPpm));
        }
        for (int a = 0; a < _actors; a++)
        {
            yield return new($"inst_max_take_ppm_{_s.Layout.ActorIds[a]}", I(_maxTake[a]));
            yield return new($"inst_max_concurrent_{_s.Layout.ActorIds[a]}", I(_maxConcurrent[a]));
        }
    }
}
