using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Singularity.Core.Commands;
using Singularity.Core.Data;
using Singularity.Core.Policy;
using Singularity.Core.WhyNot;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Client.Spike;

public enum CmdStatus { None, Pending, Applied, Rejected, Error }

/// <summary>Result of the last command, language-neutral (the UI formats it): <paramref name="Reason"/> and <paramref name="Message"/> are Session text, shown under F3 only.</summary>
public sealed record CmdResult(CmdStatus Status, string LabelKey, int LabelArg, string Reason, string Message);

/// <summary>What the UI thread reads: one immutable reference, replaced by the simulation thread (ADR-0014 п. 4).
/// <paramref name="Pending"/> = accepted commands that no tick has applied yet (always so while paused); <paramref name="CmdSeq"/> counts handled commands.</summary>
public sealed record HostSnapshot(
    long Tick, CivilDate Date, SliceView View, int Speed,
    CmdResult Last, int Pending, long CmdSeq, string LastOutcome, long Seq,
    ImmutableArray<BudgetShares?> SharesInForce,
    int WhyActor, WhyNotTarget? Target, IReadOnlyList<Link> Links, string WhyError, double LastTickMs, string JobError = "", ScienceSnap Science = null)
{
    public bool LastRejected => Last.Status == CmdStatus.Rejected;
}

/// <summary>What the tech map asks of the session for the node and the goal it shows (TASK-0192): reads of the month of the view, made on the simulation thread.
/// <paramref name="NoticeSeq"/> counts the pauses for «an institute is free» (S12); the UI shows the banner when it changes.</summary>
public sealed record ScienceSnap(int Node, int Goal, ImmutableArray<InstituteFitView> Fit, ImmutableArray<ImmutableArray<FocusQuoteView>> Focus, PathView Path, ImmutableArray<QueuePlan> Plan, long FreeNoticeMonth, int NoticeSeq,
    ImmutableArray<ImmutableArray<InstituteFitView>> PathFit = default, int Compare = 0, ImmutableArray<FocusQuoteView> CompareQuote = default);

/// <summary>The node (number, 0 = none) and the goal (number, 0 = none) the tech map shows.</summary>
public readonly record struct ScienceAsk(int Node, int Goal, int Compare = 0);

/// <summary>
/// Runs SliceSession on a background thread. The UI thread never touches the session: it reads <see cref="Snap"/>, and sends
/// commands and why_not requests through a queue that the simulation thread drains between ticks.
/// </summary>
public sealed class SimHost : IDisposable
{
    // Ticks are days (Calendar.DateOf = start day + tick). Steps and rates: SpeedSteps (#481).

    readonly ConcurrentQueue<Action> _jobs = new();
    readonly Thread _thread;
    volatile bool _stop;
    int _speed;
    int _whyActor;
    int _playerActor = -1;
    volatile HostSnapshot _snap;
    volatile string _status = "status.loading";
    volatile string _fatal;

    // Simulation-thread state
    SliceSession _session;
    int _localN;
    CmdResult _last = new(CmdStatus.None, "", 0, "", "");
    string _lastOutcome = "";
    int _pending; long _cmdSeq;
    long _seq;
    SliceView _lastWhyView;
    int _lastWhyActor = -1;
    readonly ViewSubscriptionSync _sub = new();    WhyNotTarget? _target; IReadOnlyList<Link> _links = Array.Empty<Link>(); string _whyErr = "";
    ImmutableArray<BudgetShares?> _shares;
    double _lastTickMs;
    string _jobError = "";

    public GameData Data { get; private set; }
    public HostSnapshot Snap => _snap;
    public string Status => _status;
    public string Fatal => _fatal;
    public SliceLayoutView Layout { get; private set; }
    public bool Ready => _snap != null;

    readonly int _seed; readonly GameData _preloaded; readonly string _playerActorId;
    /// <summary>Hash of the initial state (tick 0): two starts with the same seed and scenario must give the same value.</summary>
    public int Seed => _seed;
    public string StartHash { get; private set; } = "";

    /// <param name="seed">World seed (default 7, as the spikes before the menu).</param>
    /// <param name="data">Mods already loaded by the menu; null = load here.</param>
    public SimHost(int seed = 7, GameData data = null, string playerActorId = null) { _seed = seed; _preloaded = data; _playerActorId = playerActorId; _thread = new Thread(Run) { IsBackground = true, Name = "sim" }; }
    public void Start() => _thread.Start();

    public int Speed { get => Volatile.Read(ref _speed); set => Volatile.Write(ref _speed, value); }
    /// <summary>The actor the player plays; its nodes are always in the view (the audio reads them). Set once by the UI.</summary>
    public int PlayerActor { get => Volatile.Read(ref _playerActor); set => Volatile.Write(ref _playerActor, value); }
    ScienceAsk _ask, _lastAsk;
    readonly FreeNoticeThrottle _notice = new();
    int _noticeSeq;
    /// <summary>The adviser fills the free institutes of the player (the old behaviour, O-099): off in play, where the player chooses; the unattended runs of the shot script switch it on.</summary>
    public volatile bool AutoFillFreeInstitutes;
    /// <summary>Asks the session for the fit, focus quotes, path and layout of a node and a goal; answered in the next snapshot.</summary>
    public void AskScience(int node, int goal, int compare = 0) { lock (_askLock) _ask = new ScienceAsk(node, goal, compare); }
    readonly object _askLock = new();
    ScienceAsk CurrentAsk() { lock (_askLock) return _ask; }
    public int WhyActor { get => Volatile.Read(ref _whyActor); set { Volatile.Write(ref _whyActor, value); } }

    /// <summary>Queues a command of the player (source class Player, id 0). While paused it stays pending and applies at the first tick after unpause.</summary>
    public void Submit(SliceCommand command, string labelKey, int labelArg = 0)
    {
        _jobs.Enqueue(() =>
        {
            _cmdSeq++;   // every command is handled exactly once, whatever its outcome
            try
            {
                CommandVerdict v = _session.Submit(SourceClass.Player, 0, ++_localN, command);
                if (v.Accepted) { _pending++; _last = new CmdResult(CmdStatus.Pending, labelKey, labelArg, "", ""); }
                else _last = new CmdResult(CmdStatus.Rejected, labelKey, labelArg, v.Reason.ToString(), v.Message);
            }
            catch (Exception e)
            {
                // The failure belongs to this command; it does not disturb commands that are still pending.
                _last = new CmdResult(CmdStatus.Error, labelKey, labelArg, e.GetType().Name, e.Message);
            }
            Publish();
        });
    }

    /// <summary>Screenshot hook: shows the simulation-failure state as if the simulation thread had thrown. The thread keeps running.</summary>
    public void SimulateFatal(string text) => _fatal = text;

    /// <summary>Test hook: run <paramref name="ticks"/> ticks on the simulation thread regardless of tempo.</summary>
    public void StepTicks(long ticks) => _jobs.Enqueue(() => Advance(ticks));
    /// <summary>Loads the mods (core) once, so that the menu can list the scenario before a session exists.</summary>
    public static GameData LoadData()
    {
        string root = Environment.GetEnvironmentVariable("SINGULARITY_ROOT") ?? RepoRoot();
        return ModLoader.Load(Path.Combine(root, "mods", "core"));
    }

    public static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Singularity.sln"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Singularity.sln not found above the binaries");
    }

    void Run()
    {
        try
        {
            string root = Environment.GetEnvironmentVariable("SINGULARITY_ROOT") ?? RepoRoot();
            Data = _preloaded ?? ModLoader.Load(Path.Combine(root, "mods", "core"));
            _status = "status.session";
            // The player's actor: the institutes and the product lines of it are only proposed by the AI (the adviser), its licences move by commands.
            int player = _playerActorId == null ? -1 : Data.Scenario(SliceRunConfig.DefaultScenario).ActorIndex(_playerActorId);
            SliceRunConfig config = SliceRunConfig.ForClient((ulong)_seed, player);
            _session = SliceSession.Create(Data, config);
            Layout = _session.Layout;
            StartHash = _session.LastHash;
            var arr = ImmutableArray.CreateBuilder<BudgetShares?>(Layout.ActorIds.Length);
            for (int i = 0; i < Layout.ActorIds.Length; i++) arr.Add(null);
            _shares = arr.MoveToImmutable();
            Publish();
            var sw = Stopwatch.StartNew();
            double owed = 0, last = sw.Elapsed.TotalSeconds;
            while (!_stop)
            {
                while (_jobs.TryDequeue(out var job))
                {
                    try { job(); }
                    catch (Exception e)
                    {
                        // A failed job that is not a command (a test hook) does not stop the simulation and is not a command result:
                        // it is kept apart (JobError, shown under F3) so that it cannot hide or fake the state of the player's commands.
                        _jobError = e.GetType().Name + ": " + e.Message;
                        Publish();
                    }
                }
                double now = sw.Elapsed.TotalSeconds, dt = Math.Min(now - last, 0.25);
                last = now;
                int sp = Speed;
                if (sp == 0) { owed = 0; }
                else
                {
                    owed += dt * SpeedSteps.TicksPerSecond[SpeedSteps.Clamp(sp)];
                    long n = (long)owed;
                    if (n > 0) { owed -= n; Advance(n); }
                }
                if (WhyActor != _lastWhyActor || _sub.Stale(PlayerActor, WhyActor) || CurrentAsk() != _lastAsk) Publish();
                Thread.Sleep(sp == 0 ? 20 : 8);
            }
        }
        catch (Exception e)
        {
            _fatal = e.ToString();
        }
    }

    void Advance(long ticks)
    {
        var sw = Stopwatch.StartNew();
        for (long i = 0; i < ticks && !_stop; i++)
        {
            _session.Step(1);
            FillFreeInstitutes();
            WatchFreeNotice();
            if (_pending > 0)
            {
                // A tick applies every accepted command: the last result becomes «applied» only if it is still the pending one
                // (a later refusal or error stays what it is).
                _pending = 0;
                if (_last.Status == CmdStatus.Pending) _last = _last with { Status = CmdStatus.Applied };
            }
            if (!ReferenceEquals(_session.View, _lastWhyView)) Publish();
        }
        _lastTickMs = ticks > 0 ? sw.Elapsed.TotalMilliseconds / ticks : 0;
        Publish();
    }

    long _filledMonth = -2;

    // S12: «an institute is free» pauses the game at most once in 25 real seconds; a notice that comes too early is dropped (the banner of the tech map still shows the free card).
    void WatchFreeNotice()
    {
        if (PlayerActor < 0 || PlayerActor >= _session.View.Actors.Length || _session.View.Actors[PlayerActor].Slots is not { } slots) return;
        if (!_notice.ShouldPause(slots.FreeNoticeMonth, Environment.TickCount64)) return;
        Speed = 0;
        _noticeSeq++;
    }

    // With the tech map the player chooses; the adviser fills the free institutes only on request (AutoFillFreeInstitutes), once a month (O-099).
    void FillFreeInstitutes()
    {
        if (!AutoFillFreeInstitutes || PlayerActor < 0 || _session.View.Month == _filledMonth)
            return;
        _filledMonth = _session.View.Month;
        foreach (AssignNode start in _session.AdviserStarts(PlayerActor))
            _session.Submit(SourceClass.Player, 0, ++_localN, start);
    }

    void Publish()
    {
        int wa = _sub.Sync(_session, PlayerActor, WhyActor);
        SliceView v = _session.View;
        if (v.Inputs != null && !ReferenceEquals(v, _lastWhyView))
        {
            var b = _shares.ToBuilder();
            for (int i = 0; i < v.Inputs.Actors.Length; i++) b[v.Inputs.Actors[i].Actor] = v.Inputs.Actors[i].Shares;
            _shares = b.MoveToImmutable();
        }
        if (!ReferenceEquals(v, _lastWhyView) || wa != _lastWhyActor)
        {
            try
            {
                _target = _session.CurrentRequirement(wa);
                _links = _target is { } t ? _session.WhyNot(wa, t) : Array.Empty<Link>();
                _whyErr = "";
            }
            catch (Exception e) { _target = null; _links = Array.Empty<Link>(); _whyErr = e.Message; }
            _lastWhyView = v; _lastWhyActor = wa;
        }
        _lastOutcome = _session.Outcomes.Count > 0 ? _session.Outcomes[^1] : "";
        ScienceSnap sci = null;
        ScienceAsk ask = CurrentAsk();
        _lastAsk = ask;
        if (PlayerActor >= 0 && ask != default && v.Actors.Length > PlayerActor && v.Actors[PlayerActor].Slots is { } slotView)
        {
            try
            {
                var fit = ask.Node > 0 ? _session.FitInstitutes(PlayerActor, ask.Node) : ImmutableArray<InstituteFitView>.Empty;
                var focus = ImmutableArray.CreateBuilder<ImmutableArray<FocusQuoteView>>(fit.Length);
                foreach (var f in fit) focus.Add(f.Standing ? _session.FocusQuote(PlayerActor, ask.Node, f.Slot) : ImmutableArray<FocusQuoteView>.Empty);
                PathView path = ask.Goal > 0 ? _session.PathToGoal(PlayerActor, ask.Goal) : null;
                var plan = ask.Goal > 0 ? _session.SuggestQueues(PlayerActor, ask.Goal) : ImmutableArray<QueuePlan>.Empty;
                // The months of each path node at every institute: a line of the layout shows the months of the institute it stands in.
                var pathFit = ImmutableArray.CreateBuilder<ImmutableArray<InstituteFitView>>();
                if (path != null && !plan.IsDefaultOrEmpty) foreach (var pn in path.Nodes) pathFit.Add(_session.FitInstitutes(PlayerActor, pn.Node));
                // The node to compare with: the quote of its best institute at no focus.
                ImmutableArray<FocusQuoteView> cmpQuote = default;
                if (ask.Compare > 0)
                {
                    var cf = _session.FitInstitutes(PlayerActor, ask.Compare);
                    int[] order = ScienceMapLogic.FitOrder(cf);
                    if (order.Length > 0) cmpQuote = _session.FocusQuote(PlayerActor, ask.Compare, order[0]);
                }
                sci = new ScienceSnap(ask.Node, ask.Goal, fit, focus.MoveToImmutable(), path, plan, slotView.FreeNoticeMonth, _noticeSeq, pathFit.ToImmutable(), ask.Compare, cmpQuote);
            }
            catch (Exception e) { _jobError = "science: " + e.Message; }
        }
        sci ??= new ScienceSnap(0, 0, ImmutableArray<InstituteFitView>.Empty, ImmutableArray<ImmutableArray<FocusQuoteView>>.Empty, null, ImmutableArray<QueuePlan>.Empty, -1, _noticeSeq);
        _snap = new HostSnapshot(_session.Tick, _session.Date, v, Speed, _last, _pending, _cmdSeq, _lastOutcome, ++_seq, _shares, wa, _target, _links, _whyErr, _lastTickMs, _jobError, sci);
    }

    /// <summary>Stops the simulation thread; called from the node's _ExitTree.</summary>
    public void Dispose() { _stop = true; if (_thread.IsAlive) _thread.Join(2000); }
}
