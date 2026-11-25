using System.Collections.Immutable;
using Singularity.Core.Policy;
using Singularity.Core.Products;
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
    private readonly IntColumn[] _dKind, _dTarget, _dGen, _dStamp;
    private readonly IntColumn[][] _dAlt;
    private readonly IntColumn[] _sStamp, _sGen, _sForce, _cStamp, _cGen;
    private readonly LongColumn[] _sRate;
    // refuse_proposal per design and kind (open, close); only in a run with an adviser. The generation is the project the refusal named.
    private readonly IntColumn[] _rOpenStamp = [], _rOpenGen = [], _rCloseStamp = [], _rCloseGen = [];
    private readonly ImmutableArray<int> _adviserActors;
    private readonly IntColumn[] _progCmd;
    private readonly ImmutableArray<int> _progNumbers;
    private readonly IntColumn[] _fKind, _fDesign, _fGen, _fN, _fStamp;
    private readonly IntColumn? _fundStamp, _fundNode;
    private readonly LongColumn? _security;
    private readonly IntColumn? _planSet;
    private readonly IntColumn[] _planTarget = [], _planMode = [];
    private readonly LongColumn? _fundAmount;
    private readonly Singularity.Core.Institutes.InstituteOrders? _institutes;

    /// <param name="altSlots">Alternatives per design command: the slot count of the product class.</param>
    /// <param name="designs"><c>max_designs</c>.</param>
    /// <param name="fundProgramme">The run has the reserve: adds the one-off slot <c>cmd.fund.*</c> (absent otherwise, so the table of other runs is unchanged).</param>
    /// <param name="intelligence">The run has intelligence: adds the security share and the target plan to the standing settings <c>cmd.shares.*</c> (absent otherwise).</param>
    /// <param name="programmeProcesses">Process numbers a mastery programme may run on (tier ≥ 5): one `cmd.prog.p{n}` column each.</param>
    public CommandState(Table table, int altSlots, int designs, ImmutableArray<int> programmeProcesses = default, ImmutableArray<int> adviserActors = default, bool fundProgramme = false, bool intelligence = false,
        Singularity.Core.Institutes.InstituteOrders? institutes = null)
    {
        _institutes = institutes;
        _progNumbers = programmeProcesses.IsDefault ? [] : programmeProcesses;
        _slots = CommandLimits.DesignSlots;
        _altSlots = altSlots;
        _sharesSet = table.AddInt("cmd.shares.set");
        _shares = [table.AddLong("cmd.shares.research"), table.AddLong("cmd.shares.capability"), table.AddLong("cmd.shares.effort"),
            table.AddLong("cmd.shares.procurement"), table.AddLong("cmd.shares.quality")];
        if (intelligence)
        {
            // The seventh share and the plan (target + 1 and mode per slot) live in the family of the shares; `plan` = 1 once the player set a plan.
            _security = table.AddLong("cmd.shares.security");
            _planSet = table.AddInt("cmd.shares.plan");
            _planTarget = [table.AddInt("cmd.shares.tgt.0"), table.AddInt("cmd.shares.tgt.1"), table.AddInt("cmd.shares.tgt.2")];
            _planMode = [table.AddInt("cmd.shares.mode.0"), table.AddInt("cmd.shares.mode.1"), table.AddInt("cmd.shares.mode.2")];
        }
        _focus = [table.AddInt("cmd.focus.0"), table.AddInt("cmd.focus.1"), table.AddInt("cmd.focus.2")];
        _dKind = new IntColumn[_slots];
        _dTarget = new IntColumn[_slots];
        _dGen = new IntColumn[_slots];
        _dStamp = new IntColumn[_slots];
        _dAlt = new IntColumn[_slots][];
        for (int k = 0; k < _slots; k++)
        {
            _dKind[k] = table.AddInt($"cmd.dsn.{k}.kind");
            _dTarget[k] = table.AddInt($"cmd.dsn.{k}.target");
            _dGen[k] = table.AddInt($"cmd.dsn.{k}.gen");
            _dStamp[k] = table.AddInt($"cmd.dsn.{k}.stamp");
            _dAlt[k] = new IntColumn[altSlots];
            for (int s = 0; s < altSlots; s++)
                _dAlt[k][s] = table.AddInt($"cmd.dsn.{k}.alt.{s}");
        }
        _fKind = new IntColumn[CommandLimits.FleetSlots];
        _fDesign = new IntColumn[CommandLimits.FleetSlots];
        _fGen = new IntColumn[CommandLimits.FleetSlots];
        _fN = new IntColumn[CommandLimits.FleetSlots];
        _fStamp = new IntColumn[CommandLimits.FleetSlots];
        for (int k = 0; k < CommandLimits.FleetSlots; k++)
        {
            _fKind[k] = table.AddInt($"cmd.flt.{k}.kind");
            _fDesign[k] = table.AddInt($"cmd.flt.{k}.design");
            _fGen[k] = table.AddInt($"cmd.flt.{k}.gen");
            _fN[k] = table.AddInt($"cmd.flt.{k}.n");
            _fStamp[k] = table.AddInt($"cmd.flt.{k}.stamp");
        }
        _progCmd = new IntColumn[_progNumbers.Length];
        for (int j = 0; j < _progCmd.Length; j++)
            _progCmd[j] = table.AddInt($"cmd.prog.p{_progNumbers[j]}");
        if (fundProgramme)
        {
            _fundStamp = table.AddInt("cmd.fund.stamp");
            _fundNode = table.AddInt("cmd.fund.node");
            _fundAmount = table.AddLong("cmd.fund.amount");
        }
        _sStamp = new IntColumn[designs];
        _sForce = new IntColumn[designs];
        _cStamp = new IntColumn[designs];
        _sGen = new IntColumn[designs];
        _cGen = new IntColumn[designs];
        _sRate = new LongColumn[designs];
        for (int d = 0; d < designs; d++)
        {
            _sStamp[d] = table.AddInt($"cmd.series.{d}.stamp");
            _sGen[d] = table.AddInt($"cmd.series.{d}.gen");
            _sRate[d] = table.AddLong($"cmd.series.{d}.rate");
            _sForce[d] = table.AddInt($"cmd.series.{d}.force");
            _cStamp[d] = table.AddInt($"cmd.close.{d}.stamp");
            _cGen[d] = table.AddInt($"cmd.close.{d}.gen");
        }
        _adviserActors = adviserActors.IsDefault ? [] : adviserActors;
        if (_adviserActors.IsEmpty)
            return;
        _rOpenStamp = new IntColumn[designs];
        _rOpenGen = new IntColumn[designs];
        _rCloseStamp = new IntColumn[designs];
        _rCloseGen = new IntColumn[designs];
        for (int d = 0; d < designs; d++)
        {
            _rOpenStamp[d] = table.AddInt($"cmd.refuse.{d}.open.stamp");
            _rOpenGen[d] = table.AddInt($"cmd.refuse.{d}.open.gen");
            _rCloseStamp[d] = table.AddInt($"cmd.refuse.{d}.close.stamp");
            _rCloseGen[d] = table.AddInt($"cmd.refuse.{d}.close.gen");
        }
    }

    /// <summary>
    /// <c>refuse_proposal</c> consumed at <paramref name="month"/>: the slot generation of the project whose open (or close) proposal was refused, 0 = none.
    /// Always 0 in a run without an adviser.
    /// </summary>
    public int RefusedGen(int actor, int design, bool close, long month)
    {
        IntColumn[] stamp = close ? _rCloseStamp : _rOpenStamp;
        return stamp.Length == 0 || stamp[design].Current[actor] != Stamp(month) ? 0 : (close ? _rCloseGen : _rOpenGen)[design].Current[actor];
    }

    // ---- readers (snapshot: what is in force at the month step being computed)

    /// <summary>Shares the player set with <c>set_budget_shares</c>, or null when the actor follows its AI profile.</summary>
    public BudgetShares? SharesOverride(int actor) => _sharesSet.Current[actor] == 0
        ? null
        : new BudgetShares(Fx6.FromRaw(_shares[0].Current[actor]), Fx6.FromRaw(_shares[1].Current[actor]), Fx6.FromRaw(_shares[2].Current[actor]),
            Fx6.FromRaw(_shares[3].Current[actor]), Fx6.FromRaw(_shares[4].Current[actor]), _security is null ? Fx6.Zero : Fx6.FromRaw(_security.Current[actor]));

    /// <summary>The plan the player set for the slot: false when there is none (the AI plan applies) or in a run without intelligence; an empty slot has target −1.</summary>
    public bool PlayerPlan(int actor, int slot, out int target, out Singularity.Core.Intelligence.IntelMode mode)
    {
        target = -1;
        mode = Singularity.Core.Intelligence.IntelMode.None;
        if (_planSet is null || _planSet.Current[actor] == 0)
            return false;
        target = _planTarget[slot].Current[actor] - 1;
        mode = (Singularity.Core.Intelligence.IntelMode)_planMode[slot].Current[actor];
        return true;
    }

    /// <summary>Process number of the k-th focus priority, −1 = empty.</summary>
    public int Focus(int actor, int k) => _focus[k].Current[actor] - 1;

    /// <summary>
    /// The <c>compose</c>/<c>derive</c>/<c>cancel</c> commands the month step <paramref name="month"/> consumes, in command order. <c>Target</c> is the handle of the
    /// parent or of the project to cancel; for a compose its slot is the requirement generation <c>g*</c> and its generation 0.
    /// </summary>
    public IReadOnlyList<(CommandKind Kind, DesignHandle Target, ImmutableArray<int> Alts)> DesignCommands(int actor, long month)
    {
        var list = new List<(CommandKind, DesignHandle, ImmutableArray<int>)>();
        for (int k = 0; k < _slots; k++)
        {
            if (_dStamp[k].Current[actor] != Stamp(month))
                continue;
            var alts = new int[_altSlots];
            for (int s = 0; s < _altSlots; s++)
                alts[s] = _dAlt[k][s].Current[actor];
            CommandKind kind = _dKind[k].Current[actor] switch { 1 => CommandKind.ComposeDesign, 2 => CommandKind.DeriveDesign, _ => CommandKind.CancelDesign };
            list.Add((kind, new DesignHandle(_dTarget[k].Current[actor], _dGen[k].Current[actor]), [.. alts]));
        }
        return list;
    }

    /// <summary>The <c>sell_cohort</c>/<c>retire_cohort</c> commands the month step <paramref name="month"/> consumes, in command order.</summary>
    public IReadOnlyList<(CommandKind Kind, DesignHandle Design, int N)> FleetCommands(int actor, long month)
    {
        var list = new List<(CommandKind, DesignHandle, int)>();
        for (int k = 0; k < CommandLimits.FleetSlots; k++)
        {
            if (_fStamp[k].Current[actor] == Stamp(month))
                list.Add((_fKind[k].Current[actor] == 1 ? CommandKind.SellCohort : CommandKind.RetireCohort, new DesignHandle(_fDesign[k].Current[actor], _fGen[k].Current[actor]), _fN[k].Current[actor]));
        }
        return list;
    }

    /// <summary>The mastery programme commands the month step <paramref name="month"/> consumes (the player's), written to <paramref name="into"/>; returns the count.</summary>
    public int ProgrammeCommands(int actor, long month, Span<ProgrammeCommand> into)
    {
        int n = 0;
        for (int j = 0; j < _progCmd.Length && n < into.Length; j++)
        {
            int v = _progCmd[j].Current[actor];
            if (v >> 3 == Stamp(month) && (v & 3) is 1 or 2)
                into[n++] = new ProgrammeCommand(_progNumbers[j], (v & 3) == 1, (v & 4) != 0);
        }
        return n;
    }

    /// <summary>The <c>fund_programme</c> the month step consumes: node number and amount, or null.</summary>
    public (int Node, Fx6 Amount)? Fund(int actor, long month) =>
        _fundStamp is not null && _fundStamp.Current[actor] == Stamp(month)
            ? (_fundNode!.Current[actor], Fx6.FromRaw(_fundAmount!.Current[actor]))
            : null;

    /// <summary><c>start_series</c> consumed at <paramref name="month"/>: handle, rate and force, or null.</summary>
    public (DesignHandle Design, Fx6 Rate, bool Force)? Series(int actor, int design, long month) =>
        _sStamp[design].Current[actor] == Stamp(month)
            ? (new DesignHandle(design, _sGen[design].Current[actor]), Fx6.FromRaw(_sRate[design].Current[actor]), _sForce[design].Current[actor] != 0)
            : null;

    /// <summary><c>close_line</c> consumed at <paramref name="month"/>: the handle, or null.</summary>
    public DesignHandle? CloseLine(int actor, int design, long month) =>
        _cStamp[design].Current[actor] == Stamp(month) ? new DesignHandle(design, _cGen[design].Current[actor]) : null;

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
                if (_security is not null)
                {
                    Set(_security, a, s.Shares.Security.Raw);
                    if (!s.Targets.IsDefault)
                    {
                        Set(_planSet!, a, 1);
                        for (int k = 0; k < _planTarget.Length; k++)
                        {
                            Set(_planTarget[k], a, k < s.Targets.Length ? s.Targets[k].Target + 1 : 0);
                            Set(_planMode[k], a, k < s.Targets.Length ? (int)s.Targets[k].Mode : 0);
                        }
                    }
                }
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
                DesignHandle target = command switch { ComposeDesign c => new DesignHandle(c.Generation, 0), DeriveDesign v => v.Parent, _ => ((CancelDesign)command).Design };
                Set(_dTarget[slot], a, target.Slot);
                Set(_dGen[slot], a, target.Generation);
                ImmutableArray<int> alts = command switch { ComposeDesign c => c.Alts, DeriveDesign v => v.Alts, _ => [] };
                for (int i = 0; i < _altSlots; i++)
                    Set(_dAlt[slot][i], a, alts.IsEmpty ? -1 : alts[i]);
                Set(_dStamp[slot], a, stamp);
                break;
            case RetireCohort or SellCohort:
                int slotNo = 0;
                while (slotNo < CommandLimits.FleetSlots && _fStamp[slotNo].Current[a] == stamp)
                    slotNo++;
                if (slotNo == CommandLimits.FleetSlots)
                    return CommandVerdict.Refuse(CommandRefusal.SlotOverflow, $"actor {a} already has {CommandLimits.FleetSlots} fleet commands for month {consumedAtMonth}");
                Set(_fKind[slotNo], a, command is SellCohort ? 1 : 2);
                DesignHandle fleetDesign = command is SellCohort sc ? sc.Design : ((RetireCohort)command).Design;
                Set(_fDesign[slotNo], a, fleetDesign.Slot);
                Set(_fGen[slotNo], a, fleetDesign.Generation);
                Set(_fN[slotNo], a, command is SellCohort sn ? sn.N : ((RetireCohort)command).N);
                Set(_fStamp[slotNo], a, stamp);
                break;
            case StartSeries s:
                // One entry per (actor, slot, month): a stale client's later command (always the lower generation) must not displace the valid one.
                if (_sStamp[s.Design.Slot].Current[a] == stamp && _sGen[s.Design.Slot].Current[a] > s.Design.Generation)
                    return Superseded(s.Design, _sGen[s.Design.Slot].Current[a]);
                Set(_sStamp[s.Design.Slot], a, stamp);
                Set(_sGen[s.Design.Slot], a, s.Design.Generation);
                Set(_sRate[s.Design.Slot], a, s.Rate.Raw);
                Set(_sForce[s.Design.Slot], a, s.Force ? 1 : 0);
                break;
            case CloseLine l:
                if (_cStamp[l.Design.Slot].Current[a] == stamp && _cGen[l.Design.Slot].Current[a] > l.Design.Generation)
                    return Superseded(l.Design, _cGen[l.Design.Slot].Current[a]);
                Set(_cStamp[l.Design.Slot], a, stamp);
                Set(_cGen[l.Design.Slot], a, l.Design.Generation);
                break;
            case RefuseProposal rp:
                if (!_adviserActors.Contains(a))
                    return CommandVerdict.Refuse(CommandRefusal.NoAdviser, $"actor {a} has no adviser: there is no proposal to refuse");
                // One entry per (actor, design, kind, month): the stamp keeps only this month's refusal.
                Set(rp.Close ? _rCloseStamp[rp.Design.Slot] : _rOpenStamp[rp.Design.Slot], a, stamp);
                Set(rp.Close ? _rCloseGen[rp.Design.Slot] : _rOpenGen[rp.Design.Slot], a, rp.Design.Generation);
                break;
            case StartMasteryProgramme or CancelMasteryProgramme:
                int number = command is StartMasteryProgramme sp ? sp.ProcessNumber : ((CancelMasteryProgramme)command).ProcessNumber;
                int j = _progNumbers.IndexOf(number);
                if (j < 0)
                    return CommandVerdict.Refuse(CommandRefusal.BadProcess, $"process {number} has no programme (below tier {MasteryParams.MinTier})");
                bool abroad = command is StartMasteryProgramme { Abroad: true };
                // One entry per (actor, process, month): the later command in key order wins.
                Set(_progCmd[j], a, (stamp << 3) | (abroad ? 4 : 0) | (command is StartMasteryProgramme ? 1 : 2));
                break;
            case FundProgramme fp:
                if (_fundStamp is null)
                    return CommandVerdict.Refuse(CommandRefusal.ReserveOff, "this run has no reserve account");
                if (_fundStamp.Current[a] == stamp)
                    return CommandVerdict.Refuse(CommandRefusal.SlotOverflow, $"actor {a} already funds a programme in month {consumedAtMonth}");
                Set(_fundNode!, a, fp.Node);
                Set(_fundAmount!, a, fp.Amount.Raw);
                Set(_fundStamp, a, stamp);
                break;
            case SetPriorityNode pn:
                return _institutes?.Apply(pn, consumedAtMonth) ?? CommandVerdict.Refuse(CommandRefusal.InstitutesOff, "this run has no institutes");
            case BuildInstitute bi:
                return _institutes?.ApplyBuild(bi, consumedAtMonth) ?? CommandVerdict.Refuse(CommandRefusal.InstitutesOff, "this run has no institutes");
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command.GetType(), "Unknown command type.");
        }
        return CommandVerdict.Ok;
    }

    private static CommandVerdict Superseded(DesignHandle stale, int kept) =>
        CommandVerdict.Refuse(CommandRefusal.StaleHandle, $"design {stale.Slot} generation {stale.Generation} is older than generation {kept}, already commanded for this month");

    private static int Stamp(long month) => checked((int)month + 1);

    // Column has no single-cell Current writer (Kernel); the tables here are tiny (one row per actor). The copy goes through a buffer kept between the
    // calls (commands are written between ticks, by one thread), so a command allocates nothing.
    private int[] _intCopy = [];
    private long[] _longCopy = [];

    private void Set(IntColumn column, int row, int value)
    {
        if (_intCopy.Length != column.Length)
            _intCopy = new int[column.Length];
        column.Current.CopyTo(_intCopy);
        _intCopy[row] = value;
        column.Load(_intCopy);
    }

    private void Set(LongColumn column, int row, long value)
    {
        if (_longCopy.Length != column.Length)
            _longCopy = new long[column.Length];
        column.Current.CopyTo(_longCopy);
        _longCopy[row] = value;
        column.Load(_longCopy);
    }
}
