using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Industry;

/// <summary>A process of a <c>prc_set</c> for <see cref="IndustrySystem.Profile"/>: number, <c>n_p</c>, <c>base_cost_p</c>.</summary>
public readonly record struct PrcSetItem(int ProcessNumber, Fx6 N, Fx6 BaseCost);

/// <summary>A broken capability invariant (reference README I1–I3).</summary>
public sealed class IndustryInvariantException(long month, int site, string message)
    : Exception($"capability invariant violated at month {month}, site {site}: {message}")
{
    public long Month { get; } = month;
    public int Site { get; } = site;
}

/// <summary>
/// Capability layer of the jet slice (contract v2.3 §4.0–4.2, §4.6; spec §3.2, §3.5, §6.1): a monthly system
/// on a table with one row per site. Compute of month t reads only the snapshot: <c>yield_p</c> → own
/// <c>proc_yield</c> (the yield output runs at in t), <c>culture_H</c> from <c>proc_v</c> of month t−1 and
/// <c>act</c> from the skilled grants of month t−1, <c>tooling_M</c> with the investment in force under the
/// MECH cap. Merge applies <c>report_output</c> by (site, prc) → <c>proc_X</c>, <c>proc_v</c>, and
/// <c>reserve_workforce</c> by (site, cat, requester_id) → <c>granted</c>, visible at t+1. No RNG anywhere in
/// the layer (#28). Own columns per site row: <c>ind.H.c.d</c>, <c>ind.M.c.d</c> per cluster and dimension,
/// then per process number <c>ind.X.N</c>, <c>ind.v.N</c>, <c>ind.y.N</c>, then <c>ind.wf.n.k.R</c>,
/// <c>ind.wf.granted.k.R</c> per category k and requester R.
/// </summary>
public sealed class IndustrySystem : ITickSystem
{
    public const int DefaultSystemId = 200;

    private static readonly string[] DimNames = ["precision", "purity", "quality_control"];

    private readonly IndustryLayout _l;
    private readonly IndustryParams _p;
    private readonly IIndustryInputs _inputs;
    private readonly IndustryMode _mode;
    private readonly ImmutableArray<CapabilityCurve> _curves;
    private readonly int _c, _n, _sites;

    private readonly LongColumn[] _h, _m;
    private readonly LongColumn[] _x, _v, _y;
    private readonly LongColumn[] _wfN, _wfGranted;

    // Scratch, rewritten every step; site s only by the chunk that owns it.
    private readonly ProcessYield[] _yieldTrace;
    private readonly CultureStep[] _cultureTrace;
    private readonly ToolingStep[] _toolingTrace;
    private readonly CultureProcess[][] _procScratch;
    private readonly CultureSpill[][] _spillScratch;
    private readonly List<IndustryMailbox> _mailboxes = new();
    private readonly List<OutputReport> _reports = new(), _lastReports = new();
    private readonly List<WorkforceRequest> _requests = new();

    public IndustrySystem(Table table, IndustryLayout layout, IndustryStart start, IndustryParams parameters,
        IIndustryInputs inputs, int chunkCount, IndustryMode mode = IndustryMode.Full,
        ImmutableArray<CapabilityCurve> curves = default, int systemId = DefaultSystemId)
    {
        if (table.RowCount != layout.SiteCount)
            throw new ArgumentException($"Table has {table.RowCount} rows, the layout has {layout.SiteCount} sites.", nameof(table));
        if (start.Layout != layout)
            throw new ArgumentException("Start state belongs to another layout.", nameof(start));
        if ((mode == IndustryMode.CurveLc) == curves.IsDefault)
            throw new ArgumentException("Curves (one per tier from 3) are needed exactly in L_C mode.", nameof(curves));
        parameters.Validate();
        if (mode == IndustryMode.CurveLc)
        {
            foreach (int tier in layout.Tier)
            {
                if (tier - 3 >= curves.Length)
                    throw new ArgumentException($"No L_C curve for tier {tier}.", nameof(curves));
            }
        }
        _l = layout;
        _p = parameters;
        _inputs = inputs;
        _mode = mode;
        _curves = curves;
        _c = layout.Clusters.Length;
        _n = layout.ProcessCount;
        _sites = layout.SiteCount;
        SystemId = systemId;

        _h = new LongColumn[_c * Dims3.Count];
        _m = new LongColumn[_c * Dims3.Count];
        for (int c = 0; c < _c; c++)
        {
            for (int d = 0; d < Dims3.Count; d++)
                _h[c * Dims3.Count + d] = table.AddLong($"ind.H.{layout.Clusters[c]}.{DimNames[d]}");
        }
        for (int c = 0; c < _c; c++)
        {
            for (int d = 0; d < Dims3.Count; d++)
                _m[c * Dims3.Count + d] = table.AddLong($"ind.M.{layout.Clusters[c]}.{DimNames[d]}");
        }
        _x = new LongColumn[_n];
        _v = new LongColumn[_n];
        _y = new LongColumn[_n];
        foreach (int r in layout.Order)
        {
            int number = layout.Number[r];
            _x[r] = table.AddLong($"ind.X.{number}");
            _v[r] = table.AddLong($"ind.v.{number}");
            _y[r] = table.AddLong($"ind.y.{number}");
        }
        int wf = layout.WorkforceCategories * layout.Requesters;
        _wfN = new LongColumn[wf];
        _wfGranted = new LongColumn[wf];
        for (int k = 0; k < layout.WorkforceCategories; k++)
        {
            for (int q = 0; q < layout.Requesters; q++)
            {
                _wfN[k * layout.Requesters + q] = table.AddLong($"ind.wf.n.{k}.{q}");
                _wfGranted[k * layout.Requesters + q] = table.AddLong($"ind.wf.granted.{k}.{q}");
            }
        }

        var raw = new long[_sites];
        for (int c = 0; c < _c; c++)
        {
            for (int d = 0; d < Dims3.Count; d++)
            {
                for (int s = 0; s < _sites; s++)
                    raw[s] = start.H[start.Cell(s, c, d)].Raw;
                _h[c * Dims3.Count + d].Load(raw);
                for (int s = 0; s < _sites; s++)
                    raw[s] = start.M[start.Cell(s, c, d)].Raw;
                _m[c * Dims3.Count + d].Load(raw);
            }
        }
        for (int r = 0; r < _n; r++)
        {
            for (int s = 0; s < _sites; s++)
                raw[s] = start.X[s * _n + r].Raw;
            _x[r].Load(raw);
        }

        _yieldTrace = new ProcessYield[_sites * _n];
        _cultureTrace = new CultureStep[_sites * _c * Dims3.Count];
        _toolingTrace = new ToolingStep[_sites * _c * Dims3.Count];
        _procScratch = new CultureProcess[chunkCount][];
        _spillScratch = new CultureSpill[chunkCount][];
        for (int k = 0; k < chunkCount; k++)
        {
            _procScratch[k] = new CultureProcess[_n];
            _spillScratch[k] = new CultureSpill[_c];
        }
    }

    public int SystemId { get; }

    public Cadence Cadence => Cadence.Monthly;

    public IndustryLayout Layout => _l;

    /// <summary>
    /// A mailbox for one sending system with <paramref name="senderChunks"/> chunks; create before the run. The
    /// sender must run on this system's cadence, otherwise its messages would wait for a later merge.
    /// </summary>
    public IndustryMailbox CreateMailbox(int senderChunks, Cadence senderCadence)
    {
        if (senderCadence != Cadence)
            throw new ArgumentException($"A sender with cadence {senderCadence} cannot post to the {Cadence} capability merge.", nameof(senderCadence));
        var box = new IndustryMailbox(senderChunks);
        _mailboxes.Add(box);
        return box;
    }

    // ---- reads of the current state (Table columns): the snapshot for other layers in compute

    public Fx6 CultureH(int site, int cluster, Dimension dim) => Fx6.FromRaw(_h[cluster * Dims3.Count + (int)dim].Current[site]);

    public Fx6 ToolingM(int site, int cluster, Dimension dim) => Fx6.FromRaw(_m[cluster * Dims3.Count + (int)dim].Current[site]);

    public Fx6 ProcX(int site, int processRow) => Fx6.FromRaw(_x[processRow].Current[site]);

    /// <summary><c>proc_v</c>: labour hours of the last month step (0 without a report).</summary>
    public Fx6 ProcV(int site, int processRow) => Fx6.FromRaw(_v[processRow].Current[site]);

    /// <summary><c>proc_yield</c>: the yield output ran at in the last month step (0 for an unknown process).</summary>
    public Fx6 ProcYield(int site, int processRow) => Fx6.FromRaw(_y[processRow].Current[site]);

    public long Requested(int site, int category, int requester) => _wfN[category * _l.Requesters + requester].Current[site];

    /// <summary><c>granted</c> of the request made in the last month step (contract §4.6: visible at t+1).</summary>
    public long Granted(int site, int category, int requester) => _wfGranted[category * _l.Requesters + requester].Current[site];

    /// <summary>
    /// <c>act[site, c]</c> (spec §3.5 A26): <c>min(1, Σ granted / Σ n)</c> over the skilled requests of the cluster
    /// in the snapshot (requester_id mod C = cluster), 1 without requests.
    /// </summary>
    public Fx6 Act(int site, int cluster)
    {
        long n = 0, g = 0;
        for (int q = cluster; q < _l.Requesters; q += _c)
        {
            n += Requested(site, _l.SkilledCategory, q);
            g += Granted(site, _l.SkilledCategory, q);
        }
        return IndustryMath.Act(n, g);
    }

    /// <summary>
    /// <c>capability_tier(site, cluster)</c> (contract §5, spec §3.5 A25): min of <c>min(H, M)</c> over the
    /// cluster's key dimensions; null for a cluster without PRC. UI only, no dynamics read it.
    /// </summary>
    public Fx6? CapabilityTier(int site, int cluster) =>
        IndustryMath.CapabilityTier(Cells(_h, site, cluster), Cells(_m, site, cluster), _l.ClusterKeyDims[cluster]);

    /// <summary>
    /// <c>yield_p</c> of month <paramref name="month"/> from the snapshot, whether or not the owner knows the
    /// process (production_profile reports both). The industry compute of that month writes the same value
    /// into <c>proc_yield</c> for known processes, so other layers may call this in their compute of month t.
    /// </summary>
    public ProcessYield Yield(int site, int processRow, long month)
    {
        switch (_mode)
        {
            case IndustryMode.ConstLc0:
                return ProcessYield.Control(Fx6.One);
            case IndustryMode.CurveLc:
                long since = _inputs.KnownSince(site, processRow, month);
                return ProcessYield.Control(since < 0 ? Fx6.Zero : _curves[_l.Tier[processRow] - 3].YieldAt(month - since));
        }
        int c = _l.ClusterIndex[processRow];
        return IndustryMath.Yield(_l.Req[processRow], Cells(_h, site, c), Cells(_m, site, c), ProcX(site, processRow),
            _p.XHalf(_l.Tier[processRow]), _inputs.LicenceShare(site, processRow, month), _p.Lambda0);
    }

    /// <summary><c>production_profile(site, prc_set)</c> of month <paramref name="month"/> from the snapshot (contract §4.2, §5).</summary>
    public ProductionProfile Profile(int site, ReadOnlySpan<PrcSetItem> prcSet, long month)
    {
        var items = new ProfileItem[prcSet.Length];
        for (int i = 0; i < prcSet.Length; i++)
        {
            int r = _l.RowOf(prcSet[i].ProcessNumber);
            if (r < 0)
                throw new ArgumentException($"Process {prcSet[i].ProcessNumber} is not in the layout.", nameof(prcSet));
            Fx6 controlCost = _mode switch
            {
                IndustryMode.CurveLc => ControlUnitCost(site, r, month),
                IndustryMode.ConstLc0 => Fx6.One,
                _ => Fx6.Zero,
            };
            items[i] = new ProfileItem(prcSet[i].ProcessNumber, Yield(site, r, month), _inputs.KnowsProcess(site, r, month),
                prcSet[i].N, prcSet[i].BaseCost, controlCost);
        }
        return IndustryMath.Profile(items, _p.Eps, _mode != IndustryMode.Full);
    }

    /// <summary>
    /// <c>world_lead(p, owner(site))</c> (contract §4.1): another actor's site ran p in month t−1 with
    /// <c>proc_v &gt; 0</c> and <c>proc_yield ≥ 0.7</c>. Own sites of the actor do not count.
    /// </summary>
    public bool WorldLead(int site, int processRow)
    {
        int owner = _l.SiteOwner[site];
        ReadOnlySpan<long> v = _v[processRow].Current, y = _y[processRow].Current;
        for (int s = 0; s < _sites; s++)
        {
            if (_l.SiteOwner[s] != owner && v[s] > 0 && y[s] >= _p.WorldLeadYield.Raw)
                return true;
        }
        return false;
    }

    /// <summary>
    /// <c>discipline_volume(actor, d)</c> (contract §4.3, §5): Σ <c>proc_v</c> of month t−1 over the actor's sites
    /// and processes of discipline d with <c>proc_yield ≥ 0.3</c>.
    /// </summary>
    public Fx6 DisciplineVolume(int actorRow, int discipline)
    {
        Fx6 sum = Fx6.Zero;
        for (int s = 0; s < _sites; s++)
        {
            if (_l.SiteOwner[s] != actorRow)
                continue;
            foreach (int r in _l.Order)
            {
                if (_l.Discipline[r] == discipline && _y[r].Current[s] >= _p.DisciplineYield.Raw)
                    sum += ProcV(s, r);
            }
        }
        return sum;
    }

    /// <summary>Alloy of month <paramref name="month"/> (spec §1.4, §3.5 A23) from the snapshot yields of the melts.</summary>
    public AlloyAvailability Alloy(int site, long month)
    {
        Span<AlloyMelt> melts = stackalloc AlloyMelt[_l.AlloyGrades.Length];
        for (int i = 0; i < melts.Length; i++)
        {
            int r = _l.AlloyGrades[i];
            melts[i] = new AlloyMelt(_l.Number[r], _inputs.KnowsProcess(site, r, month), Yield(site, r, month).Yield);
        }
        return IndustryMath.AlloySupply(_inputs.AlloyCap(site, month), _inputs.AlloyDeficit(site, month), melts);
    }

    /// <summary><c>line_capacity(site, prc)</c> limited by alloy (spec §1.4): supply / <c>alloy_kg[gen]</c>.</summary>
    public Fx6 LineCapacity(int site, Fx6 alloyPerEngine, long month) =>
        IndustryMath.LineCapacity(Alloy(site, month).Supply, alloyPerEngine);

    // ---- diagnostics of the last step (tests, journals): scratch outside the Table, not state.

    /// <summary>Diagnostics only: the yield computed for (site, process) in the last step.</summary>
    public ProcessYield LastYield(int site, int processRow) => _yieldTrace[site * _n + processRow];

    /// <summary>Diagnostics only: the <c>culture_H</c> step of a cell in the last step (Full mode).</summary>
    public CultureStep LastCulture(int site, int cluster, Dimension dim) => _cultureTrace[(site * _c + cluster) * Dims3.Count + (int)dim];

    /// <summary>Diagnostics only: the <c>tooling_M</c> step of a cell in the last step (Full mode).</summary>
    public ToolingStep LastTooling(int site, int cluster, Dimension dim) => _toolingTrace[(site * _c + cluster) * Dims3.Count + (int)dim];

    /// <summary>Diagnostics only: <c>report_output</c> applied in the last merge, in (site, prc) order.</summary>
    public IReadOnlyList<OutputReport> LastReports => _lastReports;

    // ---- tick phases

    public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
    {
        long t = MonthOf(context);
        for (int s = rowStart; s < rowEnd; s++)
        {
            int mechTier = 0;
            for (int r = 0; r < _n; r++)
            {
                ProcessYield y = Yield(s, r, t);
                if (y.Yield < Fx6.Zero || y.Yield > Fx6.One)
                    throw new IndustryInvariantException(t, s, $"yield of process {_l.Number[r]} is {y.Yield}, outside [0; 1] (I3)");
                _yieldTrace[s * _n + r] = y;
                bool admitted = _inputs.KnowsProcess(s, r, t) && y.Yield > Fx6.Zero;
                _y[r].Next[s] = admitted ? y.Yield.Raw : 0;
                if (admitted && _l.Clusters[_l.ClusterIndex[r]] == Cluster.MECH && _l.Tier[r] > mechTier)
                    mechTier = _l.Tier[r];
            }
            if (_mode != IndustryMode.Full)
                continue; // L_C, L_C0: culture_H and tooling_M are not used (§6.1)
            bool qp = _inputs.QualityProgramme(s, t);
            for (int c = 0; c < _c; c++)
            {
                Fx6 act = Act(s, c), w = _inputs.W(s, c, t);
                for (int d = 0; d < Dims3.Count; d++)
                {
                    StepCulture(chunk, s, c, d, t, act, w, qp);
                    StepTooling(s, c, d, t, mechTier);
                }
            }
        }
    }

    public void Merge(in TickContext context, int chunkCount)
    {
        long t = MonthOf(context);
        _reports.Clear();
        _requests.Clear();
        foreach (IndustryMailbox box in _mailboxes)
            box.Drain(_reports, _requests);
        MergeReports(t);
        MergeWorkforce(t);
    }

    public void Post(in TickContext context)
    {
        long month = context.Calendar!.MonthIndex(context.Tick - 1);
        for (int s = 0; s < _sites; s++)
        {
            for (int i = 0; i < _h.Length; i++)
            {
                if (_h[i].Current[s] < 0)
                    throw new IndustryInvariantException(month, s, $"culture_H {_h[i].Name} is negative (I1)");
            }
            for (int r = 0; r < _n; r++)
            {
                if (_x[r].Current[s] < 0)
                    throw new IndustryInvariantException(month, s, $"proc_X of process {_l.Number[r]} is negative");
            }
        }
    }

    // ---- compute parts

    private void StepCulture(int chunk, int s, int c, int d, long t, Fx6 act, Fx6 w, bool qp)
    {
        CultureProcess[] procs = _procScratch[chunk];
        int np = 0;
        foreach (int r in _l.Order)
        {
            if (_l.ClusterIndex[r] == c && _v[r].Current[s] > 0)
                procs[np++] = new CultureProcess(_l.Number[r], ProcV(s, r), _l.Req[r][d], WorldLead(s, r));
        }
        CultureSpill[] spill = _spillScratch[chunk];
        int ns = 0;
        for (int c2 = 0; c2 < _c; c2++)
        {
            if (c2 != c)
                spill[ns++] = new CultureSpill(_p.Spill(_l.Clusters[c], _l.Clusters[c2]), Cell(_h, s, c2, d));
        }
        Fx6 h = Cell(_h, s, c, d);
        var cell = new CultureCell(h, _inputs.Hcap(s, (Dimension)d, t), act, w, qp && d == (int)Dimension.QualityControl);
        CultureStep step = IndustryMath.Culture(cell, procs.AsSpan(0, np), spill.AsSpan(0, ns), _p);
        // I1, I2 (reference README): no negative culture, growth at most rho_max, no fall below min(H, H_floor).
        if (step.HNew < Fx6.Zero || (step.HNew - h).Raw > _p.RhoMax.Raw + 1 || step.HNew < (h < step.HFloor ? h : step.HFloor))
            throw new IndustryInvariantException(t, s, $"culture_H {_l.Clusters[c]}.{DimNames[d]} step {h} → {step.HNew} (I1, I2)");
        _cultureTrace[(s * _c + c) * Dims3.Count + d] = step;
        _h[c * Dims3.Count + d].Next[s] = step.HNew.Raw;
    }

    private void StepTooling(int s, int c, int d, long t, int mechTier)
    {
        Fx6 invest = _inputs.ToolingInvest(s, c, (Dimension)d, t);
        if (invest < Fx6.Zero)
            throw new IndustryInvariantException(t, s, $"negative tooling investment {invest}");
        ToolingStep step = IndustryMath.Tooling(Cell(_m, s, c, d), invest, mechTier, _p);
        _toolingTrace[(s * _c + c) * Dims3.Count + d] = step;
        _m[c * Dims3.Count + d].Next[s] = step.MNew.Raw;
    }

    private Fx6 ControlUnitCost(int site, int processRow, long month)
    {
        long since = _inputs.KnownSince(site, processRow, month);
        CapabilityCurve curve = _curves[_l.Tier[processRow] - 3];
        // An unknown process has no t_known; its price is the curve at τ = 0 (spec §3.5 A24).
        return curve.UnitCostAt(since < 0 ? 0 : month - since);
    }

    // ---- merge parts

    // report_output (contract §4.0): by (site, prc); proc_X += units, proc_v = labour_hours; a process without a
    // report gets proc_v = 0 and forgets proc_X. Several reports of one (site, prc) are summed.
    private void MergeReports(long t)
    {
        foreach (OutputReport rep in _reports)
        {
            if (rep.Site < 0 || rep.Site >= _sites || _l.RowOf(rep.ProcessNumber) < 0)
                throw new ArgumentException($"report_output for unknown site {rep.Site} or process {rep.ProcessNumber}.");
            if (rep.Units < Fx6.Zero || rep.LabourHours < Fx6.Zero)
                throw new IndustryInvariantException(t, rep.Site, $"negative report_output for process {rep.ProcessNumber}");
            if (_y[_l.RowOf(rep.ProcessNumber)].Next[rep.Site] <= 0)
                throw new IndustryInvariantException(t, rep.Site, $"report_output for process {rep.ProcessNumber} with yield 0 (I3)");
        }
        _reports.Sort((a, b) => a.Site != b.Site ? a.Site.CompareTo(b.Site) : a.ProcessNumber.CompareTo(b.ProcessNumber));
        _lastReports.Clear();
        foreach (OutputReport rep in _reports)
        {
            int last = _lastReports.Count - 1;
            if (last >= 0 && _lastReports[last].Site == rep.Site && _lastReports[last].ProcessNumber == rep.ProcessNumber)
                _lastReports[last] = rep with { Units = _lastReports[last].Units + rep.Units, LabourHours = _lastReports[last].LabourHours + rep.LabourHours };
            else
                _lastReports.Add(rep);
        }
        int k = 0;
        for (int s = 0; s < _sites; s++)
        {
            foreach (int r in _l.Order)
            {
                if (k < _lastReports.Count && _lastReports[k].Site == s && _lastReports[k].ProcessNumber == _l.Number[r])
                {
                    OutputReport rep = _lastReports[k++];
                    _x[r].Next[s] = (ProcX(s, r) + rep.Units).Raw;
                    _v[r].Next[s] = rep.LabourHours.Raw;
                }
                else
                {
                    _x[r].Next[s] = IndustryMath.Forget(ProcX(s, r), _p.DeltaX).Raw;
                    _v[r].Next[s] = 0;
                }
            }
        }
    }

    // reserve_workforce (contract §4.6): by (site, cat, requester_id); granted is state, visible at t+1. A requester
    // without a request this month has n = granted = 0. I6 (granted ≤ n, Σ granted ≤ avail) holds by SplitWorkforce.
    private void MergeWorkforce(long t)
    {
        for (int i = 0; i < _wfN.Length; i++)
        {
            _wfN[i].Next.Clear();
            _wfGranted[i].Next.Clear();
        }
        foreach (WorkforceRequest q in _requests)
        {
            if (q.Site < 0 || q.Site >= _sites || q.Category < 0 || q.Category >= _l.WorkforceCategories
                || q.RequesterId < 0 || q.RequesterId >= _l.Requesters || q.N < 0)
                throw new ArgumentException($"Invalid reserve_workforce {q}.");
        }
        _requests.Sort((a, b) => a.Site != b.Site ? a.Site.CompareTo(b.Site)
            : a.Category != b.Category ? a.Category.CompareTo(b.Category) : a.RequesterId.CompareTo(b.RequesterId));
        Span<long> n = stackalloc long[_l.Requesters];
        Span<long> granted = stackalloc long[_l.Requesters];
        int i0 = 0;
        while (i0 < _requests.Count)
        {
            WorkforceRequest head = _requests[i0];
            int i1 = i0;
            while (i1 < _requests.Count && _requests[i1].Site == head.Site && _requests[i1].Category == head.Category)
            {
                WorkforceRequest q = _requests[i1];
                if (i1 > i0 && q.RequesterId == _requests[i1 - 1].RequesterId)
                    throw new ArgumentException($"Requester {q.RequesterId} asked twice for category {q.Category} at site {q.Site}.");
                n[i1 - i0] = q.N;
                i1++;
            }
            int count = i1 - i0;
            IndustryMath.SplitWorkforce(_inputs.WorkforceAvailable(head.Site, head.Category, t), n[..count], granted[..count]);
            for (int j = 0; j < count; j++)
            {
                WorkforceRequest q = _requests[i0 + j];
                int col = q.Category * _l.Requesters + q.RequesterId;
                _wfN[col].Next[q.Site] = q.N;
                _wfGranted[col].Next[q.Site] = granted[j];
            }
            i0 = i1;
        }
    }

    // ---- snapshot helpers

    private static Fx6 Cell(LongColumn[] cols, int s, int c, int d) => Fx6.FromRaw(cols[c * Dims3.Count + d].Current[s]);

    private static Dims3 Cells(LongColumn[] cols, int s, int c) => new(Cell(cols, s, c, 0), Cell(cols, s, c, 1), Cell(cols, s, c, 2));

    private static long MonthOf(in TickContext context) =>
        (context.Calendar ?? throw new InvalidOperationException("The capability system is monthly and needs a runner calendar."))
            .MonthIndex(context.Tick);
}
