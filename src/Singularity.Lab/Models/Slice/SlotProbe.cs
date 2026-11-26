using System.Globalization;
using Singularity.Core.Institutes;
using Singularity.Core.Knowledge;
using Singularity.Session;

namespace Singularity.Lab.Models.Slice;

/// <summary>
/// Liveness of the slot science model (TASK-0187, SCIENCE_SLOTS §4): before the monthly tick of t it resolves what each standing institute works on in t
/// (<see cref="InstituteSystem.Resolve"/>, the same pure function the knowledge layer and the institutes call) and counts, per actor and per period
/// (1945–1990, 1991–), slot-months, idle slot-months, idle slot-months while a workable frontier node (in the frontier, not released at its progress cap,
/// not worked by another slot) was left (the invariant: 0 for an AI actor), focus levels, frontier width and money shortage months (coverage u &lt; 1 after the
/// step). Pure getters of the Session's world only, like <see cref="SupplyProbe"/>; instance buffers, no allocation per month; owner thread between ticks.
/// </summary>
internal sealed class SlotProbe
{
    /// <summary>Last month of the first period: 1990-12 when month 0 is 1945-09.</summary>
    public const long Period1LastMonth = 543;

    /// <summary>First month of the gate window 1947–1990 of the money and discovery gates (Orchestrator, 2026-11-26): 1947-01.</summary>
    public const long GateFirstMonth = 16;

    private const int Metrics = 8;
    private const int MSlots = 0, MIdle = 1, MIdleFree = 2, MFocus1 = 3, MFocus2 = 4, MFrontier = 5, MShort = 6, MMonths = 7;
    private static readonly string[] Names = ["slot_months", "idle", "idle_free", "focus1", "focus2", "frontier", "short_months", "months"];

    private readonly SliceWorld _w;
    private readonly InstituteSystem _inst;
    private readonly KnowledgeSystem _know;
    private readonly string[] _ids;
    private readonly int _player;
    private readonly int[] _node = new int[InstituteSystem.MaxSlotsLimit];
    private readonly byte[] _focus = new byte[InstituteSystem.MaxSlotsLimit];
    private readonly long[] _c;              // [actor][period][metric]
    private readonly long[] _returned, _science, _returned1, _science1, _returned2, _science2, _upkeepMax, _lastWorkable, _institutesMax;   // per actor (TASK-0187 review: the smoke gates of SCIENCE_SLOTS §10)
    private readonly long[] _nodeMonths;      // [actor][node number]: slot-months 1947–1990 on the node (TASK-0193: which nodes hold the slots in the gate window)
    private readonly int _numbers;
    private long _firstViolation = -1;
    private int _firstViolationActor = -1;

    public SlotProbe(SliceSession s, string[] ids)
    {
        _w = s.World;
        _inst = _w.Institutes ?? throw new InvalidOperationException("The slot probe needs the institutes.");
        if (!_inst.SlotsOn)
            throw new InvalidOperationException("The slot probe needs the slot science model.");
        _know = _w.Knowledge;
        _ids = ids;
        _player = _w.Config.PlayerActor ?? -1;
        _c = new long[ids.Length * 2 * Metrics];
        _returned = new long[ids.Length];
        _science = new long[ids.Length];
        _returned1 = new long[ids.Length];
        _science1 = new long[ids.Length];
        _returned2 = new long[ids.Length];
        _science2 = new long[ids.Length];
        _upkeepMax = new long[ids.Length];
        _lastWorkable = new long[ids.Length];
        _institutesMax = new long[ids.Length];
        Array.Fill(_lastWorkable, -1);
        _numbers = _w.Graph.Number.Max() + 1;
        _nodeMonths = new long[ids.Length * _numbers];
    }

    /// <summary>Anchor year from which a node counts as late in the 1947–1990 window: the 1990–2030 tree (TASK-0193).</summary>
    public const int LateAnchorYear = 1991;

    private int At(int a, long t, int m) => (a * 2 + (t <= Period1LastMonth ? 0 : 1)) * Metrics + m;

    /// <summary>Idle AI slot-months while a workable frontier node was left; must be 0.</summary>
    public long AiIdleWithFrontier { get; private set; }

    /// <summary>Before the monthly tick of t (the snapshot the step of t reads).</summary>
    public void BeforeStep(long t)
    {
        KnowledgeGraph g = _w.Graph;
        for (int a = 0; a < _ids.Length; a++)
        {
            int slots = _inst.Layout.SlotCount(a);
            Span<int> node = _node.AsSpan(0, slots);
            Span<byte> focus = _focus.AsSpan(0, slots);
            _inst.Resolve(a, t, node, focus);
            int standing = 0, idle = 0;
            for (int k = 0; k < slots; k++)
            {
                if (node[k] < 0)
                    continue;
                standing++;
                if (node[k] == 0)
                    idle++;
                else
                {
                    if (t >= GateFirstMonth && t <= Period1LastMonth)
                        _nodeMonths[a * _numbers + node[k]]++;
                    if (focus[k] == 1)
                        _c[At(a, t, MFocus1)]++;
                    else if (focus[k] >= 2)
                        _c[At(a, t, MFocus2)]++;
                }
            }
            int workable = 0, untaken = 0;
            for (int r = 0; r < g.Count; r++)
            {
                if (!_know.InFrontier(a, r) || _know.Released(a, r))
                    continue;
                workable++;
                if (node.IndexOf(g.Number[r]) < 0)
                    untaken++;
            }
            if (workable > 0)
                _lastWorkable[a] = t;
            _institutesMax[a] = Math.Max(_institutesMax[a], standing);
            // The research money the step of t reads, and the upkeep share of the institutes built by a project (the share the ceiling of #574 limits).
            long science = (_w.SharesOf(a).Research * _w.Policy.Budget(a)).Raw;
            _science[a] += science;
            if (t >= GateFirstMonth && t <= Period1LastMonth)
                _science1[a] += science;
            else if (t > Period1LastMonth)
                _science2[a] += science;
            long upkeep = 0;
            for (int k = 0; k < slots; k++)
            {
                var h = new InstituteHandle(a, k);
                if (_inst.BuiltByProject(h))
                    upkeep += (_inst.Parameters.UpkeepPerLevel * Singularity.Kernel.Fx6.FromInt(_inst.Layout.TotalLevels(h))).Raw;
            }
            _upkeepMax[a] = Math.Max(_upkeepMax[a], upkeep);
            _c[At(a, t, MSlots)] += standing;
            _c[At(a, t, MIdle)] += idle;
            _c[At(a, t, MFrontier)] += workable;
            _c[At(a, t, MMonths)]++;
            if (idle > 0 && untaken > 0)
            {
                long v = Math.Min(idle, untaken);
                _c[At(a, t, MIdleFree)] += v;
                if (a != _player)
                {
                    AiIdleWithFrontier += v;
                    if (_firstViolation < 0)
                    {
                        _firstViolation = t;
                        _firstViolationActor = a;
                    }
                }
            }
        }
    }

    /// <summary>After the monthly tick of t: the coverage u the step of t paid with.</summary>
    public void AfterStep(long t)
    {
        for (int a = 0; a < _ids.Length; a++)
        {
            if (_know.Coverage(a) < Singularity.Kernel.Fx6.One)
                _c[At(a, t, MShort)]++;
            _returned[a] += _know.ReturnedToBudget(a).Raw;
            if (t >= GateFirstMonth && t <= Period1LastMonth)
                _returned1[a] += _know.ReturnedToBudget(a).Raw;
            else if (t > Period1LastMonth)
                _returned2[a] += _know.ReturnedToBudget(a).Raw;
        }
    }

    /// <summary>model_info entries: slot_ai_idle_with_frontier (the invariant), its first month and actor, and per actor and period p1 (≤ 1990) / p2 the counters.</summary>
    public IEnumerable<KeyValuePair<string, string>> Counters()
    {
        static string I(long v) => v.ToString(CultureInfo.InvariantCulture);
        yield return new("slot_ai_idle_with_frontier", I(AiIdleWithFrontier));
        yield return new("slot_ai_idle_with_frontier_first", _firstViolation < 0 ? "-1" : $"{_ids[_firstViolationActor]}:{I(_firstViolation)}");
        yield return new("slot_upkeep_ceiling_ppm", I(_inst.SlotParameters.UpkeepCeilingShare.Raw));
        yield return new("slot_player", _player < 0 ? "" : _ids[_player]);
        for (int a = 0; a < _ids.Length; a++)
        {
            yield return new($"slot_{_ids[a]}_returned_raw", I(_returned[a]));
            yield return new($"slot_{_ids[a]}_science_raw", I(_science[a]));
            yield return new($"slot_w47_{_ids[a]}_returned_raw", I(_returned1[a]));   // 1947-01..1990-12, the gate window (Orchestrator, 2026-11-26)
            yield return new($"slot_w47_{_ids[a]}_science_raw", I(_science1[a]));
            yield return new($"slot_p2_{_ids[a]}_returned_raw", I(_returned2[a]));    // 1991-01.., information
            yield return new($"slot_p2_{_ids[a]}_science_raw", I(_science2[a]));
            yield return new($"slot_{_ids[a]}_upkeep_max_ppm", I(_upkeepMax[a]));
            yield return new($"slot_{_ids[a]}_workable_last", I(_lastWorkable[a]));
            yield return new($"slot_{_ids[a]}_institutes_max", I(_institutesMax[a]));
            // Slot-months 1947–1990 on nodes anchored in 1991 or later, and those nodes as "number:months" (TASK-0193 GAP36 diagnosis).
            long late = 0;
            var lateNodes = new List<string>();
            for (int number = 1; number < _numbers; number++)
            {
                long m = _nodeMonths[a * _numbers + number];
                int row = _w.Graph.RowOf(number);
                if (m == 0 || row < 0 || _w.Graph.AnchorYear[row] < LateAnchorYear)
                    continue;
                late += m;
                lateNodes.Add($"{I(number)}:{I(m)}");
            }
            yield return new($"slot_w47_{_ids[a]}_late_months", I(late));
            yield return new($"slot_w47_{_ids[a]}_late_nodes", string.Join(";", lateNodes));
        }
        for (int a = 0; a < _ids.Length; a++)
        {
            for (int p = 0; p < 2; p++)
            {
                for (int m = 0; m < Metrics; m++)
                    yield return new($"slot_p{p + 1}_{_ids[a]}_{Names[m]}", I(_c[(a * 2 + p) * Metrics + m]));
            }
        }
    }
}
