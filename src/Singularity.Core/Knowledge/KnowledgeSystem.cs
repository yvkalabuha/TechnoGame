using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Knowledge;

/// <summary>Full knowledge model or one of the knowledge-off controls (slice spec §6.1).</summary>
public enum KnowledgeMode
{
    Full,

    /// <summary>L_K: stochastic schedule with the moments of the full model.</summary>
    ScheduleLk,

    /// <summary>L_K0: fixed lag after the middle of the generation window (print only).</summary>
    ScheduleLk0,
}

/// <summary>Per-node trace of the last month step, for tests and diagnostics; rebuilt every step.</summary>
public readonly record struct KnowledgeNodeStep(
    bool InFrontier, Fx6 W, Fx6 Alloc, Fx6 Kmix, Fx6 CEff, Fx6 DProg, Fx6 ProgNew,
    bool HazardActive, Fx6 H, ulong U, bool Opened);

/// <summary>A broken knowledge invariant (reference README I4, I5).</summary>
public sealed class KnowledgeInvariantException(long month, int actorNumber, string message)
    : Exception($"knowledge invariant violated at month {month}, actor {actorNumber}: {message}")
{
    public long Month { get; } = month;
    public int ActorNumber { get; } = actorNumber;
}

/// <summary>
/// Knowledge layer of the jet slice (spec §3.1, contract v2.3 §4.0, §4.3): a monthly system on a
/// table with one row per actor. Compute of month t reads only the snapshot (state after t−1): frontier,
/// <c>w_n</c>, <c>alloc</c>, <c>c_eff</c>, progress, hazard (stream KNOW keyed by the month index),
/// <c>K_ind</c>. Merge applies discoveries (and licence knowledge in <c>lic_knows</c>) by
/// (actor, node) and <c>K_d</c>. Own columns: <c>know.known.N</c>, <c>know.prog.N</c> per node number N
/// (ascending), then <c>know.K_d.d</c>, <c>know.K_ind.d</c> per discipline. With <c>trackKnownSince</c> (the controls L_C and L_P, which
/// read <c>t_known</c>) also <c>know.since.N</c> per node: −1 unknown, else the month of the merge that made the node known, or
/// <c>startKnownSince</c> for the start knowledge (SLICE_COMPOSITION §13.2). Without the flag the table has no such columns.
/// </summary>
public sealed class KnowledgeSystem : ITickSystem
{
    public const int DefaultSystemId = 100;

    private readonly KnowledgeGraph _g;
    private readonly KnowledgeStart _start;
    private readonly KnowledgeParams _p;
    private readonly IKnowledgeInputs _inputs;
    private readonly CounterRng _know;
    private readonly KnowledgeMode _mode;
    private readonly ImmutableArray<long>? _due;
    private readonly bool _licKnows;
    private readonly int _n, _d, _actors;

    private readonly IntColumn[] _known;
    private readonly LongColumn[] _prog;
    private readonly LongColumn[] _kd, _kind;
    private readonly LongColumn[]? _since;
    private readonly IntColumn[]? _fundN;

    // Scratch, rewritten every step; row a only by the chunk that owns it.
    [Scratch]
    private readonly long[] _wraw, _amount;
    [Scratch]
    private readonly bool[] _granted;
    [Scratch]
    private readonly KnowledgeNodeStep[] _trace;
    [Scratch]
    private readonly long[] _spent;
    [Scratch]
    private readonly List<(int Actor, int Row, bool Discovery)>[] _opened;
    [Scratch]
    private readonly int[] _openedCount;
    [Scratch]
    private readonly List<(int ActorNumber, int NodeNumber)> _lastDiscoveries = new(), _lastGrants = new();

    public KnowledgeSystem(Table table, KnowledgeGraph graph, KnowledgeStart start, KnowledgeParams parameters,
        IKnowledgeInputs inputs, CounterRng know, int chunkCount, KnowledgeMode mode = KnowledgeMode.Full,
        ImmutableArray<long>? schedule = null, bool licenceTransfersKnowledge = false, int systemId = DefaultSystemId,
        bool trackKnownSince = false, long startKnownSince = KnownSinceMarks.StartKnown, bool fundProgrammes = false)
    {
        // The start value must lie below every month of the horizon (tau of the start section starts at the horizon, spec §6.1 v0.24 r2); −1 is the mark «unknown».
        if (trackKnownSince && startKnownSince > KnownSinceMarks.StartKnown)
            throw new ArgumentOutOfRangeException(nameof(startKnownSince), startKnownSince, $"startKnownSince must be at most {KnownSinceMarks.StartKnown}.");
        if (table.RowCount != start.ActorCount)
            throw new ArgumentException($"Table has {table.RowCount} rows, the start has {start.ActorCount} actors.", nameof(table));
        if (start.NodeCount != graph.Count || start.DisciplineCount != graph.DisciplineNames.Length)
            throw new ArgumentException("Start state does not match the graph.", nameof(start));
        if ((mode == KnowledgeMode.Full) != (schedule is null) || (schedule is not null && schedule.Value.Length != start.ActorCount * graph.Count))
            throw new ArgumentException("A schedule (one entry per actor and node) is needed exactly in the schedule modes.", nameof(schedule));
        parameters.Validate();
        _g = graph;
        _start = start;
        _p = parameters;
        _inputs = inputs;
        _know = know;
        _mode = mode;
        _due = schedule;
        _licKnows = licenceTransfersKnowledge;
        _n = graph.Count;
        _d = graph.DisciplineNames.Length;
        _actors = start.ActorCount;
        SystemId = systemId;

        _known = new IntColumn[_n];
        _prog = new LongColumn[_n];
        foreach (int r in graph.Order)
        {
            _known[r] = table.AddInt($"know.known.{graph.Number[r]}");
            _prog[r] = table.AddLong($"know.prog.{graph.Number[r]}");
        }
        _kd = new LongColumn[_d];
        _kind = new LongColumn[_d];
        for (int d = 0; d < _d; d++)
        {
            _kd[d] = table.AddLong($"know.K_d.{graph.DisciplineNames[d]}");
            _kind[d] = table.AddLong($"know.K_ind.{graph.DisciplineNames[d]}");
        }

        if (trackKnownSince)
        {
            _since = new LongColumn[_n];
            foreach (int r in graph.Order)
                _since[r] = table.AddLong($"know.since.{graph.Number[r]}");
        }

        // know.fund.N: how many programmes the actor paid into the node (the `k` of the diminishing return); absent without the reserve.
        if (fundProgrammes && mode == KnowledgeMode.Full)
        {
            _fundN = new IntColumn[_n];
            foreach (int r in graph.Order)
                _fundN[r] = table.AddInt($"know.fund.{graph.Number[r]}");
        }

        var col = new int[_actors];
        var raw = new long[_actors];
        for (int r = 0; r < _n; r++)
        {
            for (int a = 0; a < _actors; a++)
                col[a] = start.IsKnown(a, r) ? 1 : 0;
            _known[r].Load(col);
            if (_since is not null)
            {
                for (int a = 0; a < _actors; a++)
                    raw[a] = col[a] != 0 ? startKnownSince : KnownSinceMarks.Unknown;
                _since[r].Load(raw);
            }
        }
        for (int d = 0; d < _d; d++)
        {
            for (int a = 0; a < _actors; a++)
                raw[a] = start.KD[a * _d + d].Raw;
            _kd[d].Load(raw);
            for (int a = 0; a < _actors; a++)
                raw[a] = start.KInd[a * _d + d].Raw;
            _kind[d].Load(raw);
        }

        _wraw = new long[_actors * _n];
        _granted = new bool[_actors * _n];
        _trace = new KnowledgeNodeStep[_actors * _n];
        _amount = new long[_actors * _d];
        _openedCount = new int[_actors * _d];
        _spent = new long[_actors];
        _opened = new List<(int, int, bool)>[chunkCount];
        for (int c = 0; c < chunkCount; c++)
            _opened[c] = new();
    }

    public int SystemId { get; }

    public Cadence Cadence => Cadence.Monthly;

    // ---- reads of the current state (Table columns)

    public bool IsKnown(int actorRow, int nodeRow) => _known[nodeRow].Current[actorRow] != 0;

    /// <summary>Whether the table carries <c>know.since.N</c>.</summary>
    public bool TracksKnownSince => _since is not null;

    /// <summary>
    /// <c>t_known</c> of the node for the actor in the snapshot: −1 if unknown, the month of the merge that made it known, or <c>startKnownSince</c>.
    /// Throws without <c>trackKnownSince</c>: a silent −1 would make a control read «unknown» everywhere.
    /// </summary>
    public long KnownSince(int actorRow, int nodeRow) =>
        (_since ?? throw new InvalidOperationException("The knowledge system does not track t_known (trackKnownSince = false)."))[nodeRow].Current[actorRow];

    public Fx6 Prog(int actorRow, int nodeRow) => Fx6.FromRaw(_prog[nodeRow].Current[actorRow]);

    public Fx6 KD(int actorRow, int discipline) => Fx6.FromRaw(_kd[discipline].Current[actorRow]);

    public Fx6 KInd(int actorRow, int discipline) => Fx6.FromRaw(_kind[discipline].Current[actorRow]);

    /// <summary>Need of a node on the progress scale: θ_start for a hazard node (where its discovery chance begins), 1 otherwise (the scale of <c>why_not</c> links).</summary>
    public Fx6 NeedOf(int nodeRow) => _g.IsHazard[nodeRow] ? _p.ThetaStart : Fx6.One;

    /// <summary>Most rows <see cref="ResearchInProgress"/> takes (the buffer of shares is on the stack).</summary>
    public const int MaxResearchRows = 64;

    /// <summary>
    /// Research in progress of the actor: frontier nodes with <c>prog &gt; 0</c>, the ones nearest to completion first (<c>prog / need</c> descending, ties by row).
    /// Fills <paramref name="rows"/> with node rows and returns their count (at most its length, at most <see cref="MaxResearchRows"/>). A pure read of the snapshot; allocates nothing on the heap.
    /// </summary>
    public int ResearchInProgress(int actorRow, Span<int> rows)
    {
        if (rows.Length > MaxResearchRows)
            throw new ArgumentOutOfRangeException(nameof(rows), $"at most {MaxResearchRows} rows");
        int count = 0;
        Span<Fx6> share = stackalloc Fx6[rows.Length];
        for (int r = 0; r < _n && rows.Length > 0; r++)
        {
            Fx6 prog = Prog(actorRow, r);
            if (prog <= Fx6.Zero || !InFrontier(actorRow, r))
                continue;
            Fx6 need = NeedOf(r);
            Fx6 s = need > Fx6.Zero ? prog / need : Fx6.One;   // θ_start = 0 is a supported setting: a hazard node with progress is at its threshold
            if (count == rows.Length && s <= share[count - 1])
                continue;
            int at = count < rows.Length ? count++ : count - 1;
            while (at > 0 && s > share[at - 1])
            {
                share[at] = share[at - 1];
                rows[at] = rows[at - 1];
                at--;
            }
            share[at] = s;
            rows[at] = r;
        }
        return count;
    }

    /// <summary>The node is unknown and its requirements are known: it is in the actor's frontier (spec §3.1).</summary>
    public bool InFrontier(int actorRow, int nodeRow) => !IsKnown(actorRow, nodeRow) && RequiresKnown(actorRow, nodeRow);

    /// <summary><c>c_eff</c> of a node for the actor in the snapshot (spec §3.1); a pure function of the snapshot.</summary>
    public Fx6 CEff(int actorRow, int nodeRow, long month) => CEffOf(actorRow, nodeRow, KMix(actorRow, nodeRow), month);

    /// <summary>
    /// <c>Δprog</c> a frontier node gets in month <paramref name="month"/> at the current budget and weights (the speed <c>why_not</c>
    /// reads, contract §6); 0 outside the frontier. Pure function of the snapshot and the month's inputs; no RNG, no state.
    /// </summary>
    public Fx6 ProgressRate(int actorRow, int nodeRow, long month)
    {
        if (!InFrontier(actorRow, nodeRow))
            return Fx6.Zero;
        Fx6 wsum = Fx6.Zero, wn = Fx6.Zero;
        foreach (int r in _g.Order)
        {
            if (!InFrontier(actorRow, r))
                continue;
            Fx6 w = _inputs.Priority(actorRow, r, month) * _inputs.J(actorRow, r, month);
            wsum += w;
            if (r == nodeRow)
                wn = w;
        }
        if (wsum == Fx6.Zero)
            return Fx6.Zero;
        return _inputs.ResearchBudget(actorRow, month) * (wn / wsum) / CEff(actorRow, nodeRow, month);
    }

    /// <summary>Programmes the actor has paid into the node so far (0 without the reserve).</summary>
    public int ProgrammesOn(int actorRow, int nodeRow) => _fundN is null ? 0 : _fundN[nodeRow].Current[actorRow];

    /// <summary>
    /// Progress a programme of <paramref name="amount"/> adds to a node now: <c>M / c_eff · 1/(1+k)</c>, the same <c>c_eff</c> (ahead-of-time multiplier
    /// included) as the monthly money. A pure read of the snapshot; the preview and the step share it.
    /// </summary>
    public Fx6 ProgrammeGain(int actorRow, int nodeRow, long month, Fx6 amount)
    {
        Fx6 cap = ProgrammeCapacity(actorRow, nodeRow, month);
        return Gain(amount < cap ? amount : cap, CEff(actorRow, nodeRow, month), ProgrammesOn(actorRow, nodeRow));
    }

    /// <summary>
    /// The most money a programme can put into the node in month t: what the node still absorbs before its ceiling (1 for a plain node, <c>prog_cap</c> for a
    /// hazard node, after this month's regular money) times <c>c_eff·(1+k)</c>. Zero outside the frontier or at the ceiling. A pure read of the snapshot.
    /// </summary>
    public Fx6 ProgrammeCapacity(int actorRow, int nodeRow, long month)
    {
        if (!InFrontier(actorRow, nodeRow))
            return Fx6.Zero;
        Fx6 basis = Prog(actorRow, nodeRow) + ProgressRate(actorRow, nodeRow, month);
        Fx6 ceiling = _g.IsHazard[nodeRow] ? _p.ProgCap : basis > Fx6.One ? basis : Fx6.One;
        Fx6 room = ceiling - basis;
        return room > Fx6.Zero ? room * CEff(actorRow, nodeRow, month) * Fx6.FromInt(1 + ProgrammesOn(actorRow, nodeRow)) : Fx6.Zero;
    }

    private static Fx6 Gain(Fx6 amount, Fx6 ceff, int k) => amount / ceff * (Fx6.One / Fx6.FromInt(1 + k));

    /// <summary>
    /// Reserve money the actor's programme takes in month t: the funded amount when its node is in the frontier, else zero (the money stays).
    /// The reserve reads this, so what leaves the account is exactly what the node received.
    /// </summary>
    public Fx6 ProgrammeSpend(int actorRow, long month) => FundedAmount(actorRow, month, out _);

    private Fx6 FundedAmount(int a, long t, out int row)
    {
        row = -1;
        if (_fundN is null || !_inputs.FundProgramme(a, t, out int r, out Fx6 amount) || r < 0 || amount <= Fx6.Zero || !InFrontier(a, r))
            return Fx6.Zero;
        // Only what the node can absorb is spent: money that the ceiling would cut is not taken, and a node at its ceiling costs nothing (and does not count as a programme).
        Fx6 cap = ProgrammeCapacity(a, r, t);
        if (cap <= Fx6.Zero)
            return Fx6.Zero;
        row = r;
        return amount < cap ? amount : cap;
    }

    // ---- diagnostics of the last step (tests, journals): scratch outside the Table, not state, not saved
    // or hashed, valid only right after a step of this system. Simulation code must not read them.

    /// <summary>Diagnostics only: R&amp;D money allocated by the actor in the last step (≤ <c>S_a</c>; the rest burns, §3.4).</summary>
    public Fx6 LastSpent(int actorRow) => Fx6.FromRaw(_spent[actorRow]);

    /// <summary>Diagnostics only: per-node trace of the last step.</summary>
    public KnowledgeNodeStep LastStep(int actorRow, int nodeRow) => _trace[actorRow * _n + nodeRow];

    /// <summary>Diagnostics only: discoveries of the last step by (actor number, node number).</summary>
    public IReadOnlyList<(int ActorNumber, int NodeNumber)> LastDiscoveries => _lastDiscoveries;

    /// <summary>Diagnostics only: nodes made known by licences in the last step (<c>lic_knows</c>), by (actor number, node number).</summary>
    public IReadOnlyList<(int ActorNumber, int NodeNumber)> LastLicenceGrants => _lastGrants;

    // ---- tick phases

    public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
    {
        long t = MonthOf(context);
        List<(int Actor, int Row, bool Discovery)> opened = _opened[chunk];
        opened.Clear();
        for (int a = rowStart; a < rowEnd; a++)
        {
            Array.Clear(_trace, a * _n, _n);
            Array.Clear(_amount, a * _d, _d);
            _spent[a] = 0;
            if (_mode == KnowledgeMode.Full)
                ComputeFull(a, t, opened);
            else
                ComputeSchedule(a, t, opened);
            if (_licKnows)
                GrantLicences(a, t, opened);
        }
    }

    public void Merge(in TickContext context, int chunkCount)
    {
        _lastDiscoveries.Clear();
        _lastGrants.Clear();
        Array.Clear(_openedCount);
        for (int c = 0; c < chunkCount; c++)
        {
            // Per actor, discoveries precede licence grants in the list, so a node both discovered and
            // licensed in one step counts as a discovery in opened_d.
            foreach ((int a, int r, bool discovery) in _opened[c])
            {
                if (_known[r].Next[a] != 0)
                    continue;
                _known[r].Next[a] = 1;
                if (_since is not null)
                    _since[r].Next[a] = MonthOf(context);
                (discovery ? _lastDiscoveries : _lastGrants).Add((_start.ActorNumber[a], _g.Number[r]));
                if (discovery && _g.Discipline[r] >= 0)
                    _openedCount[a * _d + _g.Discipline[r]]++;
            }
        }
        _lastDiscoveries.Sort();
        _lastGrants.Sort();
        if (_mode != KnowledgeMode.Full)
            return;
        for (int a = 0; a < _actors; a++)
        {
            for (int d = 0; d < _d; d++)
            {
                Fx6 delta = KnowledgeMath.KdDelta(Fx6.FromRaw(_amount[a * _d + d]), _openedCount[a * _d + d], _p.MuK, _p.SRef, _p.KNode);
                _kd[d].Next[a] = (Fx6.FromRaw(_kd[d].Current[a]) + delta).Raw;
            }
        }
    }

    public void Post(in TickContext context)
    {
        // The step that just ran is the month of the previous day tick.
        long month = context.Calendar!.MonthIndex(context.Tick - 1);
        for (int a = 0; a < _actors; a++)
        {
            for (int r = 0; r < _n; r++)
            {
                if (_g.IsHazard[r] && _prog[r].Current[a] > _p.ProgCap.Raw)
                    throw new KnowledgeInvariantException(month, _start.ActorNumber[a], $"hazard prog of node {_g.Number[r]} is above prog_cap (I5)");
                if (IsKnown(a, r) && !RequiresKnown(a, r))
                    throw new KnowledgeInvariantException(month, _start.ActorNumber[a], $"node {_g.Number[r]} is known without its requirements (I5)");
            }
        }
    }

    // ---- compute parts

    private void ComputeFull(int a, long t, List<(int, int, bool)> opened)
    {
        int actor = _start.ActorNumber[a];
        Fx6 s = _inputs.ResearchBudget(a, t);
        Fx6 fundAmount = FundedAmount(a, t, out int fundRow);
        Fx6 wsum = Fx6.Zero;
        foreach (int r in _g.Order)
        {
            if (!IsKnown(a, r) && RequiresKnown(a, r))
            {
                Fx6 w = _inputs.Priority(a, r, t) * _inputs.J(a, r, t);
                _wraw[a * _n + r] = w.Raw;
                wsum += w;
                _trace[a * _n + r] = default(KnowledgeNodeStep) with { InFrontier = true };
            }
        }

        Fx6 spent = Fx6.Zero;
        foreach (int r in _g.Order)
        {
            bool funded = r == fundRow;
            if (!_trace[a * _n + r].InFrontier || (wsum == Fx6.Zero && !funded))
                continue;
            Fx6 wn = wsum == Fx6.Zero ? Fx6.Zero : Fx6.FromRaw(_wraw[a * _n + r]) / wsum;
            Fx6 alloc = s * wn;
            spent += alloc;
            int disc = _g.Discipline[r];
            Fx6 kmix = KMix(a, r);
            Fx6 ceff = CEffOf(a, r, kmix, t);
            Fx6 dprog = alloc / ceff;
            Fx6 prog = Prog(a, r) + dprog;
            if (funded)
            {
                // The programme adds to the node but does not carry a plain node above 1 by itself; a hazard node keeps its prog_cap below.
                Fx6 ceiling = prog > Fx6.One ? prog : Fx6.One;
                prog += Gain(fundAmount, ceff, _fundN![r].Current[a]);
                if (!_g.IsHazard[r] && prog > ceiling)
                    prog = ceiling;
                dprog = prog - Prog(a, r);
                _fundN[r].Next[a] = _fundN[r].Current[a] + 1;
            }
            bool open;
            HazardStep hz = default;
            ulong u = 0;
            if (_g.IsHazard[r])
            {
                if (prog > _p.ProgCap)
                    prog = _p.ProgCap;
                hz = KnowledgeMath.Hazard(prog, _p.ThetaStart, _p.HMax, _p.YHalf);
                if (hz.Active)
                    u = KnowledgeRng.Hazard(_know, t, actor, _g.Number[r]);
                open = hz.Active && CounterRng.Chance(u, hz.H);
            }
            else
            {
                open = prog >= Fx6.One;
            }
            _prog[r].Next[a] = prog.Raw;
            if (disc >= 0)
                _amount[a * _d + disc] += alloc.Raw;
            if (open)
                opened.Add((a, r, true));
            _trace[a * _n + r] = new KnowledgeNodeStep(true, wn, alloc, kmix, ceff, dprog, prog, hz.Active, hz.H, u, open);
        }
        if (spent > s)
            throw new KnowledgeInvariantException(t, actor, $"R&D spent {spent} exceeds S_a {s} (I4)");
        _spent[a] = spent.Raw;

        for (int d = 0; d < _d; d++)
        {
            Fx6 v = _inputs.DisciplineVolume(a, d, t);
            _kind[d].Next[a] = KnowledgeMath.KIndStep(KInd(a, d), KD(a, d), v, _p.Eta, _p.VRef, _p.ThetaInd, _p.DeltaInd).Raw;
        }
    }

    // Controls: only known_processes(a) is produced; hazard, c_eff, K_d, K_ind and j are not used (§6.1).
    private void ComputeSchedule(int a, long t, List<(int, int, bool)> opened)
    {
        foreach (int r in _g.Order)
        {
            if (!IsKnown(a, r) && _due!.Value[a * _n + r] <= t)
                opened.Add((a, r, true));
        }
    }

    // lic_knows (§6.7, v0.6, v0.13): the licensed ENG node and PRC nodes and their whole requires-closure unknown to the buyer (any
    // type; an OR-group without a known member brings its lowest-numbered member and that member's closure).
    // Grants follow this actor's discoveries in the list, so a node both discovered and licensed counts as a
    // discovery; grants never enter opened_d or amount_d.
    private void GrantLicences(int a, long t, List<(int, int, bool)> opened)
    {
        Array.Clear(_granted, a * _n, _n);
        foreach (int r in _inputs.LicensedEngines(a, t))
        {
            if (_g.Type[r] is not (NodeType.ENG or NodeType.PRC))
                throw new ArgumentException($"Licence for node {_g.Number[r]}: only the engine (ENG) and the processes (PRC) of the seller's project are licensed.");
            Grant(a, r, opened);
        }
    }

    private void Grant(int a, int r, List<(int, int, bool)> opened)
    {
        if (IsKnown(a, r) || _granted[a * _n + r])
            return;
        _granted[a * _n + r] = true;
        opened.Add((a, r, false));
        RequirementLists req = _g.Requires;
        for (int g = req.GroupStart[r]; g < req.GroupStart[r + 1]; g++)
        {
            int lowest = -1;
            bool any = false;
            foreach (int m in req.GroupMembers(g))
            {
                any |= IsKnown(a, m) || _granted[a * _n + m];
                if (lowest < 0 || _g.Number[m] < _g.Number[lowest])
                    lowest = m;
            }
            if (!any)
                Grant(a, lowest, opened);
        }
    }

    // ---- snapshot helpers

    private Fx6 KMix(int a, int r)
    {
        int disc = _g.Discipline[r];
        return disc < 0 ? Fx6.Zero : _g.Type[r] is NodeType.ENG or NodeType.PRC ? KD(a, disc) + KInd(a, disc) : KD(a, disc);
    }

    // The ahead-of-time multiplier is a pure function of (node, month): a few integer operations, no table to refresh, nothing stored.
    private Fx6 CEffOf(int a, int r, Fx6 kmix, long month) =>
        KnowledgeMath.CEff(KnowledgeMath.CMoney(_g.CostN[r], _p.SRef) * KnowledgeMath.AheadMultiplier(_p.AheadCostK, _p.AheadCostPow, _p.AheadCostMax, _g.AnchorYear[r], month, _p.CalendarOriginMonths), _p.KDiff, KnownElsewhere(a, r), _p.KK, kmix, _p.KRef, InformsSum(a, r));

    private bool RequiresKnown(int a, int r)
    {
        RequirementLists req = _g.Requires;
        for (int g = req.GroupStart[r]; g < req.GroupStart[r + 1]; g++)
        {
            bool any = false;
            foreach (int m in req.GroupMembers(g))
                any |= IsKnown(a, m);
            if (!any)
                return false;
        }
        return true;
    }

    private bool KnownElsewhere(int a, int r)
    {
        ReadOnlySpan<int> known = _known[r].Current;
        for (int b = 0; b < known.Length; b++)
        {
            if (b != a && known[b] != 0)
                return true;
        }
        return false;
    }

    private Fx6 InformsSum(int a, int r)
    {
        Fx6 sum = Fx6.Zero;
        for (int e = _g.InformedStart[r]; e < _g.InformedStart[r + 1]; e++)
        {
            if (IsKnown(a, _g.InformedFrom[e]))
                sum += _g.InformedWeight[e];
        }
        return sum;
    }

    private static long MonthOf(in TickContext context) =>
        (context.Calendar ?? throw new InvalidOperationException("The knowledge system is monthly and needs a runner calendar."))
            .MonthIndex(context.Tick);
}
