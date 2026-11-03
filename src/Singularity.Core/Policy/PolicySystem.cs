using System.Collections.Immutable;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Policy;

/// <summary>A broken policy invariant (budget shares of an actor do not sum to 1, negative budget).</summary>
public sealed class PolicyInvariantException(long month, int actor, string message)
    : Exception($"policy invariant violated at month {month}, actor {actor}: {message}")
{
    public long Month { get; } = month;
    public int Actor { get; } = actor;
}

/// <summary>
/// AI policy and budget of the jet slice (spec §3.4, §4, §3.5 A8, A12, A41), a monthly system on a table with one row per actor.
/// Own columns: <c>pol.profile</c>, <c>pol.round</c> (annual decision rounds made), <c>pol.budget</c> (<c>budget_a(t)</c> of the month
/// being computed) and the shares <c>pol.share.*</c>. Decisions are written to Next in the compute of month t and are in force from t+1
/// (ADR-0006); the decisions of month 0 are made by <see cref="Initialize"/> before tick 0 and act already in tick 0 (A8), so the
/// annual step does not repeat at t = 0.
/// Scope of this build (T6b): profile, budget, shares, the quality programme (state, <c>reserve_workforce</c>, the P2 trigger) and the
/// training-series target of P1. Requirements, launch, line rate, licences, disruptions and the why_not-driven triggers (P1/P3 programme,
/// P3 training) are later tasks (T6c, T6d); nothing here imitates them. The system draws no RNG after
/// <see cref="Initialize"/>: <c>j</c> is a pure function of its key (spec §9.1) and <c>prio</c> a pure function of the profile.
/// </summary>
public sealed class PolicySystem : ITickSystem
{
    public const int DefaultSystemId = 400;

    private readonly PolicyParams _p;
    private readonly ImmutableArray<int> _actorNumber;
    private readonly ImmutableArray<Fx6> _budget0, _growth;
    private readonly ImmutableArray<int> _forcedProfile;
    private readonly CounterRng _ai;
    private readonly IPolicyInputs _inputs;
    private readonly IndustryMailbox _mailbox;
    private readonly int _qpRequester;
    private readonly ImmutableArray<long> _qpEngineers;

    private readonly IntColumn _profile, _round, _qpActive, _qpStart, _qpCount, _train;
    private readonly LongColumn _budget;
    private readonly LongColumn[] _share;

    /// <param name="actorNumber">Stable actor number by row (scenario order, §3.5 A7).</param>
    /// <param name="budget0"><c>budget_a(t0)</c> by row.</param>
    /// <param name="growthPerYear"><c>g_a</c> by row.</param>
    /// <param name="ai">Stream <c>AI</c> (its seed already reflects <c>FIX_A</c>).</param>
    /// <param name="inputs">Reads of the other layers.</param>
    /// <param name="mailbox">Mailbox of the capability layer (<c>reserve_workforce</c> of the quality programme).</param>
    /// <param name="qpRequesterId">Requester id of the programme, above every bureau id (spec A41).</param>
    /// <param name="forcedProfile">Profile by row, or −1 to draw it from the stream (factorial cells, §6.6); default all drawn.</param>
    public PolicySystem(Table table, PolicyParams parameters, ImmutableArray<int> actorNumber, ImmutableArray<Fx6> budget0,
        ImmutableArray<Fx6> growthPerYear, CounterRng ai, IPolicyInputs inputs, IndustryMailbox mailbox, int qpRequesterId,
        ImmutableArray<int> forcedProfile = default, int systemId = DefaultSystemId)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(mailbox);
        ArgumentOutOfRangeException.ThrowIfNegative(qpRequesterId);
        parameters.Validate();
        if (table.RowCount != actorNumber.Length || budget0.Length != actorNumber.Length || growthPerYear.Length != actorNumber.Length)
            throw new ArgumentException("One actor number, start budget and growth per table row is required.", nameof(table));
        if (forcedProfile.IsDefault)
            forcedProfile = [.. Enumerable.Repeat(-1, actorNumber.Length)];
        if (forcedProfile.Length != actorNumber.Length || forcedProfile.Any(f => f < -1 || f > 2))
            throw new ArgumentException("forcedProfile needs −1, 0, 1 or 2 per row.", nameof(forcedProfile));
        foreach (Fx6 b in budget0)
        {
            if (b <= Fx6.Zero)
                throw new ArgumentException("Start budgets must be positive.", nameof(budget0));
        }
        _p = parameters;
        _actorNumber = actorNumber;
        _budget0 = budget0;
        _growth = growthPerYear;
        _forcedProfile = forcedProfile;
        _ai = ai;
        _inputs = inputs;
        _mailbox = mailbox;
        _qpRequester = qpRequesterId;
        _qpEngineers = [.. budget0.Select(b => (parameters.QpEngineerShare.Raw * parameters.EngineerBase(b)) / Fx6.Scale)];
        SystemId = systemId;
        _profile = table.AddInt("pol.profile");
        _round = table.AddInt("pol.round");
        _budget = table.AddLong("pol.budget");
        _qpActive = table.AddInt("pol.qp.active");
        _qpStart = table.AddInt("pol.qp.start");
        _qpCount = table.AddInt("pol.qp.count");
        _train = table.AddInt("pol.train");
        _share =
        [
            table.AddLong("pol.share.research"), table.AddLong("pol.share.capability"), table.AddLong("pol.share.effort"),
            table.AddLong("pol.share.procurement"), table.AddLong("pol.share.quality"),
        ];
    }

    public int SystemId { get; }

    public Cadence Cadence => Cadence.Monthly;

    public PolicyProfile Profile(int actorRow) => (PolicyProfile)_profile.Current[actorRow];

    /// <summary><c>budget_a(t)</c> of the month the next compute handles.</summary>
    public Fx6 Budget(int actorRow) => Fx6.FromRaw(_budget.Current[actorRow]);

    public BudgetShares Shares(int actorRow) => new(
        Fx6.FromRaw(_share[0].Current[actorRow]), Fx6.FromRaw(_share[1].Current[actorRow]), Fx6.FromRaw(_share[2].Current[actorRow]),
        Fx6.FromRaw(_share[3].Current[actorRow]), Fx6.FromRaw(_share[4].Current[actorRow]));

    /// <summary>Number of annual decision rounds made so far: 1 after <see cref="Initialize"/>, then one per month t with t mod 12 = 0, t &gt; 0.</summary>
    public int Rounds(int actorRow) => _round.Current[actorRow];

    /// <summary>A quality programme runs at the actor's site in the month being computed.</summary>
    public bool QualityProgrammeActive(int actorRow) => _qpActive.Current[actorRow] != 0;

    /// <summary>First month of the running programme (or of the last one), −1 if there was none.</summary>
    public int QualityProgrammeStart(int actorRow) => _qpStart.Current[actorRow];

    /// <summary>Consecutive months of the P2 trigger condition (a series with <c>q &lt; 0,7</c>).</summary>
    public int QualityTriggerCount(int actorRow) => _qpCount.Current[actorRow];

    /// <summary><c>qp_engineers</c> of the actor: the engineers the programme asks for each month.</summary>
    public long QualityEngineers(int actorRow) => _qpEngineers[actorRow];

    /// <summary>The design whose line is the training series in the month being computed (decided in the previous month), −1 without one.</summary>
    public int TrainingTarget(int actorRow) => _train.Current[actorRow];

    /// <summary>
    /// The initialisation phase (A8, ADR-0006): decisions on the start state, before tick 0. Profile from the stream key (tick 0)
    /// or the factorial cell, <c>budget(0)</c>, the shares of the profile. Call once, before the first tick.
    /// </summary>
    public void Initialize()
    {
        int n = _actorNumber.Length;
        var profile = new int[n];
        var round = new int[n];
        var budget = new long[n];
        var qpActive = new int[n];
        var qpStart = new int[n];
        var train = new int[n];
        var shares = new long[5][];
        for (int k = 0; k < shares.Length; k++)
            shares[k] = new long[n];
        for (int row = 0; row < n; row++)
        {
            PolicyProfile p = _forcedProfile[row] >= 0 ? (PolicyProfile)_forcedProfile[row] : PolicyRng.Profile(_ai, _actorNumber[row]);
            profile[row] = (int)p;
            round[row] = 1;
            budget[row] = _budget0[row].Raw;
            // P1 starts a quality programme on t0 (spec A41); the others have none.
            qpActive[row] = p == PolicyProfile.Quality ? 1 : 0;
            qpStart[row] = p == PolicyProfile.Quality ? 0 : -1;
            train[row] = -1;
            BudgetShares s = _p.Shares(p, qpActive[row] != 0);
            shares[0][row] = s.Research.Raw;
            shares[1][row] = s.Capability.Raw;
            shares[2][row] = s.Effort.Raw;
            shares[3][row] = s.Procurement.Raw;
            shares[4][row] = s.Quality.Raw;
        }
        _profile.Load(profile);
        _round.Load(round);
        _budget.Load(budget);
        _qpActive.Load(qpActive);
        _qpStart.Load(qpStart);
        _qpCount.Load(new int[n]);
        _train.Load(train);
        for (int k = 0; k < shares.Length; k++)
            _share[k].Load(shares[k]);
    }

    public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
    {
        long t = MonthOf(context);
        bool annual = t > 0 && t % 12 == 0;
        for (int row = rowStart; row < rowEnd; row++)
        {
            // budget(t+1) from budget(t): the budget column always holds the month being computed.
            _budget.Next[row] = BudgetMath.Next(Fx6.FromRaw(_budget.Current[row]), _growth[row]).Raw;
            var profile = (PolicyProfile)_profile.Current[row];
            bool active = _qpActive.Current[row] != 0;
            int start = _qpStart.Current[row];
            int count = _qpCount.Current[row];

            // Quality programme (spec A41). A running programme asks for its engineers every month; it ends after QpMonths months; the
            // P2 trigger counts consecutive months with a series below the quality bar and starts a programme at the annual step
            // (P1/P3 triggers read why_not: T6d). A programme started in month t acts from t+1.
            if (active)
                _mailbox.ReserveWorkforce(chunk, new WorkforceRequest(row, Products.ProductsLayout.EngineerCategory, _qpRequester, _qpEngineers[row]));
            if (profile == PolicyProfile.Mass)
                count = _inputs.SeriesBelowQuality(row, _p.QpQuality, t) ? count + 1 : 0;
            bool next = active;
            if (active && t + 1 >= start + _p.QpMonths)
                next = false;
            if (annual && !active && profile == PolicyProfile.Mass && count >= _p.QpTriggerMonths)
            {
                next = true;
                start = (int)t + 1;
                count = 0;
            }
            _qpActive.Next[row] = next ? 1 : 0;
            _qpStart.Next[row] = start;
            _qpCount.Next[row] = count;

            // The training series (P1; P3 needs why_not, T6d): the newest line whose bottleneck is the stock of culture and whose process
            // is limited by its output (out_lim, A41-T) gets T_a next month (spec A41).
            int train = -1;
            if (profile == PolicyProfile.Quality)
            {
                int d = _inputs.NewestSeriesDesign(row, t);
                if (d >= 0 && _inputs.Bottleneck(row, d, t) is { Source: BottleneckSource.H, OutLim: true })
                    train = d;
            }
            _train.Next[row] = train;

            // Shares of the doctrine (spec §4); under a programme P2 trades 0,05 of procurement for the QP item.
            BudgetShares s = _p.Shares(profile, next);
            _share[0].Next[row] = s.Research.Raw;
            _share[1].Next[row] = s.Capability.Raw;
            _share[2].Next[row] = s.Effort.Raw;
            _share[3].Next[row] = s.Procurement.Raw;
            _share[4].Next[row] = s.Quality.Raw;
            if (annual)
                _round.Next[row] = _round.Current[row] + 1;
        }
    }

    public void Merge(in TickContext context, int chunkCount)
    {
    }

    public void Post(in TickContext context)
    {
        long month = context.Calendar!.MonthIndex(context.Tick - 1);
        for (int row = 0; row < _actorNumber.Length; row++)
        {
            if (_budget.Current[row] <= 0)
                throw new PolicyInvariantException(month, row, "the budget is not positive");
            if (Shares(row).Sum != Fx6.One)
                throw new PolicyInvariantException(month, row, "budget shares do not sum to 1");
        }
    }

    private static long MonthOf(in TickContext context) =>
        (context.Calendar ?? throw new InvalidOperationException("The policy system is monthly and needs a runner calendar."))
            .MonthIndex(context.Tick);
}
