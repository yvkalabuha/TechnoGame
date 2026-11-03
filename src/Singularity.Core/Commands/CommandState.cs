using System.Collections.Immutable;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Core.Commands;

/// <summary>
/// The commands in force, as table columns <c>cmd.*</c> (ADR-0014 п. 2, ADR-0013 п. 4: state is hashed and saved). Not a tick
/// system: it is written only between ticks, by <see cref="Apply"/> (the session applies the commands of a tick boundary in key
/// order). Standing settings: <c>cmd.shares.*</c> with a "set by the player" flag and <c>cmd.focus.0..2</c>. One-shot commands live in
/// fixed slots with a <c>stamp</c> = month index + 1 of the monthly step that consumes them: a reader sees a slot only when the stamp
/// equals the month it computes, so nothing is ever cleared. Slots: <see cref="CommandLimits.DesignSlots"/> compose/derive/cancel per actor,
/// one <c>start_series</c> and one <c>close_line</c> per design.
/// </summary>
public sealed class CommandState
{
    private readonly int _slots, _altSlots;
    private readonly IntColumn _sharesSet;
    private readonly LongColumn[] _shares;
    private readonly IntColumn[] _focus;
    private readonly IntColumn[] _dKind, _dTarget, _dStamp;
    private readonly IntColumn[][] _dAlt;
    private readonly IntColumn[] _sStamp, _sForce, _cStamp;
    private readonly LongColumn[] _sRate;

    /// <param name="altSlots">Alternatives per design command: the slot count of the product class.</param>
    /// <param name="designs"><c>max_designs</c>.</param>
    public CommandState(Table table, int altSlots, int designs)
    {
        _slots = CommandLimits.DesignSlots;
        _altSlots = altSlots;
        _sharesSet = table.AddInt("cmd.shares.set");
        _shares = [table.AddLong("cmd.shares.research"), table.AddLong("cmd.shares.capability"), table.AddLong("cmd.shares.effort"),
            table.AddLong("cmd.shares.procurement"), table.AddLong("cmd.shares.quality")];
        _focus = [table.AddInt("cmd.focus.0"), table.AddInt("cmd.focus.1"), table.AddInt("cmd.focus.2")];
        _dKind = new IntColumn[_slots];
        _dTarget = new IntColumn[_slots];
        _dStamp = new IntColumn[_slots];
        _dAlt = new IntColumn[_slots][];
        for (int k = 0; k < _slots; k++)
        {
            _dKind[k] = table.AddInt($"cmd.dsn.{k}.kind");
            _dTarget[k] = table.AddInt($"cmd.dsn.{k}.target");
            _dStamp[k] = table.AddInt($"cmd.dsn.{k}.stamp");
            _dAlt[k] = new IntColumn[altSlots];
            for (int s = 0; s < altSlots; s++)
                _dAlt[k][s] = table.AddInt($"cmd.dsn.{k}.alt.{s}");
        }
        _sStamp = new IntColumn[designs];
        _sForce = new IntColumn[designs];
        _cStamp = new IntColumn[designs];
        _sRate = new LongColumn[designs];
        for (int d = 0; d < designs; d++)
        {
            _sStamp[d] = table.AddInt($"cmd.series.{d}.stamp");
            _sRate[d] = table.AddLong($"cmd.series.{d}.rate");
            _sForce[d] = table.AddInt($"cmd.series.{d}.force");
            _cStamp[d] = table.AddInt($"cmd.close.{d}.stamp");
        }
    }

    // ---- readers (snapshot: what is in force at the month step being computed)

    /// <summary>Shares the player set with <c>set_budget_shares</c>, or null when the actor follows its AI profile.</summary>
    public BudgetShares? SharesOverride(int actor) => _sharesSet.Current[actor] == 0
        ? null
        : new BudgetShares(Fx6.FromRaw(_shares[0].Current[actor]), Fx6.FromRaw(_shares[1].Current[actor]), Fx6.FromRaw(_shares[2].Current[actor]),
            Fx6.FromRaw(_shares[3].Current[actor]), Fx6.FromRaw(_shares[4].Current[actor]));

    /// <summary>Process number of the k-th focus priority, −1 = empty.</summary>
    public int Focus(int actor, int k) => _focus[k].Current[actor] - 1;

    /// <summary>The <c>compose</c>/<c>derive</c> commands the month step <paramref name="month"/> consumes, in command order.</summary>
    public IReadOnlyList<(CommandKind Kind, int Target, ImmutableArray<int> Alts)> DesignCommands(int actor, long month)
    {
        var list = new List<(CommandKind, int, ImmutableArray<int>)>();
        for (int k = 0; k < _slots; k++)
        {
            if (_dStamp[k].Current[actor] != Stamp(month))
                continue;
            var alts = new int[_altSlots];
            for (int s = 0; s < _altSlots; s++)
                alts[s] = _dAlt[k][s].Current[actor];
            CommandKind kind = _dKind[k].Current[actor] switch { 1 => CommandKind.ComposeDesign, 2 => CommandKind.DeriveDesign, _ => CommandKind.CancelDesign };
            list.Add((kind, _dTarget[k].Current[actor], [.. alts]));
        }
        return list;
    }

    /// <summary><c>start_series</c> consumed at <paramref name="month"/>: rate and force, or null.</summary>
    public (Fx6 Rate, bool Force)? Series(int actor, int design, long month) =>
        _sStamp[design].Current[actor] == Stamp(month) ? (Fx6.FromRaw(_sRate[design].Current[actor]), _sForce[design].Current[actor] != 0) : null;

    public bool CloseLine(int actor, int design, long month) => _cStamp[design].Current[actor] == Stamp(month);

    // ---- writer (between ticks)

    /// <summary>
    /// Writes the command into the columns; <paramref name="consumedAtMonth"/> is the month index of the first monthly step that
    /// sees it. Refuses a fourth compose/derive of one actor for the same month (the slots are a fixed capacity).
    /// </summary>
    public CommandVerdict Apply(SliceCommand command, long consumedAtMonth)
    {
        int a = command.Actor;
        int stamp = Stamp(consumedAtMonth);
        switch (command)
        {
            case SetBudgetShares s:
                Set(_sharesSet, a, 1);
                Set(_shares[0], a, s.Shares.Research.Raw);
                Set(_shares[1], a, s.Shares.Capability.Raw);
                Set(_shares[2], a, s.Shares.Effort.Raw);
                Set(_shares[3], a, s.Shares.Procurement.Raw);
                Set(_shares[4], a, s.Shares.Quality.Raw);
                break;
            case SetEffortFocus f:
                for (int k = 0; k < _focus.Length; k++)
                    Set(_focus[k], a, k < f.Processes.Length ? f.Processes[k] + 1 : 0);
                break;
            case ComposeDesign or DeriveDesign or CancelDesign:
                int slot = 0;
                while (slot < _slots && _dStamp[slot].Current[a] == stamp)
                    slot++;
                if (slot == _slots)
                    return CommandVerdict.Refuse(CommandRefusal.SlotOverflow, $"actor {a} already has {_slots} design commands for month {consumedAtMonth}");
                Set(_dKind[slot], a, command switch { ComposeDesign => 1, DeriveDesign => 2, _ => 3 });
                Set(_dTarget[slot], a, command switch { ComposeDesign c => c.Generation, DeriveDesign v => v.Parent, _ => ((CancelDesign)command).Design });
                ImmutableArray<int> alts = command switch { ComposeDesign c => c.Alts, DeriveDesign v => v.Alts, _ => [] };
                for (int i = 0; i < _altSlots; i++)
                    Set(_dAlt[slot][i], a, alts.IsEmpty ? -1 : alts[i]);
                Set(_dStamp[slot], a, stamp);
                break;
            case StartSeries s:
                Set(_sStamp[s.Design], a, stamp);
                Set(_sRate[s.Design], a, s.Rate.Raw);
                Set(_sForce[s.Design], a, s.Force ? 1 : 0);
                break;
            case CloseLine l:
                Set(_cStamp[l.Design], a, stamp);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command.GetType(), "Unknown command type.");
        }
        return CommandVerdict.Ok;
    }

    private static int Stamp(long month) => checked((int)month + 1);

    // Column has no single-cell Current writer (Kernel); the tables here are tiny (one row per actor).
    private static void Set(IntColumn column, int row, int value)
    {
        int[] copy = column.Current.ToArray();
        copy[row] = value;
        column.Load(copy);
    }

    private static void Set(LongColumn column, int row, long value)
    {
        long[] copy = column.Current.ToArray();
        copy[row] = value;
        column.Load(copy);
    }
}
