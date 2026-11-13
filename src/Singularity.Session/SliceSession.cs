using Singularity.Core.Commands;
using Singularity.Core.Data;
using Singularity.Core.WhyNot;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>
/// A running slice (ADR-0014): the only way to run the world. <see cref="Submit"/> takes an external command, checks form and right,
/// and queues it for the next tick boundary; <see cref="Step"/> runs ticks synchronously on the caller's thread, applying the queued
/// commands in key order at the start of the tick (they are written to <c>cmd.*</c> and in force for the next monthly step). Accepted
/// commands form <c>commands.log</c> (<see cref="CommandLog"/>); the same codec reads it back for <see cref="Replay"/>.
/// The state hash is taken after a non-empty tick only: a tick on which a monthly step ran or commands were applied (ADR-0006).
/// </summary>
public sealed partial class SliceSession
{
    /// <summary>Version of the public surface of the session (ADR-0014 п. 6): raised with every change of the views or of <see cref="SliceRunConfig"/>; the surface test pins it. 0 was the surface before the read views.</summary>
    public const int TestApiVersion = 1;

    private SliceView _view = null!;
    private long _month = -1;

    private readonly SliceWorld _world;
    private readonly List<LoggedCommand> _pending = new();
    private readonly List<LoggedCommand> _journal = new();
    private readonly List<string> _outcomes = new();
    // Index of the journal (last local_n per source), rebuilt by replaying the log; not world state.
    private readonly Dictionary<(SourceClass, int), int> _lastLocal = new();

    private SliceSession(SliceWorld world)
    {
        _world = world;
        LastHash = StateHash.ComputeHex(world.Table, SliceWorld.SchemaVersion, world.Runner.Tick);
        HashComputations = 1;
        Layout = ViewBuilder.Layout(world);
        _view = ViewBuilder.Snapshot(world, Tick, LastHash, _month, null);
    }

    /// <summary>The fixed shape of the world the rows of the views refer to.</summary>
    public SliceLayoutView Layout { get; }

    /// <summary>
    /// The state after the last non-empty tick as an immutable snapshot (SLICE_COMPOSITION §12.1): replaced as one reference after the hash is taken,
    /// unchanged by an empty tick. Never null; the initial one has <see cref="SliceView.Month"/> −1 and no inputs.
    /// </summary>
    public SliceView View => Volatile.Read(ref _view);

    public static SliceSession Create(GameData data, SliceRunConfig config) => new(new SliceWorld(data, config));

    internal SliceWorld World => _world;

    public long Tick => _world.Runner.Tick;

    public CivilDate Date => _world.Calendar.DateOf(Tick);

    /// <summary>Hash of the state after the last non-empty tick (hex), or of the initial state.</summary>
    public string LastHash { get; private set; }

    /// <summary>How many state hashes were computed (an empty tick computes none).</summary>
    public long HashComputations { get; private set; }

    /// <summary>All accepted external commands in key order, one JSON line each (<c>commands.log</c>).</summary>
    public string CommandLog => CommandCodec.EncodeLog(_journal.Concat(_pending));

    /// <summary>Outcome of every application (<c>accepted</c> or <c>rejected: reason</c>): an observation, not state.</summary>
    public IReadOnlyList<string> Outcomes => _outcomes;

    /// <summary>
    /// Takes an external command. It gets <c>apply_tick</c> = the next tick boundary and the key <c>(apply_tick, source, id, local_n)</c>;
    /// <paramref name="localN"/> must grow at every source. A refused command carries its reason and is not logged.
    /// </summary>
    public CommandVerdict Submit(SourceClass source, int sourceId, int localN, SliceCommand command)
    {
        var logged = new LoggedCommand(new CommandKey(Tick, source, sourceId, localN), command);
        CommandVerdict form = CommandValidator.CheckForm(logged, _world.Limits);
        if (!form.Accepted)
            return form;
        if (_pending.Exists(p => p.Key == logged.Key))
            return CommandVerdict.Refuse(CommandRefusal.DuplicateKey, $"command {logged.Key} was already submitted");
        if (LastLocal(source, sourceId) is int last && localN <= last)
            return CommandVerdict.Refuse(CommandRefusal.LocalNotMonotonic, $"local_n {localN} is not above {last} at {source}/{sourceId}");
        if (command is ComposeDesign or DeriveDesign or CancelDesign
            && _pending.Count(p => p.Command.Actor == command.Actor && p.Command is ComposeDesign or DeriveDesign or CancelDesign) >= CommandLimits.DesignSlots)
            return CommandVerdict.Refuse(CommandRefusal.TooManyDesignCommands, $"actor {command.Actor} already has {CommandLimits.DesignSlots} design commands queued");

        int at = _pending.FindIndex(p => p.Key > logged.Key);
        _pending.Insert(at < 0 ? _pending.Count : at, logged);
        RememberLocal(source, sourceId, localN);
        return CommandVerdict.Ok;
    }

    // The index is only looked up, never enumerated, and kept in methods of their own (the determinism rule is conservative).
    private int? LastLocal(SourceClass source, int sourceId) => _lastLocal.TryGetValue((source, sourceId), out int last) ? last : null;

    private void RememberLocal(SourceClass source, int sourceId, int localN) => _lastLocal[(source, sourceId)] = localN;

    /// <summary>
    /// <c>why_not(actor, target)</c> (contract §6): the sorted list of links on the snapshot now, month t of the next monthly step. A pure read:
    /// it changes nothing, so calling it never changes the state hash.
    /// </summary>
    public IReadOnlyList<Link> WhyNot(int actor, WhyNotTarget target)
    {
        RequireFullViews();
        return _world.WhyNot.WhyNot(actor, target, NextMonth());
    }

    /// <summary>The yearly target of spec §5.6: the requirement of the newest known APP the actor has not fielded, or null.</summary>
    public WhyNotTarget? CurrentRequirement(int actor)
    {
        RequireFullViews();
        return _world.WhyNot.CurrentRequirement(actor);
    }

    // Calibration runs never call why_not (spec §8.2 п. 2): the ban is structural, not only a header of the CSV.
    private void RequireFullViews()
    {
        if (_world.Config.ViewMode == SliceViewMode.Calibration)
            throw new InvalidOperationException("why_not is not available in ViewMode.Calibration.");
    }

    private long NextMonth() => _world.Calendar.MonthIndex(_world.Calendar.NextDue(Cadence.Monthly, Tick));

    /// <summary>Runs <paramref name="ticks"/> ticks.</summary>
    public void Step(long ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        for (long i = 0; i < ticks; i++)
            StepOne();
    }

    /// <summary>Runs ticks until <see cref="Tick"/> equals <paramref name="tick"/>.</summary>
    public void StepTo(long tick)
    {
        if (tick < Tick)
            throw new ArgumentOutOfRangeException(nameof(tick), tick, $"The session is already at tick {Tick}.");
        Step(tick - Tick);
    }

    /// <summary>Runs through the end of month <paramref name="month"/> (every system of the month has run and merged).</summary>
    public void StepThroughMonth(long month) => StepTo(_world.Calendar.TickOfMonth(month) + 1);

    /// <summary>
    /// Feeds a <c>commands.log</c> into a new session at the ticks it names, then runs to <paramref name="untilTick"/>: the same
    /// state hash, tick and date as the original run (ADR-0014 п. 5).
    /// </summary>
    public static SliceSession Replay(GameData data, SliceRunConfig config, string commandLog, long untilTick)
    {
        SliceSession session = Create(data, config);
        foreach (LoggedCommand c in CommandCodec.DecodeLog(commandLog))
        {
            session.StepTo(c.Key.ApplyTick);
            CommandVerdict v = session.Submit(c.Key.Source, c.Key.SourceId, c.Key.LocalN, c.Command);
            if (!v.Accepted)
                throw new InvalidOperationException($"The log command {c.Key} was refused on replay: {v.Message}");
        }
        session.StepTo(untilTick);
        return session;
    }

    private void StepOne()
    {
        long tick = Tick;
        bool applied = ApplyPending(tick);
        bool monthly = _world.Calendar.IsDue(Cadence.Monthly, tick);
        // The licences bought in the last compute begin now, after the player's commands saw the shares of the month (contract §5, spec A43.5).
        MonthInputsView? inputs = null;
        if (monthly)
        {
            long month = _world.Calendar.MonthIndex(tick);
            _world.Policy.ApplyDeals(month, _world.ProcurementBase);
            // The inputs of the step as its compute will see them: after the deals and the commands, before the step.
            inputs = ViewBuilder.Inputs(_world, month);
            _month = month;
        }
        _world.Runner.RunTick();
        if (monthly || applied)
        {
            LastHash = StateHash.ComputeHex(_world.Table, SliceWorld.SchemaVersion, Tick);
            HashComputations++;
            Volatile.Write(ref _view, ViewBuilder.Snapshot(_world, Tick, LastHash, _month, inputs));
        }
    }

    private bool ApplyPending(long tick)
    {
        if (_pending.Count == 0)
            return false;
        // Consumed by the first monthly step at or after this tick.
        long month = _world.Calendar.MonthIndex(_world.Calendar.NextDue(Cadence.Monthly, tick));
        foreach (LoggedCommand c in _pending)
        {
            CommandVerdict v = CommandValidator.CheckState(c.Command, KnowsProcess);
            if (v.Accepted)
                v = _world.Commands.Apply(c.Command, month);
            _outcomes.Add(v.Accepted ? $"{c.Key}: accepted" : $"{c.Key}: rejected: {v.Reason}: {v.Message}");
        }
        _journal.AddRange(_pending);
        _pending.Clear();
        return true;
    }

    private bool KnowsProcess(int actor, int processNumber)
    {
        int row = _world.Graph.RowOf(processNumber);
        return row >= 0 && _world.Knowledge.IsKnown(actor, row);
    }
}
