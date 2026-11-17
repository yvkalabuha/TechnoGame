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
    int WhyActor, WhyNotTarget? Target, IReadOnlyList<Link> Links, string WhyError, double LastTickMs)
{
    public bool LastRejected => Last.Status == CmdStatus.Rejected;
}

/// <summary>
/// Runs SliceSession on a background thread. The UI thread never touches the session: it reads <see cref="Snap"/>, and sends
/// commands and why_not requests through a queue that the simulation thread drains between ticks.
/// </summary>
public sealed class SimHost : IDisposable
{
    // Ticks are days (Calendar.DateOf = start day + tick). Tempo #269: slow 1 day/s, medium ~1 week/s, max ~1 month/s.
    public static readonly double[] TicksPerSecond = { 0, 1, 7, 30 };

    readonly ConcurrentQueue<Action> _jobs = new();
    readonly Thread _thread;
    volatile bool _stop;
    int _speed;
    int _whyActor;
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
    WhyNotTarget? _target; IReadOnlyList<Link> _links = Array.Empty<Link>(); string _whyErr = "";
    ImmutableArray<BudgetShares?> _shares;
    double _lastTickMs;

    public GameData Data { get; private set; }
    public HostSnapshot Snap => _snap;
    public string Status => _status;
    public string Fatal => _fatal;
    public SliceLayoutView Layout { get; private set; }
    public bool Ready => _snap != null;

    public SimHost() { _thread = new Thread(Run) { IsBackground = true, Name = "sim" }; }
    public void Start() => _thread.Start();

    public int Speed { get => Volatile.Read(ref _speed); set => Volatile.Write(ref _speed, value); }
    public int WhyActor { get => Volatile.Read(ref _whyActor); set { Volatile.Write(ref _whyActor, value); } }

    /// <summary>Queues a command of the player (source class Player, id 0). While paused it stays pending and applies at the first tick after unpause.</summary>
    public void Submit(SliceCommand command, string labelKey, int labelArg = 0)
    {
        _jobs.Enqueue(() =>
        {
            CommandVerdict v = _session.Submit(SourceClass.Player, 0, ++_localN, command);
            _cmdSeq++;
            if (v.Accepted) { _pending++; _last = new CmdResult(CmdStatus.Pending, labelKey, labelArg, "", ""); }
            else _last = new CmdResult(CmdStatus.Rejected, labelKey, labelArg, v.Reason.ToString(), v.Message);
            Publish();
        });
    }

    /// <summary>Test hook: run <paramref name="ticks"/> ticks on the simulation thread regardless of tempo.</summary>
    public void StepTicks(long ticks) => _jobs.Enqueue(() => Advance(ticks));

    static string RepoRoot()
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
            Data = ModLoader.Load(Path.Combine(root, "mods", "core"));
            _status = "status.session";
            _session = SliceSession.Create(Data, new SliceRunConfig { Seed = 7 });
            Layout = _session.Layout;
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
                        // A failed job does not stop the simulation: it shows as the last command result.
                        _cmdSeq++; _last = new CmdResult(CmdStatus.Error, "cmd.job", 0, e.GetType().Name, e.Message);
                        Publish();
                    }
                }
                double now = sw.Elapsed.TotalSeconds, dt = Math.Min(now - last, 0.25);
                last = now;
                int sp = Speed;
                if (sp == 0) { owed = 0; }
                else
                {
                    owed += dt * TicksPerSecond[sp];
                    long n = (long)owed;
                    if (n > 0) { owed -= n; Advance(n); }
                }
                if (WhyActor != _lastWhyActor) Publish();
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
            if (_pending > 0)
            {
                _pending = 0;
                if (_last.Status == CmdStatus.Pending) _last = _last with { Status = CmdStatus.Applied };
            }
            if (!ReferenceEquals(_session.View, _lastWhyView)) Publish();
        }
        _lastTickMs = ticks > 0 ? sw.Elapsed.TotalMilliseconds / ticks : 0;
        Publish();
    }

    void Publish()
    {
        SliceView v = _session.View;
        if (v.Inputs != null && !ReferenceEquals(v, _lastWhyView))
        {
            var b = _shares.ToBuilder();
            for (int i = 0; i < v.Inputs.Actors.Length; i++) b[v.Inputs.Actors[i].Actor] = v.Inputs.Actors[i].Shares;
            _shares = b.MoveToImmutable();
        }
        int wa = WhyActor;
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
        _snap = new HostSnapshot(_session.Tick, _session.Date, v, Speed, _last, _pending, _cmdSeq, _lastOutcome, ++_seq, _shares, wa, _target, _links, _whyErr, _lastTickMs);
    }

    /// <summary>Stops the simulation thread; called from the node's _ExitTree.</summary>
    public void Dispose() { _stop = true; if (_thread.IsAlive) _thread.Join(2000); }
}
