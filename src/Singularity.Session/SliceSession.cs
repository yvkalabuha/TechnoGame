using System.Collections.Immutable;
using Singularity.Core.Commands;
using Singularity.Core.Institutes;
using Singularity.Core.Knowledge;
using Singularity.Core.Policy;
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
    /// <summary>Version of the public surface of the session (ADR-0014 п. 6): raised with every change of the views or of <see cref="SliceRunConfig"/>; the surface test pins it. 28 (TASK-0187) adds the slot science model: <c>SliceRunConfig.ScienceSlots</c>, the commands <c>AssignNode</c>/<c>Unassign</c>/<c>ReorderQueue</c>/<c>SetFocus</c>, <c>ActorView.Slots</c> (<c>SlotScienceView</c>) and the slot fields of <c>InstituteView</c>; 27 (TASK-0171) turns the five levers of wave 1 on by default (<c>Reserve</c>, <c>Institutes</c>, <c>Intelligence</c>, <c>LicenceMarket</c> and the new <c>AdviserForPlayer</c>), adds <c>SliceRunConfig.Legacy</c> (all off) and <c>GameData.Alliances</c>; 26 (TASK-0151) adds the research institutes (see session-surface.v26.txt): <c>SliceRunConfig.Institutes</c>, <c>ActorView.Institutes</c> (<c>InstituteView</c>) and the commands <c>SetPriorityNode</c>/<c>BuildInstitute</c>; 25 (TASK-0153) adds <c>SliceRunConfig.AdviserActors</c>, <c>AdviserRefuseYears</c>, <c>AdviserMaxLines</c>, <c>PolicyDecisionView.Proposals</c>, <c>ProposalView</c> and the command <c>RefuseProposal</c>; 24 (TASK-0160) adds <c>SliceRunConfig.Intelligence</c>, <c>ActorInputsView.Intel</c> (<c>IntelView</c>, <c>IntelTargetView</c>), the seventh item <c>BudgetShares.Security</c> and <c>SetBudgetShares.Targets</c> with their refusals; 23 (TASK-0152) adds the licence market: <see cref="SliceRunConfig.LicenceMarket"/>, <c>LicenceExternalActors</c>, <c>Alliances</c>, the commands <c>BuyLicence</c>/<c>OfferLicence</c> (<c>CommandVerdict.Licence</c>, <c>Offer</c>), <c>ActorView.Market</c> (<c>LicenceMarketView</c>) and <c>DesignView.LicensedFrom</c>; 22 (TASK-0150) adds <c>SliceRunConfig.Reserve</c>, <c>ActorInputsView.Reserve</c> (<c>ReserveView</c>) and the command <c>FundProgramme</c> with its refusals; 21 (TASK-0119) adds <c>ActorView.Alloy</c> (<c>AlloyCoverage</c>) and <c>ActorView.Research</c> (<c>ResearchView</c>); 20 (TASK-0147) adds <c>SliceRunConfig.ResearchWeightByDiscipline</c> and <c>DisciplineWeight</c>; 19 (TASK-0147) adds <c>SliceRunConfig.AheadCostK</c>, <c>AheadCostPow</c>, <c>AheadCostMax</c> (ahead-of-time cost of a node, #458); 18 (TASK-0134) adds <c>SliceRunConfig.HcapGrowthPerYear</c>, <c>HcapGrowthMax</c>, <c>MasteryXProg</c>, <c>MasteryUpkeepMult</c>, <c>MasteryRules</c> and the commands <c>StartMasteryProgramme</c>/<c>CancelMasteryProgramme</c> (<c>CommandVerdict.Mastery</c> carries the <c>why_not</c> code); 17 (TASK-0131) adds <c>SliceRunConfig.CostMultG5</c> and <c>CostMultG6</c> (c_n multipliers of generations 5 and 6, a calibration parameter); 16 (TASK-0131) makes <c>LicenceView.Mask</c> and <c>OfferMask</c> a two-word <c>LicenceMask</c> (nodes 1…127); 15 (TASK-0130, ADR-0025) adds <c>SliceRunConfig.ViewScope</c>, <c>SetViewSubscription</c>, <c>RebuildView</c> and <c>NodeList.IsPresent</c>; 14 (TASK-0122) adds <c>SliceRunConfig.BudgetRule</c> (a Lab measurement switch); 13 (TASK-0115) makes <c>DesignHandle(Slot, Generation)</c> mandatory: <c>Submit</c> takes commands about a design by handle, <c>WhyNotTarget.Design</c> takes a handle, <c>DesignView.Handle</c>; 12 (TASK-0113) renames <c>DesignView.Generation</c> to <c>SlotGeneration</c>; 11 (TASK-0103 R1) replaces <c>ActorView.Nodes</c> (an <c>ImmutableArray</c> of record-class <c>NodeView</c>) by the compact <c>NodeList</c> of record-struct <c>NodeView</c>; 10 (TASK-0099) adds <c>DesignView.Generation</c> (slot generation, ADR-0005 p. 3); 9 (TASK-0078, ADR-0017) adds <c>HashCheckpoints</c>/<c>HashEveryTick</c>, makes <c>SliceView.StateHash</c> nullable and <c>LastHash</c> lazy (owner thread only); 0 was the surface before the read views; 8 (TASK-0063) adds the execution-only <c>SliceRunConfig.DailyTickProbe</c>; 7 (TASK-0058 step 5) drops <c>PilotChoice</c>/<c>IntroChannel</c> (I-OC and A41 SLOT are the build; <c>PilotForm</c>, <c>PilotProfiles</c> stay as options) and adds <c>RequirementView.PilotSlots</c>; 6 added <c>PilotForm</c>/<c>PilotProfiles</c>; 5 adds <c>PilotChoice</c> (HYP-0004 sketch); 4 adds the «К» parameters and <c>CapabilityTables</c> (L_C/L_P) to <c>SliceRunConfig</c>; 3 adds <c>LicenceView.Active</c>; 2 adds the step traces, <c>t_known</c> and the Requirement variants (SLICE_COMPOSITION §13.7).</summary>
    public const int TestApiVersion = 28;

    private SliceView _view = null!;
    private long _month = -1;
    // F7a: the actors of the current view with the close reasons cleared (what a fresh mid-month Snapshot gives); null = not built since the last snapshot.
    private ImmutableArray<ActorView>? _actorsMid;
    // ADR-0025: the actors whose node blocks the views carry (null = all, ViewScope.All). A new array per change; owner thread only, an instance field.
    private bool[]? _subscribed;
    // A change applies by a full build: F7a would hand out the ActorViews of the old subscription.
    private bool _subscriptionChanged;

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
        _subscribed = world.Config.ViewScope == ViewScope.Subscribed ? new bool[world.Scenario.ActorCount] : null;
        _hash = ComputeHash();
        Layout = ViewBuilder.Layout(world);
        _view = ViewBuilder.Snapshot(world, Tick, _hash, _month, null, null, _subscribed);
    }

    /// <summary>
    /// Sets the actors whose node blocks the next views carry (ADR-0025 п. 2-3); with <see cref="ViewScope.Subscribed"/> only. Owner thread only. Takes effect at the
    /// next publication, or at once with <see cref="RebuildView"/>. Never touches the state, the hash or the RNG.
    /// </summary>
    public void SetViewSubscription(ReadOnlySpan<int> actors)
    {
        CheckOwner();
        if (_subscribed is null)
            throw new InvalidOperationException("ViewScope is All: every view carries every actor");
        var next = new bool[_subscribed.Length];
        foreach (int a in actors)
        {
            if ((uint)a >= (uint)next.Length)
                throw new ArgumentOutOfRangeException(nameof(actors), a, $"actor must be in 0…{next.Length - 1}");
            next[a] = true;
        }
        if (next.AsSpan().SequenceEqual(_subscribed))
            return;
        _subscribed = next;
        _subscriptionChanged = true;
    }

    /// <summary>
    /// Publishes the current view again, built in full (not the F7a path) with the current subscription, without a tick. When the last tick published, the view keeps its
    /// tick, hash, inputs and step; when later ticks ran without publishing, the view is built from the state now: current tick, no hash, no step (it never claims an older tick for
    /// newer state). The inputs stay: they carry their own month and record what the last monthly step read, so a panel keeps its budget until the next month. For a paused game after <see cref="SetViewSubscription"/>. Owner thread only. A no-op while <see cref="SkipViews"/> is set.
    /// </summary>
    public void RebuildView()
    {
        CheckOwner();
        if (SkipViews)
            return;
        SliceView v = _view;
        bool current = v.Tick == Tick;
        Volatile.Write(ref _view, ViewBuilder.Snapshot(_world, Tick, current ? v.StateHash : null, _month, v.Inputs, current ? v.Step : null, _subscribed));
        _actorsMid = null;
        _subscriptionChanged = false;
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

        if (command is StartSeries start && LineLimitReached(start))
            return CommandVerdict.Refuse(CommandRefusal.LineLimit, $"actor {start.Actor} already runs the most lines allowed ({_world.Policy.Adviser!.MaxLines}); another line is not supported yet");

        if (command is RetireCohort { Acknowledged: false } retire && NeedsConfirmation(retire) is { } warning)
            return warning;

        string? slotWarning = null;
        if (command is AssignNode assign && StartCoverage(assign, NextStepMonth, out Fx6 before) is { } after && after < _world.Institutes!.SlotParameters.UnderfundedBelow)
        {
            string percent = $"{after.Raw * 100 / Fx6.Scale} %";
            if (before >= _world.Institutes.SlotParameters.UnderfundedBelow && !assign.Acknowledged)
                return CommandVerdict.Refuse(CommandRefusal.NeedsConfirmation, $"after the start the money covers {percent} of what the institutes ask (now {before.Raw * 100 / Fx6.Scale} %): send the command with Acknowledged to start anyway");
            slotWarning = $"accepted; the money covers only {percent} of what the institutes ask after the start";
        }

        int at = _pending.FindIndex(p => p.Key > logged.Key);
        _pending.Insert(at < 0 ? _pending.Count : at, logged);
        RememberLocal(source, sourceId, localN);
        return slotWarning is null ? CommandVerdict.Ok : new CommandVerdict(CommandRefusal.None, slotWarning);
    }

    // Levers doc 1.2: a player actor under an adviser runs at most MaxLines lines. A start on a design that is already a line only changes its rate.
    private bool LineLimitReached(StartSeries s)
    {
        if (_world.Policy.Adviser is not { } plan || !plan.Rows.Contains(s.Actor) || _world.Products.Stage(s.Actor, s.Design.Slot) == DesignStage.Series)
            return false;
        int lines = 0;
        for (int d = 0; d < _world.ProductsLayout.Designs; d++)
        {
            // A start applied at a tick boundary waits in the command columns for the monthly step: it counts as a line too, or starts sent on different days would pass.
            if (d != s.Design.Slot && (_world.Products.Stage(s.Actor, d) == DesignStage.Series || StartQueued(s.Actor, d) || _world.Commands.Series(s.Actor, d, NextMonth()) is not null))
                lines++;
        }
        return lines >= plan.MaxLines;
    }

    // The proposal the player refuses is the one the view of that month showed (same kind, same project).
    private bool Proposed(RefuseProposal r, long month)
    {
        foreach (AdviserProposal p in _world.Policy.AdviserProposals(r.Actor, month))
        {
            if (p.Design == r.Design.Slot && p.Handle == r.Design && p.Kind == (r.Close ? ProposalKind.Close : ProposalKind.Open))
                return true;
        }
        return false;
    }

    private bool StartQueued(int actor, int design) => _pending.Exists(p => p.Command is StartSeries q && q.Actor == actor && q.Design.Slot == design);

    // #437: retiring the newest generation without a replacement is allowed, but only after a warning. A stale handle or a bad count is the layer's business.
    private CommandVerdict? NeedsConfirmation(RetireCohort r)
    {
        if (_world.Config.ViewMode == SliceViewMode.Calibration || !_world.Products.Resolves(r.Actor, r.Design) || r.N < 1)
            return null;
        FleetForecast f = _world.Products.RetireForecast(r.Actor, r.Design.Slot, r.N, NextMonth());
        return f.GenAfter < f.GenBefore
            ? new CommandVerdict(CommandRefusal.NeedsConfirmation,
                $"retiring {r.N} units of design {r.Design.Slot} lowers the best generation in service from {f.GenBefore} to {f.GenAfter}; send it again with Acknowledged to confirm",
                f.GenBefore, f.GenAfter)
            : null;
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

    // 1: the identity field holds the data hash as well as the configuration; the table carries its state hash.
    // 2: a SHA-256 digest of every byte before it closes the snapshot (and the table snapshot inside carries its own).
    private const int SnapshotFormat = 2;
    private static ReadOnlySpan<byte> SnapshotMagic => "SGSS"u8;

    /// <summary>Upper bound of a saved command log (journal or queue): a corrupt length must not allocate more.</summary>
    internal const int MaxSnapshotLogBytes = 64 << 20;

    /// <summary>Test seam: the bound of a saved log block that <see cref="WriteSnapshot"/> and <see cref="LoadSnapshot"/> enforce.</summary>
    internal int SnapshotLogLimit { get; set; } = MaxSnapshotLogBytes;

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
        byte[] journalBytes = System.Text.Encoding.UTF8.GetBytes(CommandCodec.EncodeLog(_journal));
        byte[] pendingBytes = System.Text.Encoding.UTF8.GetBytes(CommandCodec.EncodeLog(_pending));
        // What Load would refuse must not be written.
        if (journalBytes.Length > SnapshotLogLimit || pendingBytes.Length > SnapshotLogLimit)
            throw new InvalidOperationException($"The command journal ({journalBytes.Length} bytes) or queue ({pendingBytes.Length} bytes) is larger than a snapshot may hold ({SnapshotLogLimit}).");
        WriteBlock(buffer, journalBytes);
        WriteBlock(buffer, pendingBytes);
        byte[] content = buffer.ToArray();
        output.Write(content);
        output.Write(System.Security.Cryptography.SHA256.HashData(content));
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
        // The intelligence parameters are outside the data hash: a snapshot must not resume under other ones (only a run with intelligence adds them).
        if (_world.IntelDataHash.Length > 0)
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes("intel:" + _world.IntelDataHash));
        // Likewise the institute data (own hash, outside the data hash; only a run with institutes adds it).
        if (_world.Config.Institutes)
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes("inst:" + _world.Data.Institutes.Hash));
        return hash.GetHashAndReset();
    }

    private static byte[] ReadBounded(Stream s, long max)
    {
        using var m = new MemoryStream();
        var chunk = new byte[81920];
        int n;
        while ((n = s.Read(chunk, 0, (int)Math.Min(chunk.Length, max + 1 - m.Length))) > 0)
        {
            m.Write(chunk, 0, n);
            if (m.Length > max)
                throw new InvalidDataException($"Session snapshot is longer than the {max} bytes this world can hold.");
        }
        return m.ToArray();
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
    internal void LoadSnapshot(Stream source)
    {
        CheckOwner();
        if (Tick != 0 || _journal.Count != 0 || _pending.Count != 0 || _tracked.Count != 0 || _outcomes.Count != 0)
            throw new InvalidOperationException("A snapshot is loaded into a new session only.");

        // Phase 1: parse and validate, no mutation. The digest over every byte before it is checked first: a flipped byte anywhere is refused
        // before any field is parsed. The read is bounded by what a snapshot of this world can hold at most.
        long most = (long)SnapshotMagic.Length + 4 + 32 + 4 + TableSnapshot.ExpectedBytes(_world.Table) + 2 * (4L + SnapshotLogLimit) + 32;
        byte[] all = ReadBounded(source, most);
        if (all.Length < 32 || !System.Security.Cryptography.SHA256.HashData(all.AsSpan(0, all.Length - 32)).AsSpan().SequenceEqual(all.AsSpan(all.Length - 32)))
            throw new InvalidDataException("Session snapshot digest does not match its bytes.");
        var input = new MemoryStream(all, 0, all.Length - 32, writable: false);
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
        byte[] journalBytes = ReadBlock(input, SnapshotLogLimit, "journal");
        byte[] pendingBytes = ReadBlock(input, SnapshotLogLimit, "queue");
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
            // The state as loaded must hash to what the snapshot carries (ADR-0013 п. 8); a mismatch rolls back.
            if (hash != TableSnapshot.StoredStateHash(tableBytes))
                throw new InvalidDataException("The loaded state does not hash to the state hash of the snapshot.");
            view = ViewBuilder.Snapshot(_world, Tick, hash, month, null, null, _subscribed);
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
        _subscriptionChanged = false;
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
        for (int i = 0; i < journal.Count; i++)
            CheckLocalNumber(last, journal[i]);
        for (int i = 0; i < pending.Count; i++)
            CheckLocalNumber(last, pending[i]);
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
            _world.Policy.ApplyDeals(month, a => _world.LicenceFunds(a, month));
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
            if (!monthly && !applied && _world.DailyViewNeutral && !_subscriptionChanged)
            {
                // F7a: no step, no command and only view-neutral systems ran, so the actors are those of the last view, up to the close reasons.
                SliceView prev = _view;
                _actorsMid ??= ViewBuilder.WithoutCloseReasons(prev.Actors);
                Volatile.Write(ref _view, prev with { Tick = Tick, Date = _world.Calendar.DateOf(Tick), StateHash = hash, Inputs = null, Step = null, Actors = _actorsMid.Value });
                return;
            }
            // The traces belong to the step that just ran; only a tick with a monthly step reads them (inputs is set exactly then).
            MonthStepView? step = inputs is null ? null : ViewBuilder.Step(_world, _month);
            Volatile.Write(ref _view, ViewBuilder.Snapshot(_world, Tick, hash, _month, inputs, step, _subscribed));
            _actorsMid = null;
            _subscriptionChanged = false;
        }
    }

    private bool ApplyPending(long tick)
    {
        if (_pending.Count == 0)
            return false;
        // Consumed by the first monthly step at or after this tick.
        long month = _world.Calendar.MonthIndex(_world.Calendar.NextDue(Cadence.Monthly, tick));
        // A programme is judged on the shares of the month that consumes it, so the other commands of the batch (a new budget split) go first.
        var verdicts = new CommandVerdict[_pending.Count];
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                SliceCommand command = _pending[i].Command;
                if ((command is FundProgramme) != (pass == 1))
                    continue;
                CommandVerdict v0 = CommandValidator.CheckState(command, _knowsProcess);
                if (v0.Accepted && command is StartMasteryProgramme start)
                    v0 = MasteryVerdict(start, month);
                if (v0.Accepted && command is RefuseProposal refuse && _world.Policy.IsAdviser(refuse.Actor) && !Proposed(refuse, month))
                    v0 = CommandVerdict.Refuse(CommandRefusal.NoProposal, $"no {(refuse.Close ? "close" : "open")} proposal for design {refuse.Design.Slot} generation {refuse.Design.Generation} in month {month}");
                if (v0.Accepted && command is FundProgramme fund)
                    v0 = FundVerdict(fund, month);
                if (v0.Accepted && command is SetPriorityNode priority)
                    v0 = PriorityVerdict(priority, month);
                if (v0.Accepted && command is BuildInstitute build)
                    v0 = BuildVerdict(build, month);
                if (v0.Accepted && command is AssignNode or Unassign or ReorderQueue or SetFocus or ApplyQueues)
                    v0 = SlotVerdict(command, month);
                verdicts[i] = !v0.Accepted ? v0 : command is BuyLicence or OfferLicence ? LicenceVerdict(command, month) : _world.Commands.Apply(command, month);
            }
        }
        for (int i = 0; i < _pending.Count; i++)
        {
            LoggedCommand c = _pending[i];
            CommandVerdict v = verdicts[i];
            // The layer rejects a stale handle in its step and only counts it; the snapshot at the tick boundary is the one it reads first, so the outcome names it here.
            if (v.Accepted && HandleOf(c.Command) is { } h && !_world.Products.Resolves(c.Command.Actor, h))
                v = CommandVerdict.Refuse(CommandRefusal.StaleHandle, $"design {h.Slot} generation {h.Generation} no longer stands in its slot");
            if (v.Accepted && (HandleOf(c.Command) is not null || c.Command is FundProgramme))
                _tracked.Add((_outcomes.Count, c.Command));
            _outcomes.Add(c.Key, v);
        }
        _journal.AddRange(_pending);
        _pending.Clear();
        return true;
    }

    // The licence commands act on the policy (the deal queue and the catalogue), not on cmd.* columns: written between ticks, so the deal is in the queue when
    // ApplyDeals runs at the start of the month, which pays it or counts its refusal. A refusal that can be known now is returned now.
    private CommandVerdict LicenceVerdict(SliceCommand command, long month)
    {
        PolicySystem policy = _world.Policy;
        if (!policy.MarketOn || !policy.IsExternal(command.Actor))
            return CommandVerdict.Refuse(CommandRefusal.LicenceNotAllowed, $"actor {command.Actor} is not under external control of a run with the licence market");
        if (_world.Reserve is null)
            return CommandVerdict.Refuse(CommandRefusal.ReserveOff, "the licence market of the player pays and earns through the reserve (O-055): the run has none");
        if (command is OfferLicence o)
        {
            OfferRefusal refusal = policy.SetOffer(o.Actor, o.Gen, o.M, o.Circle, month);
            return refusal == OfferRefusal.None ? CommandVerdict.Ok
                : new CommandVerdict(CommandRefusal.BadOffer, $"offer of generation {o.Gen} refused: {refusal}", Offer: refusal);
        }
        var b = (BuyLicence)command;
        // One deal waits per buyer; a second would replace the first and leave its outcome «accepted» for a purchase that never settles.
        if (policy.HasQueuedDeal(b.Actor))
            return CommandVerdict.Refuse(CommandRefusal.SlotOverflow, $"actor {b.Actor} already has a licence deal queued for the start of the month");
        BadLicence reason = policy.CheckBuy(b.Actor, b.Seller, b.Gen, month, _world.LicenceFunds(b.Actor, month), out _);
        if (reason != BadLicence.None)
            return new CommandVerdict(CommandRefusal.LicenceRefused, $"licence of generation {b.Gen} from {b.Seller} refused: {reason}", Licence: reason);
        policy.QueueBuy(b.Actor, b.Seller, b.Gen, month);
        return CommandVerdict.Ok;
    }

    // §1.2, O-096: a node that would start at once (a free institute, the node in the frontier) is held back for confirmation when the start moves the coverage of the money from
    // 60 % or more to less; when the money already covers less, the start goes with a warning. Returns the coverage after the start, or null when the node would not start now.
    private Fx6? StartCoverage(AssignNode a, long month, out Fx6 before)
    {
        before = Fx6.One;
        InstituteSystem institutes = _world.Institutes!;
        int row = _world.Graph.RowOf(a.Node);
        if (!_world.SlotsOn || row < 0 || (uint)a.Slot >= (uint)institutes.Layout.SlotCount(a.Actor) || !institutes.Standing(new InstituteHandle(a.Actor, a.Slot))
            || !_world.Knowledge.InFrontier(a.Actor, row) || _world.Knowledge.Released(a.Actor, row))
            return null;
        var node = new int[institutes.Layout.SlotCount(a.Actor)];
        institutes.Resolve(a.Actor, month, node, new byte[node.Length]);
        if (node[a.Slot] != 0 || node.Contains(a.Node))
            return null;
        before = _world.Knowledge.CoverageWith(a.Actor, month, null);
        return _world.Knowledge.CoverageWith(a.Actor, month, new SlotWork(a.Slot, row, institutes.InstituteMultiplier(new InstituteHandle(a.Actor, a.Slot), row), 0));
    }

    private long NextStepMonth => _world.Calendar.MonthIndex(_world.Calendar.NextDue(Cadence.Monthly, Tick));

    /// <summary>
    /// <c>FitInstitutes(node)</c> (SCIENCE_SLOTS §12): every institute of the actor against the node: overlap level, speed, months, what it works on now. Empty without the slot model or for an unknown node.
    /// A pure read of the snapshot.
    /// </summary>
    public ImmutableArray<InstituteFitView> FitInstitutes(int actor, int nodeNumber) =>
        _world.SlotsOn && _world.Graph.RowOf(nodeNumber) is >= 0 and int row ? SlotPlanner.Fit(_world, actor, row, NextStepMonth) : [];

    /// <summary><c>FocusQuote(node, institute)</c>: months, money per month and the price of the rest of the node at focus 0, 1 and 2. Empty as <see cref="FitInstitutes"/>.</summary>
    public ImmutableArray<FocusQuoteView> FocusQuote(int actor, int nodeNumber, int slot) =>
        _world.SlotsOn && _world.Graph.RowOf(nodeNumber) is >= 0 and int row && (uint)slot < (uint)_world.Institutes!.Layout.SlotCount(actor) ? SlotPlanner.Focus(_world, actor, row, slot, NextStepMonth) : [];

    /// <summary><c>PathToGoal(node)</c>: the unknown nodes on the way, their months and prices, and the time of the whole way (a range). Null without the slot model or for an unknown node.</summary>
    public PathView? PathToGoal(int actor, int nodeNumber) =>
        _world.SlotsOn && _world.Graph.RowOf(nodeNumber) is >= 0 and int row ? SlotPlanner.Path(_world, actor, row, NextStepMonth) : null;

    /// <summary><c>PathToGoal(product)</c>: the way to the generation of the product (its APP node).</summary>
    public PathView? PathToGeneration(int actor, int generation) =>
        generation >= 1 && generation <= _world.ProductsLayout.Generations ? PathToGoal(actor, _world.ProductsLayout.GenApp[generation - 1]) : null;

    /// <summary><c>SuggestQueues(goal)</c>: a layout of the path over the standing institutes, to send as <c>ApplyQueues</c> after the player confirms it (§4.3). Empty without the slot model.</summary>
    public ImmutableArray<QueuePlan> SuggestQueues(int actor, int goalNodeNumber) =>
        _world.SlotsOn && _world.Graph.RowOf(goalNodeNumber) is >= 0 and int row ? SlotPlanner.Suggest(_world, actor, row, NextStepMonth) : [];

    // build_institute: the window of the project, the cap of a small country and one project per slot (research-institutes-priorities §8).
    private CommandVerdict BuildVerdict(BuildInstitute b, long month)
    {
        InstituteSystem institutes = _world.Institutes ?? throw new InvalidOperationException("The validator accepted build_institute without institutes.");
        var h = new InstituteHandle(b.Actor, b.Slot);
        return institutes.CanBuild(h, month) switch
        {
            BuildCheck.Ok => CommandVerdict.Ok,
            BuildCheck.AlreadyStanding => CommandVerdict.Refuse(CommandRefusal.BuildRefused, "the institute stands already"),
            BuildCheck.AlreadyBuilding => CommandVerdict.Refuse(CommandRefusal.BuildRefused, "the project of this institute runs already"),
            BuildCheck.TooEarly => CommandVerdict.Refuse(CommandRefusal.BuildRefused, $"the window opens in {institutes.Layout.RefYear(h) - institutes.Parameters.WindowYears}"),
            BuildCheck.UpkeepCeiling => CommandVerdict.Refuse(CommandRefusal.BuildRefused, "the upkeep of the institutes would pass its share of the research money (#574)"),
            _ => CommandVerdict.Refuse(CommandRefusal.BuildRefused, $"a small country builds at most {institutes.Parameters.SmallCountryLevelCap} levels in one project"),
        };
    }

    // Slot model: the node must still be unknown; a node of another institute's list is refused by the list itself (NodeTaken).
    private CommandVerdict SlotVerdict(SliceCommand command, long month)
    {
        InstituteSystem institutes = _world.Institutes ?? throw new InvalidOperationException("The validator accepted a slot command without institutes.");
        if (command is AssignNode a)
        {
            if (!institutes.Standing(new InstituteHandle(a.Actor, a.Slot)))
                return CommandVerdict.Refuse(CommandRefusal.PriorityNotAvailable, $"the institute in slot {a.Slot} is not built");
            int row = _world.Graph.RowOf(a.Node);
            if (row < 0 || _world.Knowledge.IsKnown(a.Actor, row))
                return CommandVerdict.Refuse(CommandRefusal.NodeKnown, $"node {a.Node} is already known to actor {a.Actor}");
        }
        if (command is SetFocus f && f.Level > 0 && !institutes.Standing(new InstituteHandle(f.Actor, f.Slot)))
            return CommandVerdict.Refuse(CommandRefusal.NoActiveNode, $"the institute in slot {f.Slot} is not built");
        return CommandVerdict.Ok;
    }

    // set_priority_node: the node must still be unknown and no other slot of the actor works on it; a slot the player has set may be changed once per period.
    private CommandVerdict PriorityVerdict(SetPriorityNode p, long month)
    {
        InstituteSystem institutes = _world.Institutes ?? throw new InvalidOperationException("The validator accepted set_priority_node without institutes.");
        if (!institutes.Standing(new InstituteHandle(p.Actor, p.Slot)))
            return CommandVerdict.Refuse(CommandRefusal.PriorityNotAvailable, $"the institute in slot {p.Slot} is not built");
        int row = _world.Graph.RowOf(p.Node);
        if (row < 0 || _world.Knowledge.IsKnown(p.Actor, row))
            return CommandVerdict.Refuse(CommandRefusal.PriorityNotAvailable, $"node {p.Node} is already known to actor {p.Actor}");
        var slot = new InstituteHandle(p.Actor, p.Slot);
        if (_world.SlotsOn)
        {
            // The slot model: the node becomes the active one of the slot; another institute that works on it keeps it.
            Span<int> work = stackalloc int[institutes.Layout.SlotCount(p.Actor)];
            Span<byte> focus = stackalloc byte[work.Length];
            institutes.Resolve(p.Actor, month, work, focus);
            for (int k = 0; k < work.Length; k++)
            {
                if (k != p.Slot && work[k] == p.Node)
                    return CommandVerdict.Refuse(CommandRefusal.PriorityNotAvailable, $"another institute already works on node {p.Node}");
            }
            return CommandVerdict.Ok;
        }
        for (int k = 0; k < institutes.Layout.SlotCount(p.Actor); k++)
        {
            if (k != p.Slot && institutes.GoalNode(new InstituteHandle(p.Actor, k)) == p.Node)
                return CommandVerdict.Refuse(CommandRefusal.PriorityNotAvailable, $"another institute already works on node {p.Node}");
        }
        int order = _world.InstituteOrders!.OrderNode(slot);
        long ordered = _world.InstituteOrders!.OrderMonth(slot);
        bool active = order != 0 && order != p.Node && !_world.Knowledge.IsKnown(p.Actor, _world.Graph.RowOf(order));
        if (active && month - ordered < institutes.Parameters.ReassignMonths)
            return CommandVerdict.Refuse(CommandRefusal.PriorityTooSoon, $"the slot got its node in month {ordered}; the next change is allowed in month {ordered + institutes.Parameters.ReassignMonths}");
        return CommandVerdict.Ok;
    }

    // fund_programme needs the node in the frontier and the money in the reserve now; the layer repeats both on the snapshot of its step, so a late change costs nothing.
    private CommandVerdict FundVerdict(FundProgramme f, long month)
    {
        int row = _world.Graph.RowOf(f.Node);
        if (row < 0 || !_world.Knowledge.InFrontier(f.Actor, row))
            return CommandVerdict.Refuse(CommandRefusal.ProgrammeNotFundable, $"node {f.Node} is not being researched by actor {f.Actor}");
        if (_world.Knowledge.ProgrammeCapacity(f.Actor, row, month) <= Fx6.Zero)
            return CommandVerdict.Refuse(CommandRefusal.ProgrammeNotFundable, $"node {f.Node} cannot take more progress this month");
        if (_world.Knowledge.ProgrammeGain(f.Actor, row, month, f.Amount) <= Fx6.Zero)
            return CommandVerdict.Refuse(CommandRefusal.ProgrammeNotFundable, $"the amount {f.Amount} is too small to move node {f.Node}");
        // A licence deal accepted earlier in the month keeps its price in the reserve: the programme may not take it (the deal would be refused for money at settlement).
        Fx6 balance = (_world.Reserve?.Spendable(f.Actor) ?? Fx6.Zero) - (_world.FlowViaReserve(f.Actor) ? _world.Policy.QueuedPrice(f.Actor) : Fx6.Zero);
        return balance >= f.Amount
            ? CommandVerdict.Ok
            : CommandVerdict.Refuse(CommandRefusal.ProgrammeNotFundable, $"the reserve can pay {balance} this month (after its leak), the programme needs {f.Amount}");
    }

    // The player's programme start meets the check the AI and the layer use (production-mastery.md §1): the refusal names its why_not code.
    private CommandVerdict MasteryVerdict(StartMasteryProgramme start, long month)
    {
        ProgrammeVerdict v = MasteryChecks.Verdict(_world, start.Actor, start.ProcessNumber, start.Abroad, month);
        return v.Accepted ? CommandVerdict.Ok
            : new CommandVerdict(CommandRefusal.ProgrammeRefused, $"programme on process {start.ProcessNumber} refused: {v.Reason}", Mastery: v.Reason, MasteryDetail: v.Detail);
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
            // A budget change or a discovery after the verdict can change what the programme may take: ask again on the snapshot the step reads.
            if (command is FundProgramme fund && FundVerdict(fund, _world.Calendar.MonthIndex(Tick)) is { Accepted: false } late)
                _outcomes.Replace(outcome, late);
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
