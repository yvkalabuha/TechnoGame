using System.Collections.Immutable;
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
    /// <summary>Version of the public surface of the session (ADR-0014 п. 6): raised with every change of the views or of <see cref="SliceRunConfig"/>; the surface test pins it. 12 (TASK-0113) renames <c>DesignView.Generation</c> to <c>SlotGeneration</c>; 11 (TASK-0103 R1) replaces <c>ActorView.Nodes</c> (an <c>ImmutableArray</c> of record-class <c>NodeView</c>) by the compact <c>NodeList</c> of record-struct <c>NodeView</c>; 10 (TASK-0099) adds <c>DesignView.Generation</c> (slot generation, ADR-0005 p. 3); 9 (TASK-0078, ADR-0017) adds <c>HashCheckpoints</c>/<c>HashEveryTick</c>, makes <c>SliceView.StateHash</c> nullable and <c>LastHash</c> lazy (owner thread only); 0 was the surface before the read views; 8 (TASK-0063) adds the execution-only <c>SliceRunConfig.DailyTickProbe</c>; 7 (TASK-0058 step 5) drops <c>PilotChoice</c>/<c>IntroChannel</c> (I-OC and A41 SLOT are the build; <c>PilotForm</c>, <c>PilotProfiles</c> stay as options) and adds <c>RequirementView.PilotSlots</c>; 6 added <c>PilotForm</c>/<c>PilotProfiles</c>; 5 adds <c>PilotChoice</c> (HYP-0004 sketch); 4 adds the «К» parameters and <c>CapabilityTables</c> (L_C/L_P) to <c>SliceRunConfig</c>; 3 adds <c>LicenceView.Active</c>; 2 adds the step traces, <c>t_known</c> and the Requirement variants (SLICE_COMPOSITION §13.7).</summary>
    public const int TestApiVersion = 12;

    private SliceView _view = null!;
    private long _month = -1;
    // F7a: the actors of the current view with the close reasons cleared (what a fresh mid-month Snapshot gives); null = not built since the last snapshot.
    private ImmutableArray<ActorView>? _actorsMid;

    // Lazy hash (ADR-0017 п. 6-7): valid while the table version is unchanged. Only the owner thread computes it.
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private long _hashVersion;
    private string? _hash;

    private readonly SliceWorld _world;
    private readonly List<LoggedCommand> _pending = new();
    private readonly ChunkedList<LoggedCommand> _journal = new();
    private readonly OutcomeLog _outcomes = new();
    // Index of the journal (last local_n per source), rebuilt by replaying the log; not world state.
    private readonly Dictionary<(SourceClass, int), int> _lastLocal = new();

    // One delegate for the life of the session: a method group would allocate one per applied command.
    private readonly Func<int, int, bool> _knowsProcess;

    private SliceSession(SliceWorld world)
    {
        _world = world;
        _knowsProcess = KnowsProcess;
        _hash = ComputeHash();
        Layout = ViewBuilder.Layout(world);
        _view = ViewBuilder.Snapshot(world, Tick, _hash, _month, null, null);
    }

    /// <summary>The fixed shape of the world the rows of the views refer to.</summary>
    public SliceLayoutView Layout { get; }

    /// <summary>
    /// Measurement seam (TASK-0107, ADR-0023 п. 2.1): when set, a tick publishes no snapshot (the <see cref="MonthStepView"/> is still built and
    /// dropped), so the bytes of a run are the tick path alone. <see cref="View"/> stays at the last published one; the state and the hash do not
    /// depend on it (views are pure reads). Not part of the public surface.
    /// </summary>
    internal bool SkipViews { get; set; }

    /// <summary>
    /// The state after the last non-empty tick as an immutable snapshot (SLICE_COMPOSITION §12.1): replaced as one reference after the hash is taken,
    /// unchanged by an empty tick. Never null; the initial one has <see cref="SliceView.Month"/> −1, no inputs and no step.
    /// </summary>
    public SliceView View => Volatile.Read(ref _view);

    public static SliceSession Create(GameData data, SliceRunConfig config) => new(new SliceWorld(data, config));

    internal SliceWorld World => _world;

    public long Tick => _world.Runner.Tick;

    public CivilDate Date => _world.Calendar.DateOf(Tick);

    /// <summary>
    /// Hash of the state after the last post (hex), or of the initial state; never null. Computed on demand and cached until the table changes
    /// (ADR-0017 п. 6-8), so it equals the hash taken right after post. Owner thread only (the one that called <see cref="Create"/>).
    /// </summary>
    public string LastHash
    {
        get
        {
            CheckOwner();
            return CurrentHash();
        }
    }

    /// <summary>How many state hashes were actually computed (a cache hit and an empty tick compute none).</summary>
    public long HashComputations { get; private set; }

    private void CheckOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("The state hash is computed on the thread that created the session (ADR-0017 п. 7).");
    }

    private string CurrentHash() => _hashVersion == _world.Table.Version && _hash is not null ? _hash : (_hash = ComputeHash());

    private string ComputeHash()
    {
        string hash = StateHash.ComputeHex(_world.Table, SliceWorld.SchemaVersion);
        _hashVersion = _world.Table.Version;
        HashComputations++;
        return hash;
    }

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
        return _world.WhyNot.CurrentRequirement(actor, NextMonth());
    }

    // Calibration runs never call why_not (spec §8.2 п. 2): the ban is structural, not only a header of the CSV.
    private void RequireFullViews()
    {
        if (_world.Config.ViewMode == SliceViewMode.Calibration)
            throw new InvalidOperationException("why_not is not available in ViewMode.Calibration.");
    }

    private long NextMonth() => _world.Calendar.MonthIndex(_world.Calendar.NextDue(Cadence.Monthly, Tick));

    /// <summary>Runs <paramref name="ticks"/> ticks. Owner thread only (ADR-0014 п. 5).</summary>
    public void Step(long ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        CheckOwner();
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
        _ = session.LastHash;   // the end of a replay is a mandatory hash point (ADR-0017 п. 2)
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
        SliceRunConfig config = _world.Config;
        // HashEveryTick hashes every non-empty tick (ADR-0017 п. 4), also one that publishes no view.
        bool nonEmpty = monthly || applied || _world.Runner.LastTickRan;
        string? hash = config.HashEveryTick && nonEmpty || config.HashCheckpoints && (monthly || applied) ? CurrentHash() : null;
        if (monthly || applied || config.DailyTickProbe)
        {
            if (SkipViews)
            {
                // The step view is part of the tick path (ADR-0023 п. 2.1), the snapshot is not: build the one, publish nothing.
                if (inputs is not null)
                    _ = ViewBuilder.Step(_world, _month);
                return;
            }
            if (!monthly && !applied && _world.DailyViewNeutral)
            {
                // F7a: no step, no command and only view-neutral systems ran, so the actors are those of the last view, up to the close reasons.
                SliceView prev = _view;
                _actorsMid ??= ViewBuilder.WithoutCloseReasons(prev.Actors);
                Volatile.Write(ref _view, prev with { Tick = Tick, Date = _world.Calendar.DateOf(Tick), StateHash = hash, Inputs = null, Step = null, Actors = _actorsMid.Value });
                return;
            }
            // The traces belong to the step that just ran; only a tick with a monthly step reads them (inputs is set exactly then).
            MonthStepView? step = inputs is null ? null : ViewBuilder.Step(_world, _month);
            Volatile.Write(ref _view, ViewBuilder.Snapshot(_world, Tick, hash, _month, inputs, step));
            _actorsMid = null;
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
            CommandVerdict v = CommandValidator.CheckState(c.Command, _knowsProcess);
            if (v.Accepted)
                v = _world.Commands.Apply(c.Command, month);
            _outcomes.Add(c.Key, v);
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
