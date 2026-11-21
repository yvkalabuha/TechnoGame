using System.Collections.Immutable;
using Singularity.Core.Commands;
using Singularity.Core.Products;
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
    /// <summary>Version of the public surface of the session (ADR-0014 п. 6): raised with every change of the views or of <see cref="SliceRunConfig"/>; the surface test pins it. 14 (TASK-0122) adds <c>SliceRunConfig.BudgetRule</c> (a Lab measurement switch); 13 (TASK-0115) makes <c>DesignHandle(Slot, Generation)</c> mandatory: <c>Submit</c> takes commands about a design by handle, <c>WhyNotTarget.Design</c> takes a handle, <c>DesignView.Handle</c>; 12 (TASK-0113) renames <c>DesignView.Generation</c> to <c>SlotGeneration</c>; 11 (TASK-0103 R1) replaces <c>ActorView.Nodes</c> (an <c>ImmutableArray</c> of record-class <c>NodeView</c>) by the compact <c>NodeList</c> of record-struct <c>NodeView</c>; 10 (TASK-0099) adds <c>DesignView.Generation</c> (slot generation, ADR-0005 p. 3); 9 (TASK-0078, ADR-0017) adds <c>HashCheckpoints</c>/<c>HashEveryTick</c>, makes <c>SliceView.StateHash</c> nullable and <c>LastHash</c> lazy (owner thread only); 0 was the surface before the read views; 8 (TASK-0063) adds the execution-only <c>SliceRunConfig.DailyTickProbe</c>; 7 (TASK-0058 step 5) drops <c>PilotChoice</c>/<c>IntroChannel</c> (I-OC and A41 SLOT are the build; <c>PilotForm</c>, <c>PilotProfiles</c> stay as options) and adds <c>RequirementView.PilotSlots</c>; 6 added <c>PilotForm</c>/<c>PilotProfiles</c>; 5 adds <c>PilotChoice</c> (HYP-0004 sketch); 4 adds the «К» parameters and <c>CapabilityTables</c> (L_C/L_P) to <c>SliceRunConfig</c>; 3 adds <c>LicenceView.Active</c>; 2 adds the step traces, <c>t_known</c> and the Requirement variants (SLICE_COMPOSITION §13.7).</summary>
    public const int TestApiVersion = 14;

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
    // Accepted commands about a design that the month step still to run will consume: (index in _outcomes, command), in journal order.
    private readonly List<(int Outcome, SliceCommand Command)> _tracked = new();
    private readonly List<(int Actor, DesignHandle Handle)> _ended = new();
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

    private const int SnapshotFormat = 0;
    private static ReadOnlySpan<byte> SnapshotMagic => "SGSS"u8;

    /// <summary>Upper bound of a saved command log (journal or queue): a corrupt length must not allocate more.</summary>
    internal const int MaxSnapshotLogBytes = 64 << 20;

    /// <summary>The last tick a snapshot may name (about 8200 years of days): keeps every date and due-tick calculation inside its range.</summary>
    internal const long MaxSnapshotTick = 3_000_000;

    /// <summary>
    /// In-memory snapshot of everything that decides the next tick (TASK-0124, ADR-0013 п. 5): magic, format, the fingerprint of the run
    /// configuration (<see cref="SliceRunConfig.SnapshotFingerprint"/>), the table (<see cref="TableSnapshot"/>), the journal and the queue of
    /// commands waiting for their tick, both as <see cref="CommandCodec"/> logs. Not a player save file. The outcomes are an observation and are not saved.
    /// </summary>
    internal void WriteSnapshot(Stream output)
    {
        CheckOwner();
        using var table = new MemoryStream();
        TableSnapshot.Write(SnapshotSource(), SliceWorld.SchemaVersion, Tick, table);
        using var buffer = new MemoryStream();
        buffer.Write(SnapshotMagic);
        WriteInt(buffer, SnapshotFormat);
        buffer.Write(SnapshotIdentity());
        WriteInt(buffer, checked((int)table.Length));
        table.WriteTo(buffer);
        WriteBlock(buffer, System.Text.Encoding.UTF8.GetBytes(CommandCodec.EncodeLog(_journal)));
        WriteBlock(buffer, System.Text.Encoding.UTF8.GetBytes(CommandCodec.EncodeLog(_pending)));
        buffer.WriteTo(output);
    }

    /// <summary>
    /// What the saved table does not say about the run: the run configuration (<see cref="SliceRunConfig.SnapshotFingerprint"/>) and the content hash of
    /// the loaded data (<see cref="GameData.DataHash"/>: nodes, product classes and every scenario, so the same scenario id with another growth_pa differs).
    /// </summary>
    private byte[] SnapshotIdentity()
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData(_world.Config.SnapshotFingerprint());
        string data = _world.Data.DataHash;
        var bytes = new byte[sizeof(int) + data.Length * sizeof(char)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, data.Length);
        for (int i = 0; i < data.Length; i++)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(sizeof(int) + i * sizeof(char)), data[i]);
        hash.AppendData(bytes);
        return hash.GetHashAndReset();
    }

    private static void WriteInt(Stream s, int v)
    {
        Span<byte> b = stackalloc byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(b, v);
        s.Write(b);
    }

    private static void WriteBlock(Stream s, byte[] block)
    {
        WriteInt(s, block.Length);
        s.Write(block);
    }

    private static int ReadInt(Stream s)
    {
        Span<byte> b = stackalloc byte[sizeof(int)];
        s.ReadExactly(b);
        return System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(b);
    }

    // The length is checked against the bound before anything is allocated.
    private static byte[] ReadBlock(Stream s, int max, string what)
    {
        int length = ReadInt(s);
        if (length < 0 || length > max)
            throw new InvalidDataException($"Snapshot {what} length {length} is outside 0…{max}.");
        var block = new byte[length];
        s.ReadExactly(block);
        return block;
    }

    /// <summary>Test seam (TASK-0124): columns whose name it accepts are saved with the values of a fresh world, as if they had not been saved.</summary>
    internal Func<string, bool>? OmitColumnInSnapshot { get; set; }

    private Table SnapshotSource()
    {
        if (OmitColumnInSnapshot is not { } omit)
            return _world.Table;
        Table fresh = new SliceWorld(_world.Data, _world.Config).Table;
        var mixed = new Table(_world.Table.RowCount);
        for (int i = 0; i < _world.Table.Columns.Count; i++)
        {
            Column c = _world.Table.Columns[i];
            Column from = omit(c.Name) ? fresh.Columns[i] : c;
            if (c is LongColumn)
                mixed.AddLong(c.Name).Load(((LongColumn)from).Current);
            else
                mixed.AddInt(c.Name).Load(((IntColumn)from).Current);
        }
        return mixed;
    }

    /// <summary>
    /// Loads a snapshot into a session just created with the same data and configuration that has not run. Two phases: everything the stream says
    /// is read and checked first (a failure there changes nothing); then the table is replaced under one rollback, so any failure leaves the session
    /// as it was (state, clock, journal, queue, view).
    /// </summary>
    internal void LoadSnapshot(Stream input)
    {
        CheckOwner();
        if (Tick != 0 || _journal.Count != 0 || _pending.Count != 0 || _tracked.Count != 0 || _outcomes.Count != 0)
            throw new InvalidOperationException("A snapshot is loaded into a new session only.");

        // Phase 1: parse and validate, no mutation.
        var magic = new byte[SnapshotMagic.Length];
        input.ReadExactly(magic);
        if (!SnapshotMagic.SequenceEqual(magic))
            throw new InvalidDataException("Not a session snapshot (bad magic).");
        int format = ReadInt(input);
        if (format != SnapshotFormat)
            throw new InvalidDataException($"Session snapshot format {format} is not supported (expected {SnapshotFormat}).");
        var configFingerprint = new byte[32];
        input.ReadExactly(configFingerprint);
        if (!configFingerprint.AsSpan().SequenceEqual(SnapshotIdentity()))
            throw new InvalidDataException("The snapshot was taken with another run configuration or another data set.");
        int expected = TableSnapshot.ExpectedBytes(_world.Table);
        int tableLength = ReadInt(input);
        if (tableLength != expected)
            throw new InvalidDataException($"Snapshot table length {tableLength} differs from the {expected} bytes this world needs.");
        var tableBytes = new byte[tableLength];
        input.ReadExactly(tableBytes);
        byte[] journalBytes = ReadBlock(input, MaxSnapshotLogBytes, "journal");
        byte[] pendingBytes = ReadBlock(input, MaxSnapshotLogBytes, "queue");
        if (input.ReadByte() >= 0)
            throw new InvalidDataException("Session snapshot has data after the queue.");

        long tick = TableSnapshot.Inspect(_world.Table, SliceWorld.SchemaVersion, tableBytes);
        if (tick > MaxSnapshotTick)
            throw new InvalidDataException($"Snapshot tick {tick} is beyond the calendar (last {MaxSnapshotTick}).");
        List<LoggedCommand> journal = DecodeCommands(journalBytes, "journal");
        List<LoggedCommand> pending = DecodeCommands(pendingBytes, "queue");
        CheckKeys(journal, "journal", c => c.Key.ApplyTick < tick);
        CheckKeys(pending, "queue", c => c.Key.ApplyTick == tick);
        CheckLocalNumbers(journal, pending);

        // Phase 2: one rollback for every failure.
        using var backup = new MemoryStream();
        TableSnapshot.Write(_world.Table, SliceWorld.SchemaVersion, 0, backup);
        string hash;
        long month;
        SliceView view;
        try
        {
            string[] seedColumns = ["run.seed.know", "run.seed.events", "run.seed.ai", "run.seed.start"];
            long[][] seeds = [.. seedColumns.Select(n => _world.Table.GetLong(n).Current.ToArray())];
            TableSnapshot.Read(_world.Table, SliceWorld.SchemaVersion, new MemoryStream(tableBytes));
            // The seeds are the identity of the run: a snapshot of another seed must not continue in this session.
            for (int i = 0; i < seedColumns.Length; i++)
            {
                if (!_world.Table.GetLong(seedColumns[i]).Current.SequenceEqual(seeds[i]))
                    throw new InvalidDataException($"Snapshot column {seedColumns[i]} differs from the seed of this session.");
            }
            _world.Runner.Restore(tick);
            // The last monthly step ran on day 1 of the month of the last completed tick.
            month = tick == 0 ? -1 : _world.Calendar.MonthIndex(tick - 1);
            hash = ComputeHash();
            view = ViewBuilder.Snapshot(_world, Tick, hash, month, null, null);
        }
        catch
        {
            backup.Position = 0;
            TableSnapshot.Read(_world.Table, SliceWorld.SchemaVersion, backup);
            _world.Runner.Restore(0);
            _hash = null;
            throw;
        }

        // Commit: nothing below can fail.
        foreach (LoggedCommand c in journal)
        {
            _journal.Add(c);
            RememberLocal(c.Key.Source, c.Key.SourceId, c.Key.LocalN);
        }
        foreach (LoggedCommand c in pending)
        {
            _pending.Add(c);
            RememberLocal(c.Key.Source, c.Key.SourceId, c.Key.LocalN);
        }
        _month = month;
        _hash = hash;
        _actorsMid = null;
        Volatile.Write(ref _view, view);
    }

    private static List<LoggedCommand> DecodeCommands(byte[] bytes, string what)
    {
        try
        {
            return CommandCodec.DecodeLog(System.Text.Encoding.UTF8.GetString(bytes));
        }
        catch (Exception e) when (e is not InvalidDataException)
        {
            throw new InvalidDataException($"Snapshot {what} is not a command log: {e.Message}", e);
        }
    }

    // As Submit demands: per (source, id) local_n grows in apply order, so the restored index of last numbers is the maximum.
    private static void CheckLocalNumbers(List<LoggedCommand> journal, List<LoggedCommand> pending)
    {
        var last = new Dictionary<(SourceClass, int), int>();
        foreach (LoggedCommand c in journal.Concat(pending))
            CheckLocalNumber(last, c);
    }

    private static void CheckLocalNumber(Dictionary<(SourceClass, int), int> last, LoggedCommand c)
    {
        (SourceClass, int) source = (c.Key.Source, c.Key.SourceId);
        if (last.TryGetValue(source, out int previous) && c.Key.LocalN <= previous)
            throw new InvalidDataException($"Snapshot command {c.Key}: local_n {c.Key.LocalN} is not above {previous} at {c.Key.Source}/{c.Key.SourceId}.");
        last[source] = c.Key.LocalN;
    }

    // Keys strictly ascending (the order Submit keeps), apply ticks as the part of the history requires, each command well formed.
    private void CheckKeys(List<LoggedCommand> commands, string what, Func<LoggedCommand, bool> tickOk)
    {
        for (int i = 0; i < commands.Count; i++)
        {
            LoggedCommand c = commands[i];
            if (!tickOk(c))
                throw new InvalidDataException($"Snapshot {what}: command {c.Key} has an apply tick that does not fit the snapshot tick.");
            if (i > 0 && commands[i - 1].Key >= c.Key)
                throw new InvalidDataException($"Snapshot {what}: keys are not strictly ascending at {c.Key}.");
            CommandVerdict v = CommandValidator.CheckForm(c, _world.Limits);
            if (!v.Accepted)
                throw new InvalidDataException($"Snapshot {what}: command {c.Key} is malformed: {v.Message}");
        }
    }

    private void StepOne()
    {
        long tick = Tick;
        bool applied = ApplyPending(tick);
        bool monthly = _world.Calendar.IsDue(Cadence.Monthly, tick);
        if (monthly)
            ReconcileOutcomes();
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
            // The layer rejects a stale handle in its step and only counts it; the snapshot at the tick boundary is the one it reads first, so the outcome names it here.
            if (v.Accepted && HandleOf(c.Command) is { } h && !_world.Products.Resolves(c.Command.Actor, h))
                v = CommandVerdict.Refuse(CommandRefusal.StaleHandle, $"design {h.Slot} generation {h.Generation} no longer stands in its slot");
            if (v.Accepted && HandleOf(c.Command) is not null)
                _tracked.Add((_outcomes.Count, c.Command));
            _outcomes.Add(c.Key, v);
        }
        _journal.AddRange(_pending);
        _pending.Clear();
        return true;
    }

    /// <summary>
    /// Makes the outcomes of the month's commands agree with the verdict of the layer (H2). Its step applies the design commands first, in journal order,
    /// then the line closes, the fleet commands and the starts: a cancel that succeeds ends its handle, so every later command on that handle in the step is
    /// <c>StaleHandle</c>. Only a cancel ends a handle inside the command phases of a step; the commands of the AI of the same actor are not modelled
    /// (an externally controlled actor runs without the product AI). Runs on the monthly tick, once the month's commands are all applied.
    /// </summary>
    private void ReconcileOutcomes()
    {
        if (_tracked.Count == 0)
            return;
        _ended.Clear();
        foreach ((int outcome, SliceCommand command) in _tracked)
        {
            if (command is not (DeriveDesign or CancelDesign))
                continue;
            DesignHandle h = HandleOf(command)!.Value;
            if (_ended.Contains((command.Actor, h)))
                MarkStale(outcome, h);
            else if (command is CancelDesign && _world.Products.Stage(command.Actor, h.Slot) is DesignStage.Project or DesignStage.Prototype)
                _ended.Add((command.Actor, h));
        }
        foreach ((int outcome, SliceCommand command) in _tracked)
        {
            if (command is not (DeriveDesign or CancelDesign) && HandleOf(command) is { } h && _ended.Contains((command.Actor, h)))
                MarkStale(outcome, h);
        }
        _tracked.Clear();
    }

    private void MarkStale(int outcome, DesignHandle h) =>
        _outcomes.Replace(outcome, CommandVerdict.Refuse(CommandRefusal.StaleHandle, $"design {h.Slot} generation {h.Generation} was ended by an earlier command of the same month step"));

    private static DesignHandle? HandleOf(SliceCommand command) => command switch
    {
        DeriveDesign d => d.Parent,
        StartSeries s => s.Design,
        CloseLine l => l.Design,
        CancelDesign x => x.Design,
        RetireCohort r => r.Design,
        SellCohort s => s.Design,
        _ => null,
    };

    private bool KnowsProcess(int actor, int processNumber)
    {
        int row = _world.Graph.RowOf(processNumber);
        return row >= 0 && _world.Knowledge.IsKnown(actor, row);
    }
}
