using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.Policy;

// The adviser mode of the player actors (TASK-0153): proposals instead of line decisions. State is columns of the policy entity, grouped by design.
public sealed partial class PolicySystem
{
    private AdviserPlan? _adv;
    private bool[] _advRow = [];
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
        _adv = plan;
        _advRow = new bool[table.RowCount];
        foreach (int r in plan.Rows)
        {
            if (r < 0 || r >= _advRow.Length)
                throw new ArgumentOutOfRangeException(nameof(plan), r, "An adviser row names a row of the table.");
            _advRow[r] = true;
        }
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
    public bool IsAdviser(int actor) => _advRow.Length != 0 && _advRow[actor];

    /// <summary>
    /// Turns the line decisions of an adviser actor into proposals: reads the refusals of the month, keeps what is not refused, and empties the decision
    /// (no design command, no start, no close reaches the layer). <paramref name="stamp"/> is the month index + 1 that will see the result.
    /// </summary>
    private void Advise(int row, Decisions dec, int stamp, bool next)
    {
        if (next)
        {
            // The refusal command of the month t = stamp - 2 was consumed by this compute (its own stamp is Stamp(t) = stamp - 1).
            long t = stamp - 2;
            for (int d = 0; d < _designs; d++)
            {
                int refused = _prod!.ProposalRefusals(row, d, t);
                if ((refused & 1) != 0)
                    PutInt(_advNoOpen[d], row, true, stamp - 1 + _adv!.RefuseMonths);
                if ((refused & 2) != 0)
                    PutInt(_advNoClose[d], row, true, stamp - 1 + _adv!.RefuseMonths);
            }
        }
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
        if (_adv is null || !_advRow[actor])
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
