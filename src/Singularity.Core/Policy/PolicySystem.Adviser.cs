using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.Policy;

// The adviser mode of the player actors (TASK-0153): proposals instead of line decisions. State is columns of the policy entity, grouped by design.
public sealed partial class PolicySystem
{
    private AdviserPlan? _adv;
    private System.Collections.Immutable.ImmutableArray<bool> _advRow = [];   // filled once; immutable, so it is no scratch
    // Proposal stamps (the month that sees it, like the start/close stamps), the project handle and the rate; refusals hold the first stamp a proposal may come back at.
    private IntColumn[] _advOpen = [], _advOpenGen = [], _advClose = [], _advCloseGen = [], _advCloseWhy = [], _advNoOpen = [], _advNoClose = [];
    private LongColumn[] _advRate = [];

    private void AddAdviserColumns(Table table, AdviserPlan? plan, int designs)
    {
        if (plan is null)
            return;
        if (_prod is null)
            throw new ArgumentException("The adviser needs the product AI.", nameof(plan));
        if (plan.RefuseYears < 1 || plan.MaxLines < 1)
            throw new ArgumentOutOfRangeException(nameof(plan), "An adviser needs a refusal period of at least a year and a line limit of at least 1.");
        // The policy invariant of the default rule: more than two lines of one actor stop the run, so a limit that allows them is refused at load.
        if (plan.MaxLines > 2 && _pp.LineClose == LineCloseRule.YProd && !_derive)
            throw new ArgumentOutOfRangeException(nameof(plan), plan.MaxLines, "The line limit above 2 needs LINE_CLOSE = ADMIT or the DERIV arm: the default rule allows two lines.");
        _adv = plan;
        var rows = new bool[table.RowCount];
        foreach (int r in plan.Rows)
        {
            if (r < 0 || r >= rows.Length)
                throw new ArgumentOutOfRangeException(nameof(plan), r, "An adviser row names a row of the table.");
            rows[r] = true;
        }
        _advRow = System.Collections.Immutable.ImmutableArray.Create(rows);
        _advOpen = new IntColumn[designs];
        _advOpenGen = new IntColumn[designs];
        _advClose = new IntColumn[designs];
        _advCloseGen = new IntColumn[designs];
        _advCloseWhy = new IntColumn[designs];
        _advNoOpen = new IntColumn[designs];
        _advNoClose = new IntColumn[designs];
        _advRate = new LongColumn[designs];
        for (int d = 0; d < designs; d++)
        {
            _advOpen[d] = table.AddInt($"pol.adv.{d}.open");
            _advOpenGen[d] = table.AddInt($"pol.adv.{d}.open.gen");
            _advRate[d] = table.AddLong($"pol.adv.{d}.rate");
            _advClose[d] = table.AddInt($"pol.adv.{d}.close");
            _advCloseGen[d] = table.AddInt($"pol.adv.{d}.close.gen");
            _advCloseWhy[d] = table.AddInt($"pol.adv.{d}.close.why");
            _advNoOpen[d] = table.AddInt($"pol.adv.{d}.no.open");
            _advNoClose[d] = table.AddInt($"pol.adv.{d}.no.close");
        }
    }

    /// <summary>The plan of the run's adviser, or null when the AI leads every actor.</summary>
    public AdviserPlan? Adviser => _adv;

    /// <summary>The AI only advises this actor.</summary>
    public bool IsAdviser(int actor) => !_advRow.IsDefaultOrEmpty && _advRow[actor];

    /// <summary>
    /// The player's orders of the month t that bear on the adviser, read before the decisions of the compute (so that a refused project is passed over by the launch
    /// search and the automatic decisions never see it): refusals of proposals made for that very project, and a start of the player's own on an AI line.
    /// </summary>
    private void ReadOrders(int row, long t)
    {
        int stamp = Stamp(t + 1);
        for (int d = 0; d < _designs; d++)
        {
            // A refusal silences only a proposal that was made for that very project in this month's view.
            int open = _prod!.ProposalRefusal(row, d, false, t);
            if (open != 0 && _advOpen[d].Current[row] == stamp - 1 && _advOpenGen[d].Current[row] == open)
                PutInt(_advNoOpen[d], row, true, stamp - 1 + _adv!.RefuseMonths);
            int close = _prod.ProposalRefusal(row, d, true, t);
            if (close != 0 && _advClose[d].Current[row] == stamp - 1 && _advCloseGen[d].Current[row] == close)
                PutInt(_advNoClose[d], row, true, stamp - 1 + _adv!.RefuseMonths);
            // The player's own start (a handle that stands) on a scenario-start line takes the rate from the AI for good (levers doc 1.2).
            if (_line[d].Current[row] != 0 && _prod.PlayerSeries(row, d, t))
                PutInt(_line[d], row, true, 0);
        }
    }

    /// <summary>The open proposal of the design is silenced for the month decided at <paramref name="t"/> (a refusal still in force, this compute's included).</summary>
    private bool OpenSilenced(int row, int d, long t) => Silent(_advNoOpen[d], row, Stamp(t + 1), true);

    /// <summary>
    /// Turns the line decisions of an adviser actor into proposals: reads the refusals of the month, keeps what is not refused, and empties the decision
    /// (no design command, no start, no close reaches the layer). <paramref name="stamp"/> is the month index + 1 that will see the result.
    /// </summary>
    private void Advise(int row, Decisions dec, int stamp, bool next)
    {
        foreach ((int d, Fx6 rate) in dec.Starts)
        {
            if (Silent(_advNoOpen[d], row, stamp, next))
                continue;
            PutInt(_advOpen[d], row, next, stamp);
            PutInt(_advOpenGen[d], row, next, dec.Designs[d].SlotGeneration);
            PutLong(_advRate[d], row, next, rate.Raw);
        }
        for (int i = 0; i < dec.Closes.Count; i++)
        {
            int d = dec.Closes[i];
            if (Silent(_advNoClose[d], row, stamp, next))
                continue;
            PutInt(_advClose[d], row, next, stamp);
            PutInt(_advCloseGen[d], row, next, dec.Designs[d].SlotGeneration);
            PutInt(_advCloseWhy[d], row, next, (int)dec.CloseReasons[i]);
        }
        dec.Design.Clear();
        dec.Starts.Clear();
        dec.Closes.Clear();
        dec.CloseReasons.Clear();
    }

    // A refusal written in this very compute is in Next; an older one in Current.
    private static bool Silent(IntColumn until, int row, int stamp, bool next) => (next ? until.Next[row] : until.Current[row]) > stamp;

    /// <summary>The proposals the player sees at <paramref name="month"/> (stamped for it), opens before closes, by design.</summary>
    public IReadOnlyList<AdviserProposal> AdviserProposals(int actor, long month)
    {
        var list = new List<AdviserProposal>();
        if (_adv is null || !IsAdviser(actor))
            return list;
        for (int d = 0; d < _advOpen.Length; d++)
        {
            if (_advOpen[d].Current[actor] == Stamp(month))
                list.Add(new AdviserProposal(ProposalKind.Open, d, new DesignHandle(d, _advOpenGen[d].Current[actor]), Fx6.FromRaw(_advRate[d].Current[actor]), LineCloseReason.None));
        }
        for (int d = 0; d < _advClose.Length; d++)
        {
            if (_advClose[d].Current[actor] == Stamp(month))
                list.Add(new AdviserProposal(ProposalKind.Close, d, new DesignHandle(d, _advCloseGen[d].Current[actor]), Fx6.Zero, (LineCloseReason)_advCloseWhy[d].Current[actor]));
        }
        return list;
    }

    /// <summary>First month index a refused proposal of the design may come back at, or −1 when it was never refused (a read for the view and tests).</summary>
    public long ProposalSilentUntil(int actor, int design, ProposalKind kind) =>
        (kind == ProposalKind.Open ? _advNoOpen : _advNoClose) is { Length: > 0 } c && c[design].Current[actor] > 0 ? c[design].Current[actor] - 1 : -1;
}
